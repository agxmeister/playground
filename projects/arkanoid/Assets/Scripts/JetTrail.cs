using UnityEngine;

// The exhaust behind a boosted paddle (see "The paddle can be a rocket"). The
// boost is otherwise invisible — a paddle at three and a half times its own
// speed looks exactly like a paddle, only sooner somewhere else — so this is
// what says the thrust is on, and how hard.
//
// Like Ricochet and Debris it is cosmetic and runtime-only: plain meshes moved
// by hand in Update with no physics components, so nothing here can disturb the
// 2D gameplay colliders, and nothing about it is authored.
//
// It is made of two kinds of piece, for the same reason a ricochet is: one
// carries the shape of the thing and the other carries the detail. The *plume*
// is a slab thrown off the paddle's trailing edge every frame, wide across the
// paddle and shrinking away fast — a run of them overlapping is the ribbon the
// eye reads as a flame. The *embers* are the sparks that scatter out of it.
// Neither is parented to the paddle: the whole point of an exhaust is that it
// is left behind, and a plume that travelled with the paddle would read as a
// glow bolted to it.
public class JetTrail : MonoBehaviour
{
    // How many embers a second the nozzle throws at full thrust. Fewer than
    // this and a fast paddle lays down a dotted line rather than a trail.
    public const float EmberRate = 70f;

    // The exhaust's colours as it cools: white at the nozzle, flame through the
    // middle of its life, all but out by the end. A piece is tinted every frame
    // rather than once at birth — this is the one place in the game where a
    // thing's colour is the point of it, and an ember that stayed white would
    // read as debris rather than as something burning.
    static readonly Color Hot = new Color(1f, 0.95f, 0.78f);
    static readonly Color Flame = new Color(1f, 0.5f, 0.12f);
    static readonly Color Ash = new Color(0.32f, 0.07f, 0.02f);

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

    static Mesh cubeMesh;
    static MaterialPropertyBlock colorBlock;

    Vector3 velocity;
    Vector3 fromScale;
    Vector3 toScale;
    float life;
    float age;
    // Where in the cooling the piece starts. The plume is the flame itself and
    // is never white-hot for long; an ember is thrown out of the hottest part
    // of it and has the whole of the fall to cool down.
    float heat;
    Renderer body;

    // One frame's worth of plume: the streak the nozzle laid down between where
    // it was last frame (`tailFrom`) and where it is now (`tailTo`), running
    // `back` — the way the exhaust goes, which is opposite the way the paddle
    // is travelling. `height` is the paddle's drawn height and `strength` is 0
    // for a paddle barely over cruising speed and 1 for one at the top of its
    // thrust.
    //
    // It is the *swept path* rather than a puff at the nozzle, so consecutive
    // frames' streaks abut end to end however fast or slow the game is drawing.
    // A puff per frame was the first version: at sixty frames a second it read
    // as a ribbon and at the ten a backgrounded editor manages it read as a
    // dotted line, which is a trail whose thickness is a fact about the
    // machine rather than about the paddle.
    public static void Plume(Vector3 tailFrom, Vector3 tailTo, Vector3 back, float height,
        float speed, float strength, Material material)
    {
        // Past the nozzle by a little, so the flame is attached to the paddle
        // rather than trailing a gap behind it.
        float length = Vector3.Distance(tailFrom, tailTo) + height * 0.8f;
        float thick = height * Mathf.Lerp(0.7f, 1.15f, strength);

        var piece = NewPiece(material, tailTo + back * (length * 0.5f),
            Quaternion.LookRotation(Vector3.forward, back));
        // Local Y runs along `back` under that rotation, so the length is the y
        // of the scale and the thickness is the x — across the exhaust, which
        // is up and down the screen.
        piece.fromScale = new Vector3(thick, length, thick);
        // Thins away without shortening: a contrail dissipates where it was
        // rather than being sucked back into the engine.
        piece.toScale = new Vector3(thick * 0.15f, length, thick * 0.15f);
        // How long a piece lasts is worked out from how long the *ribbon* should
        // be, rather than picked as a time: the ribbon is the ground the nozzle
        // covers while a piece is alive, so a fixed life would give a flame
        // whose length was the paddle's speed — a yard of it while spooling up
        // and half the field at the top of the thrust. Length is the thing worth
        // choosing, so it is chosen (in paddle heights, so the menu's smaller
        // paddle gets a smaller flame) and the life falls out of it. The floor
        // is for the paddle slowest over cruising speed, whose flame would
        // otherwise last long enough to still be hanging there after it stopped.
        float wanted = height * Mathf.Lerp(3.5f, 8f, strength);
        piece.life = Mathf.Clamp(wanted / Mathf.Max(speed, 0.01f), 0.06f, 0.35f);
        // White-hot at birth, so the nozzle end of the ribbon is the bright end
        // and the exhaust reads as cooling away from the paddle rather than as
        // one orange bar.
        piece.heat = 0f;
        piece.transform.localScale = piece.fromScale;
    }

    // One ember, thrown out of the plume and left to cool. `nozzle` is the
    // middle of the paddle's trailing edge; the rest is as Plume. The paddle
    // asks for these on a cadence (`EmberRate`) rather than once a frame, for
    // the same reason the plume is a swept path.
    public static void Ember(Vector3 nozzle, Vector3 back, float height, float strength,
        Material material)
    {
        var piece = NewPiece(material, nozzle, Random.rotation);
        // Fanned narrowly about the exhaust: an ember flying out sideways would
        // be a spark off an impact, and nothing has been hit.
        var across = new Vector3(-back.y, back.x, 0f);
        float angle = Random.Range(-22f, 22f) * Mathf.Deg2Rad;
        var direction = back * Mathf.Cos(angle) + across * Mathf.Sin(angle);
        piece.velocity = direction * Random.Range(1.5f, 5f) * Mathf.Lerp(0.6f, 1f, strength);
        piece.velocity.z = Random.Range(-0.4f, 0.4f);
        piece.transform.position += across * Random.Range(-0.5f, 0.5f) * height;

        float size = height * Random.Range(0.2f, 0.45f) * Mathf.Lerp(0.7f, 1.1f, strength);
        piece.fromScale = new Vector3(size, size, size);
        piece.toScale = Vector3.zero;
        piece.life = Random.Range(0.15f, 0.32f);
        piece.heat = 0f;
        piece.transform.localScale = piece.fromScale;
    }

    // The pieces are unparented roots, like debris and ricochet sparks, so they
    // outlive the paddle's boost and have to be swept up when the screen they
    // were thrown on changes.
    public static void ClearAll()
    {
        // The sortless overload: the sorted one is obsolete in Unity 6, and
        // nothing here cares what order the pieces come back in.
        foreach (var piece in FindObjectsByType<JetTrail>())
            Destroy(piece.gameObject);
    }

    static JetTrail NewPiece(Material material, Vector3 at, Quaternion rotation)
    {
        var go = new GameObject("JetTrail");
        go.transform.SetPositionAndRotation(at, rotation);
        go.AddComponent<MeshFilter>().sharedMesh = CubeMesh;
        var meshRenderer = go.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        // Exhaust throws no shadow. Every shadow on either screen lands on the
        // backdrop behind it, and a flame's would be a dark smear chasing the
        // paddle across the fog — the one thing on screen that would say the
        // trail is a row of solid boxes.
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var piece = go.AddComponent<JetTrail>();
        piece.body = meshRenderer;
        return piece;
    }

    // The stock cube mesh isn't loadable by name at runtime, so it is lifted off
    // a throwaway primitive once and shared by every piece, exactly as Ricochet
    // and Debris do.
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
        if (age >= life)
        {
            Destroy(gameObject);
            return;
        }

        float t = age / life;
        transform.position += velocity * Time.deltaTime;
        transform.localScale = Vector3.Lerp(fromScale, toScale, t);

        // Cooling: white into flame over the first half of what is left of the
        // piece's heat, flame into ash over the second.
        float cooled = Mathf.Lerp(heat, 1f, t);
        var colour = cooled < 0.5f
            ? Color.Lerp(Hot, Flame, cooled * 2f)
            : Color.Lerp(Flame, Ash, (cooled - 0.5f) * 2f);
        colorBlock ??= new MaterialPropertyBlock();
        colorBlock.SetColor(BaseColorId, colour);
        // Matte, so the exhaust is the colour it is rather than the colour the
        // light makes of it: the paddle's own material is glossy, and a
        // highlight sliding across a flame gives away that it is a box.
        colorBlock.SetFloat(SmoothnessId, 0f);
        body.SetPropertyBlock(colorBlock);
    }
}
