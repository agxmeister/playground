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
// still means what it did; a material with no designs, which is every material
// but Polymer, goes on rolling. What a design must never become is a second
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

    // The three, in the enum's own order so `(int)design` indexes this directly.
    //
    // The values were picked on the bench in design mode and measured off the
    // render rather than guessed: a block at `Value` 0 comes out at about the
    // same brightness as the darkest block the old roll ever produced, and one
    // at 0.95 at about the lightest. They are meant to be nudged — a look that
    // is nearly right is one constant away from right, which is the whole reason
    // they live in one table instead of being scattered through the levels.
    static readonly Definition[] All =
    {
        new Definition(BlockMaterial.Polymer, Stipple, 0.95f),  // Chalk
        new Definition(BlockMaterial.Polymer, Pebble, 0.50f),   // Ash
        new Definition(BlockMaterial.Polymer, Crumb, 0.08f),    // Basalt
    };

    public static int Count => All.Length;

    public static Definition Of(BlockDesign design)
    {
        int index = (int)design;
        return index >= 0 && index < All.Length ? All[index] : All[0];
    }

    public static string NameOf(BlockDesign design) => design.ToString();

    // The designs a material has, in enum order. Polymer has three and every
    // other material has none, which is not a gap to be filled but the same
    // ordinary case `VarietyOf` already answers null for: no design means the
    // block rolls, exactly as it did before any of this existed.
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
        // The designs of one material are contiguous in `All` while Polymer is
        // the only material with any; the first of them is where its run starts.
        for (int i = 0; i < All.Length; i++)
            if (All[i].Material == material) return (BlockDesign)(i + index);
        return BlockDesign.Chalk;
    }
}
