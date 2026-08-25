using UnityEngine;

// The exhaust behind anything on either screen travelling faster than it has
// any right to: a boosted paddle (see "The paddle can be a rocket") and a ball
// carrying a push (see "The push can be charged"). Both boosts are otherwise
// invisible — a paddle at three and a half times its own speed looks exactly
// like a paddle, only sooner somewhere else, and a ball at three times its own
// looks exactly like the ball — so this is what says the speed is on, and how
// much of it. What tells the two apart is the colour it burns (`Blaze`) and
// nothing else, so the player learns one shape and reads the colour.
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

    // How much brighter the hottest exhaust burns than the coolest one. A piece
    // is tinted with its palette multiplied by this, and the project renders in
    // HDR, so a strength near the top pushes the core of the ribbon past white
    // and the exhaust reads as *brighter* and not merely as thicker. Thickness
    // and ribbon length are the other two things strength buys (see Plume); on
    // their own they said "there is more of it" where the point is "it is
    // burning harder".
    const float GlowTop = 2.4f;

    // An exhaust's colours as it cools: white at the nozzle, the flame proper
    // through the middle of its life, all but out by the end. A piece is tinted
    // every frame rather than once at birth — this is the one place in the game
    // where a thing's colour is the point of it, and an ember that stayed white
    // would read as debris rather than as something burning.
    //
    // It is a parameter rather than three constants because there are now two
    // things in the game with an exhaust, and what tells them apart is exactly
    // this: chemical orange behind the paddle, which burns fuel, and blue
    // behind a ball travelling faster than it has any business doing. Nothing
    // else about the two trails differs, which is the point — the player learns
    // one shape and reads the colour.
    public readonly struct Blaze
    {
        public readonly Color Hot;
        public readonly Color Flame;
        public readonly Color Ash;

        public Blaze(Color hot, Color flame, Color ash)
        {
            Hot = hot;
            Flame = flame;
            Ash = ash;
        }
    }

    // The paddle's: a rocket motor, so the yellow-white of something burning
    // fuel, cooling through orange into the near-black of soot.
    public static readonly Blaze Rocket = new Blaze(
        new Color(1f, 0.95f, 0.78f),
        new Color(1f, 0.5f, 0.12f),
        new Color(0.32f, 0.07f, 0.02f));

    // The ball's: hotter than a flame, because what it is showing is not fuel
    // being burnt but a ball carrying speed that was hit into it. Blue is the
    // colour that says "too hot for orange", and it pairs the ball's trail with
    // the blue gauge the push was wound on — the same mechanic, seen at both
    // ends of it.
    //
    // These are written in the space the shader takes them in — that is,
    // already-linear — rather than in sRGB and converted, because the palette
    // above is, and the two exhausts have to be tuned against each other. It is
    // why the blue looks improbably dark written down: 0.03 of green here is a
    // good 0.19 on screen, and a green picked to look right on paper came out a
    // washed-out cyan (the same trap `PowerWave`'s ramp fell into).
    public static readonly Blaze Plasma = new Blaze(
        new Color(0.55f, 0.80f, 1f),
        new Color(0.03f, 0.18f, 1f),
        new Color(0.004f, 0.012f, 0.06f));

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
    // What the piece burns like, and how hard. Carried per piece rather than
    // looked up when it is drawn, because a piece outlives the thrust that
    // threw it: the tail of a ribbon has to keep cooling at the brightness it
    // was born at even after the engine behind it has gone out.
    Blaze blaze;
    float glow;
    Renderer body;

    // One frame's worth of plume: the streak the nozzle laid down between where
    // it was last frame (`tailFrom`) and where it is now (`tailTo`), running
    // `back` — the way the exhaust goes, which is opposite the way the paddle
    // is travelling. `strength` is 0 for a paddle barely over cruising speed
    // and 1 for one at the top of its thrust.
    //
    // The exhaust is measured in two units rather than one, and they are two
    // because they are answers to different questions. `bore` is how wide the
    // nozzle is — the flame across, and the size of the sparks in it. `reach`
    // is the unit the ribbon's *length* is counted in. The paddle hands its own
    // height in for both, which is where the single number came from
    // originally; the ball wants a wide short jet, and a wake measured in one
    // unit could only be wide *and* long or narrow and short.
    //
    // It is the *swept path* rather than a puff at the nozzle, so consecutive
    // frames' streaks abut end to end however fast or slow the game is drawing.
    // A puff per frame was the first version: at sixty frames a second it read
    // as a ribbon and at the ten a backgrounded editor manages it read as a
    // dotted line, which is a trail whose thickness is a fact about the
    // machine rather than about the paddle.
    public static void Plume(Vector3 tailFrom, Vector3 tailTo, Vector3 back, float bore,
        float reach, float speed, float strength, Blaze blaze, Material material)
    {
        // Past the nozzle by a little, so the flame is attached to what is
        // burning rather than trailing a gap behind it. In bores, because what
        // it is covering is the mouth of the nozzle.
        float length = Vector3.Distance(tailFrom, tailTo) + bore * 0.8f;
        float thick = bore * Mathf.Lerp(0.7f, 1.15f, strength);

        var piece = NewPiece(material, tailTo + back * (length * 0.5f),
            Quaternion.LookRotation(Vector3.forward, back), blaze, strength);
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
        // choosing, so it is chosen (in `reach`es, so the menu's smaller paddle
        // gets a smaller flame) and the life falls out of it. The floor is for
        // the paddle slowest over cruising speed, whose flame would otherwise
        // last long enough to still be hanging there after it stopped.
        float wanted = reach * Mathf.Lerp(3.5f, 8f, strength);
        piece.life = Mathf.Clamp(wanted / Mathf.Max(speed, 0.01f), 0.06f, 0.35f);
        // White-hot at birth, so the nozzle end of the ribbon is the bright end
        // and the exhaust reads as cooling away from the paddle rather than as
        // one orange bar.
        piece.heat = 0f;
        piece.transform.localScale = piece.fromScale;
    }

    // One ember, thrown out of the plume and left to cool. `nozzle` is the
    // middle of the paddle's trailing edge; the rest is as Plume, except that
    // there is no `reach` here — a spark has no ribbon, so the bore is the only
    // measurement it needs. The paddle asks for these on a cadence
    // (`EmberRate`) rather than once a frame, for the same reason the plume is
    // a swept path.
    public static void Ember(Vector3 nozzle, Vector3 back, float bore, float strength,
        Blaze blaze, Material material)
    {
        var piece = NewPiece(material, nozzle, Random.rotation, blaze, strength);
        // Fanned narrowly about the exhaust: an ember flying out sideways would
        // be a spark off an impact, and nothing has been hit.
        var across = new Vector3(-back.y, back.x, 0f);
        float angle = Random.Range(-22f, 22f) * Mathf.Deg2Rad;
        var direction = back * Mathf.Cos(angle) + across * Mathf.Sin(angle);
        piece.velocity = direction * Random.Range(1.5f, 5f) * Mathf.Lerp(0.6f, 1f, strength);
        piece.velocity.z = Random.Range(-0.4f, 0.4f);
        piece.transform.position += across * Random.Range(-0.5f, 0.5f) * bore;

        float size = bore * Random.Range(0.2f, 0.45f) * Mathf.Lerp(0.7f, 1.1f, strength);
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

    static JetTrail NewPiece(Material material, Vector3 at, Quaternion rotation,
        Blaze blaze, float strength)
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
        piece.blaze = blaze;
        piece.glow = Mathf.Lerp(1f, GlowTop, strength);
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
            ? Color.Lerp(blaze.Hot, blaze.Flame, cooled * 2f)
            : Color.Lerp(blaze.Flame, blaze.Ash, (cooled - 0.5f) * 2f);
        colorBlock ??= new MaterialPropertyBlock();
        // The glow multiplies the colour rather than being lerped towards
        // white, so what brightens is the colour the exhaust already is: a
        // hard-burning blue goes to a blue-white core with blue still around
        // it, where a lerp to white would have bleached the flame out at
        // exactly the moment it is meant to be most itself.
        colorBlock.SetColor(BaseColorId, colour * glow);
        // Matte, so the exhaust is the colour it is rather than the colour the
        // light makes of it: the paddle's own material is glossy, and a
        // highlight sliding across a flame gives away that it is a box.
        colorBlock.SetFloat(SmoothnessId, 0f);
        body.SetPropertyBlock(colorBlock);
    }
}
