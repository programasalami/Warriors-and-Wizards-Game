#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
precision highp usampler2D;

uniform sampler2D GameTexture;

in vec2 MODEL_OUT_BaseUV;
in vec4 MODEL_OUT_UV;
in vec3 MODEL_OUT_Extra;
in float MODEL_OUT_Zed;

out vec4 FragColor;

vec2 map(vec2 base, vec2 uvMin, vec2 uvMax) {
    return vec2(base.x * (uvMax.x - uvMin.x) + uvMin.x, base.y * (uvMax.y - uvMin.y) + uvMin.y);
}

void main() {
    vec2 uv = map(MODEL_OUT_BaseUV, MODEL_OUT_UV.xy, MODEL_OUT_UV.xy + MODEL_OUT_UV.zw);
    vec4 color = texture(GameTexture, uv);
    
    color /= color.a;
    color.rgb -= MODEL_OUT_Extra.z * 0.241 * clamp(0.6 - MODEL_OUT_Zed, 0.0 , 0.6);
    
    FragColor = color;
}