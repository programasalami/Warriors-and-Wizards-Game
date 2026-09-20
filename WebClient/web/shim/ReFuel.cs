// ReFuel.Stb (native stb wrapper) replacement: same StbImage API on top of the managed StbImageSharp.
using System.Runtime.InteropServices;

namespace ReFuel.Stb;

public enum StbiImageFormat { Default = 0, Grey = 1, GreyAlpha = 2, Rgb = 3, Rgba = 4 }

public sealed class StbImage {
    public readonly int Width;
    public readonly int Height;
    private readonly byte[] _data;

    private StbImage(byte[] data, int w, int h) { _data = data; Width = w; Height = h; }

    public static StbImage Load(ReadOnlySpan<byte> file, StbiImageFormat format) {
        var img = StbImageSharp.ImageResult.FromMemory(file.ToArray(), StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        return new StbImage(img.Data, img.Width, img.Height);
    }

    public ReadOnlySpan<T> AsSpan<T>() where T : unmanaged => MemoryMarshal.Cast<byte, T>(_data.AsSpan());
}
