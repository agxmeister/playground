using System.Collections.Generic;
using UnityEngine;

// A damaged block's *geometry*: the block as it stands once pieces of it have
// fallen, and each of those pieces as a mesh of its own.
//
// **The face is a height field, not a carved solid.** There is no CSG in this
// project and there does not need to be: the front of a block is a grid of
// cells, dropped wherever a piece has come away or a bite has been taken out
// of the outline, with side walls raised along whatever boundary that leaves.
// That construction is what makes an arbitrary set of missing pieces cost the
// same as none of them, and it is why the outline can change shape at all — a
// hole is a run of cells that are no longer there, and the walls follow.
//
// **It is built per block and per hit.** The mesh belongs to the instance and
// dies with it (see Brick.ReleaseDamage); the prefab's shared mesh is never
// touched.
public static class BlockDamage
{
    // How fine the grid is, in world units. It has two jobs and no others now
    // that nothing is cut into the face: hold the stair-step on a piece's
    // boundary under a pixel at the size a block is drawn, and give the rim
    // chamfer somewhere to sit. A block is about 130 px wide on a 1080p screen
    // and 1.5 units across, so a cell of 0.018 leaves the staircase on a
    // diagonal break about a pixel and a half deep — measured on the bench at
    // 0.025, where it was just visible on a long diagonal.
    const float CellSize = 0.018f;

    // The chamfer around the front face's rim, matching the bevel the shared
    // box mesh carries (ArkanoidSetup.BlockBevel), so a block does not visibly
    // sharpen its edges the moment it takes its first hit.
    const float RimChamfer = 0.03f;

    // Ceilings on the grid, because a mesh rebuilt on every hit is a cost paid
    // during play. A 1.5 x 0.5 face at the density above comes to 60 x 20
    // cells, comfortably inside these.
    const int MostColumns = 128;
    const int MostRows = 64;

    // A piece knocked out of the block's outline: where, in the mesh's own
    // local space, and how big. It goes all the way through the block rather
    // than scooping the face, because the camera looks at this game head-on —
    // a scallop that left the silhouette intact would be a notch nobody could
    // see, and a hole right through shows the backdrop, which is the whole
    // point of breaking an edge.
    public struct Bite
    {
        public Vector2 At;
        public float Radius;
    }

    // How a block is divided into the pieces it can lose. The sites are a
    // jittered grid over the face and a cell belongs to the site nearest it —
    // a Voronoi partition, which is what a brittle slab actually breaks into
    // and, more to the point here, is a partition a *cell mask* can answer in
    // one nearest-site test rather than by carrying polygons around.
    //
    // The count comes from the block's hardness, so a block sheds roughly one
    // piece per hit it survives and the hit that kills it takes what is left.
    // That is the whole reason the number is not a constant: a Polymer slab
    // that came apart in eight pieces would have to lose seven of them in the
    // one hit it can take, and a Neutronium slab that came apart in three would
    // stand there whole for eight hits and then vanish.
    public class Shards
    {
        public Vector2[] Sites { get; private set; }

        // How far a site may sit off its grid place, as a share of a cell.
        // Enough that no two pieces are the same shape, not so much that a
        // piece ends up a sliver.
        const float SiteJitter = 0.3f;

        public static Shards Build(int seed, Vector2 halfExtent, int count)
        {
            var random = new System.Random(seed);
            // Laid out along the block's own proportions, so a slab three times
            // wider than it is tall breaks into pieces that are roughly square
            // rather than into three tall strips.
            int columns = Mathf.Max(1, Mathf.RoundToInt(
                Mathf.Sqrt(count * halfExtent.x / Mathf.Max(halfExtent.y, 0.0001f))));
            int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)columns));

            var sites = new List<Vector2>();
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < columns; i++)
                {
                    float cellWidth = halfExtent.x * 2f / columns;
                    float cellHeight = halfExtent.y * 2f / rows;
                    sites.Add(new Vector2(
                        -halfExtent.x + (i + 0.5f) * cellWidth
                            + (float)(random.NextDouble() * 2 - 1) * SiteJitter * cellWidth,
                        -halfExtent.y + (j + 0.5f) * cellHeight
                            + (float)(random.NextDouble() * 2 - 1) * SiteJitter * cellHeight));
                }
            return new Shards { Sites = sites.ToArray() };
        }

        Shards() { }

        // Which piece a point belongs to.
        public int At(Vector2 point)
        {
            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < Sites.Length; i++)
            {
                float distance = (point - Sites[i]).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = i;
            }
            return nearest;
        }

        // The piece nearest a hit that has not already fallen, or -1 when the
        // block has nothing left to lose.
        public int NearestStanding(Vector2 point, bool[] gone)
        {
            int nearest = -1;
            float best = float.MaxValue;
            for (int i = 0; i < Sites.Length; i++)
            {
                if (gone[i]) continue;
                float distance = (point - Sites[i]).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = i;
            }
            return nearest;
        }
    }

    // The damaged mesh. `localSize` is the space the block's own mesh is
    // authored in — a unit cube for the box blocks, final size for the rounded
    // prism — and `worldSize` is what that comes to once the transform's scale
    // is applied. The two are separate because the *geometry* has to land in
    // the first and every width in here is a judgement about the second: a
    // groove is 0.05 of a world unit whatever local space it is cut in.
    //
    // `grown` is the share of the net that has cracked so far, `bites` the
    // pieces gone from the outline, and `cornerRadius` the outline's own
    // rounding in world units (zero for a square block).
    //
    // UVs come out in world units, which is the convention all three flat-faced
    // block shapes already use (their `grainUvPerUnit` is 1) — so the grain and
    // its normal map tile across a damaged block exactly as they did across a
    // whole one, and nothing downstream has to know the mesh changed.
    // What is left of the block: every piece that has not fallen, with the
    // bites taken out of its outline.
    public static Mesh BuildBlock(
        Vector3 localSize, Vector3 worldSize,
        IReadOnlyList<Bite> bites, float cornerRadius, Shards shards, bool[] gone) =>
        Build(localSize, worldSize, bites, cornerRadius, shards, gone, -1, out _);

    // One piece, on its own, as it comes away: the same cells the block has
    // just stopped drawing, built as a mesh of their own and recentred on the
    // piece's own middle so it can be dropped into the world at `centre` and
    // tumble about itself.
    public static Mesh BuildPiece(
        Vector3 localSize, Vector3 worldSize,
        IReadOnlyList<Bite> bites, float cornerRadius, Shards shards, int piece,
        out Vector3 centre) =>
        Build(localSize, worldSize, bites, cornerRadius, shards, null, piece, out centre);

    // `piece` of -1 builds the block (every standing shard); anything else
    // builds that one shard alone and answers where it sat.
    static Mesh Build(
        Vector3 localSize, Vector3 worldSize,
        IReadOnlyList<Bite> bites, float cornerRadius, Shards shards, bool[] gone,
        int piece, out Vector3 centre)
    {
        var halfLocal = localSize * 0.5f;
        // Local units per world unit, per axis: everything measured in world
        // units below is cut in local space through this.
        var perWorld = new Vector3(
            localSize.x / Mathf.Max(worldSize.x, 0.0001f),
            localSize.y / Mathf.Max(worldSize.y, 0.0001f),
            localSize.z / Mathf.Max(worldSize.z, 0.0001f));

        int columns = Mathf.Clamp(Mathf.RoundToInt(worldSize.x / CellSize), 8, MostColumns);
        int rows = Mathf.Clamp(Mathf.RoundToInt(worldSize.y / CellSize), 8, MostRows);

        // The outline as a signed distance: negative inside, and a bite is a
        // disc subtracted from it. Doing the shape this way rather than as a
        // list of cells is what lets the rim chamfer and the cell mask come off
        // the same function — the chamfer is just "how far inside the boundary
        // is this node", whatever shape a hit has left the boundary in.
        float Outline(Vector2 point)
        {
            var corner = new Vector2(
                Mathf.Abs(point.x) - (halfLocal.x - cornerRadius * perWorld.x),
                Mathf.Abs(point.y) - (halfLocal.y - cornerRadius * perWorld.y));
            float inside = Mathf.Min(Mathf.Max(corner.x, corner.y), 0f);
            float outside = new Vector2(Mathf.Max(corner.x, 0f), Mathf.Max(corner.y, 0f)).magnitude;
            float distance = inside + outside - cornerRadius * perWorld.x;

            for (int i = 0; i < bites.Count; i++)
                distance = Mathf.Max(distance, bites[i].Radius - Vector2.Distance(point, bites[i].At));
            return distance;
        }

        float chamfer = RimChamfer * perWorld.x;
        float chamferDepth = RimChamfer * perWorld.z;

        var nodes = new Vector3[(columns + 1) * (rows + 1)];
        var nodeUvs = new Vector2[nodes.Length];
        int Index(int i, int j) => j * (columns + 1) + i;

        for (int j = 0; j <= rows; j++)
            for (int i = 0; i <= columns; i++)
            {
                var point = new Vector2(
                    -halfLocal.x + i * (halfLocal.x * 2f / columns),
                    -halfLocal.y + j * (halfLocal.y * 2f / rows));

                // Into the block from the front face: the rim's chamfer, and
                // nothing else. The face a block shows is flat — what says it
                // is damaged is the pieces missing from it, not anything cut
                // into what is left.
                float inward = -Outline(point);
                float rim = inward < chamfer ? (chamfer - Mathf.Max(inward, 0f)) : 0f;

                nodes[Index(i, j)] = new Vector3(
                    point.x, point.y,
                    -halfLocal.z + rim * (chamferDepth / Mathf.Max(chamfer, 0.0001f)));
                nodeUvs[Index(i, j)] = new Vector2(
                    point.x / perWorld.x, point.y / perWorld.y);
            }

        // Whether each cell is still there, measured at its own centre.
        var solid = new bool[columns * rows];
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < columns; i++)
            {
                var middle = new Vector2(
                    -halfLocal.x + (i + 0.5f) * (halfLocal.x * 2f / columns),
                    -halfLocal.y + (j + 0.5f) * (halfLocal.y * 2f / rows));
                if (Outline(middle) >= 0f) continue;

                // Which piece this cell belongs to decides whether it is drawn
                // at all: the block draws every piece still standing, and a
                // falling piece draws only itself.
                int owner = shards != null ? shards.At(middle) : 0;
                solid[j * columns + i] = shards == null
                    || (piece < 0 ? gone == null || !gone[owner] : owner == piece);
            }

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        int Add(Vector3 position, Vector2 uv)
        {
            vertices.Add(position);
            uvs.Add(uv);
            return vertices.Count - 1;
        }

        void Quad(int a, int b, int c, int d)
        {
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(a); triangles.Add(c); triangles.Add(d);
        }

        // The front face. Its nodes are shared between neighbouring cells, so
        // a groove comes out as one continuous valley rather than as a row of
        // separate pits.
        var frontIndex = new int[nodes.Length];
        for (int n = 0; n < nodes.Length; n++) frontIndex[n] = -1;
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < columns; i++)
            {
                if (!solid[j * columns + i]) continue;
                int a = Corner(i, j), b = Corner(i + 1, j), c = Corner(i + 1, j + 1), d = Corner(i, j + 1);
                Quad(a, d, c, b);
            }

        int Corner(int i, int j)
        {
            int node = Index(i, j);
            if (frontIndex[node] < 0) frontIndex[node] = Add(nodes[node], nodeUvs[node]);
            return frontIndex[node];
        }

        // The back. Flat, and only where the front is: a bite goes right
        // through, so what shows in the notch is the room behind the block.
        var backIndex = new int[nodes.Length];
        for (int n = 0; n < nodes.Length; n++) backIndex[n] = -1;

        int BackCorner(int i, int j)
        {
            int node = Index(i, j);
            if (backIndex[node] < 0)
            {
                var front = nodes[node];
                backIndex[node] = Add(
                    new Vector3(front.x, front.y, halfLocal.z), nodeUvs[node]);
            }
            return backIndex[node];
        }

        for (int j = 0; j < rows; j++)
            for (int i = 0; i < columns; i++)
            {
                if (!solid[j * columns + i]) continue;
                int a = BackCorner(i, j), b = BackCorner(i + 1, j);
                int c = BackCorner(i + 1, j + 1), d = BackCorner(i, j + 1);
                Quad(a, b, c, d);
            }

        // The walls: every cell edge with nothing beside it, whether that is
        // the block's own outline or the inside of a notch. Drawn with their
        // own vertices so the wall meets the face in a hard edge rather than
        // smearing the face's shading round the corner.
        float depthUv = worldSize.z;
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < columns; i++)
            {
                if (!solid[j * columns + i]) continue;
                bool left = i == 0 || !solid[j * columns + i - 1];
                bool right = i == columns - 1 || !solid[j * columns + i + 1];
                bool down = j == 0 || !solid[(j - 1) * columns + i];
                bool up = j == rows - 1 || !solid[(j + 1) * columns + i];

                if (left) Wall(Index(i, j + 1), Index(i, j));
                if (right) Wall(Index(i + 1, j), Index(i + 1, j + 1));
                if (down) Wall(Index(i, j), Index(i + 1, j));
                if (up) Wall(Index(i + 1, j + 1), Index(i, j + 1));
            }

        // One wall panel, from the front node `from` to `to` and back to the
        // rear plane. The order the two nodes are given in is what faces it
        // outward, which is why each of the four sides above passes its own.
        void Wall(int from, int to)
        {
            var frontFrom = nodes[from];
            var frontTo = nodes[to];
            float along = Vector3.Distance(frontFrom, frontTo) / Mathf.Max(perWorld.x, 0.0001f);
            var baseUv = nodeUvs[from];

            int a = Add(frontFrom, baseUv);
            int b = Add(frontTo, new Vector2(baseUv.x + along, baseUv.y));
            int c = Add(new Vector3(frontTo.x, frontTo.y, halfLocal.z),
                new Vector2(baseUv.x + along, baseUv.y + depthUv));
            int d = Add(new Vector3(frontFrom.x, frontFrom.y, halfLocal.z),
                new Vector2(baseUv.x, baseUv.y + depthUv));
            Quad(a, b, c, d);
        }

        // A piece is built where it stood and then moved onto its own middle,
        // so the object that carries it can be put at that point in the world
        // and turned about itself. The block keeps the origin it always had.
        centre = Vector3.zero;
        if (piece >= 0 && vertices.Count > 0)
        {
            var sum = Vector3.zero;
            for (int i = 0; i < vertices.Count; i++) sum += vertices[i];
            centre = sum / vertices.Count;
            for (int i = 0; i < vertices.Count; i++) vertices[i] -= centre;
        }

        var mesh = new Mesh { name = piece >= 0 ? "BlockPiece" : "BlockDamaged" };
        // A damaged slab runs to a few thousand vertices, which is still well
        // inside a 16-bit index buffer; saying so keeps it there rather than
        // letting Unity widen it.
        mesh.indexFormat = vertices.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        // The grain rides a normal map, and a normal map is meaningless without
        // a tangent frame — the same trap stage 92 exists to avoid on the
        // texture side.
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }
}
