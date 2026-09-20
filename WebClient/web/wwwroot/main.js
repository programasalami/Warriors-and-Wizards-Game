// Boots the .NET WebAssembly runtime, downloads the game content into its in-memory file system, then starts the client and drives
// it with requestAnimationFrame. DOM input events are forwarded to C# (WebHost.On*), which raises the same events OpenTK would.
const loading = document.getElementById('loading');
const barFill = document.querySelector('#bar > div');
const statusEl = document.getElementById('status');
const canvas = document.getElementById('game-canvas');
// Stamped with the build's id by tools/build_web.py (left as-is when the site is served straight from the source folder).
const BUILD = '__BUILD__';

let lastProgress = performance.now();
let started = false;
const setStatus = (t, pct) => { lastProgress = performance.now(); statusEl.textContent = t; if (pct !== undefined) barFill.style.width = pct + '%'; };

// ---- safety net: a stale cached file (or any silent stall) must not leave the page loading forever ---------------------------------------
// The runtime's boot file is imported with the build id in its URL, so a browser holding an older copy of it can never be handed the wrong one.
// If the boot still fails - or makes no progress for STALL_MS - the page refreshes its own cached files once by itself, and if that does not
// help either it says so, with a button, instead of spinning.
const AUTOFIX_KEY = 'ww:autofix';
const STALL_MS = 90000;

async function refreshCachedFiles() {
    const urls = ['./', './main.js', './host.js', './gl.js', './audio.js', './content.json', './_framework/dotnet.js'];
    await Promise.all(urls.map((u) => fetch(u, { cache: 'reload' }).catch(() => { })));
}

function showFailure(reason) {
    statusEl.textContent = 'The game could not start (' + reason + '). Your browser may be holding old files - try reloading.';
    barFill.style.width = '100%';
    if (!document.getElementById('ww-reload')) {
        const b = document.createElement('button');
        b.id = 'ww-reload';
        b.textContent = 'Reload';
        b.style.cssText = 'margin-top:16px;padding:8px 22px;font:inherit;cursor:pointer';
        b.onclick = async () => { sessionStorage.removeItem(AUTOFIX_KEY); await refreshCachedFiles(); location.reload(); };
        statusEl.parentElement.appendChild(b);
    }
}

async function recover(reason) {
    console.warn('[web] boot problem: ' + reason);
    if (started) return;
    if (!sessionStorage.getItem(AUTOFIX_KEY)) {
        sessionStorage.setItem(AUTOFIX_KEY, '1');
        statusEl.textContent = 'Updating game files…';
        await refreshCachedFiles();
        location.reload();
        return;
    }
    showFailure(reason);
}

setInterval(() => { if (!started && performance.now() - lastProgress > STALL_MS) recover('no progress for ' + Math.round(STALL_MS / 1000) + ' s'); }, 5000);

// Runtime configuration (can be overridden by window.WW_CONFIG before this script runs): where the account server (HTTP api) and the
// game server (WebSocket bridge) live. Defaults: same origin, /api and /game.
const cfg = Object.assign({ api: location.origin + '/api', game: (location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/game' }, window.WW_CONFIG || {});
const q = new URLSearchParams(location.search);   // ?api=...&game=... overrides, for testing against other servers
if (q.get('api')) cfg.api = q.get('api');
if (q.get('game')) cfg.game = q.get('game');
window.WW_CONFIG = cfg;

async function boot() {
    setStatus('Starting the game engine…', 2);
    const { dotnet } = await import('./_framework/dotnet.js?v=' + BUILD);
    const { getAssemblyExports, getConfig, runMain } = await dotnet.withDiagnosticTracing(false).create();
    const exports = await getAssemblyExports(getConfig().mainAssemblyName);
    await runMain();   // imports gl.js / audio.js / host.js modules

    // ---- content -> in-memory file system -------------------------------------------------------------------------
    setStatus('Downloading game files…', 8);
    const manifest = await (await fetch('content.json')).json();
    const total = manifest.reduce((a, f) => a + f.size, 0);
    let done = 0, next = 0;
    async function worker() {
        while (next < manifest.length) {
            const f = manifest[next++];
            const res = await fetch('content/' + f.path);
            if (!res.ok) throw new Error('Missing game file: ' + f.path);
            exports.WarriorsWeb.WebHost.WriteFile('/Content/' + f.path, new Uint8Array(await res.arrayBuffer()));
            done += f.size;
            setStatus('Downloading game files… ' + Math.round(done / 1048576) + ' / ' + Math.round(total / 1048576) + ' MB', 8 + (done / total) * 82);
        }
    }
    await Promise.all(Array.from({ length: 6 }, worker));

    // ---- canvas size (device pixels) --------------------------------------------------------------------------------
    const dpr = () => Math.min(window.devicePixelRatio || 1, 2);
    const size = () => [Math.max(1, Math.floor(innerWidth * dpr())), Math.max(1, Math.floor(innerHeight * dpr()))];
    let [w, h] = size();
    canvas.width = w; canvas.height = h;

    setStatus('Starting…', 94);
    const H = exports.WarriorsWeb.WebHost;

    // saved login + settings (the client reads them from its in-memory local folder at startup): restore them from localStorage first
    try {
        const dir = H.LocalFolder();
        for (const name of H.PersistedFileList().split('|')) {
            const b64 = localStorage.getItem('ww:' + name);
            if (b64) H.WriteFile(dir + '/' + name, Uint8Array.from(atob(b64), (c) => c.charCodeAt(0)));
        }
    } catch (e) { console.warn('could not restore saved data', e); }

    if (H.Start(w, h) !== 1) { setStatus('Could not start the game (WebGL2 needed). See the console for details.', 100); return; }

    // ---- input -----------------------------------------------------------------------------------------------------------
    const pos = (e) => [e.clientX * dpr(), e.clientY * dpr()];
    addEventListener('resize', () => { [w, h] = size(); canvas.width = w; canvas.height = h; H.OnResize(w, h); });
    canvas.addEventListener('mousemove', (e) => H.OnMouseMove(...pos(e)));
    canvas.addEventListener('mousedown', (e) => { canvas.focus(); H.OnMouseMove(...pos(e)); H.OnMouseButton(true, e.button); e.preventDefault(); });
    addEventListener('mouseup', (e) => H.OnMouseButton(false, e.button));
    canvas.addEventListener('contextmenu', (e) => e.preventDefault());
    canvas.addEventListener('wheel', (e) => {
        const k = e.deltaMode === 1 ? 1 : 1 / 100;
        H.OnScroll(-Math.sign(e.deltaX) * Math.min(3, Math.abs(e.deltaX * k)), -Math.sign(e.deltaY) * Math.min(3, Math.abs(e.deltaY * k)));
        e.preventDefault();
    }, { passive: false });
    const blockedCombos = (e) => (e.ctrlKey || e.metaKey) && ['KeyV', 'KeyC', 'KeyX', 'KeyA'].includes(e.code);
    addEventListener('keydown', (e) => {
        if (!e.repeat) H.OnKey(true, e.code);
        if (e.key && e.key.length === 1 && !e.ctrlKey && !e.metaKey) H.OnText(e.key);
        if (!(e.code === 'F5' || e.code === 'F12' || e.code === 'F11' || blockedCombos(e))) e.preventDefault();
    });
    addEventListener('keyup', (e) => { H.OnKey(false, e.code); e.preventDefault(); });
    addEventListener('paste', (e) => { const t = (e.clipboardData || window.clipboardData).getData('text'); H.OnClipboard(t); H.OnText(t); });
    addEventListener('focus', () => H.OnFocus(true));
    addEventListener('blur', () => H.OnFocus(false));
    canvas.focus();
    setInterval(() => H.PersistNow(false), 4000);
    setInterval(() => H.PersistNow(true), 30000);
    addEventListener('pagehide', () => H.PersistNow(true));
    document.addEventListener('visibilitychange', () => { if (document.hidden) H.PersistNow(true); });

    // ---- loop ---------------------------------------------------------------------------------------------------------------
    let frames = 0, last = performance.now(), worst = 0;
    const frame = (t) => {
        const t0 = performance.now(); H.Frame(t); const dt = performance.now() - t0; worst = Math.max(worst, dt); frames++;
        if (t0 - last >= 5000) { console.log('[fps] ' + (frames * 1000 / (t0 - last)).toFixed(1) + ' fps, worst frame ' + worst.toFixed(0) + ' ms'); frames = 0; worst = 0; last = t0; }
        requestAnimationFrame(frame);
    };
    requestAnimationFrame(frame);
    loading.classList.add('done');
    started = true;
    sessionStorage.removeItem(AUTOFIX_KEY);
    window.__ww = { exports, H, cfg };
}

boot().catch((e) => { console.error(e); recover(e && e.message ? e.message : String(e)); });
