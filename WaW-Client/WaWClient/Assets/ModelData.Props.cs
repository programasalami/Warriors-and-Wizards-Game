using System.Collections.Generic;
using OpenTK.Mathematics;

namespace WaWClient.Assets;

// Real 3D shapes for the big interactive props (Bug Board, Jukebox), so they keep their facing when the camera turns - you see their sides and back,
// like the guild board - instead of being flat pictures that always face you (which made them swing through neighbouring walls). Each is a few boxes plus one
// picture quad per side, built here in code from the art's own pixel layout (the "forestProps" sheet, 80x120 cells).
//
//  * The FRONT picture is the whole cell drawn on a quad just in front of the body (its see-through parts are cut out by Model.frag).
//  * A prop with a readable back (the Bug Board) gets the same picture on a quad behind it; the others get a plain back.
//  * Everything else (sides, top, back, legs) is a "solid colour": all four corners of the face point at one pixel of the art, so it takes the art's own wood / lid colour.
//
// Coordinates: the footprint is centred on the object (x sideways, y front-to-back with +y the FRONT, which faces the viewer at the default camera angle), z is up,
// one unit = one tile. Faces are wound exactly like the built-in Wall mesh, because the entity pass culls back faces. The depth of a whole model is one value (see
// Model.vert), so overlapping faces inside one model are decided by DRAW ORDER (first drawn wins): the picture quads come first, then the solid parts.
public static partial class ModelData {
    private const float PropCellW = 80f;
    private const float PropCellH = 120f;
    private const float PropGroundRow = 117f;         // the pixel row of the cell the art stands on (the sheet leaves 3 empty rows under it)
    private const float PictureGap = 0.003f;          // how far the picture quad floats in front of the body, so it never sits inside it

    private sealed class PropBuilder(float pixel) {
        private readonly List<VertexData> _vertices = [];
        private readonly List<ushort> _indices = [];

        // art pixel column / row (in the 80x120 cell) -> world x / z
        public float X(float px) => (px - PropCellW / 2f) * pixel;

        public float Z(float py) => (PropGroundRow - py) * pixel;

        // The uv of the centre of one art pixel: a face whose four corners use it is a solid colour.
        public static Vector2 Colour(int px, int py) => new((px + 0.5f) / PropCellW, (py + 0.5f) / PropCellH);

        // The whole cell as a picture on the front (+y) side, at depth y. Same corner order and uv as the Wall mesh's front face.
        public void FrontPicture(float y) {
            var x0 = X(0);
            var x1 = X(PropCellW);
            var top = Z(0);
            var bottom = Z(PropCellH);
            Quad(new Vector3(x0, y, top), new Vector3(x1, y, top), new Vector3(x1, y, bottom), new Vector3(x0, y, bottom),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
        }

        // The same picture on the back (-y) side, reading the right way round for someone standing behind it (the Wall mesh's back face).
        public void BackPicture(float y) {
            var x0 = X(0);
            var x1 = X(PropCellW);
            var top = Z(0);
            var bottom = Z(PropCellH);
            Quad(new Vector3(x1, y, top), new Vector3(x0, y, top), new Vector3(x0, y, bottom), new Vector3(x1, y, bottom),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
        }

        // A solid-colour box. Faces that are hidden by something else in the model can be left out.
        public void Box(float x0, float x1, float y0, float y1, float z0, float z1, Vector2 colour, bool front = true, bool back = true, bool top = true) {
            if (front)
                Quad(new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), colour, colour, colour, colour);
            if (back)
                Quad(new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), colour, colour, colour, colour);
            Quad(new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), new Vector3(x0, y0, z0), colour, colour, colour, colour);      // left
            Quad(new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), colour, colour, colour, colour);      // right
            if (top)
                Quad(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), colour, colour, colour, colour);
        }

        private void Quad(Vector3 tl, Vector3 tr, Vector3 br, Vector3 bl, Vector2 uvTl, Vector2 uvTr, Vector2 uvBr, Vector2 uvBl) {
            var start = (ushort) _vertices.Count;
            _vertices.Add(new VertexData(tl, uvTl));
            _vertices.Add(new VertexData(tr, uvTr));
            _vertices.Add(new VertexData(br, uvBr));
            _vertices.Add(new VertexData(bl, uvBl));
            _indices.AddRange([start, (ushort) (start + 1), (ushort) (start + 2), start, (ushort) (start + 2), (ushort) (start + 3)]);
        }

        public MeshData Build(ModelType type) => new(_vertices.ToArray(), _indices.ToArray(), type, true);
    }

    // The Bug Board: a notice board on two legs, 1.35 tiles wide and only 0.14 thick. Its picture is on both sides.
    private static MeshData BugBoardModel() {
        var b = new PropBuilder(0.025f);
        const float half = 0.07f;
        var wood = PropBuilder.Colour(33, 81);

        b.FrontPicture(half + PictureGap);
        b.BackPicture(-half - PictureGap);
        b.Box(b.X(13), b.X(67), -half, half, b.Z(65), b.Z(58), wood);                                                  // the roof beam
        b.Box(b.X(15), b.X(65), -half, half, b.Z(97), b.Z(65), wood, top: false);                                      // the board
        b.Box(b.X(19), b.X(27), -0.05f, 0.05f, b.Z(117), b.Z(97), wood, top: false);                                   // left leg
        b.Box(b.X(53), b.X(61), -0.05f, 0.05f, b.Z(117), b.Z(97), wood, top: false);                                   // right leg
        return b.Build(ModelType.BugBoard);
    }

    // The Jukebox: a cabinet about 1 tile wide and half a tile deep, on two feet. Front picture only; the back is plain wood.
    private static MeshData JukeboxModel() {
        var b = new PropBuilder(0.024f);
        const float half = 0.28f;
        var wood = PropBuilder.Colour(24, 61);

        b.FrontPicture(half + PictureGap);
        b.Box(b.X(19), b.X(61), -half, half, b.Z(113), b.Z(58), wood);                                                  // the cabinet
        b.Box(b.X(21), b.X(30), -0.16f, 0.16f, b.Z(117), b.Z(113), wood, top: false);                                   // left foot
        b.Box(b.X(50), b.X(59), -0.16f, 0.16f, b.Z(117), b.Z(113), wood, top: false);                                   // right foot
        return b.Build(ModelType.Jukebox);
    }
}
