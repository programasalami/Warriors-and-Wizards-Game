"""Ports the desktop GLSL 330/430 shaders to WebGL2 (GLSL ES 3.00) into web/shaders. Rerun after shader changes upstream.
What changes: the #version header (+ default precisions), interface blocks (`in/out VS_OUT { .. } name;`, not allowed in ES 3.00)
become plain varyings named name_member, and the shader-storage-buffer reads (Ui.vert / Particle.vert) become texelFetch reads of the
RGBA32UI data texture that StorageBuffer<T> is emulated with (see shim/StorageBuffer.cs and wwwroot/gl.js)."""
import os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
LT = os.path.normpath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(LT, 'WebClient', 'web', 'shaders')
SRC_DIRS = [os.path.join(LT, 'AlloyClient', 'Alloy.UiLib', 'AdditionalFiles'), os.path.join(LT, 'AlloyClient', 'AlloyClient', 'AdditionalFiles')]
TEX_W = 2048   # must match SSBO_W in gl.js

HEADER = ('#version 300 es\n'
          'precision highp float;\nprecision highp int;\nprecision highp sampler2D;\nprecision highp usampler2D;\n')

BLOCK_RE = re.compile(r'(?P<dir>in|out)\s+(?P<block>\w+)\s*\{(?P<body>.*?)\}\s*(?P<inst>\w+)\s*;', re.S)


def convert_blocks(text):
    """`in/out BLOCK { [flat] type name; ... } inst;`  ->  `flat in/out type inst_name;` and inst.name -> inst_name"""
    def repl(m):
        d, inst = m.group('dir'), m.group('block')   # prefix = the BLOCK name so vertex and fragment varyings match
        lines = []
        for member in m.group('body').split(';'):
            member = member.strip()
            if not member:
                continue
            flat = ''
            if member.startswith('flat '):
                flat = 'flat '
                member = member[5:].strip()
            typ, name = member.rsplit(None, 1)
            lines.append('%s%s %s %s_%s;' % (flat, d, typ, inst, name))
        return '\n'.join(lines)
    while True:
        m = BLOCK_RE.search(text)
        if not m:
            break
        inst = m.group('inst')
        blk = m.group('block')
        new = repl(m)
        text = text[:m.start()] + new + text[m.end():]
        text = re.sub(r'\b%s\.(\w+)' % re.escape(inst), r'%s_\1' % blk, text)
    return text


FETCH_HELPER = '''
uniform highp usampler2D SsboTex0;   // instance data: STRIDE texels (uvec4) per instance, %d texels per row

uvec4 fetchTexel(int index) { return texelFetch(SsboTex0, ivec2(index %% %d, index / %d), 0); }
''' % (TEX_W, TEX_W, TEX_W)


def port_ui_vert(text):
    # struct stays; the buffer block goes away, replaced by a fetch function
    text = re.sub(r'layout\(std140, binding = 0\) readonly buffer InstanceBuffer \{\s*InstanceData data\[\];\s*\} instanceBuffer;', '', text)
    fetch = FETCH_HELPER + '''
InstanceData fetchInstance(uint id) {
    int b = int(id) * 7;
    uvec4 t0 = fetchTexel(b + 0);
    uvec4 t1 = fetchTexel(b + 1);
    uvec4 t2 = fetchTexel(b + 2);
    uvec4 t3 = fetchTexel(b + 3);
    uvec4 t4 = fetchTexel(b + 4);
    uvec4 t5 = fetchTexel(b + 5);
    uvec4 t6 = fetchTexel(b + 6);
    InstanceData d;
    d.VertexScale = uintBitsToFloat(t0.xy);
    d.VertexRotation = uintBitsToFloat(t0.zw);
    d.VertexOffset = uintBitsToFloat(t1.xy);
    d.VertexAnchor = uintBitsToFloat(t1.zw);
    d.Color = t2.x;
    d.ColorOverride = t2.y;
    d.Info = uintBitsToFloat(t2.zw);
    d.Scissor = uintBitsToFloat(t3);
    d.Extra1 = uintBitsToFloat(t4);
    d.Extra2 = uintBitsToFloat(t5);
    d.ColorTransform = uintBitsToFloat(t6);
    return d;
}
'''
    i = text.index('layout (location = 0) in')
    text = text[:i] + fetch + '\n' + text[i:]
    text = text.replace('InstanceData data = instanceBuffer.data[InstanceId];', 'InstanceData data = fetchInstance(InstanceId);')
    return text


def port_particle_vert(text):
    text = re.sub(r'layout\(std140, binding = 0\) readonly buffer InstanceBuffer \{\s*InstanceData data\[\];\s*\} instanceBuffer;', '', text)
    fetch = FETCH_HELPER + '''
InstanceData fetchInstance(int id) {
    InstanceData d;
    d.Position = uintBitsToFloat(fetchTexel(id * 2));
    d.Color = uintBitsToFloat(fetchTexel(id * 2 + 1));
    return d;
}
'''
    i = text.index('out vec2 BaseUV;')
    text = text[:i] + fetch + '\n' + text[i:]
    assert 'instanceBuffer.data[instanceId]' in text
    return text.replace('instanceBuffer.data[instanceId]', 'fetchInstance(instanceId)')


def port_shadow_vert(text):
    # The shadow uniform block (1000 x 16 bytes, dynamically indexed) fails to compile on some D3D11 drivers under ANGLE, so shadows are read from the
    # same kind of RGBA32UI data texture as the sprites: one texel (uvec4) per shadow {Position.xy, Scale, Color}. See shim/UniformBuffer.cs.
    text, n = re.subn(r'layout\(std140\) uniform ShadowData \{\s*InstanceData data\[ShadowBuffer\];\s*\} instanceBuffer;', '', text)
    assert n == 1, 'Shadow.vert: uniform block not found'
    fetch = FETCH_HELPER + '''
InstanceData fetchInstance(int id) {
    uvec4 t = fetchTexel(id);
    InstanceData d;
    d.Position = uintBitsToFloat(t.xy);
    d.Scale = uintBitsToFloat(t.z);
    d.Color = t.w;
    return d;
}
'''
    i = text.index('out vec2 BaseUV;')
    text = text[:i] + fetch + '\n' + text[i:]
    assert 'instanceBuffer.data[instanceId]' in text
    return text.replace('instanceBuffer.data[instanceId]', 'fetchInstance(instanceId)')


# GLSL ES 3.00 has no implicit int -> float conversion (desktop GLSL 330 does): literal fixes, exact text -> replacement.
# (old, new, expected_count)
FIXES = {
    'Object.frag': [
        ('(pRange - 12) / pRange', '(pRange - 12.0) / pRange', 1),
        ('outputColor.a == 0)', 'outputColor.a == 0.0)', 1),
    ],
    'Particle.frag': [
        ('float scale = 1;', 'float scale = 1.0;', 1),
        ('BaseUV.x - dx <= 0 || BaseUV.y - dy <= 0 || BaseUV.x + dx >= 1 || BaseUV.y + dy >= 1',
         'BaseUV.x - dx <= 0.0 || BaseUV.y - dy <= 0.0 || BaseUV.x + dx >= 1.0 || BaseUV.y + dy >= 1.0', 1),
    ],
    'Ui.frag': [
        ('map(val, 0, border,', 'map(val, 0.0, border,', 1),
        ('map(val, 1.0 - border, 1, ', 'map(val, 1.0 - border, 1.0, ', 1),
        ('color.a > 0)', 'color.a > 0.0)', 1),
        ('color.a > 0 &&', 'color.a > 0.0 &&', 1),
        ('min(4, ', 'min(4.0, ', 1),
        ('max(1, scale)', 'max(1.0, scale)', 1),
        ('max(6, 6.0 * scale)', 'max(6.0, 6.0 * scale)', 1),
        ('for (float i = 1; i <= glowSize', 'for (float i = 1.0; i <= glowSize', 1),
        ('.a == 0){', '.a == 0.0){', 1),
        ('exp(-normalized * 4)', 'exp(-normalized * 4.0)', 1),
        ('coords.x < 0 || coords.x > 1 || coords.y < 0 || coords.y > 1', 'coords.x < 0.0 || coords.x > 1.0 || coords.y < 0.0 || coords.y > 1.0', 1),
        ('ry * ry) > 1)', 'ry * ry) > 1.0)', 1),
        ('if (inner > 1) {', 'if (inner > 1.0) {', 1),
        ('color_val = 1;', 'color_val = 1.0;', 1),
        ('color_val = 0;', 'color_val = 0.0;', 1),
    ],
}


def port(name, text):
    text = text.lstrip('﻿')
    text = re.sub(r'^\s*#version[^\n]*\n', '', text, count=1)
    text = convert_blocks(text)
    text = re.sub(r'\b(in|out)\s+flat\b', r'flat \1', text)          # ES wants interpolation qualifiers before storage qualifiers
    text = re.sub(r'(VS_OUT_Extra[12]\.\w+\s*[!=]=\s*)-1\b(?!\.)', r'\1-1.0', text)
    if name == 'Ui.vert':
        text = port_ui_vert(text)
    if name == 'Particle.vert':
        text = port_particle_vert(text)
    if name == 'Shadow.vert':
        text = port_shadow_vert(text)
    for old, new, expect in FIXES.get(name, []):
        n = text.count(old)
        if n != expect:
            sys.exit('SHADER FIX FAILED in %s: expected %d x %r, found %d' % (name, expect, old, n))
        text = text.replace(old, new)
    return HEADER + text


os.makedirs(OUT, exist_ok=True)
count = 0
for d in SRC_DIRS:
    for f in sorted(os.listdir(d)):
        if f.endswith('.vert') or f.endswith('.frag'):
            src = open(os.path.join(d, f), encoding='utf-8-sig').read()
            open(os.path.join(OUT, f), 'w', encoding='utf-8', newline='\n').write(port(f, src))
            count += 1
print('ported %d shaders into %s' % (count, OUT))
