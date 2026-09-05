using UnityEngine;

// Rubble spawned when a brick shatters. Fragments are plain meshes moved by
// hand in Update — no physics components, so they can never disturb the 2D
// gameplay colliders.
//
// A round's rubble is worth catching: every fragment a brick drops can be
// landed on the paddle for `GameManager.DebrisPoints`, so breaking a brick is
// only half the score in it and the paddle has somewhere to be besides under
// the ball. The menu's slabs shatter into the same fragments with no catcher
// handed over, and those stay purely cosmetic — there is no score to add to on
// the menu screen.
public class Debris : MonoBehaviour
{
    const float Gravity = 18f;
    const float KillY = -8f;
    // Below this fraction of remaining life the fragment shrinks away, since
    // the opaque Lit material can't alpha-fade. Only cosmetic rubble ever gets
    // there: a fragment that can be caught has to survive the whole fall, or it
    // would wink out of the air on its way to a paddle already moving for it.
    const float ShrinkFraction = 0.35f;

    static Mesh cubeMesh;
    static MaterialPropertyBlock colorBlock;

    Vector3 velocity;
    Vector3 spinAxis;
    float spinSpeed;
    float life;
    float age;
    Vector3 baseScale;
    // The paddle this fragment can be landed on, or null for rubble that is
    // only scenery. Held rather than looked up so the fragment keeps answering
    // to the paddle it was thrown for.
    Paddle catcher;
    // Where the fragment was last frame, so a fast one can't fall straight
    // through the paddle between two positions either side of it.
    Vector3 previous;

    // `amount` scales the fragment count for casters bigger than a brick — a
    // menu slab four bricks wide would otherwise break into the same handful of
    // chunks and read as too sparse for its size. `catcher` is the paddle the
    // rubble may be landed on for points; leaving it out spawns rubble that is
    // scenery and nothing else, which is what the menu's slabs want.
    public static void Spawn(Vector3 origin, Vector3 brickSize, Color color, Material material,
        float amount = 1f, Paddle catcher = null)
    {
        int count = Mathf.Max(1, Mathf.RoundToInt(Random.Range(6, 10) * amount));
        for (int i = 0; i < count; i++)
        {
            var offset = new Vector3(
                Random.Range(-0.4f, 0.4f) * brickSize.x,
                Random.Range(-0.4f, 0.4f) * brickSize.y,
                0f);

            float chunk = Mathf.Min(brickSize.x, brickSize.y);
            var scale = new Vector3(
                Random.Range(0.15f, 0.35f),
                Random.Range(0.15f, 0.35f),
                Random.Range(0.15f, 0.35f)) * chunk;

            var debris = Fragment(origin + offset, scale, color, material);
            debris.catcher = catcher;
            debris.velocity = new Vector3(
                offset.x * Random.Range(2f, 5f),
                Random.Range(1f, 4f),
                // Catchable rubble is kept on the playing plane. The catch is
                // judged in X and Y like everything else in the rally, so a
                // fragment that had drifted a body's depth in front of the
                // paddle would still be caught — and be seen not touching it.
                catcher != null ? 0f : Random.Range(-0.5f, 0.5f));
            debris.spinAxis = Random.onUnitSphere;
            debris.spinSpeed = Random.Range(90f, 480f);
            // Rubble that can be caught lives until it is, or until it has
            // plainly fallen past the paddle; only scenery is on a timer.
            debris.life = catcher != null ? Mathf.Infinity : Random.Range(1.2f, 2f);
        }
    }

    // How many pieces come off a chip, and how big each is as a share of the
    // hollow the decal draws. Two or three, so what falls is *the flake* —
    // this is one piece of glaze breaking away, not the block coming apart,
    // and a handful of confetti off every hit would read as the second.
    const int ChipPiecesFewest = 2;
    const int ChipPiecesMost = 4;
    const float ChipPieceSmallest = 0.45f;
    const float ChipPieceLargest = 1f;

    // How the flake leaves: thrown out along the way the ball came in, kicked
    // up a little because it was struck rather than dropped, and pushed toward
    // the camera so it falls in *front* of the wall instead of down its face —
    // the blocks stand in a plane and a piece sliding down inside it would
    // disappear behind the block below.
    const float ChipToss = 1.8f;
    const float ChipLift = 1.2f;
    const float ChipForward = 1.1f;

    // How long a piece is in the air. Measured on the bench: at this gravity a
    // piece is five units below the block it came off within a second, which is
    // the height of the field — so the whole of what this number buys is
    // whether the flake is still on screen when the eye gets to it. Shorter and
    // it is a flicker at the point of impact; much longer and the ball is
    // hitting the next block while the last one is still shedding.
    const float ChipLifeShortest = 1f;
    const float ChipLifeLongest = 1.6f;

    // What a chipped block actually loses: two or three pieces of its own
    // face, thrown off where the ball landed and falling out of the round.
    // Scenery by construction — no catcher is taken, because a block chips on
    // every hit it survives and rubble worth points off every one of those
    // would pay a player for not breaking anything (see Brick.Chip).
    //
    // `size` is the decal's own size, so the pieces are the size of the hole
    // they came out of; `away` is the direction out of the block through the
    // point that was struck, which is the whole of where they go.
    public static void Chip(Vector3 at, Vector2 away, float size, Color color, Material material)
    {
        int count = Random.Range(ChipPiecesFewest, ChipPiecesMost);
        for (int i = 0; i < count; i++)
        {
            var scale = Vector3.one * (size * Random.Range(ChipPieceSmallest, ChipPieceLargest));
            var debris = Fragment(at, scale, color, material);
            debris.velocity = new Vector3(
                away.x * ChipToss * Random.Range(0.4f, 1.3f) + Random.Range(-0.3f, 0.3f),
                away.y * ChipToss * Random.Range(0.2f, 0.8f) + Random.Range(0.2f, 1f) * ChipLift,
                -Random.Range(0.3f, 1f) * ChipForward);
            debris.spinAxis = Random.onUnitSphere;
            debris.spinSpeed = Random.Range(180f, 720f);
            debris.life = Random.Range(ChipLifeShortest, ChipLifeLongest);
        }
    }

    // One fragment, however it was thrown: the mesh, the material, the shade
    // and the place. What it does next — how fast, how long, whether it can be
    // caught — belongs to the caller, and is the only thing the two kinds of
    // rubble disagree about.
    static Debris Fragment(Vector3 position, Vector3 scale, Color color, Material material)
    {
        colorBlock ??= new MaterialPropertyBlock();

        var fragment = new GameObject("Debris");
        fragment.AddComponent<MeshFilter>().sharedMesh = CubeMesh;
        var renderer = fragment.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;

        // Per-fragment brightness variation makes the pile read as broken
        // chunks rather than uniform confetti.
        float shade = Random.Range(0.7f, 1.05f);
        colorBlock.SetColor("_BaseColor", new Color(color.r * shade, color.g * shade, color.b * shade, color.a));
        renderer.SetPropertyBlock(colorBlock);

        fragment.transform.position = position;
        fragment.transform.rotation = Random.rotation;
        fragment.transform.localScale = scale;

        var debris = fragment.AddComponent<Debris>();
        debris.baseScale = scale;
        debris.previous = position;
        return debris;
    }

    // Sweeps away every fragment still in the air. Fragments are unparented
    // root objects that outlive whatever spawned them, so a screen change has
    // to clear them by hand or the last screen's rubble rains over the next
    // one. Called at transitions only, which is why the scene scan is fine.
    public static void ClearAll()
    {
        foreach (var fragment in FindObjectsByType<Debris>(FindObjectsSortMode.None))
            Destroy(fragment.gameObject);
    }

    // The stock cube mesh isn't loadable by name at runtime, so it is lifted
    // off a throwaway primitive once and shared by every fragment.
    static Mesh CubeMesh
    {
        get
        {
            if (cubeMesh == null)
            {
                var template = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cubeMesh = template.GetComponent<MeshFilter>().sharedMesh;
                Destroy(template);
            }
            return cubeMesh;
        }
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age >= life || transform.position.y < KillY)
        {
            Destroy(gameObject);
            return;
        }

        velocity.y -= Gravity * Time.deltaTime;
        previous = transform.position;
        transform.position += velocity * Time.deltaTime;
        transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.World);

        if (catcher != null && LandedOnCatcher())
        {
            // The chunk bursts where it is caught rather than simply vanishing,
            // in the same sparks a ball throws off a border — a caught fragment
            // is worth score, so it has to be seen being caught.
            var renderer = GetComponent<MeshRenderer>();
            Ricochet.Spawn(transform.position, Vector2.up, renderer.sharedMaterial);
            if (GameManager.Instance != null) GameManager.Instance.OnDebrisCaught();
            Destroy(gameObject);
            return;
        }

        // Scenery only: a catchable fragment has no life to run out of, and
        // measuring what is left of an endless one gives nothing back.
        if (catcher != null) return;
        float remaining = (life - age) / life;
        if (remaining < ShrinkFraction) transform.localScale = baseScale * (remaining / ShrinkFraction);
    }

    // The paddle is measured by what is *drawn* rather than by its collider,
    // both across and upward: catching rubble is an eye's judgement of a chunk
    // landing on a paddle, not the rally's contact, and the collider leaves out
    // the `edgeRadius` the paddle carries most of its height in.
    bool LandedOnCatcher()
    {
        var paddle = catcher.GetComponent<Renderer>();
        if (paddle == null) return false;

        // The paddle's face grown by the fragment's own half-size, so contact is
        // judged between two bodies rather than between a point and a body.
        var bounds = paddle.bounds;
        float reach = transform.localScale.x * 0.5f;
        float left = bounds.min.x - reach, right = bounds.max.x + reach;
        float bottom = bounds.min.y - reach, top = bounds.max.y + reach;

        var position = transform.position;
        if (position.x >= left && position.x <= right && position.y >= bottom && position.y <= top)
            return true;

        // Rubble is at its fastest by the time it reaches the paddle — fast
        // enough to be above the paddle one frame and below it the next — so the
        // step it just took is crossed against the paddle's top face as well.
        if (previous.y <= top || position.y >= top) return false;
        float t = (top - previous.y) / (position.y - previous.y);
        float crossing = Mathf.Lerp(previous.x, position.x, t);
        return crossing >= left && crossing <= right;
    }
}
