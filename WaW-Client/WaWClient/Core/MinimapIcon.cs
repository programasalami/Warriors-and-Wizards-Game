using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace WaWClient.Core;

// Your own marker on the minimap (2026-09-22, "Bugs and Todo" 2): a shape and a colour chosen on the options' Extra tab, drawn from plain
// triangles by MinimapLayer instead of the old blue arrow picture (which changed size while the camera turned). The enums are saved in
// settings.xml by name, so add new members at the end and never rename one. Emoji icons are a later idea: a third setting that picks
// from a library the client ships.
public enum MinimapIconShape {
    Square,
    Circle,
    Diamond,
    Triangle,
}

public enum MinimapIconColor {
    Green,
    Blue,
    Red,
    Yellow,
    White,
    Orange,
    Purple,
    Cyan,
    Pink,
}

public static class MinimapIcon {
    // Half the icon's size in minimap pixels: the other markers are 3.25, you are bigger so you are found at a glance.
    public const float HalfSize = 6f;
    private const int CircleSegments = 16;

    public static uint Rgb(MinimapIconColor color) => color switch {
        MinimapIconColor.Green => 0x2ECC40,
        MinimapIconColor.Blue => 0x2E86FF,
        MinimapIconColor.Red => 0xFF3B30,
        MinimapIconColor.Yellow => 0xFFDC00,
        MinimapIconColor.White => 0xFFFFFF,
        MinimapIconColor.Orange => 0xFF851B,
        MinimapIconColor.Purple => 0xB10DC9,
        MinimapIconColor.Cyan => 0x00E5FF,
        MinimapIconColor.Pink => 0xFF69B4,
        _ => 0x2ECC40
    };

    public readonly record struct Shape(Vector2[] Offsets, ushort[] Indices);

    private static readonly Dictionary<MinimapIconShape, Shape> Cache = [];

    // The triangles of a shape as offsets from its centre (built once per shape; nothing allocates per frame).
    public static Shape Get(MinimapIconShape shape) {
        if (Cache.TryGetValue(shape, out var cached))
            return cached;
        var built = Build(shape, HalfSize);
        Cache[shape] = built;
        return built;
    }

    public static Shape Build(MinimapIconShape shape, float half) {
        switch (shape) {
            case MinimapIconShape.Circle: {
                var offsets = new Vector2[CircleSegments + 1];
                var indices = new ushort[CircleSegments * 3];
                offsets[0] = Vector2.Zero;
                for (var i = 0; i < CircleSegments; i++) {
                    var a = MathF.Tau * i / CircleSegments;
                    offsets[i + 1] = new Vector2(MathF.Cos(a), MathF.Sin(a)) * half;
                    indices[i * 3] = 0;
                    indices[i * 3 + 1] = (ushort)(i + 1);
                    indices[i * 3 + 2] = (ushort)(i + 2 > CircleSegments ? 1 : i + 2);
                }
                return new Shape(offsets, indices);
            }
            case MinimapIconShape.Diamond: {
                var r = half * 1.3f;         // a diamond's points reach further so it carries the same visual weight as the square
                return new Shape([new(0, -r), new(r, 0), new(0, r), new(-r, 0)], [0, 1, 2, 0, 2, 3]);
            }
            case MinimapIconShape.Triangle: {
                var r = half * 1.25f;        // points up on the map
                return new Shape([new(0, -r), new(r, r * 0.8f), new(-r, r * 0.8f)], [0, 1, 2]);
            }
            default:
                return new Shape([new(-half, -half), new(half, -half), new(half, half), new(-half, half)], [0, 1, 2, 0, 2, 3]);
        }
    }
}
