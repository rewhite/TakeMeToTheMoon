using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.WebRTC;
using UnityEngine;

namespace Visko
{
    // Reactor API v1 / WebRTC v1.0. Direct device-to-Reactor transport; no Python process.
    public class ViskoStreamClient : MonoBehaviour
    {
        [Header("Reactor")]
        public string model = "reactor/visko-orbis-dynamic";
        [Tooltip("Connect in Start. Disable when another component calls Connect() at the right moment.")]
        public bool connectOnStart = true;
        [HideInInspector] public bool driveWebRtcUpdate = true;
        [Header("Generation")]
        [TextArea(3, 8)] public string startPrompt = "A vast lunar landscape seen from just above the grey dusty surface, the blue Earth rising slowly over the horizon. Slow cinematic forward drift, a single unbroken take.";
        public Texture2D startImage;
        public bool autoStart = true;
        public string resolution = "";
        public bool generateAudio = true;
        [Header("Screen")]
        [Tooltip("Only expose VideoTexture; another component puts it on a screen. No quad or material is created.")]
        public bool externalScreen;
        public Renderer targetRenderer;
        public Material screenMaterial;
        public float screenDistance = 3f;
        public float screenWidth = 3.2f;
        public float screenHeightOffset;
        [Header("Audio")]
        public bool playAudio = true;
        [Tooltip("Screen audio defaults to centered 2D. External/shared screens always use 2D audio.")]
        [Range(0, 1)] public float spatialBlend = 0f;
        [Header("Debug")]
        public bool showDebugGui = true;
        public bool showWorldStatus = true;

        public event Action<string, string> MessageReceived;
        public bool SessionReady { get; private set; }
        public int ConnectionAttemptCount { get; private set; }
        public bool BridgeConnected => SessionReady; // Compatibility with existing callers.
        public Texture VideoTexture { get; private set; }
        public ulong DecodedVideoFrames { get; private set; }
        public float LastVideoFrameTime { get; private set; } = float.NegativeInfinity;
        public string Status { get; private set; } = "Disconnected";

        const string Api = "https://api.reactor.inc";
        RTCPeerConnection _peer;
        RTCDataChannel _data, _control;
        readonly List<MediaStreamTrack> _tracks = new List<MediaStreamTrack>();
        readonly List<JObject> _candidates = new List<JObject>();
        CancellationTokenSource _lifetime;
        Task _connectionTask;
        Task _commands = Task.CompletedTask;
        string _jwt, _session, _connectionPath, _debugPrompt;
        bool _iceSignalingReady, _iceFinalSent, _sendingIce, _createdScreen, _imageAccepted;
        float _nextPing, _nextIce;
        Material _material, _previousMaterial;
        AudioSource _audio;
        bool _outputMuted;
        TextMesh _statusText;

        // Receive/decode preloaded audio normally, but keep it inaudible behind the prologue.
        public void SetOutputMuted(bool muted)
        {
            _outputMuted = muted;
            if (_audio != null) _audio.mute = muted;
        }

        async void Start()
        {
            _debugPrompt = startPrompt;
#if UNITY_EDITOR
            gameObject.AddComponent<ViskoEditorOverlay>().Initialize(this);
#endif
            SetUpScreen();
            SetOutputMuted(_outputMuted);
            if (driveWebRtcUpdate) StartCoroutine(WebRTC.Update());
            StartCoroutine(ObserveVideoFrames());
            if (connectOnStart) await Connect();
        }

        public async Task Connect()
        {
            if (_disconnectTask != null) await _disconnectTask;
            if (_lifetime != null)
            {
                if (_connectionTask != null) await _connectionTask;
                return;
            }
            _lifetime = new CancellationTokenSource();
            var token = _lifetime.Token;
            _connectionTask = ConnectCore(token);
            try { await _connectionTask; }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                SetStatus("Connection failed: " + SafeError(e));
                // Quest has no Editor overlay. Log the sanitized failure without headers or response bodies.
                Debug.LogWarning("[Visko] " + Status, this);
                await DisconnectCore(false);
            }
        }

        async Task ConnectCore(CancellationToken token)
        {
            ConnectionAttemptCount++;
            var config = Resources.Load<TextAsset>("ReactorLocalConfig");
            string key = config == null ? null : (string)JObject.Parse(config.text)["apiKey"];
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("Set the key in Tools > Visko > Local API Key, then reconnect.");
            SetStatus("Authenticating");
            var auth = await Request("POST", "/tokens", new JObject {
                ["expires_after"] = 21600,
                ["authorization_details"] = new JArray(new JObject {
                    ["type"] = "session", ["resources"] = new JObject {
                        ["models"] = new JObject { ["match"] = new JArray(model) }
                    }
                })
            }, token, key: key);
            _jwt = (string)auth["jwt"] ?? throw new InvalidOperationException("Missing token response.");
            SetStatus("Placing session");
            // Once created, retain the ID even if cancellation occurs during this request,
            // so DisconnectCore can explicitly terminate the allocated session.
            var created = await Request("POST", "/sessions", new JObject {
                ["model"] = new JObject { ["name"] = model },
                ["client_info"] = ClientInfo(),
                ["supported_transports"] = new JArray(new JObject { ["protocol"] = "webrtc", ["version"] = "1.0" })
            }, token, finishOnCancel: true);
            _session = (string)created["session_id"] ?? throw new InvalidOperationException("Missing session ID.");
            token.ThrowIfCancellationRequested();
            JObject session = null;
            for (int i = 0; i < 180; i++)
            {
                session = await Request("GET", SessionPath, null, token);
                string state = (string)session["state"];
                if (state == "CLOSED" || state == "INACTIVE") throw new InvalidOperationException("Session ended while waiting.");
                SetStatus("Session " + state);
                if (session["capabilities"] is JObject && session["selected_transport"] is JObject) break;
                await Task.Delay(1000, token);
            }
            if (!(session?["capabilities"] is JObject capabilities)) throw new TimeoutException("Session placement timed out.");
            if ((string)session["selected_transport"]?["protocol"] != "webrtc") throw new InvalidOperationException("Server did not select WebRTC.");
            string transport = SessionPath + "/transport/webrtc";
            var ice = await Request("GET", transport + "/ice_servers", null, token);
            var servers = new List<RTCIceServer>();
            foreach (var item in (JArray)ice["ice_servers"])
                servers.Add(new RTCIceServer {
                    urls = item["uris"].ToObject<string[]>(),
                    username = (string)item["credentials"]?["username"],
                    credential = (string)item["credentials"]?["password"]
                });
            var rtcConfig = new RTCConfiguration { iceServers = servers.ToArray() };
            _peer = new RTCPeerConnection(ref rtcConfig);
            _peer.OnIceCandidate = c => {
                if (c != null) _candidates.Add(new JObject { ["candidate"] = c.Candidate, ["sdp_mid"] = c.SdpMid, ["sdp_mline_index"] = c.SdpMLineIndex });
            };
            _peer.OnTrack = OnTrack;
            _peer.OnConnectionStateChange = s => {
                if (s == RTCPeerConnectionState.Failed) { SetStatus("Connection lost. Reconnect to try again."); _ = DisconnectCore(false); }
                else if (s == RTCPeerConnectionState.Disconnected) SetStatus("Connection interrupted");
            };
            _control = _peer.CreateDataChannel("control");
            _data = _peer.CreateDataChannel("data");
            _data.OnMessage = OnMessage;
            var mappings = new List<(JToken track, RTCRtpTransceiver transceiver)>();
            foreach (var track in (JArray)capabilities["tracks"])
            {
                if ((string)track["direction"] != "recvonly") throw new InvalidOperationException("This client supports receive-only models.");
                var t = _peer.AddTransceiver((string)track["kind"] == "video" ? TrackKind.Video : TrackKind.Audio,
                    new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly });
                mappings.Add((track, t));
            }
            var offer = _peer.CreateOffer();
            while (!offer.IsDone) { token.ThrowIfCancellationRequested(); await Task.Yield(); }
            if (offer.IsError) throw new InvalidOperationException("Could not create WebRTC offer.");
            var desc = offer.Desc;
            var local = _peer.SetLocalDescription(ref desc);
            while (!local.IsDone) { token.ThrowIfCancellationRequested(); await Task.Yield(); }
            if (local.IsError) throw new InvalidOperationException("Could not set local WebRTC description.");
            var mapping = new JArray();
            foreach (var entry in mappings)
            {
                var m = (JObject)entry.track.DeepClone(); m["mid"] = entry.transceiver.Mid; mapping.Add(m);
            }
            var connection = await Request("POST", transport + "/connections", null, token);
            _connectionPath = transport + "/connections/" + (int)connection["connection_id"];
            await Request("POST", _connectionPath + "/sdp_params", new JObject {
                ["sdp_offer"] = _peer.LocalDescription.sdp, ["track_mapping"] = mapping, ["client_info"] = ClientInfo()
            }, token);
            _iceSignalingReady = true;
            SetStatus("Connecting video and audio");
            string answer = null;
            for (int i = 0; i < 90; i++)
            {
                var reply = await Request("GET", _connectionPath + "/sdp_params", null, token);
                answer = (string)reply["sdp_answer"];
                if (!string.IsNullOrEmpty(answer)) break;
                await Task.Delay(1000, token);
            }
            if (string.IsNullOrEmpty(answer)) throw new TimeoutException("WebRTC answer timed out.");
            desc = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = answer };
            var remote = _peer.SetRemoteDescription(ref desc);
            while (!remote.IsDone) { token.ThrowIfCancellationRequested(); await Task.Yield(); }
            if (remote.IsError) throw new InvalidOperationException("Could not set remote WebRTC description.");
            var deadline = Time.realtimeSinceStartup + 45;
            while (_peer.ConnectionState != RTCPeerConnectionState.Connected || _data.ReadyState != RTCDataChannelState.Open || _control.ReadyState != RTCDataChannelState.Open)
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("WebRTC connection timed out. Check the network.");
                await Task.Delay(100, token);
            }
            token.ThrowIfCancellationRequested();
            // Reactor starts received tracks paused. Match the SDK's local direction
            // update and explicit control-channel resume before expecting media.
            bool needsResume = mappings.Exists(entry => entry.transceiver.CurrentDirection != RTCRtpTransceiverDirection.RecvOnly);
            if (needsResume)
            {
                var resumeOffer = new RTCSessionDescription { type = RTCSdpType.Offer, sdp = MediaDirection(_peer.LocalDescription.sdp, "recvonly") };
                var resumeAnswer = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = MediaDirection(_peer.RemoteDescription.sdp, "sendonly") };
                var setOffer = _peer.SetLocalDescription(ref resumeOffer);
                while (!setOffer.IsDone) { token.ThrowIfCancellationRequested(); await Task.Yield(); }
                if (setOffer.IsError) throw new InvalidOperationException("Could not resume local media tracks.");
                var setAnswer = _peer.SetRemoteDescription(ref resumeAnswer);
                while (!setAnswer.IsDone) { token.ThrowIfCancellationRequested(); await Task.Yield(); }
                if (setAnswer.IsError) throw new InvalidOperationException("Could not resume remote media tracks.");
            }
            foreach (var entry in mappings)
                _control.Send(new JObject { ["type"] = "notification", ["event"] = "resume_track",
                    ["data"] = new JObject { ["name"] = entry.track["name"] } }.ToString(Formatting.None));
            SessionReady = true;
            SetStatus("Ready");
            if (!string.IsNullOrWhiteSpace(resolution)) SetResolution(resolution);
            SetAudioEnabled(generateAudio);
            if (autoStart)
            {
                if (startImage != null) SetImage(startImage);
                SetPrompt(startPrompt);
                StartGeneration();
            }
        }

        static string MediaDirection(string sdp, string direction)
        {
            var sections = Regex.Split(sdp, @"(?m)(?=^m=)");
            for (int i = 0; i < sections.Length; i++)
                if (sections[i].StartsWith("m=video ") || sections[i].StartsWith("m=audio "))
                    sections[i] = Regex.Replace(sections[i], @"(?m)^a=(sendonly|recvonly|sendrecv|inactive)(?=\r?$)", "a=" + direction);
            return string.Concat(sections);
        }

        // The server validates these version fields; wire format follows official JS SDK 2.12.4.
        static JObject ClientInfo() => new JObject { ["sdk_type"] = "js", ["sdk_version"] = "2.12.4" };
        string SessionPath => "/sessions/" + Uri.EscapeDataString(_session);

        async Task<JObject> Request(string method, string path, JObject body, CancellationToken token,
            string key = null, bool finishOnCancel = false)
        {
            token.ThrowIfCancellationRequested();
            var headers = new Dictionary<string, string> {
                ["Reactor-API-Version"] = "1", ["Reactor-API-Accept-Version"] = "1",
                ["Reactor-WebRTC-Version"] = "1.0"
            };
            if (body != null) headers["Content-Type"] = "application/json";
            if (key != null) headers["Reactor-API-Key"] = key.Trim();
            else headers["Authorization"] = "Bearer " + _jwt;
            var reply = await ReactorHttps.Send(method, Api + path, headers,
                body == null ? null : Encoding.UTF8.GetBytes(body.ToString(Formatting.None)),
                finishOnCancel ? CancellationToken.None : token);
            if (!string.IsNullOrEmpty(reply.Error))
                throw new InvalidOperationException(method + " " + path + ": " + reply.Error);
            if (reply.Status < 200 || reply.Status >= 300)
                throw new InvalidOperationException(method + " " + path + " failed (HTTP " + reply.Status + "). Check API key, credits and model access.");
            return string.IsNullOrWhiteSpace(reply.Body) ? new JObject() : JObject.Parse(reply.Body);
        }

        void Update()
        {
            if (_statusText != null) _statusText.text = Status;
            if (_lifetime == null || _lifetime.IsCancellationRequested) return;
            if (_data?.ReadyState == RTCDataChannelState.Open && Time.unscaledTime >= _nextPing) {
                _nextPing = Time.unscaledTime + 5;
                SendNow("ping", new JObject(), "runtime");
            }
            if (_iceSignalingReady && _connectionPath != null && !_sendingIce && Time.unscaledTime >= _nextIce &&
                (_candidates.Count > 0 || (!_iceFinalSent && _peer.GatheringState == RTCIceGatheringState.Complete))) {
                _nextIce = Time.unscaledTime + .1f;
                FlushIce(_lifetime.Token);
            }
        }

        async void FlushIce(CancellationToken token)
        {
            _sendingIce = true;
            try {
                bool final = _peer.GatheringState == RTCIceGatheringState.Complete;
                var batch = new JArray(_candidates); _candidates.Clear();
                await Request("POST", _connectionPath + "/ice_candidates", new JObject {
                    ["candidates"] = batch, ["is_final"] = final, ["client_info"] = ClientInfo()
                }, token);
                _iceFinalSent = final;
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { SetStatus(SafeError(e)); await DisconnectCore(false); }
            finally { _sendingIce = false; }
        }

        void OnTrack(RTCTrackEvent e)
        {
            _tracks.Add(e.Track);
            if (e.Track is VideoStreamTrack video) video.OnVideoReceived += texture => {
                VideoTexture = texture;
                if (_material == null) return;
                _material.mainTexture = texture;
                if (_material.HasProperty("_BaseMap")) _material.SetTexture("_BaseMap", texture);
                if (_createdScreen) targetRenderer.transform.localScale = new Vector3(screenWidth, screenWidth * texture.height / texture.width, 1);
            };
            if (playAudio && e.Track is AudioStreamTrack audio) { _audio.SetTrack(audio); _audio.Play(); }
        }

        // A texture is allocated once per track, even before decoded video arrives. Poll receiver
        // stats instead of treating OnVideoReceived/texture existence as a new-shot notification.
        System.Collections.IEnumerator ObserveVideoFrames()
        {
            while (true)
            {
                if (SessionReady && _peer != null)
                {
                    var peer = _peer;
                    var op = peer.GetStats();
                    yield return op;
                    if (!op.IsError)
                    {
                        using (var report = op.Value)
                        {
                            if (peer == _peer && SessionReady)
                            {
                                ulong frames = 0;
                                foreach (var stats in report.Stats.Values)
                                    if (stats is RTCInboundRTPStreamStats inbound && inbound.kind == "video")
                                        frames += inbound.framesDecoded;
                                if (frames > DecodedVideoFrames) LastVideoFrameTime = Time.realtimeSinceStartup;
                                DecodedVideoFrames = frames;
                            }
                        }
                    }
                }
                yield return new WaitForSecondsRealtime(.2f);
            }
        }

        void OnMessage(byte[] bytes)
        {
            try {
                var envelope = JObject.Parse(Encoding.UTF8.GetString(bytes));
                var message = envelope["scope"] != null ? envelope["data"] : envelope;
                if ((string)envelope["scope"] == "runtime") return;
                string type = (string)message["type"];
                var data = message["data"];
                if (type == "state") SetStatus((bool?)data?["running"] == true ? "Generating" : (bool?)data?["paused"] == true ? "Paused" : "Ready / idle");
                if (type == "image_accepted") _imageAccepted = true;
                if (type == "generation_complete") SetStatus("Finished — press Start for another run");
                if (type == "command_error") { _commandError = true; SetStatus("Command rejected: " + (string)data?["command"]); }
                MessageReceived?.Invoke(type, message.ToString(Formatting.None));
            }
            catch (JsonException) { SetStatus("Received an invalid server message"); }
        }

        bool _commandError;
        void Queue(Func<CancellationToken, Task> work)
        {
            if (!SessionReady || _lifetime == null) { SetStatus("Not connected yet"); return; }
            _commands = RunQueued(_commands, work, _lifetime.Token);
        }
        async Task RunQueued(Task previous, Func<CancellationToken, Task> work, CancellationToken token)
        {
            try { await previous; token.ThrowIfCancellationRequested(); await work(token); }
            catch (OperationCanceledException) { }
            catch (Exception e) { SetStatus(SafeError(e)); _lifetime?.Cancel(); await DisconnectCore(false); }
        }
        void SendNow(string command, JObject data, string scope = "application", JObject uploads = null)
        {
            if (_data?.ReadyState != RTCDataChannelState.Open) throw new InvalidOperationException("Data channel is closed.");
            var inner = new JObject { ["type"] = command, ["data"] = data };
            if (uploads != null) inner["uploads"] = uploads;
            _data.Send(new JObject { ["scope"] = scope, ["data"] = inner }.ToString(Formatting.None));
        }
        public void SendCommand(string command, string dataJson = "{}") => Queue(token => { SendNow(command, JObject.Parse(dataJson)); return Task.CompletedTask; });
        public void SetPrompt(string prompt) => SendCommand("set_prompt", new JObject { ["prompt"] = prompt }.ToString());
        public void SetAudioPrompt(string prompt) => SendCommand("set_audio_prompt", new JObject { ["prompt"] = prompt }.ToString());
        public void StartGeneration() => SendCommand("start");
        public void PauseGeneration() => SendCommand("pause");
        public void ResumeGeneration() => SendCommand("resume");
        public void ResetGeneration() => SendCommand("reset");
        public void SetSeed(int seed) => SendCommand("set_seed", new JObject { ["seed"] = Mathf.Max(0, seed) }.ToString());
        public void SetResolution(string value) => SendCommand("set_resolution", new JObject { ["resolution"] = value }.ToString());
        public void SetAudioEnabled(bool value) => SendCommand("set_audio_enabled", new JObject { ["audio_enabled"] = value }.ToString());
        public void SetImage(Texture2D image)
        {
            if (image == null) return;
            var rt = RenderTexture.GetTemporary(image.width, image.height);
            var previous = RenderTexture.active;
            Texture2D readable = null;
            try {
                Graphics.Blit(image, rt); RenderTexture.active = rt;
                readable = new Texture2D(image.width, image.height, TextureFormat.RGB24, false);
                readable.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); readable.Apply();
                SetImageBytes(readable.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); if (readable != null) Destroy(readable); }
        }
        public void SetImageBytes(byte[] image) => Queue(async token => {
            bool png = image.Length > 8 && image[0] == 137 && image[1] == 80;
            string mime = png ? "image/png" : "image/jpeg", name = png ? "start.png" : "start.jpg";
            var slot = await Request("POST", SessionPath + "/uploads", new JObject { ["name"] = name, ["size"] = image.Length, ["mime_type"] = mime }, token);
            var uploaded = await ReactorHttps.Send("PUT", (string)slot["presigned_url"],
                new Dictionary<string, string>(), image, token);
            if (!string.IsNullOrEmpty(uploaded.Error) || uploaded.Status < 200 || uploaded.Status >= 300)
                throw new InvalidOperationException("Image upload failed (HTTP " + uploaded.Status + ").");
            var file = new JObject { ["upload_id"] = slot["presigned_id"], ["name"] = name, ["mime_type"] = mime, ["size"] = image.Length };
            SendNow("fileUploaded", file, "runtime");
            _imageAccepted = false; _commandError = false;
            SendNow("set_image", new JObject(), uploads: new JObject { ["image"] = file.DeepClone() });
            // Do not start prompt-only generation while the opening image is still decoding.
            for (int i = 0; i < 300 && !_imageAccepted && !_commandError; i++) await Task.Delay(100, token);
            if (!_imageAccepted) throw new InvalidOperationException("Opening image was not accepted. Reconnect and try text-only or another image.");
        });

        public async void Disconnect() { await DisconnectCore(true); }
        public Task DisconnectAsync() => DisconnectCore(true);
        Task _disconnectTask;
        Task DisconnectCore(bool showStatus)
        {
            if (_disconnectTask != null && !_disconnectTask.IsCompleted) return _disconnectTask;
            return _disconnectTask = DisconnectSession(showStatus);
        }
        async Task DisconnectSession(bool showStatus)
        {
            var lifetime = _lifetime;
            if (lifetime == null || _disconnecting) return;
            _disconnecting = true;
            lifetime.Cancel();
            SessionReady = false;
            // Await session allocation before deleting, including cancellation during POST /sessions.
            if (_connectionTask != null && !_connectionTask.IsCompleted) {
                try { await _connectionTask; } catch { }
            }
            string sessionPath = _session == null ? null : SessionPath;
            _session = null;
            _connectionPath = null;
            _data?.Close(); _control?.Close(); _peer?.Close();
            if (_audio != null) _audio.Stop();
            foreach (var track in _tracks) track.Dispose();
            _tracks.Clear();
            _data?.Dispose(); _control?.Dispose(); _peer?.Dispose();
            _data = _control = null; _peer = null; VideoTexture = null;
            DecodedVideoFrames = 0;
            LastVideoFrameTime = float.NegativeInfinity;
            _candidates.Clear(); _iceFinalSent = false; _iceSignalingReady = false;
            bool terminated = true;
            try { if (sessionPath != null) await Request("DELETE", sessionPath, new JObject { ["reason"] = "Unity client disconnected" }, CancellationToken.None); }
            catch { terminated = false; SetStatus("Disconnected; session cleanup could not be confirmed. Check Reactor sessions."); }
            _jwt = null; _lifetime = null; _disconnecting = false; lifetime.Dispose();
            if (showStatus && terminated) SetStatus("Disconnected");
        }
        bool _disconnecting;
        void OnApplicationPause(bool paused) { if (paused) Disconnect(); }
        void OnDisable() { Disconnect(); }
        void OnDestroy()
        {
            Disconnect();
            if (_material != null) Destroy(_material);
            if (_createdScreen && targetRenderer != null) Destroy(targetRenderer.gameObject);
            else if (targetRenderer != null && !externalScreen) targetRenderer.sharedMaterial = _previousMaterial;
            if (_statusText != null) Destroy(_statusText.gameObject);
        }

        void SetStatus(string status) { Status = status; }
        static string SafeError(Exception error) => error is InvalidOperationException || error is TimeoutException
            ? error.Message : "Unexpected connection error (" + error.GetType().Name + ")";
        void SetUpScreen()
        {
            if (externalScreen) {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.spatialBlend = externalScreen ? 0f : spatialBlend;
                _audio.panStereo = 0f; _audio.spatialize = false; _audio.dopplerLevel = 0f;
                _audio.loop = true; _audio.playOnAwake = false;
                return;
            }
            if (targetRenderer == null) {
                var screen = GameObject.CreatePrimitive(PrimitiveType.Quad); screen.name = "Visko Screen";
                Destroy(screen.GetComponent<Collider>());
                var camera = Camera.main;
                Vector3 origin = camera != null ? camera.transform.position : Vector3.up * 1.6f;
                Vector3 forward = camera != null ? Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized : Vector3.forward;
                if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
                screen.transform.SetPositionAndRotation(origin + forward * screenDistance + Vector3.up * screenHeightOffset, Quaternion.LookRotation(forward));
                screen.transform.localScale = new Vector3(screenWidth, screenWidth * 480f / 832, 1);
                targetRenderer = screen.GetComponent<Renderer>(); _createdScreen = true;
            }
            _previousMaterial = targetRenderer.sharedMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            _material = screenMaterial != null ? new Material(screenMaterial) : new Material(shader); targetRenderer.sharedMaterial = _material;
            _audio = targetRenderer.gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = externalScreen ? 0f : spatialBlend;
                _audio.panStereo = 0f; _audio.spatialize = false; _audio.dopplerLevel = 0f;
                _audio.loop = true; _audio.playOnAwake = false;
            if (showWorldStatus) {
                var label = new GameObject("Visko Status"); _statusText = label.AddComponent<TextMesh>();
                label.transform.SetPositionAndRotation(targetRenderer.transform.position + Vector3.up * (screenWidth * .32f), targetRenderer.transform.rotation);
                _statusText.anchor = TextAnchor.MiddleCenter; _statusText.fontSize = 40; _statusText.characterSize = .025f;
            }
        }
        void OnGUI()
        {
#if UNITY_EDITOR
            return; // Editor uses a screen-space UI Toolkit panel, independent of XR rendering.
#else
            if (!showDebugGui) return;
            GUILayout.BeginArea(new Rect(10, 10, 460, 330), GUI.skin.box);
            GUILayout.Label("Visko direct: " + Status);
            _debugPrompt = GUILayout.TextArea(_debugPrompt ?? startPrompt, GUILayout.Height(100));
            GUI.enabled = SessionReady;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Steer")) SetPrompt(_debugPrompt);
            if (GUILayout.Button("Start")) { SetPrompt(_debugPrompt); StartGeneration(); }
            if (GUILayout.Button("Pause")) PauseGeneration();
            if (GUILayout.Button("Resume")) ResumeGeneration();
            if (GUILayout.Button("Reset")) ResetGeneration();
            GUILayout.EndHorizontal(); GUI.enabled = true;
            if (_lifetime == null) { if (GUILayout.Button("Connect")) _ = Connect(); }
            else if (GUILayout.Button("Disconnect")) Disconnect();
            GUILayout.EndArea();
#endif
        }
    }
}
