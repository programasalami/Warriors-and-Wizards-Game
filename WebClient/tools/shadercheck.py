"""Compiles every ported shader (web/shaders) in headless Chrome's WebGL2 and prints the driver's errors, so all GLSL ES problems show up
in one pass:   python tools/shadercheck.py [Name ...]"""
import os, sys, json
from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
SH = os.path.normpath(os.path.join(HERE, '..', 'web', 'shaders'))
names = sys.argv[1:]
files = sorted(f for f in os.listdir(SH) if f.endswith(('.vert', '.frag')) and (not names or f.split('.')[0] in names))
JS = """
(items) => {
  const c = document.createElement('canvas'); const gl = c.getContext('webgl2'); const out = {};
  for (const [name, src] of items) {
    const s = gl.createShader(name.endsWith('.vert') ? gl.VERTEX_SHADER : gl.FRAGMENT_SHADER);
    gl.shaderSource(s, src); gl.compileShader(s);
    out[name] = gl.getShaderParameter(s, gl.COMPILE_STATUS) ? 'OK' : gl.getShaderInfoLog(s);
  }
  // link check per program name
  const names = [...new Set(items.map(i => i[0].split('.')[0]))];
  for (const n of names) {
    const v = items.find(i => i[0] === n + '.vert'), f = items.find(i => i[0] === n + '.frag'); if (!v || !f) continue;
    if (out[n + '.vert'] !== 'OK' || out[n + '.frag'] !== 'OK') continue;
    const p = gl.createProgram();
    for (const [src, t] of [[v[1], gl.VERTEX_SHADER], [f[1], gl.FRAGMENT_SHADER]]) { const s = gl.createShader(t); gl.shaderSource(s, src); gl.compileShader(s); gl.attachShader(p, s); }
    gl.linkProgram(p); out[n + ' (link)'] = gl.getProgramParameter(p, gl.LINK_STATUS) ? 'OK' : gl.getProgramInfoLog(p);
  }
  return out;
}
"""
with sync_playwright() as p:
    b = p.chromium.launch(executable_path=r'C:\Program Files\Google\Chrome\Application\chrome.exe', headless=True,
                          args=['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'])
    pg = b.new_page()
    pg.goto('about:blank')
    items = [[f, open(os.path.join(SH, f), encoding='utf-8').read().replace('#define ShadowBuffer', '#define ShadowBuffer 512')] for f in files]
    res = pg.evaluate(JS, items)
    bad = 0
    for k, v in res.items():
        print('%-22s %s' % (k, 'OK' if v == 'OK' else 'FAILED'))
        if v != 'OK':
            bad += 1
            for line in v.strip().splitlines()[:14]:
                print('     ' + line[:170])
    b.close()
    sys.exit(1 if bad else 0)
