using System;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;
using WaW.UiLib.Extra;
using WaW.UiLib.Rendering;
using WaW.UiLib.Signals;
using WaWClient.Utils;
using WaW.Common;
using WaWClient.Ui;
using WaWClient.Ui.Components.Buttons;
using OpenTK.Mathematics;

namespace WaWClient.Game.Components.Hud;

public sealed class Minimap : Sprite {

    public static readonly SingleSignal<int> OnZoom = new();
    public static readonly SingleSignal<int, int> OnNewMap = new();

    private static readonly ColorTransform DefaultCt = new (1f, 1f, 1f, 1f);
    private static readonly ColorTransform FadeCt = new (0.5f, 0.5f, 0.5f, 1f);

    public const int MapSize = 230;

    private float _zoom = 4.0f;
    private float _maxZoom;
    private float _zoomStep;
    private float _size;

    private readonly MinimapLayer _layer;

    private readonly Container _zoomIn;
    private readonly Container _zoomOut;

    public Minimap() {
        TextureId = TextureType.Minimap;

        ResizeBackBuffer();
        FillData();

        OnZoom.Set(ZoomHandle);
        OnNewMap.Set(OnMapEnter);

        _layer = new MinimapLayer();
        AddChild(_layer);

        // the pack's plus / minus in small slot buttons, top-right of the map
        const int zoomSize = 32;
        _zoomIn = WaWStyle.IconButton("WaW/Plus", 6, 6, 3, zoomSize, () => ZoomHandle(1));
        _zoomIn.X = MapSize - zoomSize - 4;
        _zoomIn.Y = 4;
        AddChild(_zoomIn);

        _zoomOut = WaWStyle.IconButton("WaW/Minus", 6, 2, 3, zoomSize, () => ZoomHandle(-1));
        _zoomOut.X = MapSize - zoomSize - 4;
        _zoomOut.Y = 4 + zoomSize + 4;
        AddChild(_zoomOut);

        // Your own marker is drawn by the layer (MinimapLayer + MinimapIcon: shape and colour from the Extra options tab). The blue arrow
        // picture that used to sit here changed size while the camera turned (2026-09-22).
        AddEventListener(Event.EnterFrame, OnFrameEnter);
    }

    private void UpdateButtons() {
        if (_zoom <= 1f) {
            _zoomIn.ColorTransformation = Transforms.Default;
            _zoomOut.ColorTransformation = Transforms.Dark;
        } else if (_zoom >= _maxZoom) {
            _zoomIn.ColorTransformation = Transforms.Dark;
            _zoomOut.ColorTransformation = Transforms.Default;
        } else {
            _zoomIn.ColorTransformation = Transforms.Default;
            _zoomOut.ColorTransformation = Transforms.Default;
        }
    }

    private void ResizeBackBuffer() {
        VertexData = new VertexUi[4];
        Indices = [0, 1, 2, 0, 2, 3];
    }

    private void FillData() {
        VertexData[0] = new VertexUi(new Vector2(0, 0)); //Top Left
        VertexData[1] = new VertexUi(new Vector2(MapSize, 0)); //Top Right
        VertexData[2] = new VertexUi(new Vector2(MapSize, MapSize)); //Bottom Right
        VertexData[3] = new VertexUi(new Vector2(0, MapSize)); //Bottom Left

        SetGraphicsBuffer();
    }

    private void ZoomHandle(int zoom) {
        _zoom += _zoomStep * zoom;
        _zoom = Math.Max(1, Math.Min(_maxZoom, _zoom));
        UpdateButtons();
    }

    private void OnMapEnter(int w, int h) {
        var size = (float)Math.Max(w, h);
        _maxZoom = size / 32;
        _zoomStep = size / Settings.DefaultScreenWidth ;
        _size = size;
        MinimapTexture.ClearData();
    }

    private void OnFrameEnter() {
        if (Map.LocalPlayer == null) return;

        var pos = Map.LocalPlayer.Position;
        var size = _size / _zoom / 2.0f;

        var x1 = pos.X - size;
        var x2 = pos.X + size;
        var y1 = pos.Y - size;
        var y2 = pos.Y + size;
        VertexData[0].UV = new Vector2(x1 / 4096, y1 / 4096);
        VertexData[1].UV = new Vector2(x2 / 4096, y1 / 4096);
        VertexData[2].UV = new Vector2(x2 / 4096, y2 / 4096);
        VertexData[3].UV = new Vector2(x1 / 4096, y2 / 4096);

        _layer.SetSize(size);
    }
}