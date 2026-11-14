using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Visko
{
    /// <summary>
    /// Streams Reactor's Visko Orbis model into the scene through the local Python bridge
    /// (ReactorBridge/visko_bridge.py). Shows the video on a world-space screen, plays the
    /// generated audio from that screen, and exposes the model's commands as public methods
    /// so XR UI, voice input, or game events can steer the scene.
    /// </summary>
    public class ViskoStreamClient : MonoBehaviour
    {
        [Header("Bridge")]
        public string bridgeHost = "127.0.0.1";
        public int bridgePort = 7777;

        [Header("Generation")]
        [Tooltip("Sent automatically with Start once the Reactor session is ready.")]
        [TextArea(3, 8)]
        public string startPrompt =
            "A vast lunar landscape seen from just above the grey dusty surface, the blue Earth rising slowly over the horizon, stars sharp in a black sky. Slow cinematic forward drift, a single unbroken take.";
        [Tooltip("Optional opening frame. Best at 16:9; other aspects get squashed to 832x480.")]
        public Texture2D startImage;
        public bool autoStart = true;
        [Tooltip("Optional delivery resolution: 1080p, 2k or 4k. Leave empty for the model default. Higher means more bandwidth and texture upload per frame.")]
        public string resolution = "";
        public bool generateAudio = true;

        [Header("Screen")]
        [Tooltip("Renderer to show the video on. If empty, a screen quad is created in front of the main camera.")]
        public Renderer targetRenderer;
        public float screenDistance = 3f;
        public float screenWidth = 3.2f;
        public float screenHeightOffset = 0f;

        [Header("Audio")]
        public bool playAudio = true;
        [Range(0f, 1f)] public float spatialBlend = 1f;

        [Header("Debug")]
        [Tooltip("Prompt box and buttons in the Game view (desktop only; not visible in a headset).")]
        public bool showDebugGui = true;

        /// <summary>Every model or bridge event: (type, raw JSON). E.g. state, chunk_complete, generation_complete, command_error.</summary>
        public event Action<string, string> MessageReceived;

        public bool BridgeConnected => _socketConnected;
        public bool SessionReady { get; private set; }
        public Texture2D VideoTexture => _texture;

        const byte MsgVideo = 1, MsgAudio = 2, MsgJson = 3, MsgImage = 4;
        const int AudioSampleRate = 48000;
        const int AudioBufferSeconds = 4;

        // Networking
        Thread _thread;
        volatile bool _running;
        volatile bool _socketConnected;
        TcpClient _client;
        NetworkStream _stream;
        readonly object _sendLock = new object();
        readonly ConcurrentQueue<string> _messages = new ConcurrentQueue<string>();

        // Video: the network thread fills _backBuffer then swaps it with _frontBuffer.
        readonly object _videoLock = new object();
        byte[] _frontBuffer = new byte[0], _backBuffer = new byte[0];
        int _frameWidth, _frameHeight;
        bool _frameReady;
        Texture2D _texture;
        Material _material;

        // Audio ring buffer (mono float), filled by the network thread and drained by the audio thread.
        readonly object _audioLock = new object();
        readonly float[] _ring = new float[AudioSampleRate * AudioBufferSeconds];
        int _ringRead, _ringCount;
        bool _warnedSampleRate;
        AudioSource _audioSource;

        string _debugPrompt;
        string _status = "waiting for bridge";
        int _lastChunk = -1;

        void Start()
        {
            _debugPrompt = startPrompt;
            SetUpScreen();
            if (playAudio) SetUpAudio();
            _running = true;
            _thread = new Thread(NetworkLoop) { IsBackground = true, Name = "ViskoBridge" };
            _thread.Start();
        }

        void OnDestroy()
        {
            _running = false;
            CloseSocket();
            if (_thread != null && _thread.IsAlive) _thread.Join(500);
            if (_texture != null) Destroy(_texture);
        }

        // ------------------------------------------------------------------ public API

        public void SetPrompt(string prompt) => SendCommand("set_prompt", "{\"prompt\":" + JsonString(prompt) + "}");
        public void SetAudioPrompt(string prompt) => SendCommand("set_audio_prompt", "{\"prompt\":" + JsonString(prompt) + "}");
        public void StartGeneration() => SendCommand("start");
        public void PauseGeneration() => SendCommand("pause");
        public void ResumeGeneration() => SendCommand("resume");
        /// <summary>Stops the run and clears the prompt and image. Call SetPrompt + StartGeneration to go again.</summary>
        public void ResetGeneration() => SendCommand("reset");
        public void SetSeed(int seed) => SendCommand("set_seed", "{\"seed\":" + Mathf.Max(0, seed) + "}");
        public void SetResolution(string res) => SendCommand("set_resolution", "{\"resolution\":" + JsonString(res) + "}");
        public void SetAudioEnabled(bool on) => SendCommand("set_audio_enabled", "{\"audio_enabled\":" + (on ? "true" : "false") + "}");

        /// <summary>Opening frame for the next start. Works with non-readable textures too.</summary>
        public void SetImage(Texture2D image)
        {
            if (image == null) return;
            SetImageBytes(EncodeToPng(image));
        }

        /// <summary>PNG or JPG bytes for the opening frame. Only takes effect at the next start (after a reset if already running).</summary>
        public void SetImageBytes(byte[] encodedImage) => Send(MsgImage, encodedImage);

        /// <summary>Send any model command by name. dataJson is a JSON object, e.g. {"seed":7}.</summary>
        public void SendCommand(string command, string dataJson = "{}")
        {
            string json = "{\"command\":" + JsonString(command) + ",\"data\":" + (string.IsNullOrEmpty(dataJson) ? "{}" : dataJson) + "}";
            Send(MsgJson, Encoding.UTF8.GetBytes(json));
        }

        // ------------------------------------------------------------------ main thread

        void Update()
        {
            while (_messages.TryDequeue(out var json)) HandleMessage(json);

            lock (_videoLock)
            {
                if (_frameReady)
                {
                    _frameReady = false;
                    if (_texture == null || _texture.width != _frameWidth || _texture.height != _frameHeight)
                        CreateTexture(_frameWidth, _frameHeight);
                    _texture.LoadRawTextureData(_frontBuffer);
                    _texture.Apply(false);
                }
            }
        }

        void HandleMessage(string json)
        {
            var header = JsonUtility.FromJson<MessageHeader>(json);
            string type = header?.type ?? "";

            switch (type)
            {
                case "bridge_status":
                {
                    var s = JsonUtility.FromJson<StatusMessage>(json);
                    string status = s?.data?.status ?? "";
                    _status = "session " + status;
                    bool wasReady = SessionReady;
                    SessionReady = status == "ready";
                    if (SessionReady && !wasReady) OnSessionReady();
                    break;
                }
                case "state":
                {
                    var s = JsonUtility.FromJson<StateMessage>(json);
                    if (s?.data != null)
                        _status = s.data.running ? (s.data.paused ? "paused" : $"generating (chunk {s.data.current_chunk})")
                                                 : "ready, idle";
                    break;
                }
                case "chunk_complete":
                {
                    var c = JsonUtility.FromJson<ChunkMessage>(json);
                    if (c?.data != null) _lastChunk = c.data.chunk_index;
                    break;
                }
                case "generation_complete":
                    _status = "generation complete (call StartGeneration to run again)";
                    break;
                case "command_error":
                case "bridge_error":
                    Debug.LogWarning("[Visko] " + json);
                    break;
            }

            MessageReceived?.Invoke(type, json);
        }

        void OnSessionReady()
        {
            Debug.Log("[Visko] Reactor session ready");
            if (!string.IsNullOrEmpty(resolution)) SetResolution(resolution);
            if (!generateAudio) SetAudioEnabled(false);
            if (!autoStart) return;
            if (startImage != null) SetImage(startImage);
            SetPrompt(startPrompt);
            StartGeneration();
        }

        void CreateTexture(int w, int h)
        {
            if (_texture != null) Destroy(_texture);
            _texture = new Texture2D(w, h, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, name = "ViskoVideo" };
            _material.mainTexture = _texture;
            if (_material.HasProperty("_BaseMap")) _material.SetTexture("_BaseMap", _texture);

            // Keep the screen's aspect matched to the stream.
            if (_createdScreen)
            {
                var t = targetRenderer.transform;
                t.localScale = new Vector3(screenWidth, screenWidth * h / w, 1f);
            }
        }

        bool _createdScreen;

        void SetUpScreen()
        {
            if (targetRenderer == null)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Visko Screen";
                Destroy(quad.GetComponent<Collider>());
                var cam = Camera.main;
                Vector3 origin = cam != null ? cam.transform.position : Vector3.up * 1.6f;
                Vector3 forward = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
                quad.transform.position = origin + forward * screenDistance + Vector3.up * screenHeightOffset;
                quad.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
                quad.transform.localScale = new Vector3(screenWidth, screenWidth * 480f / 832f, 1f);
                targetRenderer = quad.GetComponent<Renderer>();
                _createdScreen = true;

                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
                targetRenderer.material = new Material(shader);
            }
            _material = targetRenderer.material;
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", Color.white);
            else _material.color = Color.white;
        }

        void SetUpAudio()
        {
            var host = targetRenderer != null ? targetRenderer.gameObject : gameObject;
            if (!host.TryGetComponent(out _audioSource)) _audioSource = host.AddComponent<AudioSource>();
            _audioSource.spatialBlend = spatialBlend;
            _audioSource.loop = true;
            _audioSource.playOnAwake = false;
            // Streaming clip: Unity pulls samples from the ring buffer and resamples to the output rate.
            _audioSource.clip = AudioClip.Create("ViskoAudio", AudioSampleRate, 1, AudioSampleRate, true, ReadAudio);
            _audioSource.Play();
        }

        // Audio thread.
        void ReadAudio(float[] data)
        {
            lock (_audioLock)
            {
                int n = Mathf.Min(data.Length, _ringCount);
                for (int i = 0; i < n; i++)
                {
                    data[i] = _ring[_ringRead];
                    _ringRead = (_ringRead + 1) % _ring.Length;
                }
                _ringCount -= n;
                for (int i = n; i < data.Length; i++) data[i] = 0f;
            }
        }

        // ------------------------------------------------------------------ network thread

        void NetworkLoop()
        {
            var header = new byte[5];
            while (_running)
            {
                try
                {
                    _client = new TcpClient { NoDelay = true, ReceiveBufferSize = 8 * 1024 * 1024 };
                    _client.Connect(bridgeHost, bridgePort);
                    _stream = _client.GetStream();
                    _socketConnected = true;
                    _messages.Enqueue("{\"type\":\"bridge_status\",\"data\":{\"status\":\"bridge connected\"}}");

                    while (_running)
                    {
                        ReadExactly(_stream, header, 5);
                        byte type = header[0];
                        int length = BitConverter.ToInt32(header, 1);
                        switch (type)
                        {
                            case MsgVideo: ReadVideo(length); break;
                            case MsgAudio: ReadAudioPacket(length); break;
                            case MsgJson:
                            {
                                var buf = new byte[length];
                                ReadExactly(_stream, buf, length);
                                _messages.Enqueue(Encoding.UTF8.GetString(buf));
                                break;
                            }
                            default:
                                Skip(length);
                                break;
                        }
                    }
                }
                catch (Exception e) when (e is SocketException || e is IOException || e is ObjectDisposedException || e is InvalidOperationException)
                {
                    // Bridge not running yet, or it went away. Retry below.
                }
                finally
                {
                    bool was = _socketConnected;
                    _socketConnected = false;
                    CloseSocket();
                    if (was) _messages.Enqueue("{\"type\":\"bridge_status\",\"data\":{\"status\":\"bridge disconnected\"}}");
                }
                for (int i = 0; i < 20 && _running; i++) Thread.Sleep(100);
            }
        }

        void ReadVideo(int length)
        {
            var dims = new byte[4];
            ReadExactly(_stream, dims, 4);
            int w = BitConverter.ToUInt16(dims, 0), h = BitConverter.ToUInt16(dims, 2);
            int size = length - 4;
            if (size != w * h * 3) { Skip(size); return; }
            if (_backBuffer.Length != size) _backBuffer = new byte[size];
            ReadExactly(_stream, _backBuffer, size);
            lock (_videoLock)
            {
                (_frontBuffer, _backBuffer) = (_backBuffer, _frontBuffer);
                _frameWidth = w;
                _frameHeight = h;
                _frameReady = true;
            }
        }

        void ReadAudioPacket(int length)
        {
            var buf = new byte[length];
            ReadExactly(_stream, buf, length);
            if (!playAudio) return;
            int rate = BitConverter.ToInt32(buf, 0);
            int channels = Math.Max(1, (int)BitConverter.ToUInt16(buf, 4));
            if (rate != AudioSampleRate && !_warnedSampleRate)
            {
                _warnedSampleRate = true;
                Debug.LogWarning($"[Visko] Audio arrives at {rate} Hz, expected {AudioSampleRate}; pitch will be off.");
            }
            int frames = (length - 6) / 2 / channels;
            lock (_audioLock)
            {
                for (int f = 0; f < frames; f++)
                {
                    // Downmix to mono.
                    float sum = 0f;
                    for (int c = 0; c < channels; c++)
                        sum += BitConverter.ToInt16(buf, 6 + (f * channels + c) * 2) / 32768f;
                    if (_ringCount == _ring.Length)
                    {
                        _ringRead = (_ringRead + 1) % _ring.Length; // full: drop the oldest sample
                        _ringCount--;
                    }
                    _ring[(_ringRead + _ringCount) % _ring.Length] = sum / channels;
                    _ringCount++;
                }
            }
        }

        void Skip(int length)
        {
            var buf = new byte[Math.Min(length, 65536)];
            while (length > 0)
            {
                int n = Math.Min(length, buf.Length);
                ReadExactly(_stream, buf, n);
                length -= n;
            }
        }

        static void ReadExactly(NetworkStream s, byte[] buf, int count)
        {
            int off = 0;
            while (off < count)
            {
                int n = s.Read(buf, off, count - off);
                if (n <= 0) throw new IOException("bridge closed the connection");
                off += n;
            }
        }

        void Send(byte type, byte[] payload)
        {
            if (!_socketConnected || _stream == null)
            {
                Debug.LogWarning("[Visko] Not connected to the bridge; is visko_bridge.py running?");
                return;
            }
            var header = new byte[5];
            header[0] = type;
            BitConverter.GetBytes(payload.Length).CopyTo(header, 1);
            try
            {
                lock (_sendLock)
                {
                    _stream.Write(header, 0, 5);
                    _stream.Write(payload, 0, payload.Length);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Visko] Send failed: " + e.Message);
            }
        }

        void CloseSocket()
        {
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
            _stream = null;
            _client = null;
        }

        // ------------------------------------------------------------------ helpers

        static byte[] EncodeToPng(Texture2D source)
        {
            if (source.isReadable) return source.EncodeToPNG();
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var png = copy.EncodeToPNG();
            Destroy(copy);
            return png;
        }

        static string JsonString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char ch in s ?? "")
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        void OnGUI()
        {
            if (!showDebugGui) return;
            GUILayout.BeginArea(new Rect(10, 10, 440, 260), GUI.skin.box);
            GUILayout.Label($"Visko: {(BridgeConnected ? _status : "bridge not running (start visko_bridge.py)")}" +
                            (_lastChunk >= 0 ? $"  | last chunk {_lastChunk}" : ""));
            _debugPrompt = GUILayout.TextArea(_debugPrompt, GUILayout.Height(110));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Steer (set_prompt)")) SetPrompt(_debugPrompt);
            if (GUILayout.Button("Start")) StartGeneration();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Pause")) PauseGeneration();
            if (GUILayout.Button("Resume")) ResumeGeneration();
            if (GUILayout.Button("Reset")) ResetGeneration();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // JsonUtility DTOs (fields are filled by reflection)
#pragma warning disable 0649
        [Serializable] class MessageHeader { public string type; }
        [Serializable] class StatusData { public string status; }
        [Serializable] class StatusMessage { public string type; public StatusData data; }
        [Serializable] class StateData { public bool started, running, paused, has_image, has_prompt; public int current_chunk; public string resolution; }
        [Serializable] class StateMessage { public string type; public StateData data; }
        [Serializable] class ChunkData { public int chunk_index, frames_emitted; }
        [Serializable] class ChunkMessage { public string type; public ChunkData data; }
#pragma warning restore 0649
    }
}
