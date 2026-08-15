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

    public static Mesh BuildWordMesh(string name, string word, float cell, float depth, float uvScale) =>
        BuildMesh(name, WordCells(word), cell, depth, uvScale, Vector2.zero);

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

    // The menu's options are arrows: a triangular prism pointing along X, flat
    // side to the ball's approach. The 2D outline is the collider too (see
    // ArrowOutline), so the ball reflects off the real slanted edges.
    public static Vector2[] ArrowOutline(float width, float height, bool pointingRight)
    {
        float tip = pointingRight ? width / 2f : -width / 2f;
        float back = -tip;
        // Listed counter-clockwise as the camera sees it, which BuildArrowMesh
        // relies on to work out which way each face looks.
        return pointingRight
            ? new[]
            {
                new Vector2(back, -height / 2f),
                new Vector2(tip, 0f),
                new Vector2(back, height / 2f),
            }
            : new[]
            {
                new Vector2(tip, 0f),
                new Vector2(back, -height / 2f),
                new Vector2(back, height / 2f),
            };
    }

    public static Mesh BuildArrowMesh(string name, float width, float height, float depth, bool pointingRight)
    {
        var outline = ArrowOutline(width, height, pointingRight);
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

        Vector3 Front(Vector2 p) => new Vector3(p.x, p.y, -halfDepth);
        Vector3 Back(Vector2 p) => new Vector3(p.x, p.y, halfDepth);

        Triangle(Front(outline[0]), Front(outline[1]), Front(outline[2]), Vector3.back);
        Triangle(Back(outline[0]), Back(outline[1]), Back(outline[2]), Vector3.forward);

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
