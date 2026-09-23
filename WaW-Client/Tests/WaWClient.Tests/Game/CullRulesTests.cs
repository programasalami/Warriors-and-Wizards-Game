using WaWClient.Game;
using WaWClient.Rendering;
using WaWClient.Rendering.VertexData;
using OpenTK.Mathematics;

namespace WaWClient.Tests.Game;

// Entity culling and the tile expansion helper (2026-09-21 audit, step 6). Both are pure; the GL side is checked by hand.
public class CullRulesTests {

    [Fact]
    public void RadiusCoversARotatedScreenPlusAMargin() {
        // 1280x720 at zoom 100: half-extents 12.8 x 7.2 tiles; the half-diagonal is 14.7, so the radius must exceed that.
        var r = CullRules.Radius(new Vector2(12.8f, 7.2f));
        Assert.True(r > 14.7f, $"radius {r}");
        Assert.True(r < 25f, $"radius {r} is wastefully large");
    }

    [Fact]
    public void EntitiesInsideTheCircleAreVisibleAndFarOnesAreNot() {
        var cam = new Vector2(50, 50);
        var r = CullRules.Radius(new Vector2(12.8f, 7.2f));
        Assert.True(CullRules.IsVisible(new Vector2(50, 50), cam, r));
        Assert.True(CullRules.IsVisible(new Vector2(62, 57), cam, r));     // screen corner area
        Assert.True(CullRules.IsVisible(new Vector2(50, 50 + r), cam, r)); // exactly on the edge
        Assert.False(CullRules.IsVisible(new Vector2(50, 50 + r + 0.01f), cam, r));
        Assert.False(CullRules.IsVisible(new Vector2(0, 0), cam, r));
    }

    [Fact]
    public void ExpandTilesMakesSixVerticesPerTileWithTheTilesData() {
        var tiles = new TileData[] {
            new(new Vector4(3, 4, 0, 0), new Vector4(0.1f, 0.2f, 0.3f, 0.4f), Vector4.Zero, Vector4.One),
            new(new Vector4(5, 6, 0, 0), new Vector4(0.5f, 0.6f, 0.7f, 0.8f), Vector4.One, Vector4.Zero)
        };
        var vertices = new TileVertexExpanded[12];
        var count = Render.ExpandTiles(tiles, vertices);

        Assert.Equal(12, count);
        Assert.Equal(new Vector4(3, 4, 0, 0), vertices[0].Position);
        Assert.Equal(new Vector4(3, 4, 0, 0), vertices[5].Position);
        Assert.Equal(new Vector4(5, 6, 0, 0), vertices[6].Position);
        Assert.Equal(new Vector4(0.5f, 0.6f, 0.7f, 0.8f), vertices[11].UV);
        // two triangles per tile: the six local corners cover the unit square
        var corners = vertices.Take(6).Select(v => v.LocalPos).Distinct().ToList();
        Assert.Equal(4, corners.Count);
    }

    [Fact]
    public void ExpandTilesRefusesAnUndersizedTarget() {
        var tiles = new TileData[3];
        var vertices = new TileVertexExpanded[12];
        Assert.Throws<ArgumentException>(() => Render.ExpandTiles(tiles, vertices));
    }
}
