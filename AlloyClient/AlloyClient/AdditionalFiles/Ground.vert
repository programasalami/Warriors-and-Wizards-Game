#version 330 core

uniform mat4 FullMatrix;
uniform float GameTime;

layout(location = 0) in vec2 iLocalPos;
layout(location = 1) in vec2 iLocalUV;
layout(location = 2) in vec4 iPosition;
layout(location = 3) in vec4 iUV;
layout(location = 4) in vec4 iAnimate;
layout(location = 5) in vec4 iMask;
layout(location = 6) in vec4 iTemp;

out GROUND_OUTPUT {
    vec2 baseUV;
    vec2 coreUV;
    vec4 UV;
    vec4 Mask;
    float Swizzle;
} vsOutput;

void main() {
    vec4 inputPosition = vec4(iLocalPos, 0, 1);
    inputPosition.xy = (inputPosition.xy - 0.5) * 1.002 + 0.5;
    inputPosition.xy += iPosition.xy;
    gl_Position = inputPosition * FullMatrix;

    vsOutput.baseUV = iLocalUV;
    vsOutput.coreUV.x = iLocalUV.x + iPosition.z + sin(GameTime * iAnimate.x) + GameTime * iAnimate.z;
    vsOutput.coreUV.y = iLocalUV.y + iPosition.w + sin(GameTime * iAnimate.y) + GameTime * iAnimate.w;
    vsOutput.UV = iUV;
    vsOutput.Mask = iMask;
    vsOutput.Swizzle = iTemp.x;
}
