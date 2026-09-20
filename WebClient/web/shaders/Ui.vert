#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
precision highp usampler2D;

uniform mat4 ViewMatrix;

struct InstanceData {
    // Vertex Changes
    vec2 VertexScale;
    vec2 VertexRotation;
    vec2 VertexOffset;
    vec2 VertexAnchor;

    // Sprite Data
    uint Color;
    uint ColorOverride;
    vec2 Info;
    vec4 Scissor;
    vec4 Extra1;
    vec4 Extra2;
    vec4 ColorTransform;
};




uniform highp usampler2D SsboTex0;   // instance data: STRIDE texels (uvec4) per instance, 2048 texels per row

uvec4 fetchTexel(int index) { return texelFetch(SsboTex0, ivec2(index % 2048, index / 2048), 0); }

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

layout (location = 0) in vec2 Position;
layout (location = 1) in vec2 UVCoords;
layout (location = 2) in uint Color;
layout (location = 3) in uint InstanceId;

out vec4 VS_OUT_Position1;
flat out uint VS_OUT_Color;
flat out uint VS_OUT_Override;
out vec2 VS_OUT_Info;
out vec2 VS_OUT_UVCoords;
out vec4 VS_OUT_Scissor;
out vec4 VS_OUT_Extra1;
out vec4 VS_OUT_Extra2;
out vec4 VS_OUT_ColorTransform;

void main() {
    InstanceData data = fetchInstance(InstanceId);
    float rotation = data.VertexRotation.x;
    vec2 pos = Position + data.VertexAnchor;
    float x = (pos.x * cos(rotation) - pos.y * sin(rotation) - data.VertexAnchor.x) * data.VertexScale.x + data.VertexOffset.x;
    float y = (pos.x * sin(rotation) + pos.y * cos(rotation) - data.VertexAnchor.y) * data.VertexScale.y + data.VertexOffset.y;
    pos = vec2(x, y);
    
    gl_Position = vec4(pos, 0, 1) * ViewMatrix;
    VS_OUT_Position1 = gl_Position;
    VS_OUT_Color = (Color == 0u) ? data.Color : Color;
    VS_OUT_Override = data.ColorOverride;
    VS_OUT_Info = data.Info;
    VS_OUT_UVCoords = UVCoords;
    VS_OUT_Scissor.xy = (vec4(data.Scissor.x, data.Scissor.y, 0, 1) * ViewMatrix).xy;
    VS_OUT_Scissor.zw = (vec4(data.Scissor.z, data.Scissor.w, 0, 1) * ViewMatrix).xy;
    VS_OUT_Extra1 = data.Extra1;
    VS_OUT_Extra2 = data.Extra2;
    VS_OUT_ColorTransform = data.ColorTransform;
}