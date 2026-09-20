"""Headless-Chrome test runner for the web client.
   python webtest.py URL [--wait SEC] [--shot out.png] [--js "expr"] [--size 1280x720] [--actions file.json]
Prints console messages / page errors, optionally evaluates JS and saves a screenshot. Uses the system Chrome (no download)."""
import sys, os, json, time, argparse
from playwright.sync_api import sync_playwright

ap = argparse.ArgumentParser()
ap.add_argument('url'); ap.add_argument('--wait', type=float, default=8)
ap.add_argument('--shot'); ap.add_argument('--js'); ap.add_argument('--size', default='1280x720')
ap.add_argument('--actions', help='json list of {t: seconds, do: move|click|down|up|key|type, x,y,key,text}')
ap.add_argument('--shot-at', help='comma list of seconds: extra screenshots out_N.png')
a = ap.parse_args()
w, h = map(int, a.size.split('x'))
with sync_playwright() as p:
    b = p.chromium.launch(executable_path=r'C:\Program Files\Google\Chrome\Application\chrome.exe', headless=True,
                          args=(['--use-angle=d3d11', '--ignore-gpu-blocklist', '--enable-gpu-rasterization'] if os.environ.get('WT_GPU') else ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist']) + ['--autoplay-policy=no-user-gesture-required'])
    pg = b.new_page(viewport={'width': w, 'height': h})
    pg.on('console', lambda m: print('[console.%s] %s' % (m.type, m.text)))
    pg.on('pageerror', lambda e: print('[pageerror] %s' % e))
    pg.on('requestfailed', lambda r: print('[requestfailed] %s %s' % (r.url, r.failure)))
    if os.environ.get('WT_NET'):
        pg.on('request', lambda r: print('[req] %s %s %s' % (r.method, r.url, (r.post_data or '')[:200])) if '/api/' in r.url else None)
    pg.goto(a.url)
    acts = json.load(open(a.actions)) if a.actions else []
    shots = sorted(float(x) for x in a.shot_at.split(',')) if a.shot_at else []
    t0 = time.time(); ai = 0; si = 0
    while time.time() - t0 < a.wait:
        now = time.time() - t0
        while ai < len(acts) and acts[ai]['t'] <= now:
            d = acts[ai]; ai += 1
            k = d['do']
            if k == 'move': pg.mouse.move(d['x'], d['y'])
            elif k == 'click': pg.mouse.click(d['x'], d['y'])
            elif k == 'down': pg.mouse.down()
            elif k == 'up': pg.mouse.up()
            elif k == 'key': pg.keyboard.press(d['key'])
            elif k == 'reload': pg.reload()
            elif k == 'kdown': pg.keyboard.down(d['key'])
            elif k == 'kup': pg.keyboard.up(d['key'])
            elif k == 'type': pg.keyboard.type(d['text'])
        while si < len(shots) and shots[si] <= now:
            base = a.shot or 'shot.png'
            pg.screenshot(path=base.replace('.png', '_%g.png' % shots[si])); si += 1
        time.sleep(0.05)
    if a.js: print('[js]', pg.evaluate(a.js))
    if a.shot: pg.screenshot(path=a.shot)
    b.close()
