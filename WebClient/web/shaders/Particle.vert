#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
precision highp usampler2D;

uniform mat4 FullMatrix;
uniform mat4 BillMatrix;

const vec2 particlePos[6] = vec2[6](
    vec2(-0.1, 0.1),
    vec2(0.1, 0.1),
    vec2(-0.1, -0.1),
    vec2(-0.1, -0.1),
    vec2(0.1, 0.1),
    vec2(0.1, -0.1)
);

const vec2 particleUV[6] = vec2[6](
    vec2(0.0, 1.0),
    vec2(1.0, 1.0),
    vec2(0.0, 0.0),
    vec2(0.0, 0.0),
    vec2(1.0, 1.0),
    vec2(1.0, 0.0)
);

struct InstanceData {
    vec4 Position;
    vec4 Color;
};




uniform highp usampler2D SsboTex0;   // instance data: STRIDE texels (uvec4) per instance, 2048 texels per row

uvec4 fetchTexel(int index) { return texelFetch(SsboTex0, ivec2(index % 2048, index / 2048), 0); }

InstanceData fetchInstance(int id) {
    InstanceData d;
    d.Position = uintBitsToFloat(fetchTexel(id * 2));
    d.Color = uintBitsToFloat(fetchTexel(id * 2 + 1));
    return d;
}

out vec2 BaseUV;
out vec4 Color;
out float Depth;

void main() {
    int instanceId = gl_VertexID / 6;
    int verId = gl_VertexID % 6;
    
    InstanceData data = fetchInstance(instanceId);
    
    vec4 pos = vec4(particlePos[verId] * data.Position.w, 0, 1.0) * BillMatrix;
    
    
    BaseUV = particleUV[verId];
    Color = data.Color;
    
    vec4 depth = vec4(data.Position.xy, 0, 1) * FullMatrix;
    
    pos.xyz += data.Position.xyz;
    pos = pos * FullMatrix;
    pos.z = 0.5f + 0.4f * depth.y;
    
    gl_Position = pos;
}