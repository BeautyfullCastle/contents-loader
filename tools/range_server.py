"""Throttled HTTP file server with Range support for device download checks.

Usage: python -I tools/range_server.py <root> <port> <bytes_per_second>
Logs one line per request: method, path, Range header, bytes sent.
"""
import http.server
import os
import sys
import time

ROOT, PORT, RATE = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
CHUNK = 4096


class Handler(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def do_GET(self):
        path = os.path.join(ROOT, os.path.basename(self.path.split("?")[0]))
        if not os.path.isfile(path):
            self.send_error(404)
            return
        size = os.path.getsize(path)
        start = 0
        range_header = self.headers.get("Range")
        if range_header and range_header.startswith("bytes="):
            start = int(range_header[len("bytes="):].split("-")[0])
            self.send_response(206)
            self.send_header("Content-Range", f"bytes {start}-{size - 1}/{size}")
        else:
            self.send_response(200)
        self.send_header("Content-Length", str(size - start))
        self.send_header("Accept-Ranges", "bytes")
        self.end_headers()

        sent = 0
        try:
            with open(path, "rb") as f:
                f.seek(start)
                while chunk := f.read(CHUNK):
                    self.wfile.write(chunk)
                    sent += len(chunk)
                    time.sleep(len(chunk) / RATE)
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            pass
        print(f"GET {os.path.basename(path)} range={range_header} sent={sent}/{size - start}", flush=True)

    def log_message(self, *args):
        pass


http.server.ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
