using Alloy.UiLib.Core;
using Alloy.UiLib.Rendering;
using OpenTK.Mathematics;

namespace AlloyClient.Ui.Components.Graphics;

public sealed class ScreenGraphic : UiElement {

    private const int TexWidth = 1451;
    private const int TexHeight = 1084;

    public ScreenGraphic(bool splash = false) {

        TextureId = splash ? TextureType.TitleGraphic : TextureType.TitleBackground;

        ResizeBackBuffer();
        FillData(TexWidth, TexHeight);
    }

    protected override void OnResize(ResizeEvent args) {
        // Stretch to exactly fill the window every time, regardless of aspect ratio - no
        // cropping (a "cover" fit would crop this image's border on wide screens) and no
        // letterbox bars (a "contain" fit would leave them on the sides).
        X = 0;
        Y = 0;

        FillData(args.Width, args.Height);
    }
    
    private void ResizeBackBuffer() {
        VertexData = new VertexUi[4];
        Indices = [0, 1, 2, 0, 2, 3];
    }

   private void FillData(int width, int height) {
       VertexData[0] = new VertexUi(new Vector2(0, height), new Vector2(0f, 1f));
       VertexData[1] = new VertexUi(new Vector2(0, 0), new Vector2(0f, 0f));
       VertexData[2] = new VertexUi(new Vector2(width, 0), new Vector2(1f, 0f));
       VertexData[3] = new VertexUi(new Vector2(width, height), new Vector2(1f, 1f));
       
       SetGraphicsBuffer();
    }
}