using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace H.ContentsLoader.Integration.Tests
{
    // 이어받기 테스트용 로컬 HTTP 서버. 첫 응답을 중간에 끊어서 앱 종료를 흉내낼 수 있음
    internal sealed class RangeServer : IDisposable
    {
        readonly HttpListener listener = new();
        readonly string root;
        readonly List<string> ranges = new();
        long cutAfterBytes;

        public RangeServer(string root, long cutFirstResponseAfterBytes = -1, bool ignoreRange = false)
        {
            this.root = root;
            cutAfterBytes = cutFirstResponseAfterBytes;
            IgnoreRange = ignoreRange;

            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            listener.Prefixes.Add($"http://localhost:{Port}/");
            listener.Start();
            new Thread(Serve) { IsBackground = true }.Start();
        }

        public int Port { get; }
        public bool IgnoreRange { get; }
        public string Url => $"http://localhost:{Port}";

        public IReadOnlyList<string> Ranges
        {
            get
            {
                lock (ranges) return ranges.ToArray();
            }
        }

        void Serve()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = listener.GetContext();
                }
                catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }
                Respond(context);
            }
        }

        void Respond(HttpListenerContext context)
        {
            var path = Path.Combine(root, Path.GetFileName(context.Request.Url.AbsolutePath));
            var range = context.Request.Headers["Range"];
            lock (ranges) ranges.Add(range ?? "");

            if (!File.Exists(path))
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }

            var bytes = File.ReadAllBytes(path);
            long start = 0;
            if (range != null && !IgnoreRange)
            {
                start = long.Parse(range.Substring("bytes=".Length).TrimEnd('-'));
                context.Response.StatusCode = 206;
                context.Response.AddHeader("Content-Range", $"bytes {start}-{bytes.Length - 1}/{bytes.Length}");
            }

            var length = bytes.Length - start;
            context.Response.ContentLength64 = length;
            try
            {
                var cut = Interlocked.Exchange(ref cutAfterBytes, -1);
                if (cut >= 0)
                {
                    context.Response.OutputStream.Write(bytes, (int)start, (int)Math.Min(cut, length));
                    context.Response.OutputStream.Flush();
                    Thread.Sleep(200);
                    context.Response.Abort();
                    return;
                }
                context.Response.OutputStream.Write(bytes, (int)start, (int)length);
                context.Response.Close();
            }
            catch (Exception e) when (e is HttpListenerException or IOException)
            {
                // 클라이언트가 먼저 끊으면 남은 응답은 버림
            }
        }

        public void Dispose()
        {
            listener.Stop();
            listener.Close();
        }
    }
}
