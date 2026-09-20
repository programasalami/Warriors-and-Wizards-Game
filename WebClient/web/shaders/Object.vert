#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
precision highp usampler2D;

uniform mat4 FullMatrix;
uniform mat4 BillMatrix;
uniform int RenderPass;

const int OpaquePass = 0;
const int OutlineGlowPass = 1;

layout(location = 0) in vec2 iLocalPos;
layout(location = 1) in vec2 iLocalUV;
layout(location = 2) in vec4 iPosition;
layout(location = 3) in vec4 iUV;
layout(location = 4) in vec4 iScale;
layout(location = 5) in vec4 iRotation;
layout(location = 6) in vec4 iExtra;
layout(location = 7) in vec4 iColor;
layout(location = 8) in vec4 iMask1;
layout(location = 9) in vec4 iMask2;

// Extra used to be a struct-typed varying in the middle of this interface block. Some
// GLSL compilers/drivers mishandle struct members inside in/out interface blocks and
// corrupt the plain vec2/vec4 varyings around them - on this GPU/driver it was zeroing
// out the per-pixel variation of BaseUV/UV (declared before it), making every sprite
// sample a single texel instead of its real texture. Flattened to a plain vec4 here.
out vec2 OBJECT_OUT_BaseUV;
out vec4 OBJECT_OUT_UV;
out vec4 OBJECT_OUT_Extra;
out vec4 OBJECT_OUT_Color;
out vec4 OBJECT_OUT_Mask1;
out vec4 OBJECT_OUT_Mask2;

const float TypeGameObject = 0.0;
const float TypeText = 3.0;
const float TypeBar = 4.0;
const float TypeEffect = 5.0;

vec2 GetUV(vec2 uv, float flip) {
    uv.x = 0.5 + (0.5 - uv.x) * flip;
    return uv;
}

void main() {
    float extraType = iExtra.x;
    float extraSortId = iExtra.y;

    if (RenderPass == OutlineGlowPass && extraType != TypeGameObject){
        // Discard vertex: push it well past the far clip plane with a normal, non-degenerate
        // w=1 (the old vec4(2,0,0,0) had w=0, a degenerate/infinite clip coordinate that some
        // older GPU drivers clip incorrectly instead of cleanly discarding).
        gl_Position = vec4(0, 0, 2, 1);
        return;
    }

    vec4 position = vec4(iLocalPos, 0, 1);
    position.xy *= iScale.xy;

    mat4 rotate = mat4(
        iRotation.y * iRotation.z, iRotation.x * iRotation.z, 0, iScale.z * iRotation.z * -iRotation.w,
        -iRotation.x * iRotation.z, iRotation.y * iRotation.z, 0, iScale.w * iRotation.z,
        0, 0, 1, 0,
        0, 0, 0, 1
    );

    position = position * rotate * BillMatrix;
    position.xyz += iPosition.xyz;
    position = position * FullMatrix;
    position.z = extraSortId;
    gl_Position = position;

    OBJECT_OUT_BaseUV = GetUV(iLocalUV, iRotation.w);
    OBJECT_OUT_UV = iUV;
    OBJECT_OUT_Extra = iExtra;
    OBJECT_OUT_Color = iColor;
    OBJECT_OUT_Mask1 = iMask1;
    OBJECT_OUT_Mask2 = iMask2;
}
