using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Visko
{
    /// <summary>Native HTTPS for TLS 1.3-only Reactor endpoints. Never disables certificate validation.</summary>
    public static class ReactorHttps
    {
        public sealed class Reply { public long Status; public string Body; public string Error; }
        public static async Task<Reply> Send(string method, string url, Dictionary<string, string> headers,
            byte[] body, CancellationToken token)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "https")
                throw new InvalidOperationException("HTTPS is required.");
            token.ThrowIfCancellationRequested();
#if UNITY_EDITOR_OSX
            // The macOS system curl uses TLS 1.3 and the normal trust store. Secrets travel
            // over stdin, never process arguments or a saved config. This is Editor-only.
            return await Task.Run(async () => {
                string payloadFile = null;
                try {
                    var config = new StringBuilder();
                    config.AppendLine("url = " + Quote(url));
                    config.AppendLine("request = " + Quote(method));
                    foreach (var h in headers) config.AppendLine("header = " + Quote(h.Key + ": " + h.Value));
                    if (body != null) {
                        payloadFile = Path.GetTempFileName(); File.WriteAllBytes(payloadFile, body);
                        config.AppendLine("data-binary = " + Quote("@" + payloadFile));
                    }
                    using (var process = new System.Diagnostics.Process()) {
                        process.StartInfo = new System.Diagnostics.ProcessStartInfo("/usr/bin/curl",
                            "--silent --show-error --proto =https --connect-timeout 15 --max-time 30 --write-out __REACTOR_HTTP__%{http_code} --config -") {
                            UseShellExecute = false, RedirectStandardInput = true,
                            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
                        };
                        process.Start();
                        using (token.Register(() => { try { if (!process.HasExited) process.Kill(); } catch { } })) {
                            var stdout = process.StandardOutput.ReadToEndAsync();
                            var stderr = process.StandardError.ReadToEndAsync();
                            await process.StandardInput.WriteAsync(config.ToString()); process.StandardInput.Close();
                            string output = await stdout; await stderr;
                            process.WaitForExit(); token.ThrowIfCancellationRequested();
                            if (process.ExitCode != 0) return new Reply { Error = "macOS HTTPS failed (curl " + process.ExitCode + ")" };
                            int split = output.LastIndexOf("__REACTOR_HTTP__", StringComparison.Ordinal);
                            long status;
                            if (split < 0 || !long.TryParse(output.Substring(split + "__REACTOR_HTTP__".Length), out status))
                                return new Reply { Error = "Invalid HTTPS response" };
                            return new Reply { Status = status, Body = output.Substring(0, split) };
                        }
                    }
                } finally { if (payloadFile != null) File.Delete(payloadFile); }
            }, token);
#elif UNITY_ANDROID && !UNITY_EDITOR
            // Run blocking Java I/O off Unity's render thread. Android retains its normal
            // TLS, hostname and certificate checks. Timeouts bound in-flight requests.
            string headerJson = JsonConvert.SerializeObject(headers);
            return await Task.Run(() => {
                token.ThrowIfCancellationRequested();
                AndroidJNI.AttachCurrentThread();
                try {
                    using (var bridge = new AndroidJavaClass("com.takemetothemoon.visko.ReactorHttps")) {
                        string json = bridge.CallStatic<string>("request", method, url, headerJson, body ?? new byte[0]);
                        var result = JObject.Parse(json);
                        return new Reply { Status = (long?)result["status"] ?? 0, Body = (string)result["body"] ?? "", Error = (string)result["error"] };
                    }
                } finally { AndroidJNI.DetachCurrentThread(); }
            }, token);
#else
            using (var request = new UnityWebRequest(url, method)) {
                request.downloadHandler = new DownloadHandlerBuffer(); request.timeout = 30;
                if (body != null) request.uploadHandler = new UploadHandlerRaw(body);
                foreach (var h in headers) request.SetRequestHeader(h.Key, h.Value);
                var op = request.SendWebRequest();
                while (!op.isDone) {
                    if (token.IsCancellationRequested) { request.Abort(); token.ThrowIfCancellationRequested(); }
                    await Task.Yield();
                }
                return new Reply { Status = request.responseCode, Body = request.downloadHandler.text,
                    Error = request.result == UnityWebRequest.Result.ConnectionError ? request.error : null };
            }
#endif
        }
#if UNITY_EDITOR_OSX
        static string Quote(string value)
        {
            if (value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0) throw new InvalidOperationException("Invalid HTTP parameter.");
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
#endif
    }
}
