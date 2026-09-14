using UnityEngine;

// A look with a name on it: one grain and one point on its material's band,
// written down so a scene shows the same block every time it is played.
//
// This is the answer to a plain problem with rolling every look. A roll makes a
// *wall of plastic* rather than a spreadsheet, which is what BlockVariety was
// written for, but it also means no two runs of a level look alike and nobody
// can say "the pale one" and be understood. A design fixes the two things that
// carry the look — which relief it was moulded with, and how light the batch
// came out, gloss riding along with it — and leaves the one thing that is not
// part of the look at all still random: where in the tiled grain the block
// happens to sit. So a level is repeatable and no two blocks in it are the same
// picture twice.
//
// **The die is not gone, it is narrowed.** `BlockVariety.Roll` still exists and
// still means what it did; a material with no designs — which is every material
// but Polymer, Ceramics and Crystal — goes on rolling. What a design must never become is a second
// hardness — nothing here touches `BlockMaterials`, and two blocks of one
// material differing in look differ in nothing else.
public enum BlockDesign
{
    // The near-white end with the tight Stipple speckle: cream rather than
    // white, matte, the grain reading as a softened sheen rather than as
    // countable bumps.
    Chalk,
    // The middle of the band with Pebble's orange peel — the value where the
    // tint and the relief are both doing work.
    Ash,
    // The dark end with the coarse Crumb granulate: near-black and glossy, its
    // big caps throwing the hard highlights that carry the relief down where
    // there is almost no tint left to modulate.
    Basalt,
    // Ceramics', chosen on the bench the same way and written down from the same
    // readout. Light to dark like Polymer's, and each one names a surface as
    // well as a value, because on a ceramic the two arrive together: the pale
    // end of this band is the fine white body you would only put veining on,
    // and the dark end is the one that wants a cloud rather than a net.
    //
    // The near-white marble: veining on a body pale enough for the veins to be
    // the only thing on it.
    Porcelain,
    // The middle, wearing the craquelure net — the one design of the three whose
    // character is a *line*, and the value where a warm mid grey gives those
    // lines something to be dark against.
    Craze,
    // The dark end, clouded. Down here the net would read as a grid of grout and
    // the veining as scratches; a cloud is what still reads, because it is the
    // one character that is in the body rather than on the face.
    Slate,
    // Crystal's three, and the one band whose points name a *substance* rather
    // than a shade: it runs frost to amethyst, so where a block lands on it
    // decides which mineral it is as much as how bright it came out. Light to
    // dark like the other two.
    //
    // **These three were picked off a rolled grid, not walked to on the dials**,
    // and the numbers are the rolls themselves — a probe in `BlockVariety.Roll`
    // printed what seed 1 dealt each of the twelve blocks, so these are the
    // looks that were actually on screen and not a reading of a picture of
    // them. It shows: none of the three sits at an end of the band, where
    // Polymer's outer two nearly do. That is the point rather than a rough
    // edge — the ends are where a die hardly ever lands, so a table written
    // from the ends is a table of looks nobody has seen in a wall. Which grain
    // goes with which brightness came out of the same session, and came out the
    // *other* way round from the reasoned version this replaces (facets pale,
    // druse dark).
    //
    // The pale end, shattered: slivers crossing every way on a body light
    // enough that the breaks are the only dark thing on it. Ice rather than a
    // stone, which is what the pale end of this band has always been called.
    Frost,
    // The middle, and a druse rather than a face — a bed of points on a lilac
    // body. The pairing goes this way round because points are what the middle
    // of the band can afford to spend: there is still colour here to see them
    // against, and a facet's single plane would waste it on one highlight.
    Amethyst,
    // The dark end, cleanly cleaved: coarse flat facets and nothing else on the
    // face. Down here the tint is nearly gone and the *creases* are what is
    // left to read the block by — which a facet field has more of than either
    // of the other two, and which stay legible at any brightness because they
    // are discontinuities rather than shading.
    Smoke,
}

public static class BlockDesigns
{
    // What a design *is*: the material it belongs to, which of that material's
    // grains it was moulded with, and where on the band the batch came out.
    // Everything else about the look follows from those — the tint is the band
    // read at `Value`, and the smoothness rides along with it, because in real
    // moulded plastic the dark stock is the glossy stock.
    public readonly struct Definition
    {
        public readonly BlockMaterial Material;
        public readonly int Grain;
        public readonly float Value;

        public Definition(BlockMaterial material, int grain, float value)
        {
            Material = material;
            Grain = grain;
            Value = value;
        }
    }

    // The grains in the order `ArkanoidSetup.PolymerGrains` writes them and
    // stage 95 wires them, which is the order `BlockVariety.grains` is indexed
    // in. Named constants rather than 0/1/2 at the call site, because a table of
    // bare indices is a table nobody can check by reading.
    const int Pebble = 0;
    const int Stipple = 1;
    const int Crumb = 2;

    // And Ceramics' three, in `ArkanoidSetup.CeramicsGrains`' order.
    const int Crackle = 0;
    const int Vein = 1;
    const int Cloudy = 2;

    // Crystal's, in `ArkanoidSetup.CrystalGrains`' order.
    const int Facet = 0;
    const int Shard = 1;
    const int Druse = 2;

    // Every design, in the enum's own order so `(int)design` indexes this
    // directly.
    //
    // The values were picked on the bench in design mode and read off its own
    // readout rather than guessed — which is the whole point of that readout
    // printing the numbers. They are meant to be nudged: a look that is nearly
    // right is one constant away from right, and that is the reason they live in
    // one table instead of being scattered through the levels.
    static readonly Definition[] All =
    {
        new Definition(BlockMaterial.Polymer, Stipple, 0.95f),   // Chalk
        new Definition(BlockMaterial.Polymer, Pebble, 0.50f),    // Ash
        new Definition(BlockMaterial.Polymer, Crumb, 0.08f),     // Basalt
        new Definition(BlockMaterial.Ceramics, Vein, 0.90f),     // Porcelain
        new Definition(BlockMaterial.Ceramics, Crackle, 0.35f),  // Craze
        new Definition(BlockMaterial.Ceramics, Cloudy, 0.10f),   // Slate
        new Definition(BlockMaterial.Crystal, Shard, 0.66f),     // Frost
        new Definition(BlockMaterial.Crystal, Druse, 0.48f),     // Amethyst
        new Definition(BlockMaterial.Crystal, Facet, 0.14f),     // Smoke
    };

    public static int Count => All.Length;

    public static Definition Of(BlockDesign design)
    {
        int index = (int)design;
        return index >= 0 && index < All.Length ? All[index] : All[0];
    }

    public static string NameOf(BlockDesign design) => design.ToString();

    // Whether a material has any designs at all. Polymer, Ceramics and Crystal
    // have three each and every other material has none, which is not a gap to
    // be filled but the same ordinary case `VarietyOf` already answers null for:
    // no design means the block rolls, exactly as it did before any of this
    // existed.
    public static bool HasAny(BlockMaterial material)
    {
        foreach (var definition in All)
            if (definition.Material == material) return true;
        return false;
    }

    // Which design the block in a given slot wears. Deterministic in the slot
    // and in nothing else, which is the whole point: the same level laid out
    // twice is the same wall twice, down to which block is the pale one.
    //
    // **This is a placeholder for a level format, and is meant to be replaced.**
    // There is one board in the game and it is a demonstration board — a column
    // per material, a row per shape — so there is nothing yet that could say
    // which design belongs where. Cycling by slot at least puts all three on
    // screen at once and puts them back in the same places next time; a real
    // level will name the design it wants and this will go.
    public static BlockDesign ForSlot(BlockMaterial material, int row, int column)
    {
        int count = 0;
        foreach (var definition in All)
            if (definition.Material == material) count++;
        if (count <= 0) return BlockDesign.Chalk;

        // Offset by the column as well as the row so a board that is one column
        // per material does not give every block in that column the same look.
        int index = ((row + column) % count + count) % count;
        // Counted off among this material's own designs rather than added to
        // where its run starts. The first version did the latter, which was only
        // correct because Polymer's three were the whole table; a second
        // material's designs made "contiguous in `All`" an invariant nobody had
        // written down, and this owes nothing to the order they are declared in.
        for (int i = 0; i < All.Length; i++)
        {
            if (All[i].Material != material) continue;
            if (index == 0) return (BlockDesign)i;
            index--;
        }
        return BlockDesign.Chalk;
    }
}
