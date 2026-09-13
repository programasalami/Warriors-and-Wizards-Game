#version 330 core
precision highp float;

// Extra used to be a struct-typed varying in the middle of this interface block. Some
// GLSL compilers/drivers mishandle struct members inside in/out interface blocks and
// corrupt the plain vec2/vec4 varyings around them - on this GPU/driver it was zeroing
// out the per-pixel variation of BaseUV/UV (declared before it), making every sprite
// sample a single texel instead of its real texture. Flattened to a plain vec4 here:
// x=Type, y=SortId, z=Shade, w=Alpha.
in OBJECT_OUT {
    vec2 BaseUV;
    vec4 UV;
    vec4 Extra;
    vec4 Color;
    vec4 Mask1;
    vec4 Mask2;
} vsInput;

out vec4 FragColor;

uniform sampler2D GameTexture;
uniform float PixelRange;
uniform vec2 TextTextureSize;
uniform sampler2D TextTexture;
uniform float Zoom;
uniform int RenderPass; // 0 = opaque, 1 = transparent

const int OpaquePass = 0;
const int OutlineGlowPass = 1;

const float TypeGameObject = 0.0;
const float TypeText = 3.0;
const float TypeBar = 4.0;
const float TypeEffect = 5.0;

vec2 map(vec2 base, vec2 uvMin, vec2 uvMax) {
    return vec2(base.x * (uvMax.x - uvMin.x) + uvMin.x, base.y * (uvMax.y - uvMin.y) + uvMin.y);
}

float median(float a, float b, float c) {
    return max(min(a, b), min(max(a, b), c));
}

vec4 GetGameObject() {
    // uvMax precomputed once, reused in map()
    vec2 uvMax = vsInput.UV.xy + vsInput.UV.zw;
    vec2 uv = map(vsInput.BaseUV, vsInput.UV.xy, uvMax);
    // textureLod at LOD 0 instead of textureGrad() with manually computed dFdx/dFdy: those
    // derivatives are unreliable on this GPU/driver (garbage/degenerate mip selection made
    // sprites look like flat blocks of solid color). Plain texture() (automatic derivatives)
    // isn't safe either here - screen-space UV derivatives are discontinuous at atlas sprite
    // boundaries, so automatic mip selection bleeds in neighboring atlas entries. Locking to
    // LOD 0 sidesteps derivatives entirely and always samples the sharp, correct texels.
    vec4 color = textureLod(GameTexture, uv, 0.0);
    color.rgb -= vsInput.Extra.z * 0.241 * clamp(vsInput.BaseUV.y - 0.4, 0.0, 0.4);
    if (RenderPass == OpaquePass){
        if (color.a < 1.0 || vsInput.Extra.w < 1.0){
            discard;
        }
        return color;
    }

    // Fading-in objects (Extra.w = Alpha < 1) still show their full opaque texture color
    // here. The neighbor-sampling outline/glow effect that used to follow is disabled: on
    // this GPU/driver it misfired broadly instead of only along sprite edges, painting
    // whole quads with vsInput.Color (opaque black for most objects) and blacking out
    // everything behind them. Purely cosmetic effect - safe to skip.
    if (color.a >= 1.0 && vsInput.Extra.w < 1.0){
        return color;
    }

    discard;
}

vec4 GetText() {
    vec2 uv = map(vsInput.BaseUV, vsInput.UV.xy, vsInput.UV.xy + vsInput.UV.zw);
    vec3 samp = texture(TextTexture, uv).rgb;
    float pRange = PixelRange;
    vec2 dim = TextTextureSize;

    vec2 msdfUnit = pRange / dim;
    float sigDist = median(samp.r, samp.g, samp.b) - 0.5f;
    sigDist = sigDist * dot(msdfUnit, 0.5f / fwidth(uv));
    const float strokeThickness = 0.250f * 0.75f;
    float strokeDist = median(samp.r, samp.g, samp.b) - 0.25f * (1.0 + (pRange - 12) / pRange) - strokeThickness;
    strokeDist = -(abs(strokeDist) - strokeThickness);
    strokeDist = strokeDist * dot(msdfUnit, 0.5f / fwidth(uv));
    float opacity = clamp(sigDist + 0.5f, 0.0f, 1.0f);
    float strokeOpacity = clamp(strokeDist + 0.5f, 0.0f, 1.0f);
    return mix(vec4(0, 0, 0, 1), vsInput.Color, opacity) * max(opacity, strokeOpacity);
}

void main() {
    vec4 outputColor;
    float id = vsInput.Extra.x;

    if (id == TypeGameObject || id == TypeEffect) {
        outputColor = GetGameObject();
    } else if (id == TypeText) {
        outputColor = GetText();
    } else if (id == TypeBar) {
        outputColor = vsInput.Color;
    } else {
        outputColor = vec4(0, 0, 0, 0);
    }

    outputColor.a *= vsInput.Extra.w;
    if (outputColor.a == 0) {
        discard;
    }

    FragColor = outputColor;
}
