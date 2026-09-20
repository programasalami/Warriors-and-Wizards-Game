#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
precision highp usampler2D;

in vec4 VS_OUT_Position1;
flat in uint VS_OUT_Color;
flat in uint VS_OUT_Override;
in vec2 VS_OUT_Info;
in vec2 VS_OUT_UVCoords;
in vec4 VS_OUT_Scissor;
in vec4 VS_OUT_Extra1;
in vec4 VS_OUT_Extra2;
in vec4 VS_OUT_ColorTransform;

out vec4 FragColor;

uniform sampler2D GameAtlasTexture;
uniform sampler2D UiAtlasTexture;
uniform sampler2D UiAtlasTextureLinear;
uniform sampler2D MinimapTexture;

uniform float PixelRange;
uniform vec2 TextTextureSize;
uniform sampler2D TextTexture;

uniform float PixelRange2;
uniform vec2 TextTextureSize2;
uniform sampler2D TextTexture2;

uniform float PixelRange3;
uniform vec2 TextTextureSize3;
uniform sampler2D TextTexture3;




uniform sampler2D TitleBackgroundTexture;
uniform sampler2D TitleGraphicTexture;


const float TextTypeNormal = 0.0;
const float TextTypeSmall = 1.0;

const float IdColor = 0.0;
const float IdGameAtlas = 1.0;
const float IdUiAtlas = 2.0;
const float IdUiAtlasLinear = 3.0;
const float IdUiSlice = 4.0;
const float IdText = 5.0;
const float IdTitleBackground =6.0;
const float IdTitleGraphic = 7.0;
const float IdMinimap = 8.0;
const float IdEllipse = 9.0;
const float IdText2 = 10.0;
const float IdText3 = 11.0;

vec4 unpackColor(uint color) {
    return vec4(
    float(color & 0x000000FFu) / 255.0,
    float((color & 0x0000FF00u) >> 8u) / 255.0,
    float((color & 0x00FF0000u) >> 16u) / 255.0,
    float((color & 0xFF000000u) >> 24u) / 255.0
    );
}

float map(float value, float originalMin, float originalMax, float newMin, float newMax) {
    return (value - originalMin) / (originalMax - originalMin) * (newMax - newMin) + newMin;
}

float scale(float val, vec2 rect, float border, float borderTex) {
    if (val <= border)
    return map(val, 0.0, border, rect.x, rect.x + borderTex);
    if (val >= 1.0 - border)
    return map(val, 1.0 - border, 1.0, rect.y - borderTex, rect.y);
    return map(val, border, 1.0 - border, rect.x + borderTex, rect.y - borderTex);
}

vec4 slice() {
    vec2 uv;
    uv.x = scale(VS_OUT_UVCoords.x, VS_OUT_Extra1.xy, VS_OUT_Extra2.z, VS_OUT_Extra2.x);
    uv.y = scale(VS_OUT_UVCoords.y, VS_OUT_Extra1.zw, VS_OUT_Extra2.w, VS_OUT_Extra2.y);
    return texture(UiAtlasTexture, uv);
}

float median(float a, float b, float c) {
    return max(min(a, b), min(max(a, b), c));
}

float screenPxRange(vec2 uv, float pixelRange, vec2 textureSize) {
    vec2 unitRange = vec2(pixelRange, pixelRange) / textureSize;
    vec2 screenSize = vec2(1.0, 1.0) / fwidth(uv);
    return max(0.5 * dot(unitRange, screenSize), 1.0);
}

vec2 SafeNormalize(vec2 v) {
    float vLength = length(v);

    vLength = (vLength > 0.0) ?
    1.0 / vLength : 0.0;

    return v * vLength;
}

float GetOpacityFromDistance(float signedDistance, vec2 Jdx, vec2 Jdy, float pixelRange) {
    const float distanceLimit = sqrt(2.0f) / 2.0f;
    float thickness = 1.0f / (pixelRange / 2.0);

    vec2 gradientDistance = SafeNormalize(vec2(dFdx(signedDistance), dFdy(signedDistance)));
    vec2 gradient = vec2(gradientDistance.x * Jdx.x + gradientDistance.y * Jdy.x, gradientDistance.x * Jdx.y + gradientDistance.y * Jdy.y);
    float scaledDistanceLimit = min(thickness * distanceLimit * length(gradient), 0.5f);

    return smoothstep(-scaledDistanceLimit, scaledDistanceLimit, signedDistance);
}

vec4 RenderText(sampler2D tex, float pixelRange, vec2 textureSize) {
    vec4 mtsdf = texture(tex, VS_OUT_UVCoords);
    float dist = median(mtsdf.r, mtsdf.g, mtsdf.b) - 0.5;
    float pxRange = screenPxRange(VS_OUT_UVCoords, pixelRange, textureSize);

    float bodyDist = dist * pxRange;
    float glowDist = mtsdf.a;
    float glowSize = VS_OUT_Extra1.x / pixelRange;
    float bodyAlpha;
    float glowAlpha;

    if (VS_OUT_Extra1.y == TextTypeSmall) {
        vec2 pixelCoord = VS_OUT_UVCoords * textureSize;
        vec2 Jdx = dFdx(pixelCoord);
        vec2 Jdy = dFdy(pixelCoord);
        bodyAlpha = GetOpacityFromDistance(bodyDist, Jdx, Jdy, pixelRange);
        glowAlpha = GetOpacityFromDistance(glowDist, Jdx, Jdy, pixelRange) * glowSize;
    } else {
        bodyAlpha = clamp(bodyDist + 0.5f, 0.0f, 1.0f);
        glowAlpha = glowDist * glowSize;
    }

    vec4 color = mix(unpackColor(VS_OUT_Override), unpackColor(VS_OUT_Color), bodyAlpha);
    float alpha = bodyAlpha + glowAlpha;
    return vec4(color.rgb, alpha);
}

float samp(vec2 uv, vec2 dx, vec2 dy) {
    return textureGrad(GameAtlasTexture, uv, dx, dy).a;
}

vec4 RenderOutline() {
    vec2 uv = VS_OUT_UVCoords;
    vec2 dx = dFdx(uv);
    vec2 dy = dFdy(uv);
    vec4 color = textureGrad(GameAtlasTexture, uv, dx, dy);

    if (VS_OUT_UVCoords.y > VS_OUT_Extra1.x) {
        color.rgb -= 0.241 * (((VS_OUT_UVCoords.y - VS_OUT_Extra1.y) / VS_OUT_Extra1.z) - 0.4);
    }

    if (color.a > 0.0) {
        return color;
    }

    if (VS_OUT_Extra1.w == -1.0 && VS_OUT_Extra2.z == -1.0){ // Outline and glow disabled
        discard;
    }

    vec4 outlineColor = unpackColor(VS_OUT_Override);
    float scale = min(4.0, VS_OUT_Extra2.y / 60.0); // Extra2.y is the texture height 
    
    vec2 texSize = vec2(textureSize(GameAtlasTexture, 0));
    ivec2 currentTexel = ivec2(uv * texSize);

    float pxW = length(dx);
    float pxH = length(dy);
    float invPxW = 1.0 / pxW;
    float invPxH = 1.0 / pxH;
    vec2 invPx = vec2(1.0 / pxW, 1.0 / pxH);

    float outlineSize = floor(max(1.0, scale));
    float glowSize = max(6.0, 6.0 * scale);

    // Base directions (unit steps in screen space), scaled by i in the loop
    vec2 dirs[8] = vec2[](
    -dx - dy, -dy, dx - dy, dx,
    dx + dy,  dy, -dx + dy, -dx
    );

    float outlineAlpha = 0.0;
    float nearestDist = 999.0;

    for (float i = 1.0; i <= glowSize && outlineAlpha == 0.0; i++) {
        for (int j = 0; j < 8; j++) {
            vec2 sampleUV = uv + dirs[j] * i;
            ivec2 neighborTexel = ivec2(sampleUV * texSize);
            if (neighborTexel == currentTexel){
                continue;
            }

            if (texelFetch(GameAtlasTexture, neighborTexel, 0).a == 0.0){
                continue;
            }

            // Distance from fragment to nearest point on solid texel
            vec2 nearestPoint = clamp(uv, vec2(neighborTexel) / texSize, vec2(neighborTexel + ivec2(1)) / texSize);
            vec2 distPx = abs(uv - nearestPoint) * invPx;

            if (max(distPx.x, distPx.y) <= outlineSize) {
                outlineAlpha = 1.0;
                break;
            }

            nearestDist = min(nearestDist, length(distPx));
        }
    }

    if (VS_OUT_Extra1.w != -1.0 && outlineAlpha > 0.0){
        return vec4(outlineColor.rgb, 1.0);
    }

    if (VS_OUT_Extra2.z != -1.0 && nearestDist < 999.0) {
        float normalized = nearestDist / glowSize;
        float glowAlpha = 0.8 * exp(-normalized * 4.0) * (1.0 - smoothstep(0.8, 1.0, normalized));
        if (glowAlpha > 0.0){
            return vec4(outlineColor.rgb, glowAlpha);
        }
    }

    discard;
}

vec4 RenderNoOutline(sampler2D tex) {
    return texture(tex, VS_OUT_UVCoords);
}

vec4 RenderMinimap() {
    vec2 coords = VS_OUT_UVCoords;
    if (coords.x < 0.0 || coords.x > 1.0 || coords.y < 0.0 || coords.y > 1.0) {
        return vec4(0, 0, 0, 1);
    }

    return texture(MinimapTexture, coords);
}

vec4 RenderEllipse() {
    float rx = VS_OUT_Extra1.x - VS_OUT_Extra1.z, ry = VS_OUT_Extra1.y - VS_OUT_Extra1.z;
    float x = VS_OUT_UVCoords.x, y = VS_OUT_UVCoords.y;

    float inner = x * x / (rx * rx) + y * y / (ry * ry);
    rx = VS_OUT_Extra1.x; ry = VS_OUT_Extra1.y;
    float outline = x * x / (rx * rx) + y * y / (ry * ry);
    if (x * x / (rx * rx) + y * y / (ry * ry) > 1.0)
    return vec4(0, 0, 0, 0);
    float color_val;

    if (inner > 1.0) {
        color_val = 1.0;
    } else {
        color_val = 0.0;
    }

    return mix(unpackColor(VS_OUT_Color), unpackColor(VS_OUT_Override), color_val);
}

void main() {
    vec4 pixel = vec4(0);


    //TODO: replace pos1 with gl_FragCoord and send screen coords in scissor instead
    if (VS_OUT_Position1.x < VS_OUT_Scissor.x || VS_OUT_Position1.x > VS_OUT_Scissor.z || VS_OUT_Position1.y < VS_OUT_Scissor.w || VS_OUT_Position1.y > VS_OUT_Scissor.y) {
        discard;
    }

    vec4 color = unpackColor(VS_OUT_Color);

    float type = VS_OUT_Info.x;

    if (type == IdColor) {
        pixel = color;
    } else if (type == IdGameAtlas) {
        pixel = RenderOutline();
    } else if (type == IdUiAtlas) {
        pixel = RenderNoOutline(UiAtlasTexture);
    } else if (type == IdUiAtlasLinear) {
        pixel = RenderNoOutline(UiAtlasTextureLinear);// todo: msdfa sampling
    } else if (type == IdUiSlice) {
        pixel = slice();
    } else if (type == IdText) {
        pixel = RenderText(TextTexture, PixelRange, TextTextureSize);
    } else if (type == IdText2) {
        pixel = RenderText(TextTexture2, PixelRange2, TextTextureSize2);
    } else if (type == IdText3) {
        pixel = RenderText(TextTexture3, PixelRange3, TextTextureSize3);
    } else if (type == IdTitleBackground) {
        pixel = RenderNoOutline(TitleBackgroundTexture);
    } else if (type == IdTitleGraphic) {
        pixel = RenderNoOutline(TitleGraphicTexture);
    } else if (type == IdMinimap) {
        pixel = RenderMinimap();
    } else if (type == IdEllipse) {
        pixel = RenderEllipse();
    }

    if (color.a > 0.0 && type != IdColor && type != IdText && type != IdText2 && type != IdText3 && type != IdEllipse)
    pixel *= color;

    vec4 add = floor(VS_OUT_ColorTransform / 1000.0);
    vec4 mult = VS_OUT_ColorTransform - add * 1000.0;

    pixel = clamp(pixel, vec4(0.0), vec4(1.0));

    pixel = mult * pixel;
    pixel += add / 255.0;

    pixel.a *= VS_OUT_Info.y;
    FragColor = pixel;
}