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

    // The bevel around the front face's rim, which has to be the same number
    // ArkanoidSetup.BlockBevel is: this mesh replaces the prefab's bevelled box
    // on a block's first hit, and anything the two disagree about is a change
    // the player sees a block make when it should only have cracked.
    //
    // It is built the way the prefab builds it, and that shape is the whole
    // point. The front face is inset by the bevel; a 45-degree strip runs from
    // that inset edge out to the silhouette, ending one bevel deeper; the side
    // wall starts there. **The strip's inner vertices carry the front face's
    // normal and its outer ones the side's**, so the shading grades across the
    // strip while the face and the side each stay flat — which is why this mesh
    // sets its normals rather than calling RecalculateNormals.
    //
    // Two earlier attempts are worth knowing about, because both looked
    // plausible and both were reported as a bug. A chamfer that pushed the rim
    // nodes back in z and met the wall in a hard crease read as a *frame* — a
    // bright line inside the top edge, a dark one down the sides. Removing it
    // instead left the front face meeting a bare vertical wall, and a wall
    // facing sideways gets nothing from a head-on key light, so perspective
    // showed a raw unlit grey band down the end of every damaged block where the
    // prefab shows a lit bevel.
    const float RimBevel = 0.03f;

    // How far in front of the face the drawn crack sits, in world units. Far
    // enough not to z-fight with the face it lies on, near enough that no
    // parallax shows at the angles this camera sees a block from.
    const float CrackLift = 0.004f;

    // How wide the drawn crack is, in cells. One is a hairline at the size a
    // block is drawn; the ribbon is built on the cell grid because that is what
    // the piece boundary is already measured on.
    const int CrackCells = 1;

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

        // How far the smooth style bends a border off the straight line the
        // Voronoi would draw, as a share of one piece's own width, and how
        // coarse that bending is. A conchoidal fracture curves over the *whole*
        // piece rather than wobbling along it — one bow per border, not a
        // ripple — so the lattice is deliberately coarser than the pieces are.
        const float SmoothBow = 0.42f;
        const int BowLattice = 5;

        BreakEdge edge;
        float bow;                 // in local units; zero for a sharp edge
        float[,] bowX, bowY;
        Vector2 half;

        public static Shards Build(int seed, Vector2 halfExtent, int count, BreakEdge edge)
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
            // The bow is measured against a piece, so it needs the piece's own
            // size — which is only known here, where the grid was laid out.
            float pieceWidth = halfExtent.x * 2f / columns;
            float pieceHeight = halfExtent.y * 2f / rows;

            return new Shards
            {
                Sites = sites.ToArray(),
                edge = edge,
                half = halfExtent,
                bow = edge == BreakEdge.Smooth
                    ? SmoothBow * Mathf.Min(pieceWidth, pieceHeight)
                    : 0f,
                bowX = edge == BreakEdge.Smooth ? Lattice(random) : null,
                bowY = edge == BreakEdge.Smooth ? Lattice(random) : null,
            };
        }

        // A small square of random values, drawn from the same sequence the
        // sites were, so a block that lays down the same pieces bends their
        // borders the same way too.
        static float[,] Lattice(System.Random random)
        {
            var values = new float[BowLattice, BowLattice];
            for (int y = 0; y < BowLattice; y++)
                for (int x = 0; x < BowLattice; x++)
                    values[x, y] = (float)random.NextDouble() * 2f - 1f;
            return values;
        }

        // The point a nearest-site test should actually be run at. For a sharp
        // edge that is the point itself and every border comes out straight,
        // which is what a cleavage plane is. For a smooth one the whole plane is
        // dragged around by a slow noise before the test — a *domain warp* — so
        // the borders bend without ever breaking or crossing: displacing the
        // question rather than the answer cannot tear the partition, where
        // moving the boundary itself could leave a piece with a hole in it.
        Vector2 Warped(Vector2 point)
        {
            if (bow <= 0f) return point;
            float u = Mathf.Clamp01((point.x + half.x) / Mathf.Max(half.x * 2f, 0.0001f));
            float v = Mathf.Clamp01((point.y + half.y) / Mathf.Max(half.y * 2f, 0.0001f));
            return point + new Vector2(Sample(bowX, u, v), Sample(bowY, u, v)) * bow;
        }

        static float Sample(float[,] values, float u, float v)
        {
            float x = u * (BowLattice - 1), y = v * (BowLattice - 1);
            int x0 = Mathf.Clamp((int)x, 0, BowLattice - 1);
            int y0 = Mathf.Clamp((int)y, 0, BowLattice - 1);
            int x1 = Mathf.Min(x0 + 1, BowLattice - 1);
            int y1 = Mathf.Min(y0 + 1, BowLattice - 1);
            // Smoothstepped, so the lattice's own grid does not show through as
            // creases — the same reason the fog's noise is (ArkanoidSetup).
            float fx = Mathf.SmoothStep(0f, 1f, x - x0);
            float fy = Mathf.SmoothStep(0f, 1f, y - y0);
            return Mathf.Lerp(
                Mathf.Lerp(values[x0, y0], values[x1, y0], fx),
                Mathf.Lerp(values[x0, y1], values[x1, y1], fx),
                fy);
        }

        public BreakEdge Edge => edge;

        Shards() { }

        // Which piece a point belongs to. Every caller goes through here — the
        // block's own mask and a falling piece's alike — so the border a piece
        // leaves behind and the border it carries are the same curve by
        // construction, which is what makes the crack that outlines a loose
        // piece a promise the block keeps (see Brick.Carve).
        public int At(Vector2 point)
        {
            point = Warped(point);
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
    // A piece is built flush rather than sunk however it stood: what it was
    // doing on the block is a fact about the block, and in the air it is just a
    // piece.
    public static Mesh BuildPiece(
        Vector3 localSize, Vector3 worldSize,
        IReadOnlyList<Bite> bites, float cornerRadius, Shards shards, int piece,
        out Vector3 centre) =>
        Build(localSize, worldSize, bites, cornerRadius, shards, null, piece, out centre);

    // The crack, as a ribbon of its own laid just in front of the face: one
    // cell wide, running along the border of every piece that has come loose
    // but not yet fallen.
    //
    // **It is drawn rather than carved, and that is the whole shape of this
    // feature.** Cutting the crack into the block meant rebuilding the block's
    // mesh on a hit that was supposed to change nothing else, and the carver's
    // mesh and the prefab's bevelled box could not be made to agree: five
    // attempts at the rim each fixed what they aimed at and left something a
    // player could see appear — a bright frame, a raw unlit side, a mirrored
    // grain, a two-pixel line. A ribbon in front of the face cannot change the
    // block's silhouette, its edges or its grain, because it does not touch the
    // block at all. The mesh is still rebuilt when a piece actually *falls*,
    // where a hole appearing is the point and covers the change.
    //
    // The boundary comes off the same `Shards.At` the hole will, so the crack is
    // the exact shape of the piece that leaves next, and it bows on a ceramic
    // and runs straight on a crystal for free (see BlockMaterials.EdgeOf).
    //
    // Never on the block's own rim: a cell only joins the ribbon where the
    // neighbour it differs from is *inside* the outline, so the crack stops
    // short of the edge rather than drawing a line along it.
    public static Mesh BuildCrackLines(
        Vector3 localSize, Vector3 worldSize,
        IReadOnlyList<Bite> bites, float cornerRadius, Shards shards, bool[] loose)
    {
        if (shards == null || loose == null) return null;

        var halfLocal = localSize * 0.5f;
        var perWorld = new Vector3(
            localSize.x / Mathf.Max(worldSize.x, 0.0001f),
            localSize.y / Mathf.Max(worldSize.y, 0.0001f),
            localSize.z / Mathf.Max(worldSize.z, 0.0001f));
        int columns = Mathf.Clamp(Mathf.RoundToInt(worldSize.x / CellSize), 8, MostColumns);
        int rows = Mathf.Clamp(Mathf.RoundToInt(worldSize.y / CellSize), 8, MostRows);

        float Outline(Vector2 point)
        {
            var world = new Vector2(point.x / perWorld.x, point.y / perWorld.y);
            var halfWorld = new Vector2(halfLocal.x / perWorld.x, halfLocal.y / perWorld.y);
            var corner = new Vector2(
                Mathf.Abs(world.x) - (halfWorld.x - cornerRadius),
                Mathf.Abs(world.y) - (halfWorld.y - cornerRadius));
            float inside = Mathf.Min(Mathf.Max(corner.x, corner.y), 0f);
            float outside = new Vector2(Mathf.Max(corner.x, 0f), Mathf.Max(corner.y, 0f)).magnitude;
            float distance = inside + outside - cornerRadius;
            for (int i = 0; i < bites.Count; i++)
            {
                var at = new Vector2(bites[i].At.x / perWorld.x, bites[i].At.y / perWorld.y);
                distance = Mathf.Max(distance,
                    bites[i].Radius / perWorld.x - Vector2.Distance(world, at));
            }
            return distance;
        }

        Vector2 Middle(int i, int j) => new Vector2(
            -halfLocal.x + (i + 0.5f) * (halfLocal.x * 2f / columns),
            -halfLocal.y + (j + 0.5f) * (halfLocal.y * 2f / rows));

        var owners = new int[columns * rows];
        var inside2 = new bool[columns * rows];
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < columns; i++)
            {
                var middle = Middle(i, j);
                if (Outline(middle) >= 0f) continue;
                inside2[j * columns + i] = true;
                owners[j * columns + i] = shards.At(middle);
            }

        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        float lift = CrackLift * perWorld.z;
        float cellW = halfLocal.x * 2f / columns, cellH = halfLocal.y * 2f / rows;

        for (int j = 0; j < rows; j++)
            for (int i = 0; i < columns; i++)
            {
                int cell = j * columns + i;
                if (!inside2[cell] || !loose[owners[cell]]) continue;

                bool onBorder = false;
                for (int d = -CrackCells; d <= CrackCells && !onBorder; d++)
                    for (int e = -CrackCells; e <= CrackCells && !onBorder; e++)
                    {
                        int x = i + d, y = j + e;
                        if (x < 0 || y < 0 || x >= columns || y >= rows) continue;
                        int beside = y * columns + x;
                        if (inside2[beside] && !loose[owners[beside]]) onBorder = true;
                    }
                if (!onBorder) continue;

                float x0 = -halfLocal.x + i * cellW, y0 = -halfLocal.y + j * cellH;
                float z = -halfLocal.z - lift;
                int a = vertices.Count;
                vertices.Add(new Vector3(x0, y0, z));
                vertices.Add(new Vector3(x0 + cellW, y0, z));
                vertices.Add(new Vector3(x0 + cellW, y0 + cellH, z));
                vertices.Add(new Vector3(x0, y0 + cellH, z));
                triangles.Add(a); triangles.Add(a + 3); triangles.Add(a + 2);
                triangles.Add(a); triangles.Add(a + 2); triangles.Add(a + 1);
            }

        if (vertices.Count == 0) return null;

        var mesh = new Mesh { name = "BlockCrackLines" };
        mesh.indexFormat = vertices.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        var normals = new List<Vector3>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++) normals.Add(Vector3.back);
        mesh.SetNormals(normals);
        mesh.RecalculateBounds();
        return mesh;
    }

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

        // A vertex's UV, in the frame the prefab's own mesh uses
        // (ArkanoidSetup.BuildWorldUvBoxMesh): a UV unit is a world unit, the u
        // axis is picked per face, and **v is `cross(normal, u)`**.
        //
        // That cross product is the whole of why this exists. Taking the front
        // face's v as +Y — the obvious choice — mirrors the grain vertically
        // against the prefab's -Y, so the patch of tile the block shows is no
        // longer the patch it showed a moment ago. On screen that is not a
        // subtle difference, it is a different texture, and it was reported as
        // one.
        Vector3 UAxisFor(Vector3 normal)
        {
            if (Mathf.Abs(normal.z) > 0.5f) return normal.z < 0f ? Vector3.right : Vector3.left;
            if (Mathf.Abs(normal.y) >= Mathf.Abs(normal.x)) return Vector3.right;
            return normal.x >= 0f ? Vector3.forward : Vector3.back;
        }

        Vector2 UvFor(Vector3 local, Vector3 normal)
        {
            var world = new Vector3(
                local.x / Mathf.Max(perWorld.x, 0.0001f),
                local.y / Mathf.Max(perWorld.y, 0.0001f),
                local.z / Mathf.Max(perWorld.z, 0.0001f));
            var u = UAxisFor(normal);
            var v = Vector3.Cross(normal, u);
            return new Vector2(Vector3.Dot(world, u), Vector3.Dot(world, v));
        }

        int columns = Mathf.Clamp(Mathf.RoundToInt(worldSize.x / CellSize), 8, MostColumns);
        int rows = Mathf.Clamp(Mathf.RoundToInt(worldSize.y / CellSize), 8, MostRows);

        // The outline as a signed distance, negative inside, and a bite is a
        // disc subtracted from it. Doing the shape this way rather than as a
        // list of cells is what lets the rim chamfer and the cell mask come off
        // the same function — the chamfer is just "how far inside the boundary
        // is this node", whatever shape a hit has left the boundary in.
        //
        // **Measured in world units, not in the mesh's own space**, and that is
        // the one correction this had to have. A box block's local space is a
        // unit cube under a non-uniform scale, so one world unit is a different
        // number of local units on every axis: on a block scaled (1.5, 0.5, 0.6)
        // a radius of 0.03 world is 0.02 local across and 0.06 local up. The
        // first version inset by the *y*-scaled radius and inflated by the
        // *x*-scaled one, which cut 0.02 of a world unit off the top and bottom
        // edges — harmless for as long as the two box blocks' radius was zero,
        // and three pixels of missing silhouette the moment it was not. It
        // presented as the block changing shape on its first hit.
        //
        // **The frame and the bites are asked about separately**, because the
        // two boundaries are not the same kind of edge and must not be drawn
        // the same way: the frame is the block's own rim and wears the bevel,
        // a bite is a break and wears the hard wall (see Side).
        float Frame(Vector2 point)
        {
            var world = new Vector2(point.x / perWorld.x, point.y / perWorld.y);
            var halfWorld = new Vector2(halfLocal.x / perWorld.x, halfLocal.y / perWorld.y);
            var corner = new Vector2(
                Mathf.Abs(world.x) - (halfWorld.x - cornerRadius),
                Mathf.Abs(world.y) - (halfWorld.y - cornerRadius));
            float inside = Mathf.Min(Mathf.Max(corner.x, corner.y), 0f);
            float outside = new Vector2(Mathf.Max(corner.x, 0f), Mathf.Max(corner.y, 0f)).magnitude;
            return inside + outside - cornerRadius;
        }

        float Outline(Vector2 point)
        {
            var world = new Vector2(point.x / perWorld.x, point.y / perWorld.y);
            float distance = Frame(point);

            // A bite is authored in local units (Brick.Carve scales it by the
            // block's own x), so it makes the same trip.
            for (int i = 0; i < bites.Count; i++)
            {
                var at = new Vector2(
                    bites[i].At.x / perWorld.x, bites[i].At.y / perWorld.y);
                distance = Mathf.Max(distance,
                    bites[i].Radius / perWorld.x - Vector2.Distance(world, at));
            }
            return distance;
        }

        float bevelDepth = RimBevel * perWorld.z;

        Vector2 CellMiddle(int i, int j) => new Vector2(
            -halfLocal.x + (i + 0.5f) * (halfLocal.x * 2f / columns),
            -halfLocal.y + (j + 0.5f) * (halfLocal.y * 2f / rows));

        // Which cells the block's own outline covers, before a bite is taken out
        // of it. Where the *rim* runs is a different question from which cells
        // are still standing, and keeping the two apart is what lets a corner
        // arc be rimmed and a bite be broken.
        var inFrame = new bool[columns * rows];
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < columns; i++)
                inFrame[j * columns + i] = Frame(CellMiddle(i, j)) < 0f;

        bool Framed(int i, int j) =>
            i >= 0 && j >= 0 && i < columns && j < rows && inFrame[j * columns + i];

        // Is the block's own outline what passes through this node? The grid's
        // outer ring is most of the rim but not all of it: a corner radius cuts
        // the corner *cell* out of the mask, and the staircase that leaves turns
        // at a node one in from the ring. Until that node counted as rim too,
        // the two edges meeting there were built as a *break* rather than as a
        // rim — a wall running the full depth of the block, lit as a surface of
        // its own, which a head-on key light gives nothing at all. It read as a
        // black wedge at every corner the camera could see the end of, on
        // Ceramics and Crystal alone, since they are the two materials that wear
        // this mesh from the first frame. The wider the block stood from the
        // middle of the screen the wider the wedge, because what was being shown
        // was the block's own depth.
        bool OnRim(int i, int j)
        {
            if (i == 0 || j == 0 || i == columns || j == rows) return true;
            int framed = (Framed(i - 1, j - 1) ? 1 : 0) + (Framed(i, j - 1) ? 1 : 0)
                + (Framed(i - 1, j) ? 1 : 0) + (Framed(i, j) ? 1 : 0);
            return framed > 0 && framed < 4;
        }

        var nodes = new Vector3[(columns + 1) * (rows + 1)];
        var nodeUvs = new Vector2[nodes.Length];
        int Index(int i, int j) => j * (columns + 1) + i;

        // A node on the grid's outer ring, pulled onto the outline itself. The
        // corner rounding cannot come out of the cell mask: a radius of 0.03
        // world is 1.7 cells, and the mask samples cell *centres*, so the arc
        // passes three ten-thousandths outside the corner cell's centre and
        // rounds nothing whatever — computed, after a magnified capture showed
        // square corners where the prefab's bevel gives rounded ones. Moving the
        // ring's own nodes is what the grid *can* express: away from a corner
        // the projection lands a node exactly where it already was, and at one
        // it draws the arc in a few segments.
        Vector2 OnOutline(Vector2 local)
        {
            if (cornerRadius <= 0f) return local;
            var world = new Vector2(local.x / perWorld.x, local.y / perWorld.y);
            var inner = new Vector2(
                halfLocal.x / perWorld.x - cornerRadius,
                halfLocal.y / perWorld.y - cornerRadius);
            if (inner.x <= 0f || inner.y <= 0f) return local;
            var nearest = new Vector2(
                Mathf.Clamp(world.x, -inner.x, inner.x),
                Mathf.Clamp(world.y, -inner.y, inner.y));
            var away = world - nearest;
            if (away.sqrMagnitude <= 0.0000001f) return local;
            var snapped = nearest + away.normalized * cornerRadius;
            return new Vector2(snapped.x * perWorld.x, snapped.y * perWorld.y);
        }

        for (int j = 0; j <= rows; j++)
            for (int i = 0; i <= columns; i++)
            {
                var point = new Vector2(
                    -halfLocal.x + i * (halfLocal.x * 2f / columns),
                    -halfLocal.y + j * (halfLocal.y * 2f / rows));
                if (OnRim(i, j)) point = OnOutline(point);

                // The face is flat. What says a block is damaged is the pieces
                // missing from it, not anything cut into what is left.
                nodes[Index(i, j)] = new Vector3(point.x, point.y, -halfLocal.z);
                nodeUvs[Index(i, j)] = UvFor(nodes[Index(i, j)], Vector3.back);
            }

        // The rim, as two rings. `nodes` keeps the front face, which is inset by
        // the bevel; `rimOuter` holds the silhouette itself, one bevel deeper,
        // and is what the strip runs out to and the side wall starts from. Only
        // the grid's own boundary is treated this way: an edge a *bite* has cut
        // into the block is a break and gets the hard wall below, because a
        // broken edge should look broken.
        var rimOuter = new Vector3[nodes.Length];
        var isRim = new bool[nodes.Length];
        for (int j = 0; j <= rows; j++)
            for (int i = 0; i <= columns; i++)
            {
                if (!OnRim(i, j)) continue;
                int node = Index(i, j);
                var point = nodes[node];
                var world = new Vector2(point.x / perWorld.x, point.y / perWorld.y);
                var inner = new Vector2(
                    halfLocal.x / perWorld.x - cornerRadius,
                    halfLocal.y / perWorld.y - cornerRadius);
                var nearest = new Vector2(
                    Mathf.Clamp(world.x, -inner.x, inner.x),
                    Mathf.Clamp(world.y, -inner.y, inner.y));
                var away = world - nearest;
                var outward = away.sqrMagnitude > 0.0000001f
                    ? away.normalized
                    : new Vector2(i == 0 ? -1f : i == columns ? 1f : 0f,
                        j == 0 ? -1f : j == rows ? 1f : 0f).normalized;

                isRim[node] = true;
                rimOuter[node] = new Vector3(point.x, point.y, point.z + bevelDepth);
                var inset = world - outward * RimBevel;
                nodes[node] = new Vector3(
                    inset.x * perWorld.x, inset.y * perWorld.y, point.z);
                nodeUvs[node] = UvFor(nodes[node], Vector3.back);
            }

        // Which cells are inside the outline at all, and which piece each one
        // belongs to. Measured at the cell's own centre, and measured once:
        // what changes between the passes below is only *which* piece is being
        // drawn, never where the pieces are.
        var inside = new bool[columns * rows];
        var owners = new int[columns * rows];
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < columns; i++)
            {
                var middle = CellMiddle(i, j);
                if (Outline(middle) >= 0f) continue;
                inside[j * columns + i] = true;
                owners[j * columns + i] = shards != null ? shards.At(middle) : 0;
            }

        // **A block is drawn as its pieces from the first frame, one closed
        // solid apiece, rather than as one skin over whatever is left.** That
        // is the whole of this mesh's contract with the player: a hit removes
        // a piece's triangles and touches nothing else, so every surviving
        // pixel is the pixel it was before the ball arrived. The old
        // construction raised walls only along the *outside* of what was
        // standing, so the first shed rebuilt the surviving geometry — and a
        // rebuilt edge is an edge that can look different, which is exactly
        // what was reported on the Crystal blocks.
        //
        // The price is paid where it belongs: an intact block carries every
        // interior wall it will ever need, so it is a few thousand vertices
        // from the start rather than ninety-six. It also means the seams
        // between pieces are visible from the first frame — a block visibly
        // *is* made of pieces — which is the look this trades for.
        var draw = new List<int>();
        if (piece >= 0) draw.Add(piece);
        else if (shards == null) draw.Add(-1);
        else
            for (int k = 0; k < shards.Sites.Length; k++)
                if (gone == null || !gone[k]) draw.Add(k);

        // The cells one pass draws. Rebuilt per piece, which is what makes each
        // piece a closed solid: a neighbouring piece reads as empty, so the
        // wall loops raise a side there exactly as they do at a hole.
        var solid = new bool[columns * rows];

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();

        // Normals are set rather than recalculated, because the rim's shading
        // depends on which surface each vertex belongs to and an average over
        // shared vertices cannot express that.
        //
        // **No tangents, and that is the whole point of this mesh matching the
        // prefab's.** `BuildWorldUvBoxMesh` writes positions, normals and UVs
        // and nothing else, so an intact block's tangent stream is absent: the
        // TBN the shader builds is degenerate, the normal map's perturbation
        // collapses onto the vertex normal, and the grain shades as the flat
        // pattern its albedo draws. This mesh used to write a real tangent per
        // vertex, so the first hit — the one that replaces the prefab's mesh
        // with this one — switched the block's normal map on. Same texture,
        // same UVs, same tiling: the relief appeared out of nowhere, the face
        // went glassy and every end face with it, and it was reported as the
        // block's texture changing when a piece broke off. A block that loses
        // a corner should look like the same block with a corner missing, so
        // the frame it is drawn in has to be the frame it was drawn in before.
        //
        // If the grain's relief is ever wanted in play, the fix belongs at the
        // *other* end — tangents on the prefab's mesh, matching the frames
        // below — never here alone, or the change comes back on the next hit.
        //
        // One normal per vertex does two jobs: it is the frame the UV was
        // written in *and* what the surface is lit as. A caller that wants a
        // surface lit as a face it does not belong to — the rim's side wall
        // does — passes the front face's normal here and hands `UvFor` its own
        // (see Rim).
        int Add(Vector3 position, Vector2 uv, Vector3 normal)
        {
            vertices.Add(position);
            uvs.Add(uv);
            normals.Add(normal);
            return vertices.Count - 1;
        }

        void Quad(int a, int b, int c, int d)
        {
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(a); triangles.Add(c); triangles.Add(d);
        }

        var frontIndex = new int[nodes.Length];

        int Corner(int i, int j)
        {
            int node = Index(i, j);
            if (frontIndex[node] < 0)
                frontIndex[node] = Add(nodes[node], nodeUvs[node], Vector3.back);
            return frontIndex[node];
        }

        // The back. Flat, and only where the front is: a bite goes right
        // through, so what shows in the notch is the room behind the block.
        var backIndex = new int[nodes.Length];

        int BackCorner(int i, int j)
        {
            int node = Index(i, j);
            if (backIndex[node] < 0)
            {
                var front = nodes[node];
                var back = new Vector3(front.x, front.y, halfLocal.z);
                backIndex[node] = Add(back, UvFor(back, Vector3.forward), Vector3.forward);
            }
            return backIndex[node];
        }

        // The walls: every cell edge with nothing beside it, whether that is
        // the block's own outline, the inside of a notch, or the border with
        // the piece next door. Drawn with their own vertices so the wall meets
        // the face in a hard edge rather than smearing the face's shading round
        // the corner.
        bool Beside(int i, int j) =>
            i >= 0 && j >= 0 && i < columns && j < rows && solid[j * columns + i];

        // Is the cell next door inside the block's outline at all — whoever it
        // belongs to and whether or not that piece is still standing?
        bool Within(int i, int j) =>
            i >= 0 && j >= 0 && i < columns && j < rows && inside[j * columns + i];

        // A wall stands where a cell meets the outside world, and **nowhere
        // along a seam between two pieces**. A wall runs the full depth of the
        // block, so on a see-through material a seam wall is not a hairline:
        // seen through the piece in front of it, it projects as a pale wedge
        // that widens with distance from the middle of the screen, which is why
        // the blocks at the edges of the field looked worst and the ones in the
        // middle looked clean. The block's own silhouette is the only place the
        // eye has ever been shown a side, so it is the only place that keeps
        // one.
        //
        // Leaving the seams open costs the hole a piece leaves its thickness —
        // a notch shows the room behind rather than a cut face. That is what a
        // bite already does (see Bite), and it is the price of the invariant:
        // raising the wall only once the neighbour has gone would be geometry
        // appearing on a hit, which is the whole thing this construction exists
        // to prevent.
        // A falling piece is the exception, and it has to be: once it is off the
        // block it *is* its own outline, so it takes a wall all the way round
        // and reads as the chunk of a solid it is (see BuildPiece).
        void Side(int i, int j, int di, int dj, int from, int to)
        {
            if (piece >= 0 ? Beside(i + di, j + dj) : Within(i + di, j + dj)) return;
            // The rim is the block's own outline and nothing else. A bite has
            // eaten into the frame rather than ended it, so the edge it leaves
            // is a break and takes the hard wall even where both its nodes
            // happen to sit on the rim.
            if (!Framed(i + di, j + dj) && isRim[from] && isRim[to]) Rim(from, to);
            else Wall(from, to);
        }

        // One pass per piece, each writing its own vertices: nothing is shared
        // across a seam, so removing a piece can never disturb its neighbour.
        foreach (int only in draw)
        {
            for (int cell = 0; cell < solid.Length; cell++)
                solid[cell] = inside[cell] && (only < 0 || owners[cell] == only);
            for (int n = 0; n < nodes.Length; n++) frontIndex[n] = backIndex[n] = -1;

            for (int j = 0; j < rows; j++)
                for (int i = 0; i < columns; i++)
                {
                    if (!solid[j * columns + i]) continue;
                    int a = Corner(i, j), b = Corner(i + 1, j);
                    int c = Corner(i + 1, j + 1), d = Corner(i, j + 1);
                    Quad(a, d, c, b);
                }

            // The back, and **only on a piece that has come off**. A block is
            // looked at from the front and nothing standing in this room ever
            // sees behind one, so on an opaque material the back face is hidden
            // by the front and on a see-through one it is the seam between two
            // pieces' backs — offset from the seam in front of it by the depth
            // of the block — showing through the glass as a second line. A
            // falling piece is the one thing here that turns over, so it is the
            // one thing that needs a back.
            if (piece >= 0)
                for (int j = 0; j < rows; j++)
                    for (int i = 0; i < columns; i++)
                    {
                        if (!solid[j * columns + i]) continue;
                        int a = BackCorner(i, j), b = BackCorner(i + 1, j);
                        int c = BackCorner(i + 1, j + 1), d = BackCorner(i, j + 1);
                        Quad(a, b, c, d);
                    }

            for (int j = 0; j < rows; j++)
                for (int i = 0; i < columns; i++)
                {
                    if (!solid[j * columns + i]) continue;
                    Side(i, j, -1, 0, Index(i, j + 1), Index(i, j));
                    Side(i, j, 1, 0, Index(i + 1, j), Index(i + 1, j + 1));
                    Side(i, j, 0, -1, Index(i, j), Index(i + 1, j));
                    Side(i, j, 0, 1, Index(i + 1, j + 1), Index(i, j + 1));
                }
        }

        // The block's own edge: a 45-degree strip from the inset face out to the
        // silhouette, then the side wall from there to the back. The strip's
        // inner vertices take the face's normal and its outer ones the side's,
        // which is what grades the shading across it and leaves the face and the
        // side flat — the prefab's bevel, built the same way (see RimBevel).
        void Rim(int from, int to)
        {
            var faceFrom = nodes[from];
            var faceTo = nodes[to];
            var edgeFrom = rimOuter[from];
            var edgeTo = rimOuter[to];

            // Outward, in the face's plane: the side's own normal, and the one
            // the strip grades into.
            var along = edgeTo - edgeFrom;
            var outward = Vector3.Cross(along, Vector3.forward).normalized;
            if (outward.sqrMagnitude < 0.5f) outward = Vector3.up;

            // The strip borrows the front face's frame for all four of its
            // vertices, exactly as the prefab's bevel strips borrow one of the
            // faces they touch: it is one bevel wide and no grain at this scale
            // reads the seam that leaves.
            int a = Add(faceFrom, UvFor(faceFrom, Vector3.back), Vector3.back);
            int b = Add(faceTo, UvFor(faceTo, Vector3.back), Vector3.back);
            // The strip takes the *face's* normal all the way across rather than
            // grading into the side's. Graded, it came out about seven per cent
            // brighter than the face at its peak and read as a light frame
            // drawn round every damaged block — measured, 204 against the face's
            // 190. Flat, it is the face carried out to the silhouette, and what
            // little of the side the perspective still shows is one bevel
            // further back than it was.
            int c = Add(edgeTo, UvFor(edgeTo, Vector3.back), Vector3.back);
            int d = Add(edgeFrom, UvFor(edgeFrom, Vector3.back), Vector3.back);
            Quad(a, b, c, d);

            // And the side, from the strip's far edge straight back. Flat, and
            // at the silhouette rather than at the inset face, so the block is
            // exactly as wide as it was.
            //
            // **It is lit as the front face.**
            // Its own normal is `outward`, dead perpendicular to a head-on key
            // light, so a side wide enough for the perspective to show came back
            // as a raw grey band down the end of every damaged block, beside a
            // prefab whose end shades as its face. Nothing about that band was
            // the geometry: the wall stands where the prefab's does. It is the
            // shading, and the answer is the one `BuildWorldUvBoxMesh` settled
            // on for the same reason — a surface angled away from the face *is*
            // a band, whatever colour it is, so an edge surface is not lit as a
            // surface of its own.
            //
            // **The UVs stay in the side's own frame**, exactly as the prefab's
            // side face does, which is what keeps the grain on the end: what
            // shows there is the same patch of tile the prefab shows. Taking
            // the front face's UVs as well was tried and loses the texture:
            // `UvFor(_, back)` ignores z, so both rings land on one row of
            // texels and the wall is that row smeared across the depth — at a
            // wide angle a blank panel where the prefab has grain.
            var backFrom = new Vector3(edgeFrom.x, edgeFrom.y, halfLocal.z);
            var backTo = new Vector3(edgeTo.x, edgeTo.y, halfLocal.z);
            int e = Add(edgeFrom, UvFor(edgeFrom, outward), Vector3.back);
            int f = Add(edgeTo, UvFor(edgeTo, outward), Vector3.back);
            int g = Add(backTo, UvFor(backTo, outward), Vector3.back);
            int h = Add(backFrom, UvFor(backFrom, outward), Vector3.back);
            Quad(e, f, g, h);
        }

        // A hole's wall: from the face straight back through the block, with its
        // own vertices and its own flat normal, so it meets the face in a hard
        // edge. That is what a break should look like, and it is the opposite of
        // what the rim wants (see Rim).
        void Wall(int from, int to)
        {
            var frontFrom = nodes[from];
            var frontTo = nodes[to];
            var farFrom = new Vector3(frontFrom.x, frontFrom.y, halfLocal.z);
            var farTo = new Vector3(frontTo.x, frontTo.y, halfLocal.z);

            var outward = Vector3.Cross(frontTo - frontFrom, farFrom - frontFrom).normalized;
            if (outward.sqrMagnitude < 0.5f) outward = Vector3.back;

            int a = Add(frontFrom, UvFor(frontFrom, outward), outward);
            int b = Add(frontTo, UvFor(frontTo, outward), outward);
            int c = Add(farTo, UvFor(farTo, outward), outward);
            int d = Add(farFrom, UvFor(farFrom, outward), outward);
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
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);

        mesh.RecalculateBounds();
        return mesh;
    }
}
