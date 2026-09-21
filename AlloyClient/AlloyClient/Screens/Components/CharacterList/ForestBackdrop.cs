using System;
using AlloyClient.Ui;
using AlloyClient.Utils;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Extra;

namespace AlloyClient.Screens.Components.CharacterList;

// Replaces MilkyWayBackground (2026-09-18) - the sci-fi starfield didn't fit the parchment/scroll/
// book aesthetic the rest of the menus were rebuilt around, per direct feedback ("the character
// selection is a nintendo switch basically"). Built from the user's own TopDownFantasy-Forest pack
// (see Content/Sheets/Grasslands_16x16.png, SmallPlants/MediumPlants/SmallTrees/MediumTrees and the matching "Forest *" Ground/Objects
// entries added for the Nexus reskin - this backdrop reuses those exact same registered textures
// rather than a separate copy, so the character-select world and the actual in-game Nexus read as
// the same place). A *still* scene per explicit request ("no more animationssss" has come up
// before on this project) - only a handful of trees/bushes get a very small idle sway, nothing
// scrolls or pans.
//
// Ground: see the constructor (baked from the game's own tile art and blend rules). Decor: BackdropData.g.cs.
//
// Same Resize(width, height) contract as MilkyWayBackground - lives outside the screen's own
// uniform-scaled _root and stretch-fills the real window on every resize instead of sharing
// Stage.ScreenScale, so it never letterboxes.
public sealed class ForestBackdrop : Container {

    private const int LayoutSeed = 1337;

    private readonly ObjectRect[] _swayingSprites;
    private readonly float[] _swayPhase;
    private readonly float[] _swaySpeed;

    public ForestBackdrop() : base(new ContainerConfig { Width = Settings.DefaultScreenWidth, Height = Settings.DefaultScreenHeight }) {
        var rng = new Random(LayoutSeed);

        // The ground is one baked image (Tools/BookUi/build_backdrop_ground.py) made with the same tile
        // art and the same edge/blend rules as the in-game map - tile variants, dirt rims, grass/dirt
        // blending, pond banks and the objects' ground shadows - drawn at 2x (16px tiles -> 32px).
        AddChild(new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("Backdrop/ForestGround", 0, false),
            Width = 1280,
            Height = 768,
            OutlineEnabled = false,
            GlowEnabled = false
        }));

        var swaying = new System.Collections.Generic.List<ObjectRect>();
        var phases = new System.Collections.Generic.List<float>();
        var speeds = new System.Collections.Generic.List<float>();

        foreach (var d in BackdropData.Items) {
            // Bottom-centred on its base point, hanging InsetPx below it - the same "sit on the shadow" rule
            // the in-game objects use (BottomInset).
            var x = d.BaseX - d.Width / 2;
            var y = d.BaseY - d.Height + d.InsetPx;

            var sprite = new ObjectRect(new ObjectRectConfig {
                Texture = TextureHelper.FromGameAtlas(d.Sheet, d.Index, false),
                Width = d.Width,
                Height = d.Height,
                X = x,
                Y = y,
                Anchor = UiAnchor.LeftTop,
                OutlineEnabled = false,
                GlowEnabled = false
            });
            AddChild(sprite);

            if (d.Sway) {
                // Pivot the sway around the base of the sprite (its "roots"), not its centre - an
                // ObjectRect rotates around its own local anchor, so shifting the anchor to the middle and
                // repositioning X/Y to compensate keeps the trunk planted while only the canopy rocks.
                sprite.SetAnchor(UiAnchor.Middle);
                sprite.X = x + d.Width / 2;
                sprite.Y = y + d.Height / 2;

                swaying.Add(sprite);
                phases.Add((float)(rng.NextDouble() * Math.PI * 2));
                speeds.Add(0.6f + (float)rng.NextDouble() * 0.5f);
            }
        }

        _swayingSprites = swaying.ToArray();
        _swayPhase = phases.ToArray();
        _swaySpeed = speeds.ToArray();

        AddEventListener(Event.EnterFrame, OnFrameEnter);
    }

    // Stretches this element's fixed 1280x720 internal layout to exactly fill the real window -
    // same reasoning as MilkyWayBackground.Resize.
    public void Resize(int width, int height) {
        ScaleX = width / (float)Settings.DefaultScreenWidth;
        ScaleY = height / (float)Settings.DefaultScreenHeight;
    }

    // Deliberately tiny amplitude - a still scene with just a hint of life, not an animated one.
    private const float SwayAmplitudeRad = 0.028f;

    private void OnFrameEnter() {
        var t = (float)(Stage.GameTime.TotalMs / 1000.0);
        for (var i = 0; i < _swayingSprites.Length; i++) {
            _swayingSprites[i].Rotation = MathF.Sin(t * _swaySpeed[i] + _swayPhase[i]) * SwayAmplitudeRad;
        }
    }
}
