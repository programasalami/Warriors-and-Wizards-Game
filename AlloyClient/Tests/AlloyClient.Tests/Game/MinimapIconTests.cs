using AlloyClient.Core;

namespace AlloyClient.Tests.Game;

// Your minimap icon (2026-09-22): every shape is a valid, centred triangle list about the size of the square; every colour is a real colour.
public class MinimapIconTests {

    [Theory]
    [InlineData(MinimapIconShape.Square)]
    [InlineData(MinimapIconShape.Circle)]
    [InlineData(MinimapIconShape.Diamond)]
    [InlineData(MinimapIconShape.Triangle)]
    public void EveryShapeIsAWellFormedTriangleListAroundTheCentre(MinimapIconShape shape) {
        var s = MinimapIcon.Get(shape);
        Assert.True(s.Offsets.Length >= 3);
        Assert.Equal(0, s.Indices.Length % 3);
        Assert.True(s.Indices.Length >= 3);
        foreach (var i in s.Indices)
            Assert.InRange(i, 0, s.Offsets.Length - 1);

        var limit = MinimapIcon.HalfSize * 1.35f;
        float minX = 0, maxX = 0, minY = 0, maxY = 0;
        foreach (var o in s.Offsets) {
            Assert.InRange(o.X, -limit, limit);
            Assert.InRange(o.Y, -limit, limit);
            minX = Math.Min(minX, o.X); maxX = Math.Max(maxX, o.X);
            minY = Math.Min(minY, o.Y); maxY = Math.Max(maxY, o.Y);
        }
        Assert.True(maxX - minX >= MinimapIcon.HalfSize * 1.9f, "too narrow");     // about as big as the square (12 px)
        Assert.True(maxY - minY >= MinimapIcon.HalfSize * 1.9f, "too short");
        Assert.Same(s.Offsets, MinimapIcon.Get(shape).Offsets);                     // cached, nothing allocates per frame
    }

    [Fact]
    public void TheDefaultIsABiggerGreenSquare() {
        Assert.Equal(MinimapIconShape.Square, Settings.MinimapIconShape.Value);
        Assert.Equal(MinimapIconColor.Green, Settings.MinimapIconColor.Value);
        Assert.True(MinimapIcon.HalfSize > 3.25f);                                  // the other markers are 3.25 px
    }

    [Fact]
    public void EveryColourIsDistinctAndNotBlack() {
        var seen = new HashSet<uint>();
        foreach (MinimapIconColor c in Enum.GetValues<MinimapIconColor>()) {
            var rgb = MinimapIcon.Rgb(c);
            Assert.NotEqual(0u, rgb);
            Assert.True(seen.Add(rgb), $"{c} repeats a colour");
        }
    }
}
