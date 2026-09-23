using WaWClient.Game.Objects.Util;
using OpenTK.Mathematics;

namespace WaWClient.Tests.Combat;

public class HitRadiusTests {

    [Fact]
    public void HitReachesExactlyTheRadiusInTiles_NotItsSquareRoot() {
        // 0.6 tiles away: outside a 0.5 tile hit circle. (Comparing the squared distance 0.36 straight against 0.5 used to let this one through.)
        Vector2.DistanceSquared(new Vector2(0, 0), new Vector2(0.6f, 0), out var d);
        Assert.False(EntityUtils.IsWithinHitRadius(d, 0.5f));
    }

    [Theory]
    [InlineData(0f, 0f, true)]
    [InlineData(0.3f, 0.3f, true)]      // 0.42 tiles
    [InlineData(0.5f, 0f, true)]        // exactly on the edge
    [InlineData(0.36f, 0.36f, false)]   // 0.51 tiles
    [InlineData(0.7f, 0f, false)]       // the old client radius reached about this far
    public void MatchesTheServersHalfTileRadius(float x, float y, bool expected) {
        Vector2.DistanceSquared(Vector2.Zero, new Vector2(x, y), out var d);
        Assert.Equal(expected, EntityUtils.IsWithinHitRadius(d, 0.5f));
    }
}
