using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace H.ContentsLoader.Editor
{
    // 플레이 모드에서 로컬 Remote 출력 폴더를 HTTP로 내보냄. 실제 CDN처럼 UnityWebRequest, Range 이어받기, 속도 제한을 거침
    internal sealed class EditorContentServer : IDisposable
    {
        private const int Chunk = 16 * 1024;

        private static EditorContentServer current;

        private readonly HttpListener listener = new();
        private readonly string root;
        private readonly long bytesPerSecond;

        private EditorContentServer(string root, long bytesPerSecond)
        {
            this.root = Path.GetFullPath(root);
            this.bytesPerSecond = bytesPerSecond;

            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            listener.Prefixes.Add($"http://localhost:{Port}/");
            listener.Start();
            new Thread(Serve) { IsBackground = true, Name = "Contents Loader Server" }.Start();
        }

        public int Port { get; }
        public string Url => $"http://localhost:{Port}";

        // 같은 폴더와 속도면 띄워 둔 서버를 그대로 씀
        public static EditorContentServer Start(string root, long bytesPerSecond)
        {
            if (current != null && current.root == Path.GetFullPath(root) && current.bytesPerSecond == bytesPerSecond) return current;
            current?.Dispose();
            current = new EditorContentServer(root, bytesPerSecond);
            return current;
        }

        public static void StopCurrent()
        {
            current?.Dispose();
            current = null;
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            AssemblyReloadEvents.beforeAssemblyReload += StopCurrent;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) StopCurrent();
            };
        }

        private void Serve()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = listener.GetContext();
                }
                catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }
                ThreadPool.QueueUserWorkItem(_ => Respond(context));
            }
        }

        private void Respond(HttpListenerContext context)
        {
            var response = context.Response;
            try
            {
                var path = Path.Combine(root, Path.GetFileName(Uri.UnescapeDataString(context.Request.Url.AbsolutePath)));
                if (!File.Exists(path))
                {
                    response.StatusCode = 404;
                    response.Close();
                    return;
                }

                using var file = File.OpenRead(path);
                long start = 0;
                var range = context.Request.Headers["Range"];
                if (range != null && range.StartsWith("bytes=", StringComparison.Ordinal)
                                  && long.TryParse(range.Substring(6).Split('-')[0], out var from) && from < file.Length)
                {
                    start = from;
                    response.StatusCode = 206;
                    response.AddHeader("Content-Range", $"bytes {start}-{file.Length - 1}/{file.Length}");
                }
                response.AddHeader("Accept-Ranges", "bytes");
                response.ContentLength64 = file.Length - start;
                file.Seek(start, SeekOrigin.Begin);

                var buffer = new byte[Chunk];
                var started = DateTime.UtcNow;
                long sent = 0;
                int read;
                while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
                {
                    response.OutputStream.Write(buffer, 0, read);
                    sent += read;
                    if (bytesPerSecond <= 0) continue;
                    var ahead = sent / (double)bytesPerSecond - (DateTime.UtcNow - started).TotalSeconds;
                    if (ahead > 0) Thread.Sleep(TimeSpan.FromSeconds(ahead));
                }
                response.Close();
            }
            catch (Exception e) when (e is HttpListenerException or IOException or ObjectDisposedException)
            {
                // 플레이 모드를 멈추면 클라이언트가 먼저 끊음
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                response.Abort();
            }
        }

        public void Dispose()
        {
            if (!listener.IsListening) return;
            listener.Stop();
            listener.Close();
        }
    }
}
