#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
precision highp usampler2D;

out vec4 FragColor;

in vec2 BaseUV;
in vec4 Color;

const vec4 outline = vec4(0, 0, 0, 1);

void main() {
    float scale = 1.0;
    float ddx = abs(dFdx(BaseUV.x));
    float ddy = abs(dFdy(BaseUV.y));
    float dx = ddx * (0.1 / ddx);
    float dy = ddy * (0.1 / ddy);
    
    vec4 color = vec4(Color.xyz, 1);
    float val = float(BaseUV.x - dx <= 0.0 || BaseUV.y - dy <= 0.0 || BaseUV.x + dx >= 1.0 || BaseUV.y + dy >= 1.0);
    color = mix(color, outline, val);
    FragColor = color;
}