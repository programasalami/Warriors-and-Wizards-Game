// Music and sound effects through <audio> / Web Audio. Files are fetched from ./content/Sound/... on demand (they are not copied
// into the in-memory file system). Browsers only allow sound after a user gesture, so playback is retried on the first click / key.
let musicEl = null, wantedMusic = null, musicVolume = 1, masterVolume = 1, fadeTimer = 0;
let unlocked = false;
const sfxCache = new Map();
let ctx = null;

function unlock() {
    if (unlocked) return;
    unlocked = true;
    try { ctx = new (window.AudioContext || window.webkitAudioContext)(); ctx.resume(); } catch (e) { /* no web audio */ }
    if (wantedMusic && musicEl) musicEl.play().catch(() => {});
}
addEventListener('pointerdown', unlock, { once: false });
addEventListener('keydown', unlock, { once: false });

function applyVolume() { if (musicEl) musicEl.volume = Math.min(1, Math.max(0, musicVolume * masterVolume)); }

export function playMusic(path, fadeSeconds) {
    if (wantedMusic === path && musicEl) return;
    wantedMusic = path;
    const old = musicEl;
    const el = new Audio('content/Sound/' + path);
    el.loop = true;
    el.volume = 0;
    musicEl = el;
    el.play().catch(() => { /* waits for unlock() */ });
    // fade the new track in and the old one out
    clearInterval(fadeTimer);
    const steps = Math.max(1, Math.round((fadeSeconds || 0) * 20)), target = () => Math.min(1, Math.max(0, musicVolume * masterVolume));
    let i = 0;
    fadeTimer = setInterval(() => {
        i++;
        const k = Math.min(1, i / steps);
        el.volume = target() * k;
        if (old) old.volume = Math.max(0, (1 - k)) * target();
        if (k >= 1) { clearInterval(fadeTimer); if (old) old.pause(); }
    }, 50);
}
export function setMusicVolume(v) { musicVolume = v; applyVolume(); }
export function setMasterVolume(v) { masterVolume = v; applyVolume(); }
export async function playSfx(path, volume) {
    if (!ctx) return;
    try {
        let buf = sfxCache.get(path);
        if (!buf) {
            const data = await (await fetch('content/Sound/' + path)).arrayBuffer();
            buf = await ctx.decodeAudioData(data); sfxCache.set(path, buf);
        }
        const src = ctx.createBufferSource(), gain = ctx.createGain();
        src.buffer = buf; gain.gain.value = volume * masterVolume; src.connect(gain).connect(ctx.destination); src.start();
    } catch (e) { /* missing sfx */ }
}
