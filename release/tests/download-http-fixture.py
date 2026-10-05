"""仅监听本机的下载协议测试服务，不访问真实私有凭据。"""

import argparse
import json
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--trace", required=True)
args = parser.parse_args()
payload = bytes(range(256)) * 2048
lock = threading.Lock()
retry_count = 0


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def do_GET(self):
        global retry_count
        with lock:
            with Path(args.trace).open("a", encoding="utf-8") as trace:
                # 只记录请求路径与 Range；不输出认证头或任何令牌。
                trace.write(json.dumps({"path": self.path, "range": self.headers.get("Range", "")}) + "\n")
            if self.path == "/retry":
                retry_count += 1
                if retry_count == 1:
                    self.send_error(404, "fixture first attempt")
                    return
        if self.path == "/private" and self.headers.get("Authorization") != "Bearer fixture-token":
            self.send_error(401, "fixture authentication required")
            return
        if self.path == "/redirect":
            self.send_response(302)
            self.send_header("Location", "/private")
            self.end_headers()
            return
        if self.path == "/sha-channel.json":
            body = json.dumps({"schemaVersion": 1, "latestVersion": "fixture-1", "full": {"path": "bad.7z", "size": len(payload), "sha256": "0" * 64}, "patches": [], "legacyBaselines": []}).encode()
            self.send_response(200)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        start, end = 0, len(payload) - 1
        range_header = self.headers.get("Range", "")
        if range_header:
            first, last = range_header.removeprefix("bytes=").split("-", 1)
            start = int(first)
            if last:
                end = min(end, int(last))
        self.send_response(206 if range_header else 200)
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Type", "application/octet-stream")
        self.send_header("Content-Length", str(end - start + 1))
        self.send_header("ETag", '"download-fixture-v1"')
        if range_header:
            self.send_header("Content-Range", f"bytes {start}-{end}/{len(payload)}")
        self.end_headers()
        self.wfile.write(payload[start:end + 1])


server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
print(server.server_address[1], flush=True)
server.serve_forever()
