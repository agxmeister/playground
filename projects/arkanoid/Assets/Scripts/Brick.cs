using UnityEngine;

public class Brick : MonoBehaviour
{
    public int Points { get; set; } = 100;

    // The shape's own contribution to how hard the block is, authored on the
    // prefab: the full-slot slabs are 2, the half-width and round ones 1. Small
    // blocks are flimsy, which also answers for there being two of them in
    // every slot they fill.
    [SerializeField] int baseHardness = 2;

    // How many UV units this shape's face spans per world unit of it — a fact
    // about the mesh's own texture coordinates, authored per prefab, and the
    // only thing standing between one grain size and four different ones. The
    // stock cube and the stock sphere lay UVs 0..1 across a face whatever size
    // that face is, so their span is the reciprocal of their world size; the
    // rounded prism's mesh is authored at final size and puts local XY straight
    // into the UV, so its span is exactly 1. Divide the wanted grain density by
    // this and the tiling comes out in world terms on all four shapes — which
    // is what the cube-UV trap costs if it is skipped: the same texture reading
    // as pebbles on the slab and as stripes on the half-block.
    [SerializeField] Vector2 grainUvPerUnit = Vector2.one;

    [SerializeField] SpriteRenderer crackRenderer;
    // The crack net, stage-major: `stage * variants + variant`, so one flat
    // array carries what used to be a light array and a heavy one. Unity does
    // not serialize a jagged array, and a stride the setup script and this
    // component both derive from CrackStages is cheaper than a wrapper type
    // per row.
    [SerializeField] Sprite[] crackSprites;

    // How many steps of wear the net is drawn in. Four rather than the old
    // two, because a Neutronium slab owes ten hits and a two-stage overlay
    // spent across ten of them says almost nothing between the first and the
    // last. Each stage draws the same net further out from where the block was
    // first struck, so damage reads as one crack spreading rather than as a
    // different crack each time. The setup script writes exactly this many
    // textures per variant; it is the one number the two sides share.
    public const int CrackStages = 4;

    // How faint the net is on its first showing. It never starts invisible —
    // a block that has been hit should say so — but the last hit before it
    // goes should be the loudest, so opacity rides the same fraction the stage
    // does.
    const float CrackFirstOpacity = 0.55f;

    // What share of its own light a cracked face keeps along the crack. The net
    // is written white and tinted here, so this is the whole of its colour: the
    // block's own colour at half strength, which is the same substance in
    // shadow rather than a foreign grey pasted over it. A multiply keeps the
    // hue exactly, so a crack in a magenta block is a darker magenta.
    //
    // **A crack is always darker, never lighter.** The first version turned
    // round below a mid-grey pivot and drew a *pale* net on the dark half of
    // the ladder, on the reasoning that a dark line has nothing to be seen
    // against there. It reads badly, and the reason is that the overlay is a
    // sprite — unlit, drawn at exactly the value asked for — while the block is
    // lit: a pale line on a dark block is brighter than any lit part of that
    // block, so it sits *on* the face rather than *in* it, and a screen of them
    // looks like chalk rather than damage. Darker has no such failure mode,
    // because a crack in a real surface is a place light does not reach.
    //
    // What it costs is the bottom of the ladder: Neutronium's albedo is 0.02,
    // and half of nothing is nothing. That block wears its wear in the net's
    // *shape* — the sheen it takes off the metal — rather than in its value.
    const float CrackDarkness = 0.5f;

    // How far past its own edges a detonating block reaches for its neighbours.
    // The grid leaves a 0.14 gap between blocks in both directions, so anything
    // over that and under the next block's width catches exactly the ring that
    // touches this one — corners included — whatever shape either of them is.
    const float BlastReach = 0.2f;

    // Wear taken so far, and it is a float because a hit is worth what the ball
    // was carrying rather than a flat 1 (see Ball.Damage): a ball at two and a
    // half times its own speed does two and a half.
    float damage;
    int crackVariant = -1;
    // Set the moment a block starts coming apart, and checked on the way in, so
    // that two antimatter blocks side by side detonate each other once rather
    // than forever: Destroy is deferred to the end of the frame, so a block
    // already on its way out is still very much in the physics world.
    bool broken;
    BlockMaterialTraits traits = BlockMaterials.Of(BlockMaterial.Polymer);

    // The colour of the face the cracks are drawn on, in sRGB, kept because
    // the crack's colour is a function of it (see CrackTint). Both halves of
    // the look write it: the shared material asset says what the substance is,
    // and a per-block override on top of it says which pressing of it this one
    // came out of — and the second is the one the eye actually sees, so it has
    // the last word here too.
    Color faceColor = Color.white;

    // Shape times material. Nothing stores this: both halves can be set in
    // either order at spawn and the answer is always current.
    public int Hardness => baseHardness * traits.Multiplier;

    // What the block is made of, kept for anything that wants to ask after the
    // fact — the traits above are what the block itself runs on.
    public BlockMaterial Material { get; private set; } = BlockMaterial.Polymer;

    public bool Unbreakable => traits.Unbreakable;

    // The material is both the rule and the look, so the asset comes with it.
    // Assigning sharedMaterial points this instance at a shared asset rather
    // than editing one, so blocks of a material still batch together — which is
    // what the old per-instance colour tint was there to protect.
    public void SetMaterial(BlockMaterial kind, Material asset)
    {
        Material = kind;
        traits = BlockMaterials.Of(kind);
        if (asset != null)
        {
            GetComponent<MeshRenderer>().sharedMaterial = asset;
            // Materials are authored in sRGB and converted once with .linear on
            // the way into the shader, so the trip back out is .gamma. The
            // crack's contrast is a judgement about what the eye sees, and the
            // eye sees sRGB.
            if (asset.HasProperty("_BaseColor")) faceColor = asset.GetColor("_BaseColor").gamma;
        }
        else
        {
            // No asset handed over — a bench spawning a block with no game
            // running is the only way here — so the block is whatever its
            // prefab already wore, and that is what its cracks and its rubble
            // have to answer to.
            var worn = GetComponent<MeshRenderer>().sharedMaterial;
            if (worn != null && worn.HasProperty("_BaseColor")) faceColor = worn.GetColor("_BaseColor").gamma;
        }
    }

    // The casting, on top of the substance. The shared asset still says what
    // this block is made of; this says which pressing of it this particular
    // block came out of, and it goes on as a MaterialPropertyBlock rather than
    // as an edited material — a per-instance override costs no asset and leaves
    // the shared material exactly as authored, where `new Material(asset)` per
    // block would hand every brick a copy to leak.
    //
    // The comment on SetMaterial above is now only half true: blocks of one
    // material no longer batch together, because a property block opts the
    // renderer out of the SRP batcher. That was worth paying — the board holds
    // three dozen blocks and this is not where the frames go — and it is worth
    // knowing before a level holds three hundred.
    public void SetLook(BlockLook look)
    {
        if (!look.HasGrain) return;

        // The batch tint is what this block is actually the colour of, so it
        // replaces the shared asset's colour as the face the cracks answer to.
        // Already sRGB here — it makes the .linear trip below rather than
        // having made it already.
        faceColor = look.Tint;

        var tiling = new Vector2(
            look.GrainTilesPerUnit / Mathf.Max(grainUvPerUnit.x, 0.0001f),
            look.GrainTilesPerUnit / Mathf.Max(grainUvPerUnit.y, 0.0001f));

        var properties = new MaterialPropertyBlock();
        // sRGB in, linear to the shader, for the same reason every other authored
        // colour in this project makes the trip.
        properties.SetColor("_BaseColor", look.Tint.linear);
        properties.SetFloat("_Smoothness", look.Smoothness);
        properties.SetTexture("_BaseMap", look.Grain);
        properties.SetVector("_BaseMap_ST",
            new Vector4(tiling.x, tiling.y, look.GrainOffset.x, look.GrainOffset.y));
        if (look.GrainNormal != null)
        {
            properties.SetTexture("_BumpMap", look.GrainNormal);
            properties.SetVector("_BumpMap_ST",
                new Vector4(tiling.x, tiling.y, look.GrainOffset.x, look.GrainOffset.y));
        }
        GetComponent<MeshRenderer>().SetPropertyBlock(properties);
    }

    // The hit, and whether it was the last one the block could take. The *ball*
    // calls this now, out of its own collision handler, where the block used to
    // read `ball.Damage` out of one of its own. Which side owns it matters since
    // the ball started punching through what it breaks (see Ball.Punch): the
    // answer here decides whether the ball carries on or comes off, and the two
    // handlers for one contact run in an order nothing fixes — so the ball
    // cannot be told by a block whose turn may already have passed.
    //
    // A force field takes no damage and shows no wear, and answers `false`: it
    // was not destroyed, so the ball comes off it like any other bounce and
    // keeps every bit of the speed it arrived with. Unbreakable that cost the
    // ball its push would be a much smaller word.
    public bool TakeDamage(float amount)
    {
        if (traits.Unbreakable) return false;

        damage += amount;
        if (damage >= Hardness)
        {
            Break();
            return true;
        }

        if (crackRenderer == null || crackSprites == null || crackSprites.Length < CrackStages) return false;
        int variants = crackSprites.Length / CrackStages;

        // The variant (and its mirroring) is picked on the first hit and then
        // kept, so escalating damage reads as the same crack spreading.
        if (crackVariant < 0)
        {
            crackVariant = Random.Range(0, variants);
            crackRenderer.flipX = Random.value < 0.5f;
            crackRenderer.flipY = Random.value < 0.5f;
        }

        // Floor rather than round, so a block that is any part of the way
        // through shows the first stage and the last stage is the one standing
        // when the next hit takes it. A fraction of exactly 1 never arrives
        // here — that is the branch above — so the clamp is only guarding the
        // ball that overshoots.
        float fraction = damage / Hardness;
        int stage = Mathf.Clamp(Mathf.FloorToInt(fraction * CrackStages), 0, CrackStages - 1);
        crackRenderer.sprite = crackSprites[stage * variants + crackVariant];

        // Two things say "more damage" at once: more of the net is drawn, and
        // what is drawn is more opaque. Either alone was too quiet — the net
        // grows by a third of itself between stages, which is a change a player
        // has to be looking for, and opacity alone would have been the same
        // crack turned up.
        var tint = CrackTint();
        tint.a = Mathf.Lerp(CrackFirstOpacity, 1f, fraction);
        crackRenderer.color = tint;
        return false;
    }

    // What colour a crack in this block is: the face at CrackDarkness of its
    // own light, hue and all. Nothing branches on how dark the block already
    // is — see the constant for why that turning point was taken out.
    Color CrackTint() => new Color(
        faceColor.r * CrackDarkness, faceColor.g * CrackDarkness, faceColor.b * CrackDarkness);

    // How hard a block going off flares the perimeter. See `Break`.
    const float BreakFlash = 0.6f;

    // The block comes apart, however it was reached: by the ball wearing it
    // down, or by a neighbour going off next to it. A blast ignores hardness
    // entirely — a Neutronium slab beside antimatter simply goes — because a
    // block that survived the explosion next door would make placing one a
    // calculation rather than a decision.
    void Break()
    {
        if (broken) return;
        broken = true;

        var renderer = GetComponent<MeshRenderer>();
        var body = renderer.sharedMaterial;
        // The rubble is handed the round's paddle, so the chunks a brick
        // throws are worth catching rather than only worth watching.
        //
        // The chunks are the colour of *this* block rather than of the shared
        // material asset, which for anything with a variety authored for it are
        // two different colours: a Polymer block is the substance's grey only
        // on average, and a dark casting of it that shattered into pale grey
        // rubble read as another block's debris thrown where this one stood.
        // `faceColor` is the same colour the cracks answer to, and for the same
        // reason — it is what the block is actually seen to be. It is held in
        // sRGB, so it makes the usual trip on the way to a shader.
        Debris.Spawn(transform.position, renderer.bounds.size, faceColor.linear, body,
            1f, GameManager.Instance != null ? GameManager.Instance.Catcher : null);
        // The room takes note of it. Gentler than a border hit, and for a
        // reason rather than for taste: a brick goes off out in the middle of
        // the field, so its flare reaches the lamps at a share of its strength
        // already, and a full-force one would make breaking a block near an
        // edge louder than the ball hitting that edge.
        RimLights.Flash(transform.position, BreakFlash);
        if (GameManager.Instance != null) GameManager.Instance.OnBrickDestroyed(this);

        // After the score and before the object goes: a chained block calls
        // straight back into Break here, and `broken` above is what stops that
        // running back the way it came.
        if (traits.Detonates) Detonate();

        Destroy(gameObject);
    }

    // Everything touching this block goes with it — worth its full points and
    // throwing its own rubble, so a well-placed hit pays visibly. A force field
    // is immune here too: unbreakable that only held against the ball would be
    // a much smaller word.
    void Detonate()
    {
        var bounds = GetComponent<MeshRenderer>().bounds;
        var reach = new Vector2(bounds.size.x, bounds.size.y) + Vector2.one * (2f * BlastReach);
        foreach (var hit in Physics2D.OverlapBoxAll(transform.position, reach, 0f))
        {
            var neighbour = hit.GetComponent<Brick>();
            if (neighbour == null || neighbour == this || neighbour.Unbreakable) continue;
            neighbour.Break();
        }
    }
}
