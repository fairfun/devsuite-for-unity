#!/usr/bin/env python3
import http.server
import os
import sys
import socketserver

def get_default_directory():
    for candidate in ["docs", "../docs", "DevSuite/Build/WebGL_Sample", "Build/WebGL_Sample"]:
        if os.path.isdir(candidate):
            return candidate
    return "docs"

DIRECTORY = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else get_default_directory())
PORT = int(sys.argv[2]) if len(sys.argv) > 2 else 8080

class UnityWebGLHandler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=DIRECTORY, **kwargs)

    def end_headers(self):
        clean_path = self.path.split("?")[0].split("#")[0]
        if clean_path.endswith(".br"):
            self.send_header("Content-Encoding", "br")
        elif clean_path.endswith(".gz"):
            self.send_header("Content-Encoding", "gzip")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Cross-Origin-Opener-Policy", "same-origin")
        self.send_header("Cross-Origin-Embedder-Policy", "require-corp")
        super().end_headers()

    def guess_type(self, path):
        clean = path.split("?")[0].split("#")[0]
        if clean.endswith(".wasm.br") or clean.endswith(".wasm.gz") or clean.endswith(".wasm"):
            return "application/wasm"
        if clean.endswith(".js.br") or clean.endswith(".js.gz") or clean.endswith(".js"):
            return "application/javascript"
        if clean.endswith(".data.br") or clean.endswith(".data.gz") or clean.endswith(".data"):
            return "application/octet-stream"
        return super().guess_type(path)

class ThreadingHTTPServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True

if __name__ == "__main__":
    server_address = ("127.0.0.1", PORT)
    httpd = ThreadingHTTPServer(server_address, UnityWebGLHandler)
    print(f"Serving Unity WebGL from {DIRECTORY} at http://localhost:{PORT}")
    sys.stdout.flush()
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\nServer stopped.")
