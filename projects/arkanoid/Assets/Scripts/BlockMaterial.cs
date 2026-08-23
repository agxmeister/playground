// What a block is made of, which is a separate question from what shape it is.
// The shape says how hard the block is to begin with (Brick.BaseHardness, an
// authored fact about the prefab); the material multiplies that. Splitting the
// two is what lets a level lay the same slab down twice and have one of them
// take five times the beating, and it is why a block's colour is no longer
// something the round paints on at spawn — the material *is* the look, and the
// look is the warning.
//
// The order here is the order GameManager's material assets are wired in, so
// entries may be appended but not reshuffled.
public enum BlockMaterial
{
    ForceField,
    Polymer,
    Ceramics,
    Antimatter,
    Titanium,
    Crystal,
    Inconel,
    Waspaloy,
    Neutronium,
}

// One material's rules. A struct of three fields rather than a ScriptableObject
// per material: nothing here is meant to be tuned without also retuning the
// look it comes with, and the look is written in ArkanoidSetup beside the rest
// of the project's assets.
public readonly struct BlockMaterialTraits
{
    // How many times over the shape's own hardness the block has to be hit.
    // Zero is not "free" but "never": see Unbreakable.
    public readonly int Multiplier;

    // Whether breaking the block takes its neighbours with it.
    public readonly bool Detonates;

    public BlockMaterialTraits(int multiplier, bool detonates = false)
    {
        Multiplier = multiplier;
        Detonates = detonates;
    }

    // A multiplier of zero cannot mean a hardness of zero — `damage >= hardness`
    // would be true before the ball ever arrived and the block would break
    // itself on spawn. It means the block is not on the ball's terms at all.
    public bool Unbreakable => Multiplier <= 0;
}

public static class BlockMaterials
{
    // The ladder, weakest to strongest, with the two that are not really on it
    // sitting where their multiplier puts them. Whole numbers throughout: a
    // hardness is a count of hits, and the crack overlay only has two stages to
    // spend across it, so a half-step would read as noise rather than as wear.
    public static BlockMaterialTraits Of(BlockMaterial material) => material switch
    {
        // Not armour but a rule: the ball comes off it and nothing else happens.
        BlockMaterial.ForceField => new BlockMaterialTraits(0),
        BlockMaterial.Polymer => new BlockMaterialTraits(1),
        BlockMaterial.Ceramics => new BlockMaterialTraits(1),
        // One hit, and everything touching it goes too — including whatever was
        // going to take five. A soft block that makes hard blocks cheap is worth
        // more to a level than another hard block.
        BlockMaterial.Antimatter => new BlockMaterialTraits(1, detonates: true),
        BlockMaterial.Titanium => new BlockMaterialTraits(2),
        BlockMaterial.Crystal => new BlockMaterialTraits(2),
        BlockMaterial.Inconel => new BlockMaterialTraits(3),
        BlockMaterial.Waspaloy => new BlockMaterialTraits(4),
        BlockMaterial.Neutronium => new BlockMaterialTraits(5),
        _ => new BlockMaterialTraits(1),
    };

    // How many materials there are, for the array of assets GameManager holds
    // and the setup stage that fills it.
    public const int Count = (int)BlockMaterial.Neutronium + 1;
}
