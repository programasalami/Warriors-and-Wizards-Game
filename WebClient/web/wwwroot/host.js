// Small browser helpers the C# host calls (fullscreen, logging).
export function setFullscreen(on) {
    try {
        if (on && !document.fullscreenElement) document.documentElement.requestFullscreen();
        else if (!on && document.fullscreenElement) document.exitFullscreen();
    } catch (e) { /* needs a user gesture; ignore */ }
}
export function log(text) { console.log(text); }
// Opens a link in a new tab through a real anchor click (works right after the player's click; does not depend on window.open's return value).
export function openUrl(url) {
    try {
        const a = document.createElement('a');
        a.href = url; a.target = '_blank'; a.rel = 'noopener';
        document.body.appendChild(a); a.click(); a.remove();
    } catch (e) { console.log('could not open ' + url); }
}
// The newest build: a plain reload fetches it (the build id in the URLs makes the browser skip its cache).
export function reloadPage() { try { location.reload(); } catch (e) { console.log('could not reload'); } }
export const getApi = () => window.WW_CONFIG.api;
export const getGameUrl = () => window.WW_CONFIG.game;
export function persistFile(name, b64) { try { localStorage.setItem('ww:' + name, b64); } catch (e) { /* storage full / blocked */ } }
