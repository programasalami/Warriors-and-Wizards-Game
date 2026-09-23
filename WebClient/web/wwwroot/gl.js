// WebGL2 implementation of the GL calls the client makes (see shim/GL.cs). Handles are small integers indexing tables below (0 = null).
// Desktop-GL features WebGL2 lacks are emulated here:
//   * separate attribute format API (glVertexAttribFormat / glBindVertexBuffer / glVertexAttribBinding / glVertexBindingDivisor):
//     recorded per VAO and turned into vertexAttribPointer calls just before each draw;
//   * DSA-style glProgramUniform*: temporarily switch programs;
//   * program-interface queries for uniform blocks;
//   * shader storage buffers: an RGBA32UI data texture bound to a fixed unit, read with texelFetch (see the ported shaders).
let gl = null;
let canvas = null;

const bufs = [null], progs = [null], shaders = [null], texs = [null], samplers = [null], vaos = [null], ulocs = [null], storages = [null];
const reg = (arr, o) => { arr.push(o); return arr.length - 1; };
// .NET's MemoryView (Span marshalled to JS) is not itself a typed array: _unsafe_create_view() gives a zero-copy typed-array view over WASM
// memory that is valid only during the call (fine, everything below uses it immediately); slice() is the copying fallback.
const view_of = (v) => (v._unsafe_create_view ? v._unsafe_create_view() : v.slice());

let curProg = null;          // WebGLProgram currently in use
let curProgId = 0;
let curVao = null;           // my VAO wrapper (or null = default)
const ssboBindings = [];     // index -> storage texture id
const SSBO_W = 2048;         // texels per row - must match tools/port_shaders.py
const SSBO_UNIT_BASE = 14;   // storage texture for binding index i lives on texture unit 14 - i

export function init(canvasId) {
    canvas = document.getElementById(canvasId);
    gl = canvas.getContext('webgl2', { alpha: false, antialias: false, depth: true, stencil: true, premultipliedAlpha: false, powerPreference: 'high-performance', preserveDrawingBuffer: false });
    if (!gl) return 0;
    window.__gl = gl;
    gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
    return 1;
}
export const canvasWidth = () => canvas.width;
export const canvasHeight = () => canvas.height;

// ---- simple pass-throughs ---------------------------------------------------------------------------------------------
export const activeTexture = (unit) => gl.activeTexture(unit);
export const attachShader = (p, s) => { if (shaders[s]._src && gl.getShaderParameter(shaders[s], gl.SHADER_TYPE) === gl.VERTEX_SHADER) progs[p]._vs = shaders[s]._src; gl.attachShader(progs[p], shaders[s]); };
export const detachShader = (p, s) => gl.detachShader(progs[p], shaders[s]);
export const bindSampler = (unit, s) => gl.bindSampler(unit, s ? samplers[s] : null);
export const bindTexture = (target, t) => gl.bindTexture(target, t ? texs[t] : null);
export const blendFunc = (s, d) => gl.blendFunc(s, d);
export const clear = (mask) => gl.clear(mask);
export const clearColor = (r, g, b, a) => gl.clearColor(r, g, b, a);
export const cullFace = (mode) => gl.cullFace(mode);
export const depthFunc = (f) => gl.depthFunc(f);
export const depthMask = (m) => gl.depthMask(m);
export const viewport = (x, y, w, h) => gl.viewport(x, y, w, h);
export const genBuffer = () => reg(bufs, gl.createBuffer());
export const genSampler = () => reg(samplers, gl.createSampler());
export const genTexture = () => reg(texs, gl.createTexture());
export const deleteBuffer = (b) => { if (bufs[b]) gl.deleteBuffer(bufs[b]); };
export const deleteSampler = (s) => { if (samplers[s]) gl.deleteSampler(samplers[s]); };
export const deleteShader = (s) => { if (shaders[s]) gl.deleteShader(shaders[s]); };
export const samplerParameteri = (s, pname, v) => gl.samplerParameteri(samplers[s], pname, v);
export const texStorage2D = (target, levels, ifmt, w, h) => gl.texStorage2D(target, levels, ifmt, w, h);
export const texSubImage2D = (target, level, x, y, w, h, format, type, data) => gl.texSubImage2D(target, level, x, y, w, h, format, type, view_of(data));

const CAPS = new Set([gl_const('BLEND'), gl_const('CULL_FACE'), gl_const('DEPTH_TEST'), gl_const('STENCIL_TEST'), gl_const('SCISSOR_TEST'),
    gl_const('POLYGON_OFFSET_FILL'), gl_const('DITHER'), gl_const('RASTERIZER_DISCARD'), gl_const('SAMPLE_ALPHA_TO_COVERAGE'), gl_const('SAMPLE_COVERAGE')]);
function gl_const(name) { return ({ BLEND: 0x0BE2, CULL_FACE: 0x0B44, DEPTH_TEST: 0x0B71, STENCIL_TEST: 0x0B90, SCISSOR_TEST: 0x0C11, POLYGON_OFFSET_FILL: 0x8037, DITHER: 0x0BD0, RASTERIZER_DISCARD: 0x8C89, SAMPLE_ALPHA_TO_COVERAGE: 0x809E, SAMPLE_COVERAGE: 0x80A0 })[name]; }
export const enable = (cap) => { if (CAPS.has(cap)) gl.enable(cap); };     // desktop-only caps (FRAMEBUFFER_SRGB, debug output...) are ignored
export const disable = (cap) => { if (CAPS.has(cap)) gl.disable(cap); };

// ---- buffers ------------------------------------------------------------------------------------------------------------
export const bindBuffer = (target, b) => gl.bindBuffer(target, b ? bufs[b] : null);
export const bufferData = (target, size, usage) => gl.bufferData(target, size, usage);
export const bufferSubData = (target, offset, view) => gl.bufferSubData(target, offset, view_of(view));
export function bindBufferBase(target, index, b) {
    if (target === 0x90D2) return;   // shader storage buffer: emulated by storage textures (StorageBuffer<T>), nothing to bind
    gl.bindBufferBase(target, index, b ? bufs[b] : null);
}

// ---- vertex arrays with the separate attribute-format API -----------------------------------------------------------
function makeVao() {
    const attribs = [], bindings = [];
    for (let i = 0; i < 16; i++) {
        attribs.push({ enabled: false, size: 4, type: 0x1406, norm: false, rel: 0, isInt: false, binding: 0, direct: false });
        bindings.push({ buf: null, offset: 0, stride: 0, divisor: 0 });
    }
    return { native: gl.createVertexArray(), attribs, bindings, dirty: false };
}
export const genVertexArray = () => reg(vaos, makeVao());
export const deleteVertexArray = (id) => { const v = vaos[id]; if (v) { if (curVao === v) { curVao = null; gl.bindVertexArray(null); } gl.deleteVertexArray(v.native); vaos[id] = null; } };
export function bindVertexArray(id) {
    curVao = id ? vaos[id] : null;
    gl.bindVertexArray(curVao ? curVao.native : null);
}
export function enableVertexAttribArray(loc) {
    if (curVao) { curVao.attribs[loc].enabled = true; curVao.dirty = true; } else gl.enableVertexAttribArray(loc);
}
export function vertexAttribFormat(loc, size, type, norm, rel) {
    if (!curVao) return; const a = curVao.attribs[loc]; a.size = size; a.type = type; a.norm = norm; a.rel = rel; a.isInt = false; a.direct = false; curVao.dirty = true;
}
export function vertexAttribIFormat(loc, size, type, rel) {
    if (!curVao) return; const a = curVao.attribs[loc]; a.size = size; a.type = type; a.norm = false; a.rel = rel; a.isInt = true; a.direct = false; curVao.dirty = true;
}
export function vertexAttribBinding(loc, binding) { if (!curVao) return; curVao.attribs[loc].binding = binding; curVao.dirty = true; }
export function bindVertexBuffer(binding, b, offset, stride) {
    if (!curVao) return; const v = curVao.bindings[binding]; v.buf = b ? bufs[b] : null; v.offset = offset; v.stride = stride; curVao.dirty = true;
}
export function vertexBindingDivisor(binding, d) { if (!curVao) return; curVao.bindings[binding].divisor = d; curVao.dirty = true; }
export function vertexAttribPointer(loc, size, type, norm, stride, offset) {   // classic API: uses the current ARRAY_BUFFER binding
    gl.vertexAttribPointer(loc, size, type, norm, stride, offset);
    gl.enableVertexAttribArray(loc);
    if (curVao) curVao.attribs[loc].direct = true;
}
function flushVao() {
    const v = curVao;
    if (!v || !v.dirty) return;
    for (let loc = 0; loc < v.attribs.length; loc++) {
        const a = v.attribs[loc];
        if (a.direct) continue;
        if (!a.enabled) continue;
        const b = v.bindings[a.binding];
        if (!b.buf) continue;
        gl.bindBuffer(gl.ARRAY_BUFFER, b.buf);
        if (a.isInt) gl.vertexAttribIPointer(loc, a.size, a.type, b.stride, b.offset + a.rel);
        else gl.vertexAttribPointer(loc, a.size, a.type, a.norm, b.stride, b.offset + a.rel);
        gl.enableVertexAttribArray(loc);
        gl.vertexAttribDivisor(loc, b.divisor);
    }
    v.dirty = false;
}

// ---- storage-buffer emulation -----------------------------------------------------------------------------------------
export function createStorageTexture(bytes) {
    const texels = Math.ceil(bytes / 16);
    const rows = Math.max(1, Math.ceil(texels / SSBO_W));
    const t = gl.createTexture();
    gl.activeTexture(gl.TEXTURE0 + 15);
    gl.bindTexture(gl.TEXTURE_2D, t);
    gl.texStorage2D(gl.TEXTURE_2D, 1, gl.RGBA32UI, SSBO_W, rows);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    return reg(storages, { tex: t, rows });
}
export function storageTextureData(id, view) {
    const s = storages[id];
    const src = view_of(view);
    const count = src.length;                               // int32 count
    const texels = Math.ceil(count / 4);
    const rows = Math.min(s.rows, Math.max(1, Math.ceil(texels / SSBO_W)));
    const padded = new Uint32Array(rows * SSBO_W * 4);
    padded.set(new Uint32Array(src.buffer, src.byteOffset, count).subarray(0, Math.min(count, padded.length)));
    gl.activeTexture(gl.TEXTURE0 + 15);
    gl.bindTexture(gl.TEXTURE_2D, s.tex);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, SSBO_W, rows, gl.RGBA_INTEGER, gl.UNSIGNED_INT, padded);
}
export function bindStorageTexture(index, id) { ssboBindings[index] = id; }
const ssboUniformCache = new Map();
function bindStorageTexturesForDraw() {
    for (let i = 0; i < ssboBindings.length; i++) {
        const id = ssboBindings[i];
        if (!id) continue;
        const unit = SSBO_UNIT_BASE - i;
        gl.activeTexture(gl.TEXTURE0 + unit);
        gl.bindTexture(gl.TEXTURE_2D, storages[id].tex);
        gl.bindSampler(unit, null);
        if (curProg) {
            const key = curProgId + ':' + i;
            let loc = ssboUniformCache.get(key);
            if (loc === undefined) { loc = gl.getUniformLocation(curProg, 'SsboTex' + i); ssboUniformCache.set(key, loc); }
            if (loc) gl.uniform1i(loc, unit);
        }
    }
}

// ---- draws --------------------------------------------------------------------------------------------------------------
const debugDraws = new URLSearchParams(location.search).has('gldebug');
function checkDraw(kind, mode, count) {   // ?gldebug=1 : report the first GL error after a draw, once per program
    if (!debugDraws) return;
    const e = gl.getError();
    if (e === 0) return;
    const key = curProgId + ':' + e;
    if (checkDraw.seen === undefined) checkDraw.seen = new Set();
    if (checkDraw.seen.has(key)) return;
    checkDraw.seen.add(key);
    const vs = (progs[curProgId] && progs[curProgId]._vs) || '';
    console.log('[gldebug] ' + kind + ' error 0x' + e.toString(16) + ' program #' + curProgId + ' mode ' + mode + ' count ' + count + ' vs: ' + vs.split(String.fromCharCode(10)).slice(5, 9).join(' | '));
}
export function drawArrays(mode, first, count) { flushVao(); bindStorageTexturesForDraw(); gl.drawArrays(mode, first, count); checkDraw('drawArrays', mode, count); }
export function drawElements(mode, count, type, offset) { flushVao(); bindStorageTexturesForDraw(); gl.drawElements(mode, count, type, offset); checkDraw('drawElements', mode, count); }

// ---- shaders / programs ------------------------------------------------------------------------------------------------
export const createShader = (type) => reg(shaders, gl.createShader(type));
export const createProgram = () => reg(progs, gl.createProgram());
export const shaderSource = (s, src) => { shaders[s]._src = src; gl.shaderSource(shaders[s], src); };
export const compileShader = (s) => gl.compileShader(shaders[s]);
export const getShaderi = (s, pname) => { const v = gl.getShaderParameter(shaders[s], pname); return v === true ? 1 : v === false ? 0 : v; };
export const getShaderInfoLog = (s) => gl.getShaderInfoLog(shaders[s]) || '';
export function linkProgram(p) {
    const prog = progs[p];
    gl.linkProgram(prog);
    if (gl.getProgramParameter(prog, gl.LINK_STATUS)) {
        // uniform block i -> binding i, so BindBufferBase(UniformBuffer, i, buf) reaches block i (desktop uses layout(binding=..))
        const n = gl.getProgramParameter(prog, gl.ACTIVE_UNIFORM_BLOCKS);
        for (let i = 0; i < n; i++) gl.uniformBlockBinding(prog, i, i);
    }
}
export function getProgrami(p, pname) {
    const prog = progs[p];
    if (pname === 0x8B87) {   // ACTIVE_UNIFORM_MAX_LENGTH (not available in WebGL): compute
        const n = gl.getProgramParameter(prog, gl.ACTIVE_UNIFORMS); let max = 0;
        for (let i = 0; i < n; i++) max = Math.max(max, gl.getActiveUniform(prog, i).name.length + 1);
        return max;
    }
    const v = gl.getProgramParameter(prog, pname);
    return v === true ? 1 : v === false ? 0 : v;
}
export const getProgramInfoLog = (p) => gl.getProgramInfoLog(progs[p]) || '';
export const getActiveUniformName = (p, i) => gl.getActiveUniform(progs[p], i).name;
export const getActiveUniformSize = (p, i) => gl.getActiveUniform(progs[p], i).size;
export const getActiveUniformType = (p, i) => gl.getActiveUniform(progs[p], i).type;
export const getAttribLocation = (p, name) => gl.getAttribLocation(progs[p], name);
export const getUniformBlockCount = (p) => gl.getProgramParameter(progs[p], gl.ACTIVE_UNIFORM_BLOCKS);
export const getUniformBlockName = (p, i) => gl.getActiveUniformBlockName(progs[p], i);
const uniformIds = new Map();
export function getUniformLocation(p, name) {
    const key = p + ':' + name;
    let id = uniformIds.get(key);
    if (id !== undefined) return id;
    const loc = gl.getUniformLocation(progs[p], name);
    id = loc ? reg(ulocs, loc) : -1;
    uniformIds.set(key, id);
    return id;
}
export function useProgram(p) { curProgId = p; curProg = p ? progs[p] : null; gl.useProgram(curProg); }
function withProgram(p, fn) {
    const prog = progs[p];
    if (prog === curProg) { fn(); return; }
    gl.useProgram(prog); fn(); gl.useProgram(curProg);
}
export function programUniform1i(p, loc, v) { if (loc < 0) return; withProgram(p, () => gl.uniform1i(ulocs[loc], v)); }
export function programUniform1f(p, loc, v) { if (loc < 0) return; withProgram(p, () => gl.uniform1f(ulocs[loc], v)); }
function floatsOf(view, n) {   // view is a Uint8Array over wasm memory holding n floats; copy so alignment never matters
    const out = new Float32Array(n);
    new Uint8Array(out.buffer).set(view_of(view).subarray(0, n * 4));
    return out;
}
export function programUniformNf(p, loc, n, count, view) {
    if (loc < 0) return;
    const f = floatsOf(view, n * count);
    withProgram(p, () => { const u = ulocs[loc]; if (n === 2) gl.uniform2fv(u, f); else if (n === 3) gl.uniform3fv(u, f); else gl.uniform4fv(u, f); });
}
export function programUniformMatrix4f(p, loc, count, transpose, view) {
    if (loc < 0) return;
    let f = floatsOf(view, 16 * count);
    if (transpose) {   // WebGL wants column-major; transpose by hand
        const t = new Float32Array(16 * count);
        for (let m = 0; m < count; m++) for (let r = 0; r < 4; r++) for (let c = 0; c < 4; c++) t[m * 16 + c * 4 + r] = f[m * 16 + r * 4 + c];
        f = t;
    }
    withProgram(p, () => gl.uniformMatrix4fv(ulocs[loc], false, f));
}
