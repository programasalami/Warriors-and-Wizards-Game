#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
precision highp usampler2D;

uniform mat4 FullMatrix;

layout (location = 0) in vec3 Position;
layout (location = 1) in vec2 BaseUV;
layout (location = 2) in vec3 iPosition;
layout (location = 3) in vec4 iUV;
layout (location = 4) in vec3 iExtra;

out vec2 MODEL_OUT_BaseUV;
out vec4 MODEL_OUT_UV;
out vec3 MODEL_OUT_Extra;
out float MODEL_OUT_Zed;

void main() {
    float s = sin(iExtra.x);
    float c = cos(iExtra.x);

    vec4 pos = vec4(Position.xy * mat2(c, -s, s, c), Position.z, 1);
    pos = vec4((pos.xy - 0.5) * 1.005 + 0.5, pos.zw);
    pos.xyz += iPosition;

    MODEL_OUT_BaseUV = BaseUV;
    MODEL_OUT_UV = iUV;
    MODEL_OUT_Extra = iExtra;
    MODEL_OUT_Zed = pos.z;

    pos = pos * FullMatrix;
    pos.z = iExtra.y;

    gl_Position = pos;
}
