using System.Collections.Generic;
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

    // What the damage carver needs to know about this shape's outline, both
    // authored per prefab because neither can be read off a mesh with any
    // confidence. The radius is in world units and is zero for a square block;
    // `carvesDamage` is off for the round block alone, whose face is a sphere
    // rather than a flat slab and whose UVs are the stock sphere's rather than
    // this project's world-unit ones — the two assumptions the carver makes
    // (see BlockDamage.Build).
    [SerializeField] float outlineCornerRadius;
    [SerializeField] bool carvesDamage = true;

    [SerializeField] SpriteRenderer crackRenderer;
    // The crack net, stage-major: `stage * variants + variant`, so one flat
    // array carries what used to be a light array and a heavy one. Unity does
    // not serialize a jagged array, and a stride the setup script and this
    // component both derive from CrackStages is cheaper than a wrapper type
    // per row.
    [SerializeField] Sprite[] crackSprites;

    // The chip decals, one per variant shape — a flake of glaze missing, with
    // the pale body showing in the hollow. Unlike the net these are not a
    // ladder: a chip is one event at one place, so the array is a bag of
    // shapes to pick from rather than a sequence of stages, and a block that
    // has been hit three times wears three of them.
    [SerializeField] Sprite[] chipSprites;
    [SerializeField] Material crackLineMaterial;

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

    // How many chips a block may carry. A ceramic block owes two hits, so two
    // is all a round will normally show; the cap is there for the bench, which
    // damages a block a fifth of its hardness at a time, and for whatever
    // later material chips at a hardness of five. Past the cap nothing more is
    // stamped rather than the oldest chip being moved: a chip that healed over
    // and reopened somewhere else is the one reading a player would notice.
    const int MaxChips = 4;

    // How big a chip is, in world units, before its own jitter. The ball is
    // 0.4 across and a block 1.5 by 0.5, so this is a flake about a third of
    // the ball — small enough that two of them do not cover the face and big
    // enough to read at the size a block is actually drawn.
    const float ChipSize = 0.13f;
    const float ChipSizeJitter = 0.28f;

    // How far in from the block's outline a chip's centre is pulled, in its
    // own sizes. Every contact in a 2D game lands on the block's *silhouette*
    // — the ball meets the outline, never the face it is drawn on — so a chip
    // stamped at the raw contact point would hang half off the block. Pulled
    // in by a bit over half a chip, it bites into the edge it was struck on,
    // which is where a flake actually comes off.
    const float ChipInset = 0.55f;

    // How hard a chip is drawn, and it is the *only* number in its colour: the
    // decal is written black with its shading in the alpha, so it multiplies
    // the block down rather than painting anything on it, and this scales the
    // whole multiply. Nothing here reads `faceColor`, which is the difference
    // from the crack net and was a correction rather than a choice — a chip
    // tinted with the block's albedo came out cold grey on a warm-lit ceramic,
    // because the warmth on that face is the *light* and a sprite is lit by
    // nothing (see WriteChipTextures for the measurement). Drawn as shadow, a
    // chip is in the block's own colour under the block's own light for free,
    // whatever either of them is, and cannot come out brighter than the face.
    const float ChipStrength = 0.9f;

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
    // The node the chips hang under, and how many are already stamped. The
    // node is created on the first chip rather than authored on the prefab:
    // a chip is a runtime event at a runtime place, and four empty renderers
    // on every block of every material is a hierarchy nobody can read.
    Transform chipRoot;
    int chipsShown;

    // The block's own crack net, the pieces gone from its outline, and the
    // mesh those two are cut into. The mesh belongs to this instance and is
    // destroyed with it: it is built here rather than loaded, so nothing else
    // will ever free it (see ReleaseDamage).
    // How the block is divided into the pieces it can lose, and which of them
    // have already gone. Both are made on the first hit.
    BlockDamage.Shards shards;
    bool[] shardGone;

    // The pieces that have cracked but not yet fallen. Kept apart from
    // shardGone rather than folded into one three-state array, because the two
    // are asked different questions in different places: the mesh needs to know
    // which cells to drop and which to sink, and the shedding pass needs the
    // list of pieces it owes the player.
    bool[] shardLoose;

    // The ribbon the crack is drawn as, and the unlit material it wears
    // (wired by stage 110). A child object rather than part of the block,
    // because the whole point is that the block's own mesh is not touched while
    // it is only cracked — see BlockDamage.BuildCrackLines.
    Transform crackLines;
    Mesh crackLineMesh;
    readonly List<BlockDamage.Bite> bites = new List<BlockDamage.Bite>();
    Mesh damagedMesh;
    // The space the prefab's own mesh is authored in — a unit cube for the box
    // blocks, final size for the rounded prism. Read off the shared mesh once,
    // before the first carve replaces it, because after that the only mesh on
    // the renderer is one this component made.
    Vector3 bodyLocalSize = Vector3.one;
    bool bodyMeasured;
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

    // Whether this block sheds pieces, which takes *both* halves agreeing: the
    // material has to be one that comes apart rather than crazes (Ceramics and
    // Crystal, see BlockMaterials.ComesApart) and the shape has to be one the
    // pieces can be cut out of (`carvesDamage` — every block but the round one,
    // whose face is a sphere). A block that fails either test wears the crack
    // net drawn on its face and shatters into cube rubble, which is what every
    // block did before pieces existed.
    bool ComesApart => carvesDamage && BlockMaterials.ComesApart(Material);

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
    //
    // `at` is where the ball touched, in world space, and it is optional
    // because not every caller has one: the bench hands over a point on the
    // block's outline so that what it shows is what a round would show, and
    // anything that damages a block without a place to point at simply leaves
    // the face's chipping out. Only a material that chips reads it at all
    // (see BlockMaterials.Chips).
    public bool TakeDamage(float amount, Vector2? at = null)
    {
        if (traits.Unbreakable) return false;

        damage += amount;
        if (damage >= Hardness)
        {
            Break();
            return true;
        }

        // Before the net, because a chip is the hit itself where the net is
        // the state the block is left in — and a chip that landed under the
        // net's own sprite would be drawn over by it (see the sorting order in
        // Chip).
        if (at.HasValue) Chip(at.Value);

        // The damage the block wears in its own geometry: the piece the ball
        // just knocked out of it, gone from the block and falling (see
        // BlockDamage and "A block comes apart in pieces"). This is the shape
        // of the block changing, not a picture drawn on it — which is why it
        // is done here, off the same hit, rather than by the overlay below.
        Carve(at);

        // The drawn net is what a block wears when it does *not* come apart: a
        // plastic block crazes and keeps its shape, so the crack is a picture
        // on its face and the ladder of wear is the picture spreading, exactly
        // as it was before pieces existed. A block that sheds pieces has no use
        // for it — the pieces are the damage, and a net drawn over a hole is a
        // second, differently-seeded craquelure floating in front of the first.
        if (ComesApart) return false;

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

    // A flake of the face gone, where the ball landed. Nothing about the block
    // moves: this is one more unlit sprite laid on the same plane the crack net
    // is drawn on, so a chip costs a renderer and changes no geometry, no
    // collider and no hardness — a chipped block bounces the ball exactly as an
    // unmarked one does.
    //
    // The face the chips are laid on is the crack overlay's own transform: it
    // already stands just in front of the mesh's front face on all four shapes
    // and already covers the block as a unit square, so a world offset from its
    // centre lands where it is aimed without this having to know which shape it
    // is on. What it must *not* inherit is that transform's scale — the overlay
    // is squashed three to one on a slab, and a chip stretched with it would be
    // a smear rather than a flake. `chipRoot` cancels it (its own scale is the
    // reciprocal of the overlay's), which is also what makes a chip free to be
    // rotated: a rotated child of a non-uniformly scaled parent comes out
    // sheared, and under chipRoot there is no scale left to shear it.
    void Chip(Vector2 at)
    {
        if (!BlockMaterials.Chips(Material)) return;
        if (crackRenderer == null || chipsShown >= MaxChips) return;

        var face = crackRenderer.transform;
        var faceScale = face.lossyScale;
        if (chipRoot == null)
        {
            var node = new GameObject("Chips");
            chipRoot = node.transform;
            chipRoot.SetParent(face, false);
            chipRoot.localPosition = Vector3.zero;
            chipRoot.localScale = new Vector3(
                1f / Mathf.Max(Mathf.Abs(faceScale.x), 0.0001f),
                1f / Mathf.Max(Mathf.Abs(faceScale.y), 0.0001f),
                1f / Mathf.Max(Mathf.Abs(faceScale.z), 0.0001f));
        }

        float size = ChipSize * (1f + Random.Range(-ChipSizeJitter, ChipSizeJitter));

        // Pulled in off the outline the contact was on, and clamped to the
        // face so that a corner hit stays on the block. The inset never eats
        // more than a quarter of the smaller half-extent, or a chip on the
        // round block — half a unit across — would be pulled to its middle.
        var half = new Vector2(Mathf.Abs(faceScale.x), Mathf.Abs(faceScale.y)) * 0.5f;
        float inset = Mathf.Min(size * ChipInset, Mathf.Min(half.x, half.y) * 0.5f);
        var offset = at - (Vector2)face.position;
        offset.x = Mathf.Clamp(offset.x, -half.x + inset, half.x - inset);
        offset.y = Mathf.Clamp(offset.y, -half.y + inset, half.y - inset);

        var where = new Vector3(
            face.position.x + offset.x, face.position.y + offset.y, face.position.z);

        // The decal is the *drawing* of a hollow, and a block that carves has a
        // real one — the bite `Carve` takes out of its outline at this same
        // point. So it is drawn only where nothing can be cut: on the round
        // block, and on any shape whose carving is switched off. Drawn over a
        // real notch it reads as a sticker laid beside the hole it is a picture
        // of, which is what the bench showed.
        if (!ComesApart && chipSprites != null && chipSprites.Length > 0)
        {
            var chip = new GameObject("Chip" + chipsShown);
            chip.transform.SetParent(chipRoot, false);
            chip.transform.position = where;
            // Turned about the view axis only — the chip lies in the face's
            // plane and every one of them may lie in it differently.
            chip.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            chip.transform.localScale = Vector3.one * size;

            var renderer = chip.AddComponent<SpriteRenderer>();
            renderer.sprite = chipSprites[Random.Range(0, chipSprites.Length)];
            // Over the crack net rather than under it: the glaze is gone where
            // a chip is, so a craze line running across the hollow is a line
            // that is no longer there.
            renderer.sortingOrder = crackRenderer.sortingOrder + 1;
            renderer.color = ChipTint();
        }
        chipsShown++;

        // And the flake itself, which is the other half of the same event: the
        // decal is the hollow left behind, this is the piece that came out of
        // it, thrown out of the block through the point that was struck and
        // falling out of the round. Scenery only — see Debris.Chip for why it
        // is never worth catching. The pieces are the block's own colour, the
        // same one its rubble is (see Break), so a chip off a dark casting and
        // a chip off a pale one drop different debris.
        var away = offset.sqrMagnitude > 0.000001f ? offset.normalized : Vector2.up;
        Debris.Chip(where, away, size,
            faceColor.linear, GetComponent<MeshRenderer>().sharedMaterial);
    }

    // A chip is drawn in no colour of its own: black, at ChipStrength of the
    // shading its texture carries.
    static Color ChipTint() => new Color(0f, 0f, 0f, ChipStrength);

    // How big a bite the ball takes out of the outline, as a share of the chip
    // it leaves — the decal marks the hollow, and this is the piece of the
    // block that is actually gone from the edge. Under half, because a bite is
    // measured as a radius against a chip measured as a width, and because a
    // notch that swallowed the whole chip would eat a tenth of a block's height
    // on the first hit it took.
    const float BiteOfChip = 0.35f;

    // The health a block has to be down to before a piece actually leaves it.
    // Above this the hit only *cracks* the piece it landed on — the piece is
    // outlined on the face and stays where it is (BlockDamage.LooseSink); at
    // this point and below, everything cracked comes away and every further hit
    // takes its piece there and then.
    //
    // Half, so a block's life reads in two halves: it is a block with cracks in
    // it, and then it is a block with pieces missing. Two states rather than
    // one gradient is the point — the first half tells the player *which* piece
    // is going before it goes, which is a thing the old shed-on-every-hit
    // version never said.
    const float ShedsBelowHealth = 0.5f;

    // The block's shape after this hit. Cheap to say and not cheap to do — a
    // slab's damaged mesh is a few thousand vertices — so it happens once per
    // hit and never per frame, and the mesh it replaces is destroyed rather
    // than left to the garbage collector, which does not collect meshes.
    void Carve(Vector2? at)
    {
        if (!ComesApart) return;

        var filter = GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;

        if (!bodyMeasured)
        {
            bodyLocalSize = filter.sharedMesh.bounds.size;
            bodyMeasured = true;
        }

        var worldSize = Vector3.Scale(bodyLocalSize, transform.lossyScale);
        var halfLocal = new Vector2(bodyLocalSize.x * 0.5f, bodyLocalSize.y * 0.5f);
        var localHit = at.HasValue
            ? (Vector2)transform.InverseTransformPoint(at.Value.x, at.Value.y, transform.position.z)
            : Vector2.zero;

        // A material that chips loses a piece of its edge where it was hit. One
        // per hit, and the same hit the decal and the falling flake come off,
        // so all three are one event seen three ways.
        if (at.HasValue && BlockMaterials.Chips(Material))
        {
            float radius = ChipSize * BiteOfChip * (bodyLocalSize.x / Mathf.Max(worldSize.x, 0.0001f));
            bites.Add(new BlockDamage.Bite { At = localHit, Radius = radius });
        }

        // How many pieces this block comes apart in: about one per hit it can
        // take, so a slab sheds a piece at a time and the hit that kills it
        // takes whatever is left standing. Clamped at both ends — two pieces is
        // a block cut in half rather than broken, and past eight the pieces are
        // smaller than the ball that is knocking them off.
        // Seeded off where the block stands rather than off the object, so a
        // bench rebuild at the same seed lays down the same wall breaking the
        // same way — and so two blocks side by side never break alike.
        shards ??= BlockDamage.Shards.Build(
            Mathf.RoundToInt(transform.position.x * 613f + transform.position.y * 271f),
            halfLocal, Mathf.Clamp(Hardness, 3, 8), BlockMaterials.EdgeOf(Material));
        shardGone ??= new bool[shards.Sites.Length];
        shardLoose ??= new bool[shards.Sites.Length];

        // The piece the ball just knocked off — the one nearest where it
        // landed that is still there. It is thrown *before* the block is
        // rebuilt without it, so the two are one frame's worth of the same
        // event rather than a hole appearing and something falling later.
        //
        // It is worth catching, exactly like the pieces a block throws when it
        // finally goes: a piece of a block is a piece of a block whether the
        // block survived losing it or not, and the paddle has no way of telling
        // the two apart in the air. That does mean a hit which breaks nothing
        // can still pay — see GameManager.DebrisPoints for what a catch is
        // worth, and note that the paddle has to leave the ball to collect it.
        // Which half of its life the block is in, which is the whole of the
        // difference between a crack and a hole (see ShedsBelowHealth).
        bool sheds = Hardness - damage <= Hardness * ShedsBelowHealth;

        if (at.HasValue)
        {
            int struck = shards.NearestStanding(localHit, shardGone);
            if (struck >= 0 && !sheds)
            {
                // Cracked, not taken: the piece the ball landed on is drawn on
                // the face and goes nowhere yet. Nothing is thrown, because
                // nothing has come off — a flake of glaze is what a hit at this
                // stage costs, and Chip has already thrown that.
                //
                // And the block's own mesh is left exactly as it was: the crack
                // is a ribbon in front of the face, so a hit that only cracks
                // cannot change the block's silhouette, its edges or its grain.
                shardLoose[struck] = true;
                ShowCrackLines(worldSize);
                return;
            }
            else if (struck >= 0)
            {
                // Past the halfway mark. Everything that was cracked comes away
                // at once — the crack was the promise and this is it kept — and
                // so does the piece just struck, which by then may be the only
                // one left standing.
                var catcher = GameManager.Instance != null ? GameManager.Instance.Catcher : null;
                for (int i = 0; i < shardLoose.Length; i++)
                {
                    if (!shardLoose[i] || shardGone[i]) continue;
                    shardLoose[i] = false;
                    shardGone[i] = true;
                    Shed(i, worldSize, localHit, catcher);
                }
                if (!shardGone[struck])
                {
                    shardGone[struck] = true;
                    Shed(struck, worldSize, localHit, catcher);
                }
            }
        }

        // Past the crack stage the ribbon has nothing left to promise: the
        // pieces it outlined are the holes now.
        HideCrackLines();

        var carved = BlockDamage.BuildBlock(
            bodyLocalSize, worldSize, bites, outlineCornerRadius, shards, shardGone);

        ReleaseDamage();
        damagedMesh = carved;
        filter.sharedMesh = damagedMesh;


    }

    // The crack, redrawn for whatever is loose now. The mesh belongs to this
    // instance exactly as the damaged mesh does, and is freed the same way.
    void ShowCrackLines(Vector3 worldSize)
    {
        var lines = BlockDamage.BuildCrackLines(
            bodyLocalSize, worldSize, bites, outlineCornerRadius, shards, shardLoose);
        if (lines == null) return;

        if (crackLines == null)
        {
            var node = new GameObject("CrackLines");
            crackLines = node.transform;
            crackLines.SetParent(transform, false);
            crackLines.localPosition = Vector3.zero;
            crackLines.localRotation = Quaternion.identity;
            crackLines.localScale = Vector3.one;
            node.AddComponent<MeshFilter>();
            var renderer = node.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = crackLineMaterial;
            // A crack is a mark on a face, not a thing in the room: it casts
            // nothing and catches nothing, exactly as the drawn net never did.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        if (crackLineMesh != null) Destroy(crackLineMesh);
        crackLineMesh = lines;
        crackLines.GetComponent<MeshFilter>().sharedMesh = crackLineMesh;

        // The block's own colour at the same darkness the drawn net used, so a
        // crack in a magenta block is a darker magenta and not a grey line.
        var properties = new MaterialPropertyBlock();
        properties.SetColor("_BaseColor", CrackTint().linear);
        crackLines.GetComponent<MeshRenderer>().SetPropertyBlock(properties);
    }

    void HideCrackLines()
    {
        if (crackLines != null) Destroy(crackLines.gameObject);
        crackLines = null;
        if (crackLineMesh != null) Destroy(crackLineMesh);
        crackLineMesh = null;
    }

    // One piece off the block and into the world: built out of the same cells
    // the block has just stopped drawing, stood where it stood, and handed to
    // Debris to fall. Nothing about the block moves — the piece is a separate
    // object from the moment it comes away, which is what lets it turn and fall
    // while the wall it left stays exactly where it was.
    void Shed(int piece, Vector3 worldSize, Vector2 from, Paddle catcher)
    {
        var mesh = BlockDamage.BuildPiece(
            bodyLocalSize, worldSize, bites, outlineCornerRadius,
            shards, piece, out var centre);
        if (mesh == null) return;
        if (mesh.vertexCount == 0)
        {
            Destroy(mesh);
            return;
        }

        // Out of the block along the way the ball came in — measured from where
        // the piece sat rather than from the contact, so a piece knocked off
        // the far end still leaves in a direction that makes sense.
        var away = (Vector2)(centre - (Vector3)from);
        if (away.sqrMagnitude < 0.000001f) away = Vector2.up;

        Debris.Piece(
            mesh, transform.TransformPoint(centre), transform.rotation, transform.lossyScale,
            away.normalized, faceColor.linear,
            GetComponent<MeshRenderer>().sharedMaterial, catcher);
    }

    // Everything the block has left, thrown at once: what a block breaking
    // *is*, now that it is made of pieces. Like the pieces knocked off along
    // the way, they are worth catching (see "Rubble is worth catching").
    void ScatterRemains()
    {
        if (shards == null || shardGone == null || !ComesApart) return;

        var worldSize = Vector3.Scale(bodyLocalSize, transform.lossyScale);
        var catcher = GameManager.Instance != null ? GameManager.Instance.Catcher : null;
        for (int i = 0; i < shards.Sites.Length; i++)
        {
            if (shardGone[i]) continue;
            shardGone[i] = true;
            Shed(i, worldSize, shards.Sites[i] * 2f, catcher);
        }
    }

    // The instance's own mesh, freed. A Mesh is an unmanaged object: dropping
    // the reference leaks it until the scene unloads, and a round of thirty-six
    // blocks taking two hits each would leak seventy of them.
    void ReleaseDamage()
    {
        if (crackLineMesh != null)
        {
            Destroy(crackLineMesh);
            crackLineMesh = null;
        }
        if (damagedMesh == null) return;
        Destroy(damagedMesh);
        damagedMesh = null;
    }

    void OnDestroy() => ReleaseDamage();

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
        // A block that comes apart breaks into the pieces it was already
        // divided into; everything else shatters into the cube rubble, which is
        // still what a plastic block throws.
        if (ComesApart && shards != null) ScatterRemains();
        else
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
