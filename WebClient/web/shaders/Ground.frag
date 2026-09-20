#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
precision highp usampler2D;

uniform sampler2D GameTexture;
uniform vec4 AlphaBlends[8];

in vec2 GROUND_OUTPUT_baseUV;
in vec2 GROUND_OUTPUT_coreUV;
in vec4 GROUND_OUTPUT_UV;
in vec4 GROUND_OUTPUT_Mask;
in float GROUND_OUTPUT_Swizzle;

out vec4 FragColor;

void main() {
    vec2 tileTexelSize = round(GROUND_OUTPUT_UV.zw * 4096.0);
    vec2 tileTexelOrigin = round(GROUND_OUTPUT_UV.xy * 4096.0);
    vec2 wrappedTexels = floor(fract(GROUND_OUTPUT_coreUV) * tileTexelSize);
    vec2 uv = (tileTexelOrigin + wrappedTexels + 0.5) / 4096.0;
    uv = mix(uv.xy, uv.yx, GROUND_OUTPUT_Swizzle);

    vec4 ogColor = texture(GameTexture, uv);

    if (GROUND_OUTPUT_Mask.x > -1.0) {
        vec2 maskTexelSize = round(GROUND_OUTPUT_Mask.zw * 4096.0);
        vec2 maskTexelOrigin = round(GROUND_OUTPUT_Mask.xy * 4096.0);
        vec2 maskTexels = floor(GROUND_OUTPUT_baseUV * maskTexelSize);
        float alpha = texture(GameTexture, (maskTexelOrigin + maskTexels + 0.5) / 4096.0).a;
        ogColor.a = alpha;
    }

    FragColor = ogColor;
}
