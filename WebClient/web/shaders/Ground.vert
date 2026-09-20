#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
precision highp usampler2D;

uniform mat4 FullMatrix;
uniform float GameTime;

layout(location = 0) in vec2 iLocalPos;
layout(location = 1) in vec2 iLocalUV;
layout(location = 2) in vec4 iPosition;
layout(location = 3) in vec4 iUV;
layout(location = 4) in vec4 iAnimate;
layout(location = 5) in vec4 iMask;
layout(location = 6) in vec4 iTemp;

out vec2 GROUND_OUTPUT_baseUV;
out vec2 GROUND_OUTPUT_coreUV;
out vec4 GROUND_OUTPUT_UV;
out vec4 GROUND_OUTPUT_Mask;
out float GROUND_OUTPUT_Swizzle;

void main() {
    vec4 inputPosition = vec4(iLocalPos, 0, 1);
    inputPosition.xy = (inputPosition.xy - 0.5) * 1.002 + 0.5;
    inputPosition.xy += iPosition.xy;
    gl_Position = inputPosition * FullMatrix;

    GROUND_OUTPUT_baseUV = iLocalUV;
    GROUND_OUTPUT_coreUV.x = iLocalUV.x + iPosition.z + sin(GameTime * iAnimate.x) + GameTime * iAnimate.z;
    GROUND_OUTPUT_coreUV.y = iLocalUV.y + iPosition.w + sin(GameTime * iAnimate.y) + GameTime * iAnimate.w;
    GROUND_OUTPUT_UV = iUV;
    GROUND_OUTPUT_Mask = iMask;
    GROUND_OUTPUT_Swizzle = iTemp.x;
}
