using WaWClient.Game;
using WaWClient.Rendering;
using WaWClient.Rendering.VertexData;
using OpenTK.Mathematics;

namespace WaWClient.Tests.Game;

// Static props baked into area meshes (2026-09-22): the baked cards are the per-frame cards in world space, with the depth code the shader decodes.
public class StaticPropsTests {

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    public void BakedCardsHaveTwelveVerticesPerCardAtTheirGroundPoints(int cards) {
        var list = new List<ModelVertexExpanded>();
        var pos = new Vector3(10.5f, 20.5f, 0f);
        Render.BakeCrossedCards(pos, new Vector4(0.1f, 0.2f, 0.3f, 0.4f), 1.0f, 2.0f, -0.25f, cards, 0.000005f, list);

        Assert.Equal(cards * 12, list.Count);
        foreach (var v in list) {
            Assert.Equal(0f, v.Position.X);                                   // the ground point moved into iPosition
            Assert.Equal(0f, v.Position.Y);
            Assert.InRange(v.Position.Z, -0.25f - 1e-4f, 1.75f + 1e-4f);      // bottom .. bottom + height
            var dx = v.IPosition.X - pos.X;
            var dy = v.IPosition.Y - pos.Y;
            Assert.InRange(MathF.Sqrt(dx * dx + dy * dy), 0.5f - 1e-4f, 0.5f + 1e-4f);   // on the card's half-width circle
            Assert.InRange(v.IExtra.Y, -10.01f, -9.99f);                      // "ground point = iPosition" code + the nudge
        }
    }

    [Fact]
    public void DepthCodesKeepTheKindAndTheNudgeApart() {
        var atPos = Render.BakedDepthCode(Render.BakedAtPosition, 0.00001f);
        var atTile = Render.BakedDepthCode(Render.BakedAtTileCentre, -0.00001f);
        Assert.True(atPos < -5f && atPos > -15f);                            // Model.vert: < -5 = baked, > -15 = kind "at position"
        Assert.True(atTile < -15f);
        Assert.Equal(0.00001f, (atPos - Render.BakedAtPosition) * 0.001f, 6);
    }

    [Fact]
    public void AreasAreSixteenTileSquaresIncludingNegatives() {
        Assert.Equal(new Vector2i(0, 0), StaticProps.AreaKey(0.5f, 15.9f));
        Assert.Equal(new Vector2i(1, 2), StaticProps.AreaKey(16f, 40f));
        Assert.Equal(new Vector2i(-1, -1), StaticProps.AreaKey(-0.5f, -16f));
    }
}
