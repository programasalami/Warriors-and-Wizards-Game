"""Local test server for the browser client: serves WebClient/dist/site and forwards /api/* to the account server on the VPS
(the account server sends no CORS headers, so the browser can only reach it through a same-origin proxy like this one).

    python tools/serve.py [port] [--api http://104.152.50.196:8080]
Game connections go straight to the WebSocket bridge (see wwwroot/main.js WW_CONFIG.game)."""
import http.server, os, sys, urllib.request, urllib.error, mimetypes

HERE = os.path.dirname(os.path.abspath(__file__))
SITE = os.path.normpath(os.path.join(HERE, '..', 'dist', 'site'))
if '--aot' in sys.argv:
    sys.argv.remove('--aot')
    SITE = os.path.normpath(os.path.join(HERE, '..', 'dist', 'site-aot'))
port = 8123
api = 'http://104.152.50.196:8080'
args = sys.argv[1:]
while args:
    a = args.pop(0)
    if a == '--api':
        api = args.pop(0)
    else:
        port = int(a)

mimetypes.add_type('application/wasm', '.wasm')
mimetypes.add_type('text/javascript', '.js')


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **k):
        super().__init__(*a, directory=SITE, **k)

    def _proxy(self):
        url = api + self.path[len('/api'):]
        body = None
        if self.command in ('POST', 'PUT'):
            body = self.rfile.read(int(self.headers.get('Content-Length', 0)))
        req = urllib.request.Request(url, data=body, method=self.command)
        if self.headers.get('Content-Type'):
            req.add_header('Content-Type', self.headers['Content-Type'])
        try:
            with urllib.request.urlopen(req, timeout=15) as r:
                data = r.read()
                self.send_response(r.status)
                self.send_header('Content-Type', r.headers.get('Content-Type', 'text/plain'))
        except urllib.error.HTTPError as e:
            data = e.read()
            self.send_response(e.code)
            self.send_header('Content-Type', e.headers.get('Content-Type', 'text/plain'))
        except Exception as e:
            data = str(e).encode()
            self.send_response(502)
            self.send_header('Content-Type', 'text/plain')
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        if self.path.startswith('/api/'):
            return self._proxy()
        return super().do_GET()

    def do_POST(self):
        if self.path.startswith('/api/'):
            return self._proxy()
        self.send_error(405)

    def end_headers(self):
        self.send_header('Cache-Control', 'no-store')
        super().end_headers()

    def log_message(self, fmt, *a):
        if a and '/api/' in str(a[0]):
            sys.stderr.write('api ' + fmt % a + '\n')


print('serving %s on http://127.0.0.1:%d  (api -> %s)' % (SITE, port, api))
class Server(http.server.ThreadingHTTPServer):
    request_queue_size = 512   # the runtime fetches ~150 files at once; the default backlog of 5 refuses connections
    daemon_threads = True


Server(('127.0.0.1', port), Handler).serve_forever()
