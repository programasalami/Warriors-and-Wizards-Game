"""Builds the browser client into WebClient/dist/site (a static site: index.html, main.js, _framework/, content/, content.json).

    python tools/build_web.py            # patch + port shaders + publish + assemble content
    python tools/build_web.py --quick    # skip the dotnet publish (only re-copy wwwroot + content)

The game content (art / fonts / xml, ~28 MB) is copied from the desktop client's built Content folder
(AlloyClient/AlloyClient/bin/Debug/net10.0/Content - build the desktop client first if it is missing)."""
import json, os, shutil, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
LT = os.path.normpath(os.path.join(HERE, '..', '..'))
WC = os.path.join(LT, 'WebClient')
WEB = os.path.join(WC, 'web')
DIST = os.path.join(WC, 'dist')
SITE = os.path.join(DIST, 'site')
PUB = os.path.join(DIST, 'publish')
CONTENT_SRC = os.path.join(LT, 'AlloyClient', 'AlloyClient', 'bin', 'Debug', 'net10.0', 'Content')


def run(cmd, cwd):
    print('>', ' '.join(cmd))
    r = subprocess.run(cmd, cwd=cwd)
    if r.returncode != 0:
        sys.exit('FAILED: ' + ' '.join(cmd))


quick = '--quick' in sys.argv
aot = '--aot' in sys.argv      # ahead-of-time compile to WebAssembly (much faster startup/gameplay; needs the wasm-tools workload)
AOT_DOTNET = os.path.join(os.path.expanduser('~'), '.dotnet-wasm', 'dotnet.exe')   # user-local SDK that has wasm-tools installed
if aot:
    PUB = os.path.join(DIST, 'publish-aot')
    SITE = os.path.join(DIST, 'site-aot')
if not quick:
    run([sys.executable, os.path.join(HERE, 'patch_sources.py')], WC)
    run([sys.executable, os.path.join(HERE, 'port_shaders.py')], WC)
    if os.path.exists(PUB):
        shutil.rmtree(PUB)
    if aot:
        run([AOT_DOTNET, 'publish', 'web.csproj', '-c', 'Release', '--nologo', '-v:q', '-p:WwAot=true',
             '--artifacts-path', os.path.join(DIST, 'aot-artifacts'), '-o', PUB], WEB)
    else:
        run(['dotnet', 'publish', 'web.csproj', '-c', 'Release', '--nologo', '-v:q', '-o', PUB], WEB)

pub_root = os.path.join(PUB, 'wwwroot')
if not os.path.isdir(pub_root):
    sys.exit('no publish output at ' + pub_root + ' (run without --quick first)')
if not os.path.isdir(CONTENT_SRC):
    sys.exit('desktop content not built: ' + CONTENT_SRC)

if os.path.exists(SITE):
    shutil.rmtree(SITE)
shutil.copytree(pub_root, SITE)
for name in ('index.html', 'main.js', 'gl.js', 'host.js', 'audio.js'):
    shutil.copy2(os.path.join(WEB, 'wwwroot', name), os.path.join(SITE, name))
    for ext in ('.br', '.gz'):   # the SDK precompressed the published copies; those would now be stale
        p = os.path.join(SITE, name + ext)
        if os.path.exists(p):
            os.remove(p)

# Stamp this build's id into the page: it goes into the URLs of main.js and the runtime's boot file (dotnet.js), so a browser that cached an older
# (immutable) copy of either is never handed the wrong one after a redeploy.
BUILD_ID = time.strftime('%Y%m%d%H%M%S')
for name in ('index.html', 'main.js'):
    path = os.path.join(SITE, name)
    text = open(path, encoding='utf-8').read().replace('__BUILD__', BUILD_ID)
    open(path, 'w', encoding='utf-8', newline='').write(text)
print('build id', BUILD_ID)

manifest = []
out_content = os.path.join(SITE, 'content')
for root, _, files in os.walk(CONTENT_SRC):
    for f in files:
        full = os.path.join(root, f)
        rel = os.path.relpath(full, CONTENT_SRC).replace(os.sep, '/')
        dest = os.path.join(out_content, *rel.split('/'))
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        shutil.copy2(full, dest)
        if not rel.startswith('Sound/'):     # audio is streamed by the page (audio.js), not loaded into the file system
            manifest.append({'path': rel, 'size': os.path.getsize(full)})
json.dump(manifest, open(os.path.join(SITE, 'content.json'), 'w'))

total = sum(f['size'] for f in manifest) / 1048576
print('site ready: %s  (%d content files, %.1f MB into the file system)' % (SITE, len(manifest), total))
