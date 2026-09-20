#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
precision highp usampler2D;

#define ShadowBuffer 

uniform mat4 FullMatrix;
uniform mat4 BillMatrix;

const vec2 shadowPos[6] = vec2[6](
    vec2(-0.5, 0.25),
    vec2(0.5, 0.25),
    vec2(-0.5, -0.25),
    vec2(-0.5, -0.25),
    vec2(0.5, 0.25),
    vec2(0.5, -0.25)
);

const vec2 shadowUV[6] = vec2[6](
    vec2(0.0, 1.0),
    vec2(1.0, 1.0),
    vec2(0.0, 0.0),
    vec2(0.0, 0.0),
    vec2(1.0, 1.0),
    vec2(1.0, 0.0)
);

struct InstanceData {
    vec2 Position;
    float Scale;
    uint Color;
};




uniform highp usampler2D SsboTex0;   // instance data: STRIDE texels (uvec4) per instance, 2048 texels per row

uvec4 fetchTexel(int index) { return texelFetch(SsboTex0, ivec2(index % 2048, index / 2048), 0); }

InstanceData fetchInstance(int id) {
    uvec4 t = fetchTexel(id);
    InstanceData d;
    d.Position = uintBitsToFloat(t.xy);
    d.Scale = uintBitsToFloat(t.z);
    d.Color = t.w;
    return d;
}

out vec2 BaseUV;
flat out uint Color;

void main() {
    int instanceId = gl_VertexID / 6;
    int verId = gl_VertexID % 6;
    
    InstanceData data = fetchInstance(instanceId);
    
    vec4 pos = vec4(shadowPos[verId] * data.Scale, 0, 1) * BillMatrix;
    pos.xy += data.Position.xy;
    
    gl_Position = pos * FullMatrix;
    
    BaseUV = shadowUV[verId];
    Color = data.Color;
}