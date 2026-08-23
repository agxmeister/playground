using UnityEngine;

public class Brick : MonoBehaviour
{
    public int Points { get; set; } = 100;

    // The shape's own contribution to how hard the block is, authored on the
    // prefab: the full-slot slabs are 2, the half-width and round ones 1. Small
    // blocks are flimsy, which also answers for there being two of them in
    // every slot they fill.
    [SerializeField] int baseHardness = 2;

    [SerializeField] SpriteRenderer crackRenderer;
    [SerializeField] Sprite[] lightCrackSprites;
    [SerializeField] Sprite[] heavyCrackSprites;

    // How far past its own edges a detonating block reaches for its neighbours.
    // The grid leaves a 0.14 gap between blocks in both directions, so anything
    // over that and under the next block's width catches exactly the ring that
    // touches this one — corners included — whatever shape either of them is.
    const float BlastReach = 0.2f;

    int damage;
    int crackVariant = -1;
    // Set the moment a block starts coming apart, and checked on the way in, so
    // that two antimatter blocks side by side detonate each other once rather
    // than forever: Destroy is deferred to the end of the frame, so a block
    // already on its way out is still very much in the physics world.
    bool broken;
    BlockMaterialTraits traits = BlockMaterials.Of(BlockMaterial.Polymer);

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
        if (asset != null) GetComponent<MeshRenderer>().sharedMaterial = asset;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        var ball = collision.collider.GetComponent<Ball>();
        if (ball == null) return;

        TakeDamage(ball.Damage);
    }

    void TakeDamage(int amount)
    {
        // A force field takes no damage and shows no wear. The ball has already
        // been turned around by the engine by the time this runs; refusing the
        // hit is the whole of the behaviour.
        if (traits.Unbreakable) return;

        damage += amount;
        if (damage >= Hardness)
        {
            Break();
            return;
        }

        if (crackRenderer == null || lightCrackSprites == null || lightCrackSprites.Length == 0) return;

        // The variant (and its mirroring) is picked on the first hit and then
        // kept, so escalating damage reads as the same crack spreading.
        if (crackVariant < 0)
        {
            crackVariant = Random.Range(0, lightCrackSprites.Length);
            crackRenderer.flipX = Random.value < 0.5f;
            crackRenderer.flipY = Random.value < 0.5f;
        }
        float fraction = (float)damage / Hardness;
        crackRenderer.sprite = fraction <= 0.5f ? lightCrackSprites[crackVariant] : heavyCrackSprites[crackVariant];
    }

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
        Debris.Spawn(transform.position, renderer.bounds.size,
            body != null ? body.GetColor("_BaseColor") : Color.white, body,
            1f, GameManager.Instance != null ? GameManager.Instance.Catcher : null);
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
