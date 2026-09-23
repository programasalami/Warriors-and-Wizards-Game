'use strict';
// W&W Editor: a map editor for the game's own .jm maps and a maker for new items / objects. Plain browser code, no libraries, no build step.
// The .jm format (see WaW-Server/Common/Resources/World/MapData.cs): { width, height, dict:[ {ground, objs:[{id}], regions:[{id}]} ... ], data }
// where data is base64( zlib( int16 big-endian, row by row, each = an index into dict ) ).

const P = window.PALETTE;
const $ = (id) => document.getElementById(id);

// ================================================================================================================================
//  Map model (pure: no DOM), also used by the tests in the browser console (window.WW)
// ================================================================================================================================

function b64ToBytes(b64) {
  const bin = atob(b64);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return bytes;
}

function bytesToB64(bytes) {
  let s = '';
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) s += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
  return btoa(s);
}

async function streamBytes(bytes, transform) {
  const stream = new Blob([bytes]).stream().pipeThrough(transform);
  return new Uint8Array(await new Response(stream).arrayBuffer());
}

// DecompressionStream('deflate') / CompressionStream('deflate') use the zlib format, which is exactly what the game's maps use.
async function decodeCells(b64, w, h) {
  const raw = await streamBytes(b64ToBytes(b64), new DecompressionStream('deflate'));
  if (raw.length !== w * h * 2) throw new Error('The map data is ' + raw.length + ' bytes, expected ' + (w * h * 2) + ' for ' + w + 'x' + h + '.');
  const view = new DataView(raw.buffer, raw.byteOffset, raw.byteLength);
  const cells = new Int16Array(w * h);
  for (let i = 0; i < cells.length; i++) cells[i] = view.getInt16(i * 2, false);
  return cells;
}

async function encodeCells(cells) {
  const raw = new Uint8Array(cells.length * 2);
  const view = new DataView(raw.buffer);
  for (let i = 0; i < cells.length; i++) view.setInt16(i * 2, cells[i], false);
  return bytesToB64(await streamBytes(raw, new CompressionStream('deflate')));
}

// A tile description in a fixed key order, so equal tiles always get the same dict entry.
function canon(entry) {
  const out = {};
  if (entry.ground) out.ground = entry.ground;
  if (entry.objs && entry.objs.length) out.objs = entry.objs;
  if (entry.regions && entry.regions.length) out.regions = entry.regions;
  for (const k of Object.keys(entry).sort()) if (!(k in out) && k !== 'ground' && k !== 'objs' && k !== 'regions' && entry[k] != null) out[k] = entry[k];
  return out;
}

class MapModel {
  constructor(w, h) {
    this.w = w;
    this.h = h;
    this.cells = new Int16Array(w * h);
    this.dict = [{}];
    this.index = new Map([['{}', 0]]);
  }

  static blank(w, h, ground) {
    const m = new MapModel(w, h);
    if (ground) m.cells.fill(m.indexOf({ ground }));
    return m;
  }

  static async fromJm(text) {
    const j = JSON.parse(text);
    if (!(j.width > 0 && j.height > 0 && typeof j.data === 'string' && Array.isArray(j.dict))) throw new Error('This is not a map (.jm) file.');
    const m = new MapModel(j.width, j.height);
    m.dict = j.dict.map((e) => canon(e || {}));
    m.index = new Map();
    m.dict.forEach((e, i) => { const k = JSON.stringify(e); if (!m.index.has(k)) m.index.set(k, i); });
    m.cells = await decodeCells(j.data, j.width, j.height);
    for (let i = 0; i < m.cells.length; i++) if (m.cells[i] < 0 || m.cells[i] >= m.dict.length) throw new Error('The map refers to a tile description that does not exist (cell ' + i + ').');
    return m;
  }

  // Compacts the dictionary (drops entries no cell uses, in order of first use) and returns the .jm text.
  async toJm() {
    const remap = new Map();
    const dict = [];
    const cells = new Int16Array(this.cells.length);
    for (let i = 0; i < this.cells.length; i++) {
      const old = this.cells[i];
      let n = remap.get(old);
      if (n === undefined) { n = dict.length; remap.set(old, n); dict.push(this.dict[old]); }
      cells[i] = n;
    }

    return JSON.stringify({ width: this.w, data: await encodeCells(cells), height: this.h, dict });
  }

  inside(x, y) { return x >= 0 && y >= 0 && x < this.w && y < this.h; }
  get(x, y) { return this.dict[this.cells[y * this.w + x]]; }

  indexOf(entry) {
    const c = canon(entry);
    const k = JSON.stringify(c);
    let i = this.index.get(k);
    if (i === undefined) { i = this.dict.length; this.dict.push(c); this.index.set(k, i); }
    return i;
  }

  set(x, y, entry) { this.cells[y * this.w + x] = this.indexOf(entry); }

  snapshot() { return { cells: this.cells.slice(), dict: JSON.stringify(this.dict) }; }

  restore(s) {
    this.cells = s.cells.slice();
    this.dict = JSON.parse(s.dict);
    this.index = new Map();
    this.dict.forEach((e, i) => { const k = JSON.stringify(e); if (!this.index.has(k)) this.index.set(k, i); });
  }
}

// What one paint action does to a tile description. brush: {kind:'tile'|'object'|'region', id}. Returns the new entry (or the same one when nothing changes).
function applyBrush(entry, brush) {
  const e = JSON.parse(JSON.stringify(entry));
  if (brush.kind === 'tile') e.ground = brush.id;
  else if (brush.kind === 'object') e.objs = [{ id: brush.id }];
  else if (brush.kind === 'region') e.regions = [{ id: brush.id }];
  return e;
}

// The eraser takes the top layer first (object, then region, then ground); wholeCell clears everything.
function applyErase(entry, wholeCell) {
  if (wholeCell) return {};
  const e = JSON.parse(JSON.stringify(entry));
  if (e.objs && e.objs.length) delete e.objs;
  else if (e.regions && e.regions.length) delete e.regions;
  else delete e.ground;
  return e;
}

function floodGround(m, x, y, toGround) {
  const from = m.get(x, y).ground || '';
  if (from === (toGround || '')) return [];
  const changed = [];
  const seen = new Uint8Array(m.w * m.h);
  const stack = [[x, y]];
  while (stack.length) {
    const [cx, cy] = stack.pop();
    if (!m.inside(cx, cy) || seen[cy * m.w + cx]) continue;
    seen[cy * m.w + cx] = 1;
    if ((m.get(cx, cy).ground || '') !== from) continue;
    changed.push([cx, cy]);
    stack.push([cx + 1, cy], [cx - 1, cy], [cx, cy + 1], [cx, cy - 1]);
  }

  return changed;
}

window.WW = { MapModel, applyBrush, applyErase, floodGround, decodeCells, encodeCells, canon };

// ================================================================================================================================
//  Sprites
// ================================================================================================================================

const sheetImages = {};
function sheetImage(name) {
  let s = sheetImages[name];
  if (!s) {
    const info = P.sheets[name];
    const img = new Image();
    s = sheetImages[name] = { img, ok: false };
    if (info) {
      img.onload = () => { s.ok = true; onSheetLoaded(); };
      img.src = info.file;
    }
  }

  return s.ok ? s.img : null;
}

let sheetLoadTimer = 0;
function onSheetLoaded() {
  clearTimeout(sheetLoadTimer);
  sheetLoadTimer = setTimeout(() => { paintPaletteIcons(); requestDraw(); if (maker.on) drawSheetPicker(); }, 30);
}

function drawPic(ctx, pic, dx, dy, dw, dh) {
  if (!pic) return false;
  const img = sheetImage(pic.sheet);
  if (!img) return false;
  ctx.drawImage(img, pic.x, pic.y, pic.w, pic.h, dx, dy, dw, dh);
  return true;
}

const tileById = new Map(P.tiles.map((t) => [t.id, t]));
const objById = new Map(P.objects.concat(P.items).map((o) => [o.id, o]));

function hashColor(text, alpha) {
  let h = 0;
  for (let i = 0; i < text.length; i++) h = (h * 31 + text.charCodeAt(i)) | 0;
  return 'hsla(' + (Math.abs(h) % 360) + ',70%,55%,' + alpha + ')';
}

// ================================================================================================================================
//  State
// ================================================================================================================================

let map = null;
let mapFile = null;          // a FileSystemFileHandle when the map was opened through the picker (Save then writes in place)
let mapName = 'untitled.jm';
let dirty = false;
let brush = null;
let tool = 'pencil';
let tab = 'tiles';
let query = '';
const layers = { ground: true, objects: true, regions: true, grid: true };
const view = { ox: 20, oy: 20, zoom: 2 };
const undoStack = [];
const redoStack = [];
const MAX_UNDO = 40;

const canvas = $('view');
const ctx = canvas.getContext('2d');

function say(text, kind) {
  const m = $('msg');
  m.textContent = text || '';
  m.className = kind || '';
}

function setDirty(v) {
  dirty = v;
  $('mapName').textContent = (map ? mapName : 'no map') + (dirty ? ' *' : '');
  $('stMap').textContent = map ? map.w + ' x ' + map.h : '-';
}

// ================================================================================================================================
//  Drawing the map
// ================================================================================================================================

let drawQueued = false;
function requestDraw() {
  if (drawQueued) return;
  drawQueued = true;
  requestAnimationFrame(() => { drawQueued = false; draw(); });
}

function resizeCanvas() {
  const r = $('stage').getBoundingClientRect();
  const dpr = window.devicePixelRatio || 1;
  canvas.width = Math.max(1, Math.floor(r.width * dpr));
  canvas.height = Math.max(1, Math.floor(r.height * dpr));
  requestDraw();
}

const cs = () => 16 * view.zoom;

let hover = null;      // {x, y} the cell under the mouse
let dragRect = null;   // {x0, y0, x1, y1} while dragging a rectangle

function draw() {
  const dpr = window.devicePixelRatio || 1;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  const W = canvas.width / dpr, H = canvas.height / dpr;
  ctx.imageSmoothingEnabled = false;
  ctx.fillStyle = '#0f0b08';
  ctx.fillRect(0, 0, W, H);
  if (!map) {
    ctx.fillStyle = '#b39c78';
    ctx.font = '16px Segoe UI, sans-serif';
    ctx.fillText('New or Open a map to begin.', 30, 50);
    return;
  }

  const size = cs();
  const x0 = Math.max(0, Math.floor(-view.ox / size)), y0 = Math.max(0, Math.floor(-view.oy / size));
  const x1 = Math.min(map.w - 1, Math.floor((W - view.ox) / size)), y1 = Math.min(map.h - 1, Math.floor((H - view.oy) / size));

  // map edge
  ctx.fillStyle = '#17110c';
  ctx.fillRect(view.ox, view.oy, map.w * size, map.h * size);

  for (let y = y0; y <= y1; y++) {
    for (let x = x0; x <= x1; x++) {
      const e = map.get(x, y);
      const px = view.ox + x * size, py = view.oy + y * size;
      if (layers.ground && e.ground) {
        const t = tileById.get(e.ground);
        if (!(t && drawPic(ctx, t.pic, px, py, size + 0.5, size + 0.5))) {
          ctx.fillStyle = hashColor(e.ground, 0.9);
          ctx.fillRect(px, py, size + 0.5, size + 0.5);
        }
      }
    }
  }

  if (layers.objects) {
    for (let y = y0; y <= y1 + 6 && y < map.h; y++) {           // a few extra rows: tall objects reach up into the rows above them
      for (let x = Math.max(0, x0 - 2); x <= x1 + 2 && x < map.w; x++) {
        const e = map.get(x, y);
        if (!e.objs) continue;
        for (const o of e.objs) drawObject(o.id, view.ox + x * size, view.oy + y * size, size);
      }
    }
  }

  if (layers.regions) {
    for (let y = y0; y <= y1; y++) {
      for (let x = x0; x <= x1; x++) {
        const e = map.get(x, y);
        if (!e.regions) continue;
        const px = view.ox + x * size, py = view.oy + y * size;
        for (const r of e.regions) {
          ctx.fillStyle = hashColor(r.id, 0.4);
          ctx.fillRect(px, py, size, size);
          if (size >= 26) {
            ctx.fillStyle = '#fff';
            ctx.font = Math.max(9, size / 4) + 'px Segoe UI, sans-serif';
            ctx.fillText(r.id.length > 9 ? r.id.slice(0, 8) + '.' : r.id, px + 2, py + size / 2 + 3);
          }
        }
      }
    }
  }

  if (layers.grid && size >= 12) {
    ctx.strokeStyle = 'rgba(255,255,255,0.10)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    for (let x = x0; x <= x1 + 1; x++) { const px = Math.round(view.ox + x * size) + 0.5; ctx.moveTo(px, view.oy + y0 * size); ctx.lineTo(px, view.oy + (y1 + 1) * size); }
    for (let y = y0; y <= y1 + 1; y++) { const py = Math.round(view.oy + y * size) + 0.5; ctx.moveTo(view.ox + x0 * size, py); ctx.lineTo(view.ox + (x1 + 1) * size, py); }
    ctx.stroke();
  }

  // map border
  ctx.strokeStyle = '#f2b632';
  ctx.lineWidth = 2;
  ctx.strokeRect(view.ox, view.oy, map.w * size, map.h * size);

  // brush ghost / rectangle
  if (dragRect) {
    const a = Math.min(dragRect.x0, dragRect.x1), b = Math.min(dragRect.y0, dragRect.y1), c = Math.max(dragRect.x0, dragRect.x1), d = Math.max(dragRect.y0, dragRect.y1);
    ctx.strokeStyle = '#6dba79';
    ctx.strokeRect(view.ox + a * size, view.oy + b * size, (c - a + 1) * size, (d - b + 1) * size);
  } else if (hover && map.inside(hover.x, hover.y)) {
    ctx.globalAlpha = 0.6;
    if (brush && (tool === 'pencil' || tool === 'rect' || tool === 'fill')) {
      const px = view.ox + hover.x * size, py = view.oy + hover.y * size;
      if (brush.kind === 'tile') { const t = tileById.get(brush.id); if (t) drawPic(ctx, t.pic, px, py, size, size); }
      else if (brush.kind === 'object') drawObject(brush.id, px, py, size);
      else { ctx.fillStyle = hashColor(brush.id, 0.6); ctx.fillRect(px, py, size, size); }
    }

    ctx.globalAlpha = 1;
    ctx.strokeStyle = '#fff';
    ctx.lineWidth = 1;
    ctx.strokeRect(view.ox + hover.x * size + 0.5, view.oy + hover.y * size + 0.5, size - 1, size - 1);
  }
}

// Objects are drawn approximately as the game does: small pictures fill their tile, big decoration pictures are about two tiles wide; the bottom of the picture
// sits on the bottom of its tile and it is centred on it.
function drawObject(id, px, py, size) {
  const o = objById.get(id);
  if (!(o && o.pic)) {
    ctx.fillStyle = o ? 'rgba(200,200,200,0.55)' : 'rgba(230,80,60,0.8)';     // no picture: grey; unknown id: red
    ctx.fillRect(px + size * 0.2, py + size * 0.2, size * 0.6, size * 0.6);
    return;
  }

  const wTiles = o.pic.w >= 64 ? 2 : 1;
  const w = size * wTiles;
  const h = w * (o.pic.h / o.pic.w);
  drawPic(ctx, o.pic, px + (size - w) / 2, py + size - h, w, h);
}

// ================================================================================================================================
//  Editing
// ================================================================================================================================

function pushUndo() {
  undoStack.push(map.snapshot());
  if (undoStack.length > MAX_UNDO) undoStack.shift();
  redoStack.length = 0;
  updateUndoButtons();
}

function undo() {
  if (!map || !undoStack.length) return;
  redoStack.push(map.snapshot());
  map.restore(undoStack.pop());
  setDirty(true);
  updateUndoButtons();
  requestDraw();
}

function redo() {
  if (!map || !redoStack.length) return;
  undoStack.push(map.snapshot());
  map.restore(redoStack.pop());
  setDirty(true);
  updateUndoButtons();
  requestDraw();
}

function updateUndoButtons() {
  $('bUndo').disabled = !undoStack.length;
  $('bRedo').disabled = !redoStack.length;
}

function paintCell(x, y, shift) {
  if (!map.inside(x, y)) return;
  const before = map.get(x, y);
  const after = tool === 'erase' ? applyErase(before, shift) : brush ? applyBrush(before, brush) : before;
  if (JSON.stringify(canon(after)) !== JSON.stringify(before)) map.set(x, y, after);
}

function paintRect(a, b, shift) {
  for (let y = Math.min(a.y0, b.y1); y <= Math.max(a.y0, b.y1); y++) for (let x = Math.min(a.x0, b.x1); x <= Math.max(a.x0, b.x1); x++) paintCell(x, y, shift);
}

function pickFrom(x, y) {
  if (!map.inside(x, y)) return;
  const e = map.get(x, y);
  if (e.objs && e.objs.length) setBrush({ kind: 'object', id: e.objs[0].id });
  else if (e.regions && e.regions.length) setBrush({ kind: 'region', id: e.regions[0].id });
  else if (e.ground) setBrush({ kind: 'tile', id: e.ground });
}

let stroke = null;   // {kind, last}

function cellAt(ev) {
  const r = canvas.getBoundingClientRect();
  const size = cs();
  return { x: Math.floor((ev.clientX - r.left - view.ox) / size), y: Math.floor((ev.clientY - r.top - view.oy) / size) };
}

let panning = null;
let spaceDown = false;

canvas.addEventListener('contextmenu', (e) => e.preventDefault());

canvas.addEventListener('mousedown', (ev) => {
  if (!map) return;
  const c = cellAt(ev);
  if (ev.button === 1 || ev.button === 2 || tool === 'pan' || spaceDown) {
    panning = { x: ev.clientX, y: ev.clientY, ox: view.ox, oy: view.oy };
    return;
  }

  if (ev.button !== 0) return;
  if (tool === 'pick') { pickFrom(c.x, c.y); return; }
  if (tool === 'fill') {
    if (!brush || brush.kind !== 'tile') { say('Fill works with a ground tile: pick one from the Tiles tab.', 'bad'); return; }
    if (!map.inside(c.x, c.y)) return;
    const cells = floodGround(map, c.x, c.y, brush.id);
    if (!cells.length) return;
    pushUndo();
    for (const [x, y] of cells) paintCell(x, y);
    setDirty(true);
    say('Filled ' + cells.length + ' cells.', 'good');
    requestDraw();
    return;
  }

  if ((tool === 'pencil' || tool === 'rect') && !brush) { say('Pick something from the palette first.', 'bad'); return; }
  pushUndo();
  if (tool === 'rect') {
    dragRect = { x0: c.x, y0: c.y, x1: c.x, y1: c.y };
    stroke = { kind: 'rect', shift: ev.shiftKey };
  } else {
    stroke = { kind: 'paint', shift: ev.shiftKey, last: null };
    strokeTo(c);
  }
});

function strokeTo(c) {
  if (!stroke || stroke.kind !== 'paint') return;
  // walk from the last cell to this one so fast drags leave no gaps
  const from = stroke.last || c;
  let x = from.x, y = from.y;
  const dx = Math.abs(c.x - x), dy = Math.abs(c.y - y), sx = x < c.x ? 1 : -1, sy = y < c.y ? 1 : -1;
  let err = dx - dy;
  for (;;) {
    paintCell(x, y, stroke.shift);
    if (x === c.x && y === c.y) break;
    const e2 = 2 * err;
    if (e2 > -dy) { err -= dy; x += sx; }
    if (e2 < dx) { err += dx; y += sy; }
  }

  stroke.last = c;
  setDirty(true);
  requestDraw();
}

window.addEventListener('mousemove', (ev) => {
  if (panning) {
    view.ox = panning.ox + (ev.clientX - panning.x);
    view.oy = panning.oy + (ev.clientY - panning.y);
    requestDraw();
    return;
  }

  if (!map) return;
  const r = canvas.getBoundingClientRect();
  if (ev.clientX < r.left || ev.clientY < r.top || ev.clientX > r.right || ev.clientY > r.bottom) { if (!stroke) { hover = null; requestDraw(); } return; }
  const c = cellAt(ev);
  hover = c;
  $('stCell').textContent = map.inside(c.x, c.y) ? c.x + ', ' + c.y + '   ' + describe(map.get(c.x, c.y)) : '-';
  if (stroke && stroke.kind === 'paint') strokeTo(c);
  else if (stroke && stroke.kind === 'rect') { dragRect.x1 = c.x; dragRect.y1 = c.y; }
  requestDraw();
});

window.addEventListener('mouseup', () => {
  if (panning) { panning = null; return; }
  if (stroke && stroke.kind === 'rect' && dragRect) {
    paintRect(dragRect, dragRect, stroke.shift);
    setDirty(true);
    dragRect = null;
  }

  stroke = null;
  requestDraw();
});

function describe(e) {
  const parts = [];
  if (e.ground) parts.push(e.ground);
  if (e.objs) parts.push(e.objs.map((o) => o.id).join('+'));
  if (e.regions) parts.push('[' + e.regions.map((r) => r.id).join('+') + ']');
  return parts.join('  ') || '(empty)';
}

canvas.addEventListener('wheel', (ev) => {
  ev.preventDefault();
  const r = canvas.getBoundingClientRect();
  zoomAt(ev.clientX - r.left, ev.clientY - r.top, ev.deltaY < 0 ? 1.25 : 0.8);
}, { passive: false });

function zoomAt(sx, sy, factor) {
  const old = view.zoom;
  view.zoom = Math.min(16, Math.max(0.25, view.zoom * factor));
  const k = view.zoom / old;
  view.ox = sx - (sx - view.ox) * k;
  view.oy = sy - (sy - view.oy) * k;
  $('zoomLabel').textContent = Math.round(view.zoom * 50) + '%';
  requestDraw();
}

function fitMap() {
  if (!map) return;
  const r = $('stage').getBoundingClientRect();
  view.zoom = Math.min(16, Math.max(0.25, Math.min((r.width - 40) / (map.w * 16), (r.height - 40) / (map.h * 16))));
  view.ox = (r.width - map.w * cs()) / 2;
  view.oy = (r.height - map.h * cs()) / 2;
  $('zoomLabel').textContent = Math.round(view.zoom * 50) + '%';
  requestDraw();
}

// ================================================================================================================================
//  Palette
// ================================================================================================================================

function paletteEntries() {
  const q = query.trim().toLowerCase();
  const ok = (name) => !q || name.toLowerCase().includes(q);
  if (tab === 'tiles') return P.tiles.filter((t) => ok(t.id)).map((t) => ({ kind: 'tile', id: t.id, sub: t.walk ? '' : 'no walk', pic: t.pic }));
  if (tab === 'objects') {
    return P.objects.filter((o) => ok(o.id)).map((o) => ({ kind: 'object', id: o.id, sub: o.enemy ? 'enemy' : o.cls || '', pic: o.pic }));
  }

  return P.regions.filter((r) => ok(r.name)).map((r) => ({ kind: 'region', id: r.name, sub: '0x' + r.value.toString(16), pic: null }));
}

let paletteIcons = [];
function renderPalette() {
  const list = $('list');
  list.innerHTML = '';
  paletteIcons = [];
  for (const e of paletteEntries()) {
    const row = document.createElement('div');
    row.className = 'item' + (brush && brush.kind === e.kind && brush.id === e.id ? ' sel' : '');
    const cv = document.createElement('canvas');
    cv.width = 32; cv.height = 32;
    row.appendChild(cv);
    const n = document.createElement('span');
    n.className = 'n';
    n.textContent = e.id;
    row.appendChild(n);
    const t = document.createElement('span');
    t.className = 't';
    t.textContent = e.sub;
    row.appendChild(t);
    row.addEventListener('click', () => setBrush({ kind: e.kind, id: e.id }));
    list.appendChild(row);
    paletteIcons.push({ cv, e });
  }

  paintPaletteIcons();
}

function paintPaletteIcons() {
  for (const { cv, e } of paletteIcons) {
    const c = cv.getContext('2d');
    c.imageSmoothingEnabled = false;
    c.clearRect(0, 0, 32, 32);
    if (e.pic) {
      const s = Math.min(32 / e.pic.w, 32 / e.pic.h);
      drawPic(c, e.pic, (32 - e.pic.w * s) / 2, 32 - e.pic.h * s, e.pic.w * s, e.pic.h * s);
    } else if (e.kind === 'region') {
      c.fillStyle = hashColor(e.id, 0.8);
      c.fillRect(4, 4, 24, 24);
    } else {
      c.fillStyle = 'rgba(200,200,200,0.4)';
      c.fillRect(8, 8, 16, 16);
    }
  }
}

function setBrush(b) {
  brush = b;
  $('stBrush').textContent = b ? b.kind + ': ' + b.id : 'none';
  if (tool === 'pick' || tool === 'pan' || tool === 'erase') setTool('pencil');
  for (const row of $('list').children) row.classList.remove('sel');
  renderPalette();
  requestDraw();
}

function setTool(t) {
  tool = t;
  $('stTool').textContent = t;
  document.querySelectorAll('#tools button').forEach((b) => b.classList.toggle('on', b.dataset.tool === t));
  canvas.style.cursor = t === 'pan' ? 'grab' : 'crosshair';
}

document.querySelectorAll('#tools button').forEach((b) => b.addEventListener('click', () => setTool(b.dataset.tool)));
document.querySelectorAll('#tabs button').forEach((b) => b.addEventListener('click', () => {
  tab = b.dataset.tab;
  document.querySelectorAll('#tabs button').forEach((x) => x.classList.toggle('on', x === b));
  renderPalette();
}));
$('search').addEventListener('input', (e) => { query = e.target.value; renderPalette(); });

// ================================================================================================================================
//  Files
// ================================================================================================================================

const canPick = typeof window.showOpenFilePicker === 'function';

async function loadMapText(text, name, handle) {
  try {
    map = await MapModel.fromJm(text);
  } catch (err) {
    say(String(err.message || err), 'bad');
    return false;
  }

  mapName = name;
  mapFile = handle || null;
  undoStack.length = 0;
  redoStack.length = 0;
  updateUndoButtons();
  setDirty(false);
  fitMap();
  const unknown = new Set();
  for (const e of map.dict) {
    if (e.ground && !tileById.has(e.ground)) unknown.add('ground "' + e.ground + '"');
    for (const o of e.objs || []) if (!objById.has(o.id)) unknown.add('object "' + o.id + '"');
  }

  say(unknown.size ? 'Opened ' + name + '. Not in the game data (kept as they are): ' + [...unknown].slice(0, 4).join(', ') + (unknown.size > 4 ? ' ...' : '') : 'Opened ' + name + '.', unknown.size ? 'bad' : 'good');
  return true;
}

async function openMap() {
  if (canPick) {
    try {
      const [h] = await window.showOpenFilePicker({ types: [{ description: 'W&W map', accept: { 'application/json': ['.jm', '.json'] } }] });
      const f = await h.getFile();
      await loadMapText(await f.text(), f.name, h);
    } catch (err) { if (err.name !== 'AbortError') say(String(err.message || err), 'bad'); }
    return;
  }

  const inp = document.createElement('input');
  inp.type = 'file';
  inp.accept = '.jm,.json';
  inp.onchange = async () => { const f = inp.files[0]; if (f) await loadMapText(await f.text(), f.name, null); };
  inp.click();
}

async function saveMap(forceDownload) {
  if (!map) return;
  const text = await map.toJm();
  if (mapFile && !forceDownload) {
    try {
      const w = await mapFile.createWritable();
      await w.write(text);
      await w.close();
      setDirty(false);
      say('Saved ' + mapName + ' (' + Math.round(text.length / 1024) + ' KB).', 'good');
      return;
    } catch (err) { say('Could not save in place (' + (err.message || err) + '): downloading instead.', 'bad'); }
  }

  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([text], { type: 'application/json' }));
  a.download = mapName.endsWith('.jm') ? mapName : mapName + '.jm';
  a.click();
  URL.revokeObjectURL(a.href);
  setDirty(false);
  say('Downloaded ' + a.download + '. Put it in WaW-Server/Common/Resources/World/Data/ (and list it in Common.csproj if it is new).', 'good');
}

function newMapDialog() {
  const sel = $('nwGround');
  if (!sel.options.length) {
    for (const t of P.tiles) sel.add(new Option(t.id, t.id));
    sel.value = (P.tiles.find((t) => /grass/i.test(t.id)) || P.tiles[0]).id;
  }

  $('dlgNew').showModal();
}

$('nwCancel').onclick = () => $('dlgNew').close();
$('nwOk').onclick = () => {
  const w = Math.min(2048, Math.max(4, parseInt($('nwW').value, 10) || 64));
  const h = Math.min(2048, Math.max(4, parseInt($('nwH').value, 10) || 64));
  map = MapModel.blank(w, h, $('nwGround').value);
  mapName = 'newmap.jm';
  mapFile = null;
  undoStack.length = 0;
  redoStack.length = 0;
  updateUndoButtons();
  setDirty(true);
  fitMap();
  $('dlgNew').close();
  say('New ' + w + ' x ' + h + ' map.', 'good');
};

$('bNew').onclick = newMapDialog;
$('bOpen').onclick = openMap;
$('bSave').onclick = () => saveMap(false);
$('bSaveAs').onclick = () => saveMap(true);
$('bUndo').onclick = undo;
$('bRedo').onclick = redo;
$('bZoomIn').onclick = () => { const r = $('stage').getBoundingClientRect(); zoomAt(r.width / 2, r.height / 2, 1.25); };
$('bZoomOut').onclick = () => { const r = $('stage').getBoundingClientRect(); zoomAt(r.width / 2, r.height / 2, 0.8); };
$('bFit').onclick = fitMap;
for (const [id, key] of [['bGrid', 'grid'], ['bLayerGround', 'ground'], ['bLayerObjects', 'objects'], ['bLayerRegions', 'regions']]) {
  $(id).onclick = () => { layers[key] = !layers[key]; $(id).classList.toggle('on', layers[key]); requestDraw(); };
}

window.addEventListener('keydown', (ev) => {
  const typing = /INPUT|TEXTAREA|SELECT/.test((ev.target.tagName || ''));
  if (ev.code === 'Space' && !typing) { spaceDown = true; canvas.style.cursor = 'grab'; ev.preventDefault(); }
  if (typing) return;
  if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 'z') { ev.preventDefault(); ev.shiftKey ? redo() : undo(); return; }
  if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 'y') { ev.preventDefault(); redo(); return; }
  if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 's') { ev.preventDefault(); saveMap(false); return; }
  const k = ev.key.toLowerCase();
  const map2 = { b: 'pencil', r: 'rect', f: 'fill', e: 'erase', i: 'pick', h: 'pan' };
  if (map2[k] && !ev.ctrlKey) setTool(map2[k]);
  else if (k === 'g') $('bGrid').click();
  else if (k === '+' || k === '=') $('bZoomIn').click();
  else if (k === '-') $('bZoomOut').click();
});

window.addEventListener('keyup', (ev) => { if (ev.code === 'Space') { spaceDown = false; setTool(tool); } });
window.addEventListener('beforeunload', (ev) => { if (dirty) { ev.preventDefault(); ev.returnValue = ''; } });
window.addEventListener('resize', resizeCanvas);

// ================================================================================================================================
//  Item / Object maker
// ================================================================================================================================

const maker = { on: false, sheet: null, pic: null };

$('modeMap').onclick = () => setMode(false);
$('modeMaker').onclick = () => setMode(true);

function setMode(makerOn) {
  maker.on = makerOn;
  $('modeMap').classList.toggle('on', !makerOn);
  $('modeMaker').classList.toggle('on', makerOn);
  $('main').style.display = makerOn ? 'none' : 'grid';
  $('maker').style.display = makerOn ? 'grid' : 'none';
  $('mapButtons').style.display = makerOn ? 'none' : '';
  $('mapName').style.display = makerOn ? 'none' : '';
  if (makerOn) initMaker(); else resizeCanvas();
}

let makerReady = false;
function initMaker() {
  if (!makerReady) {
    makerReady = true;
    const sheet = $('mkSheet');
    for (const name of Object.keys(P.sheets).filter((n) => !P.sheets[n].animated)) sheet.add(new Option(name + '  (' + P.sheets[name].w + 'x' + P.sheets[name].h + ')', name));
    sheet.value = P.sheets.dungeonItems ? 'dungeonItems' : sheet.options[0].value;      // the item sheet (Dark Dungeon pack since 2026-09-21)
    for (const p of P.projectiles) $('mkProj').add(new Option(p, p));
    for (const id of ['mkKind', 'mkName', 'mkType', 'mkDesc', 'mkTier', 'mkSlot', 'mkMin', 'mkMax', 'mkRate', 'mkProj', 'mkSpeed', 'mkLife', 'mkShots', 'mkClass', 'mkSize', 'mkStatic', 'mkOccupy', 'mkTarget']) {
      $(id).addEventListener('input', refreshMaker);
      $(id).addEventListener('change', refreshMaker);
    }

    $('mkSheet').addEventListener('change', () => { maker.sheet = $('mkSheet').value; drawSheetPicker(); });
    $('mkKind').addEventListener('change', () => { $('mkType').value = '0x' + (($('mkKind').value === 'object' ? P.nextObjectType : P.nextItemType).toString(16)); $('mkTarget').value = $('mkKind').value === 'object' ? 'Objects.xml' : 'Equip.xml'; refreshMaker(); });
    $('sheetCanvas').addEventListener('click', pickFromSheet);
    $('mkType').value = '0x' + P.nextItemType.toString(16);
    maker.sheet = sheet.value;
  }

  drawSheetPicker();
  refreshMaker();
}

function drawSheetPicker() {
  const info = P.sheets[maker.sheet];
  const cv = $('sheetCanvas');
  const img = sheetImage(maker.sheet);
  if (!info || !img) { cv.width = 10; cv.height = 10; return; }
  const scale = Math.max(1, Math.min(8, Math.floor(700 / info.sheetW)));
  cv.width = info.sheetW * scale;
  cv.height = info.sheetH * scale;
  cv.dataset.scale = scale;
  const c = cv.getContext('2d');
  c.imageSmoothingEnabled = false;
  c.fillStyle = '#120e0a';
  c.fillRect(0, 0, cv.width, cv.height);
  c.drawImage(img, 0, 0, cv.width, cv.height);
  c.strokeStyle = 'rgba(255,255,255,0.18)';
  c.lineWidth = 1;
  c.beginPath();
  for (let x = 0; x <= info.sheetW; x += info.w) { c.moveTo(x * scale + 0.5, 0); c.lineTo(x * scale + 0.5, cv.height); }
  for (let y = 0; y <= info.sheetH; y += info.h) { c.moveTo(0, y * scale + 0.5); c.lineTo(cv.width, y * scale + 0.5); }
  c.stroke();
  if (maker.pic && maker.pic.sheet === maker.sheet) {
    c.strokeStyle = '#f2b632';
    c.lineWidth = 2;
    c.strokeRect(maker.pic.x * scale + 1, maker.pic.y * scale + 1, maker.pic.w * scale - 2, maker.pic.h * scale - 2);
  }
}

function pickFromSheet(ev) {
  const info = P.sheets[maker.sheet];
  const cv = $('sheetCanvas');
  const scale = Number(cv.dataset.scale || 1);
  const r = cv.getBoundingClientRect();
  const col = Math.floor(((ev.clientX - r.left) / scale) / info.w), row = Math.floor(((ev.clientY - r.top) / scale) / info.h);
  const cols = Math.floor(info.sheetW / info.w), rows = Math.floor(info.sheetH / info.h);
  if (col < 0 || row < 0 || col >= cols || row >= rows) return;
  maker.pic = { sheet: maker.sheet, x: col * info.w, y: row * info.h, w: info.w, h: info.h, index: row * cols + col };
  drawSheetPicker();
  refreshMaker();
}

const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

function buildXml() {
  const kind = $('mkKind').value;
  const name = $('mkName').value.trim();
  const type = $('mkType').value.trim();
  const desc = $('mkDesc').value.trim();
  const pic = maker.pic;
  const tex = pic ? '        <Texture>\n            <File>' + pic.sheet + '</File>\n            <Index>0x' + pic.index.toString(16) + '</Index>\n        </Texture>\n' : '';
  let x = '    <Object type="' + type + '" id="' + esc(name) + '">\n';
  if (kind === 'object') {
    x += '        <Class>' + $('mkClass').value + '</Class>\n' + tex;
    if ($('mkStatic').checked) x += '        <Static />\n';
    if ($('mkOccupy').checked) x += '        <OccupySquare />\n';
    if (Number($('mkSize').value) !== 100) x += '        <RealSize>' + Number($('mkSize').value) + '</RealSize>\n';
    if (desc) x += '        <Description>' + esc(desc) + '</Description>\n';
  } else {
    const slot = Number($('mkSlot').value);
    x += '        <Class>Equipment</Class>\n        <Item />\n' + tex;
    x += '        <SlotType>' + slot + '</SlotType>\n        <Tier>' + Number($('mkTier').value) + '</Tier>\n';
    if (desc) x += '        <Description>' + esc(desc) + '</Description>\n';
    if (kind === 'weapon') {
      x += '        <RateOfFire>' + Number($('mkRate').value) + '</RateOfFire>\n';
      x += '        <Sound>' + (slot === 17 ? 'weapon/fire_wand' : 'weapon/blunt_sword') + '</Sound>\n';
      x += '        <Projectile>\n            <ObjectId>' + esc($('mkProj').value) + '</ObjectId>\n            <Speed>' + Number($('mkSpeed').value) + '</Speed>\n' +
        '            <MinDamage>' + Number($('mkMin').value) + '</MinDamage>\n            <MaxDamage>' + Number($('mkMax').value) + '</MaxDamage>\n' +
        '            <LifetimeMS>' + Number($('mkLife').value) + '</LifetimeMS>\n        </Projectile>\n';
      if (Number($('mkShots').value) > 1) x += '        <NumProjectiles>' + Number($('mkShots').value) + '</NumProjectiles>\n';
    }

    x += '        <BagType>1</BagType>\n        <feedPower>5</feedPower>\n';
  }

  return x + '    </Object>\n';
}

// Everything that would make the new definition break the game or clash with an existing one.
function checkMaker() {
  const problems = [];
  const name = $('mkName').value.trim();
  const type = $('mkType').value.trim();
  const n = /^0x[0-9a-f]+$/i.test(type) ? parseInt(type, 16) : /^\d+$/.test(type) ? parseInt(type, 10) : NaN;
  if (!name) problems.push('Give it a name.');
  else if (objById.has(name) || tileById.has(name)) problems.push('The name "' + name + '" is already used by another definition.');
  else if (/[<>"&]/.test(name)) problems.push('The name cannot contain < > " or &.');
  if (Number.isNaN(n) || n < 1 || n > 0xffff) problems.push('The type id must be a number from 1 to 0xffff (e.g. 0xa05).');
  else if (P.usedTypes.includes(n)) problems.push('The type id 0x' + n.toString(16) + ' is already taken (next free: 0x' + ($('mkKind').value === 'object' ? P.nextObjectType : P.nextItemType).toString(16) + ').');
  if (!maker.pic) problems.push('Pick a picture from the sheet.');
  if ($('mkKind').value === 'weapon') {
    const lo = Number($('mkMin').value), hi = Number($('mkMax').value);
    if (!(lo >= 0 && hi >= lo)) problems.push('Maximum damage must be at least the minimum.');
    if (!P.projectiles.length) problems.push('There is no projectile object to shoot.');
  }

  return problems;
}

function refreshMaker() {
  const kind = $('mkKind').value;
  $('mkItemFields').style.display = kind === 'object' ? 'none' : '';
  $('mkWeaponFields').style.display = kind === 'weapon' ? '' : 'none';
  $('mkObjectFields').style.display = kind === 'object' ? '' : 'none';
  $('mkPicked').textContent = maker.pic ? maker.pic.sheet + ' #' + maker.pic.index + ' (0x' + maker.pic.index.toString(16) + ')' : 'nothing yet';
  const pv = $('mkPreview').getContext('2d');
  pv.imageSmoothingEnabled = false;
  pv.clearRect(0, 0, 64, 64);
  if (maker.pic) { const s = Math.min(64 / maker.pic.w, 64 / maker.pic.h); drawPic(pv, maker.pic, (64 - maker.pic.w * s) / 2, 64 - maker.pic.h * s, maker.pic.w * s, maker.pic.h * s); }
  const problems = checkMaker();
  $('mkProblems').innerHTML = problems.length ? '<div class="errs">' + problems.map((p) => '&bull; ' + esc(p)).join('<br>') + '</div>' : '<div class="oks">Looks good.</div>';
  $('xml').value = buildXml();
  $('mkAdd').disabled = problems.length > 0;
}

$('mkCopy').onclick = async () => {
  try { await navigator.clipboard.writeText($('xml').value); say('XML copied.', 'good'); } catch (e) { $('xml').select(); document.execCommand('copy'); say('XML copied.', 'good'); }
};

$('mkAdd').onclick = async () => {
  if (checkMaker().length) return;
  if (!canPick) { say('This browser cannot write files: use Copy XML and paste it into the file.', 'bad'); return; }
  try {
    const [h] = await window.showOpenFilePicker({ types: [{ description: 'Game XML', accept: { 'text/xml': ['.xml'] } }] });
    const f = await h.getFile();
    const text = await f.text();
    const name = $('mkName').value.trim();
    const type = $('mkType').value.trim();
    if (text.includes('id="' + esc(name) + '"')) { say(f.name + ' already has an object called "' + name + '".', 'bad'); return; }
    if (new RegExp('type="' + type.replace(/^0x/i, '0[xX]0*') + '"', 'i').test(text)) { say(f.name + ' already has type ' + type + '.', 'bad'); return; }
    const at = text.lastIndexOf('</Objects>');
    if (at < 0) { say(f.name + ' does not look like an Objects file (no closing </Objects>).', 'bad'); return; }
    const eol = text.includes('\r\n') ? '\r\n' : '\n';
    const out = text.slice(0, at).replace(/\s*$/, '') + eol + buildXml().replace(/\n/g, eol) + text.slice(at);
    const w = await h.createWritable();
    await w.write(out);
    await w.close();
    P.usedTypes.push(parseInt(type, 16));
    say('Added "' + name + '" to ' + f.name + '. Now run build_palette.py, rebuild and deploy the server + clients.', 'good');
    refreshMaker();
  } catch (err) { if (err.name !== 'AbortError') say(String(err.message || err), 'bad'); }
};

// ================================================================================================================================
//  Start
// ================================================================================================================================

renderPalette();
resizeCanvas();
updateUndoButtons();
setTool('pencil');
setDirty(false);
