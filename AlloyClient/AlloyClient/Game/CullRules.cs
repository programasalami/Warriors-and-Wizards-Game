using System;
using OpenTK.Mathematics;

namespace AlloyClient.Game;

// Which entities are worth drawing this frame. 2026-09-21 audit (H3 / F19): culling was commented out, so every entity of the
// loaded world (all discovered trees, bushes, walls, chests...) was depth-sorted and drawn twice per frame, however far off screen.
// The camera can rotate, so the test is a circle around the camera that covers the screen's diagonal, plus a margin so tall
// sprites near the edge do not pop.
public static class CullRules {
    public const float RotationCover = 1.25f;   // the screen's half-diagonal is at most ~1.41x its half-width; 1.25 + the margin covers it
    public const float MarginTiles = 3f;

    // visibleTileRadius: the camera's half-extents in tiles (Camera.VisibleTileRadius).
    public static float Radius(Vector2 visibleTileRadius) =>
        MathF.Max(visibleTileRadius.X, visibleTileRadius.Y) * RotationCover + MarginTiles;

    public static bool IsVisible(Vector2 entityPos, Vector2 cameraPos, float radius) {
        var dx = entityPos.X - cameraPos.X;
        var dy = entityPos.Y - cameraPos.Y;
        return dx * dx + dy * dy <= radius * radius;
    }
}
