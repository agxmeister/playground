using System.Collections.Generic;
using UnityEngine;

// The block font and the solid geometry built from it. This lives in the
// runtime assembly rather than the editor setup script because the hall of
// fame spells out champions' names, which are only known while the game is
// running — the same builder therefore has to serve both the editor-authored
// title and runtime-built text.
public static class BlockText
{
    // A 5 x 7 block font: one string per glyph row, top row first, '#' where
    // the glyph is solid. The whole alphabet and the digits are defined,
    // because a champion's name is whatever they typed in.
    public const int GlyphWidth = 5, GlyphHeight = 7;

    static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
    {
        { 'A', new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" } },
        { 'B', new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." } },
        { 'C', new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." } },
        { 'D', new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." } },
        { 'E', new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" } },
        { 'F', new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." } },
        { 'G', new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###." } },
        { 'H', new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" } },
        { 'I', new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####" } },
        { 'J', new[] { "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.." } },
        { 'K', new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" } },
        { 'L', new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" } },
        { 'M', new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" } },
        { 'N', new[] { "#...#", "##..#", "##..#", "#.#.#", "#..##", "#..##", "#...#" } },
        { 'O', new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." } },
        { 'P', new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." } },
        { 'Q', new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" } },
        { 'R', new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" } },
        { 'S', new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." } },
        { 'T', new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." } },
        { 'U', new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." } },
        { 'V', new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." } },
        { 'W', new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#" } },
        { 'X', new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" } },
        { 'Y', new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." } },
        { 'Z', new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" } },
        { '0', new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." } },
        { '1', new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." } },
        { '2', new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" } },
        { '3', new[] { "#####", "...#.", "..#..", "...#.", "....#", "#...#", ".###." } },
        { '4', new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." } },
        { '5', new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." } },
        { '6', new[] { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." } },
        { '7', new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." } },
        { '8', new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." } },
        { '9', new[] { ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.." } },
        { '-', new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." } },
        { '.', new[] { ".....", ".....", ".....", ".....", ".....", ".##..", ".##.." } },
        { ' ', new[] { ".....", ".....", ".....", ".....", ".....", ".....", "....." } },
        // Stands in for anything else a player typed into the name entry.
        { '?', new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.." } },
    };

    // Names come from free text input, so anything outside the font falls back
    // to the '?' glyph rather than throwing.
    static string[] Glyph(char character)
    {
        var upper = char.ToUpperInvariant(character);
        return Font.TryGetValue(upper, out var glyph) ? glyph : Font['?'];
    }

    public static bool[,] GlyphCells(char character)
    {
        var glyph = Glyph(character);
        var cells = new bool[GlyphHeight, GlyphWidth];
        for (int row = 0; row < GlyphHeight; row++)
            for (int column = 0; column < GlyphWidth; column++)
                cells[row, column] = glyph[row][column] == '#';
        return cells;
    }

    // A whole word's cells, with one blank column between glyphs.
    public static bool[,] WordCells(string word)
    {
        var cells = new bool[GlyphHeight, WordColumns(word)];
        int at = 0;
        foreach (var character in word)
        {
            var glyph = Glyph(character);
            for (int row = 0; row < GlyphHeight; row++)
                for (int column = 0; column < GlyphWidth; column++)
                    cells[row, at + column] = glyph[row][column] == '#';
            at += GlyphWidth + 1;
        }
        return cells;
    }

    public static int WordColumns(string word) =>
        Mathf.Max(1, word.Length * (GlyphWidth + 1) - 1);

    // Where one glyph's centre falls in a word whose own centre is the origin.
    public static float GlyphCentreX(string word, int index, float cell) =>
        -WordColumns(word) * cell / 2f
        + index * (GlyphWidth + 1) * cell
        + GlyphWidth * cell / 2f;

    // Several lines stacked into one grid, each centred on the widest. Used for
    // an option label too long to sit across its arrow in one line.
    public static bool[,] LinesCells(string[] lines, int gapRows)
    {
        int columns = 0;
        foreach (var line in lines) columns = Mathf.Max(columns, WordColumns(line));
        var cells = new bool[lines.Length * GlyphHeight + (lines.Length - 1) * gapRows, columns];

        for (int line = 0; line < lines.Length; line++)
        {
            var lineCells = WordCells(lines[line]);
            int top = line * (GlyphHeight + gapRows);
            int left = (columns - lineCells.GetLength(1)) / 2;
            for (int row = 0; row < GlyphHeight; row++)
                for (int column = 0; column < lineCells.GetLength(1); column++)
                    cells[top + row, left + column] = lineCells[row, column];
        }
        return cells;
    }

    // Solid 3D geometry for a grid of block-font cells, centered on the grid's
    // own box: every horizontal run of solid cells in a row becomes one box, so
    // a glyph is a handful of blocks rather than one per cell.
    //
    // UVs come from each corner's position offset by uvOrigin (all scaled by
    // uvScale). Passing a piece's place within a larger word as uvOrigin keeps
    // the brick masonry running continuously across separately-built pieces —
    // which is what lets the title be eight independent letters that still read
    // as one wall. The offset only ever moves the axis it belongs to, so the
    // side faces (mapped from Z) stay put whatever a piece's X.
    public static Mesh BuildMesh(string name, bool[,] cells, float cell, float depth, float uvScale, Vector2 uvOrigin)
    {
        int rows = cells.GetLength(0), columns = cells.GetLength(1);
        float halfWidth = columns * cell / 2f;
        float halfHeight = rows * cell / 2f;
        float halfDepth = depth / 2f;

        var mesh = new Mesh { name = name };
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        // Corners are listed as seen from outside the face, like the wall
        // meshes, so the winding below faces outwards.
        void Face(Vector3 bottomLeft, Vector3 bottomRight, Vector3 topRight, Vector3 topLeft, System.Func<Vector3, Vector2> uv)
        {
            int start = vertices.Count;
            foreach (var corner in new[] { bottomLeft, bottomRight, topRight, topLeft })
            {
                vertices.Add(corner);
                uvs.Add(uv(corner) * uvScale);
            }
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
            triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
        }

        System.Func<Vector3, Vector2> xy = p => new Vector2(p.x + uvOrigin.x, p.y + uvOrigin.y);
        System.Func<Vector3, Vector2> xz = p => new Vector2(p.x + uvOrigin.x, p.z);
        System.Func<Vector3, Vector2> zy = p => new Vector2(p.z, p.y + uvOrigin.y);

        void Box(float x0, float x1, float y0, float y1)
        {
            float z0 = -halfDepth, z1 = halfDepth;
            Face(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), xy); // front (-Z)
            Face(new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), xy); // back (+Z)
            Face(new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), xz); // top (+Y)
            Face(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), new Vector3(x0, y0, z0), xz); // bottom (-Y)
            Face(new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), zy); // right (+X)
            Face(new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), zy); // left (-X)
        }

        for (int row = 0; row < rows; row++)
        {
            float y1 = halfHeight - row * cell, y0 = y1 - cell;
            for (int c = 0; c < columns; c++)
            {
                if (!cells[row, c]) continue;
                int end = c;
                while (end + 1 < columns && cells[row, end + 1]) end++;
                Box(-halfWidth + c * cell, -halfWidth + (end + 1) * cell, y0, y1);
                c = end;
            }
        }

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // The menu's options are arrow banners: a body wide enough to carry the
    // option's name, drawn out to a point at one end. The rounded 2D outline is
    // the collider as well as the mesh, so the ball reflects off exactly the
    // shape the player sees, corners included.
    //
    // The corners are listed counter-clockwise as the camera sees them, which
    // both the rounding and BuildArrowMesh rely on to work out which way things
    // face. `point` is how much of the width the pointed end takes.
    public static Vector2[] ArrowOutline(float width, float height, float point, float cornerRadius,
        int cornerSegments, bool pointingRight)
    {
        float halfWidth = width / 2f, halfHeight = height / 2f;
        float notch = halfWidth - point;
        var corners = new[]
        {
            new Vector2(-halfWidth, -halfHeight),
            new Vector2(notch, -halfHeight),
            new Vector2(halfWidth, 0f),
            new Vector2(notch, halfHeight),
            new Vector2(-halfWidth, halfHeight),
        };
        // Mirrored about X for a left-pointing arrow, which reverses the
        // winding, so the order is reversed to keep it counter-clockwise.
        if (!pointingRight)
        {
            for (int i = 0; i < corners.Length; i++) corners[i].x = -corners[i].x;
            System.Array.Reverse(corners);
        }
        return RoundedOutline(corners, cornerRadius, cornerSegments);
    }

    // Each corner of a counter-clockwise polygon replaced by an arc of the
    // given radius, tangent to both of its edges. The radius is trimmed where
    // an edge is too short to give it room — the arrow's point is a sharp angle
    // that would otherwise swallow half the shape.
    static Vector2[] RoundedOutline(Vector2[] corners, float radius, int segments)
    {
        var outline = new List<Vector2>();
        int count = corners.Length;
        for (int i = 0; i < count; i++)
        {
            var previous = corners[(i + count - 1) % count];
            var corner = corners[i];
            var next = corners[(i + 1) % count];

            var toPrevious = (previous - corner).normalized;
            var toNext = (next - corner).normalized;
            float half = Vector2.Angle(toPrevious, toNext) * Mathf.Deg2Rad / 2f;
            if (half <= 0.001f || half >= Mathf.PI / 2f - 0.001f)
            {
                outline.Add(corner);
                continue;
            }

            // How far back along each edge the arc has to start, capped at half
            // the shorter edge so neighbouring corners can't overrun each other.
            float inset = Mathf.Min(radius / Mathf.Tan(half),
                (previous - corner).magnitude / 2f, (next - corner).magnitude / 2f);
            float arcRadius = inset * Mathf.Tan(half);
            var centre = corner + (toPrevious + toNext).normalized * (arcRadius / Mathf.Sin(half));
            var from = corner + toPrevious * inset;
            var to = corner + toNext * inset;

            float start = Mathf.Atan2(from.y - centre.y, from.x - centre.x);
            float sweep = Mathf.DeltaAngle(start * Mathf.Rad2Deg,
                Mathf.Atan2(to.y - centre.y, to.x - centre.x) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            for (int s = 0; s <= segments; s++)
            {
                float angle = start + sweep * s / segments;
                outline.Add(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * arcRadius);
            }
        }
        return outline.ToArray();
    }

    // A pocket sunk into a face for lettering to be set into: the cells to sink,
    // how big one of them is, and where the block of them sits on the face. The
    // pocket is one cell deep and its mouth flares one cell wider than its
    // floor, so every wall is a 45° chamfer.
    //
    // A wall has to lean. The menu is looked at head-on, so a pocket walled
    // straight down would show no wall at all on screen and the lettering would
    // sit in a hole with no visible edge — nothing would say it was sunk. Leaned,
    // each wall has real width, and the light (head-on, pitched down by
    // LightPitch) does the rest: the wall above a stroke faces away from the
    // light and goes dark, the wall below it faces into the light and comes up
    // brighter than the face around it, and that pair of edges is the whole of
    // what reads as "set into".
    public class Engraving
    {
        // The pocket, at the resolution its chamfer is measured in.
        public bool[,] Cells;
        public float Cell;
        // Where the block of cells is centred on the face it is cut into.
        public Vector2 Centre;
        // One cell in and one cell down — the 45° the walls lean at.
        public float Depth => Cell;
    }

    // The pocket a block of lettering is set into: the lettering at
    // `subdivisions` times its own resolution, grown by one sub-cell all round.
    //
    // Growing it is what gives the mouth a chamfer wider than the lettering it
    // holds. Eroding that back by the same step — which is what the geometry
    // does when it drops a corner to the floor only where every cell meeting it
    // is pocket — returns exactly the lettering again, so the blocks seated in
    // the pocket fit their own shape rather than an approximation of it. That
    // holds as long as no gap in the lettering is thinner than two sub-cells,
    // which at any sensible subdivision no block glyph is.
    public static Engraving Pocket(bool[,] cells, float cell, Vector2 centre, int subdivisions)
    {
        int rows = cells.GetLength(0), columns = cells.GetLength(1);
        var pocket = new bool[rows * subdivisions + 2, columns * subdivisions + 2];
        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                if (!cells[row, column]) continue;
                for (int r = 0; r <= subdivisions + 1; r++)
                    for (int c = 0; c <= subdivisions + 1; c++)
                        pocket[row * subdivisions + r, column * subdivisions + c] = true;
            }
        // The padding is one sub-cell on every side, so the pocket is centred
        // exactly where the lettering is.
        return new Engraving { Cells = pocket, Cell = cell / subdivisions, Centre = centre };
    }

    // The part of a convex polygon on the inner side of a half-plane — the
    // points p with dot(p, normal) >= distance — as a convex polygon of its own.
    // Cutting the arrow's face into the four regions around its label's block is
    // what lets the face be drawn with a rectangular hole in it without a
    // general triangulator: each region is still convex, so each still fans.
    static List<Vector2> Clip(List<Vector2> polygon, Vector2 normal, float distance)
    {
        var clipped = new List<Vector2>();
        for (int i = 0; i < polygon.Count; i++)
        {
            var current = polygon[i];
            var next = polygon[(i + 1) % polygon.Count];
            float here = Vector2.Dot(current, normal) - distance;
            float there = Vector2.Dot(next, normal) - distance;
            if (here >= 0f) clipped.Add(current);
            if (here >= 0f != there >= 0f)
                clipped.Add(Vector2.Lerp(current, next, here / (here - there)));
        }
        return clipped;
    }

    public static Mesh BuildArrowMesh(string name, Vector2[] outline, float depth, Engraving engraving = null)
    {
        float halfDepth = depth / 2f;

        var mesh = new Mesh { name = name };
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        // Unity draws a triangle front-facing when Cross(b - a, c - a) points
        // at the viewer, so every face is emitted in whichever of the two
        // windings sends its normal the way the face is meant to look. Working
        // it out rather than hand-winding each of the five faces is what keeps
        // the two mirrored arrows from needing separate code.
        void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            int start = vertices.Count;
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f;
            foreach (var corner in new[] { a, flip ? c : b, flip ? b : c })
            {
                vertices.Add(corner);
                uvs.Add(new Vector2(corner.x, corner.y));
            }
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            Triangle(a, b, c, outward);
            Triangle(a, c, d, outward);
        }

        Vector3 Front(Vector2 p) => new Vector3(p.x, p.y, -halfDepth);
        Vector3 Back(Vector2 p) => new Vector3(p.x, p.y, halfDepth);

        // Any convex polygon fans out from its own centre.
        void Fan(List<Vector2> polygon, float z, Vector3 outward)
        {
            if (polygon.Count < 3) return;
            var middle = Vector2.zero;
            foreach (var p in polygon) middle += p;
            middle /= polygon.Count;
            for (int i = 0; i < polygon.Count; i++)
                Triangle(new Vector3(middle.x, middle.y, z),
                    new Vector3(polygon[i].x, polygon[i].y, z),
                    new Vector3(polygon[(i + 1) % polygon.Count].x, polygon[(i + 1) % polygon.Count].y, z),
                    outward);
        }

        var shape = new List<Vector2>(outline);

        // The front face, with the label's pocket sunk into it. The pocket's
        // cells cover a rectangular block of the face, so the face outside it is
        // drawn as the four regions of the outline around that block — each one
        // still convex, and so still a fan.
        void PocketedFace(Engraving label)
        {
            var cells = label.Cells;
            int rows = cells.GetLength(0), columns = cells.GetLength(1);
            float cell = label.Cell, depth = label.Depth;
            float left = label.Centre.x - columns * cell / 2f, right = left + columns * cell;
            float bottom = label.Centre.y - rows * cell / 2f, top = bottom + rows * cell;

            Fan(Clip(shape, Vector2.up, top), -halfDepth, Vector3.back);
            Fan(Clip(shape, Vector2.down, -bottom), -halfDepth, Vector3.back);
            var band = Clip(Clip(shape, Vector2.up, bottom), Vector2.down, -top);
            Fan(Clip(band, Vector2.left, -left), -halfDepth, Vector3.back);
            Fan(Clip(band, Vector2.right, right), -halfDepth, Vector3.back);

            // Inside the block the face is a height field on the cell grid
            // rather than floors and walls fitted together: a corner of the grid
            // lies on the pocket's floor where all four cells meeting it are
            // pocket, and up at the face otherwise. Neighbouring cells then
            // share their corners' depths by construction, so the chamfer comes
            // out mitred at every outside corner of a stroke and dimpled at
            // every inside one with nothing said about corners anywhere. Fitting
            // walls together by hand is what the first attempt at this did, and
            // every awkward shape in the font was its own special case.
            bool Sunk(int row, int column) =>
                row >= 0 && row < rows && column >= 0 && column < columns && cells[row, column];
            float Corner(int row, int column) =>
                Sunk(row - 1, column - 1) && Sunk(row - 1, column)
                    && Sunk(row, column - 1) && Sunk(row, column) ? depth : 0f;
            Vector3 At(float x, float y, float z) => new Vector3(x, y, -halfDepth + z);

            for (int row = 0; row < rows; row++)
            {
                float y1 = top - row * cell, y0 = y1 - cell;
                for (int column = 0; column < columns; column++)
                {
                    float topLeft = Corner(row, column), topRight = Corner(row, column + 1);
                    float bottomLeft = Corner(row + 1, column), bottomRight = Corner(row + 1, column + 1);
                    float x0 = left + column * cell, x1 = x0 + cell;
                    // Face the pocket never reaches runs on as one piece for as
                    // long as it stays flat.
                    if (topLeft == 0f && topRight == 0f && bottomLeft == 0f && bottomRight == 0f)
                    {
                        int end = column;
                        while (end + 1 < columns
                            && Corner(row, end + 2) == 0f && Corner(row + 1, end + 2) == 0f) end++;
                        x1 = left + (end + 1) * cell;
                        column = end;
                    }
                    Quad(At(x0, y0, bottomLeft), At(x1, y0, bottomRight),
                        At(x1, y1, topRight), At(x0, y1, topLeft), Vector3.back);
                }
            }
        }

        Fan(shape, halfDepth, Vector3.forward);
        if (engraving == null) Fan(shape, -halfDepth, Vector3.back);
        else PocketedFace(engraving);

        for (int i = 0; i < outline.Length; i++)
        {
            var p = outline[i];
            var q = outline[(i + 1) % outline.Length];
            // Outward normal of an edge of a counter-clockwise outline.
            var outward = new Vector3(q.y - p.y, p.x - q.x, 0f).normalized;
            Triangle(Front(p), Front(q), Back(q), outward);
            Triangle(Front(p), Back(q), Back(p), outward);
        }

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
