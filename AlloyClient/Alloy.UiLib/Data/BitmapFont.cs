using System;
using System.Collections.Generic;
using System.Text;
using Alloy.Engine.Graphics;
using Alloy.Common.Structs;
using Alloy.ContentReader;
using Alloy.UiLib.Core;

namespace Alloy.UiLib.Data;

public class BitmapFamily {

    public readonly Texture Atlas;

    public readonly Sampler Sampler;
    
    public readonly Dictionary<FontType, BitmapFont> Fonts = [];

    public readonly float PixelRange;

    // sizeScale: multiplies every em-relative metric (glyph advance and plane bounds, kerning, line height, ascender and
    // descender) - NOT the atlas UVs. It exists so a font that is drawn small for its em size (a pixel font) can be
    // brought up to the visual size the client's nominal font sizes were tuned for, without touching every call site.
    public BitmapFamily(FontFamily data, float sizeScale = 1f) {
        Atlas = data.Texture;
        Sampler = new Sampler(Atlas, TextureFilter.Linear);

        foreach (var kvp in data.FontData) {
            if (!Enum.TryParse(kvp.Key, out FontType type)) {
                throw new Exception($"No matching FontType value for font id : {kvp.Key}");
            }
            
            Fonts[type] = new BitmapFont(kvp.Value, data.PixelRange, sizeScale);
        }

        PixelRange = data.PixelRange;
    }

}

public class BitmapFont {

    public readonly float LineHeight;
    public readonly float Ascender;
    public readonly float Descender;
    public readonly float PixelRange;

    public readonly Dictionary<char, FontGlyph> Glyphs;
    public readonly Dictionary<(char, char), float> Kernings;

    public BitmapFont(FontData fontData, float range, float sizeScale = 1f) {
        LineHeight = fontData.LineHeight * sizeScale;
        Ascender = fontData.Ascender * sizeScale;
        Descender = fontData.Descender * sizeScale;
        PixelRange = range;

        if (sizeScale == 1f) {
            Glyphs = fontData.Glyphs;
            Kernings = fontData.Kernings;
            return;
        }

        Glyphs = new Dictionary<char, FontGlyph>(fontData.Glyphs.Count);
        foreach (var kvp in fontData.Glyphs) {
            var glyph = kvp.Value;
            glyph.Advance *= sizeScale;
            glyph.Position = new GlyphData {
                X0 = glyph.Position.X0 * sizeScale,
                X1 = glyph.Position.X1 * sizeScale,
                Y0 = glyph.Position.Y0 * sizeScale,
                Y1 = glyph.Position.Y1 * sizeScale
            };
            Glyphs[kvp.Key] = glyph;
        }

        Kernings = new Dictionary<(char, char), float>(fontData.Kernings.Count);
        foreach (var kvp in fontData.Kernings) {
            Kernings[kvp.Key] = kvp.Value * sizeScale;
        }
    }

    //todo do this better
    public float ValidateOutlineSize(float size) {
        return 2 * Math.Max(Math.Min(size, PixelRange / 2f), 0f);
    }

    public (int, int) GetStartIndex(StringBuilder text, int caretIndex, int maxWidth, float outlineSize, float scale) {
        if (text.Length < 1 || maxWidth < 1)
            return (0, 0);

        var index = text.Length - 1;
        var startIndex = 0;
        var endIndex = text.Length;
        var width = 0f;

        while (index >= 0) {
            switch (text[index]) {
                case '\n':
                case '\r':
                    break;
                default:
                    if (!Glyphs.TryGetValue(text[index], out var glyph))
                        break;

                    if (index > 0) {
                        Kernings.TryGetValue((text[index - 1], text[index]), out var kern);
                        width += kern * scale;
                    }

                    width += glyph.Advance * scale;
                    break;
            }

            if (width > maxWidth) {
                startIndex = index + 1;
                break;
            }

            index--;
        }
        
        return (startIndex, endIndex);
    }
    
    public int GetStartIndex(string text, int maxWidth, float outlineSize, float scale) {
        if (string.IsNullOrWhiteSpace(text) || maxWidth < 1)
            return 0;

        var index = text.Length - 1;
        var startIndex = 0;
        var width = 0f;

        while (index >= 0) {
            switch (text[index]) {
                case '\n':
                case '\r':
                    break;
                default:
                    if (!Glyphs.TryGetValue(text[index], out var glyph))
                        break;

                    if (index > 0) {
                        Kernings.TryGetValue((text[index - 1], text[index]), out var kern);
                        width += kern * scale;
                    }

                    width += glyph.Advance * scale;
                    break;
            }

            if (width >= maxWidth) {
                startIndex = index + 1;
                break;
            }

            index--;
        }

        return startIndex;
    }
    
    public int GetCharCount(StringBuilder text) {
        var count = 0;

        for (var i = 0; i < text.Length; i++) {
            var c = text[i];
            switch (c) {
                case '\n':
                case '\r':
                    continue;
                default:
                    if (!Glyphs.TryGetValue(c, out _))
                        break;
                    count++;
                    continue;
            }
        }

        return count;
    }

    public int GetCharCount(string text) {
        var count = 0;
        foreach (var c in text) {
            switch (c) {
                case '\n':
                case '\r':
                    continue;
                default:
                    if (!Glyphs.ContainsKey(c)) {
                        break;
                    }
                    
                    count++;
                    continue;
            }
        }

        return count;
    }
}