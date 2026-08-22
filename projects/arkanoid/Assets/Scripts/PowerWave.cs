using UnityEngine;

// The charge gathering under a paddle that is winding up a push (see "The push
// can be charged" in CLAUDE.md). The charge is otherwise invisible — a paddle
// holding one looks exactly like a paddle — and it is a thing the player has to
// time to a tenth of a second, so it has to be readable at a glance: how much is
// on it, and that it is full.
//
// It is a **gauge, not a stream**. Three waves stand under the paddle, the first
// the widest and each one after it smaller, so what they make is a pyramid stood
// on its point: broad where it meets the paddle and tapering away below. The
// first fills, and only once it is at its full width does the second begin to
// appear, and so on. The number standing full is therefore the number the player
// reads — one wave is a third of the charge, two is two thirds, three is all of
// it — and a count of three is a count nobody has to actually count.
//
// The waves get *smaller* as they go and *brighter* as they go, which is one
// fact told twice: the charge is being driven to a point, so the last wave is
// the smallest and the fiercest, and how far down the taper the bright end has
// reached is the reading. It also puts the hottest colour where there is most
// room for it — out in the dark strip below, rather than up against the pale
// underside of the paddle.
//
// Like JetTrail, Ricochet and Debris it is cosmetic and runtime-only: plain
// meshes moved by hand with no physics components, so nothing here can disturb
// the 2D gameplay colliders, and nothing about it is authored.
//
// The waves are blue where the exhaust is orange, and they hang below the paddle
// where the exhaust streams behind it. Both are deliberate and both are the same
// reason: the two mechanics live on the same paddle, so a blue pyramid under it
// can never be mistaken for a flame trailing off it.
public class PowerWave : MonoBehaviour
{
    // How many waves the gauge is made of. Three is chosen against what the eye
    // can take in without counting: two is not a gauge, and five would have to
    // be read.
    const int Waves = 3;

    // Each wave's full width, as a fraction of the paddle's own, and its
    // thickness, as a fraction of the paddle's height. Both taper away down the
    // stack, which is what makes it a shape rather than a stack — the pyramid is
    // recognisable half-built, which matters because half-built is the state it
    // is usually seen in.
    //
    // The first wave is **clearly narrower than the paddle**, not merely a shade
    // under it. Matching the paddle's width was tried and it read as the paddle
    // having a coloured underside rather than as a wave standing below one — the
    // gauge needs daylight at both ends to be a thing of its own.
    static readonly float[] Widths = { 0.75f, 0.52f, 0.32f };
    static readonly float[] Thicknesses = { 0.24f, 0.2f, 0.16f };

    // How far below the paddle's underside the first wave stands, and the gap
    // between one wave and the next — both in paddle heights, because the menu's
    // paddle is a scaled-down copy of the round's, and because the strip under
    // the paddle is the one part of the screen a round has nothing standing in.
    //
    // The gap is measured **edge to edge, not centre to centre**, which is the
    // only way it can look even: the waves are of three different thicknesses,
    // so evenly-spaced centres leave more daylight between the thin ones at the
    // bottom than between the thick ones at the top, and the stack read as
    // spreading out as it went down.
    const float Clearance = 0.7f;
    const float Gap = 0.36f;

    // How far below `Clearance` each wave's middle sits, worked out once from the
    // thicknesses and the gap so that the daylight between every pair is the
    // same. Kept as a table rather than accumulated in the drawing loop because
    // a wave that is not up yet must not shift the ones below it.
    static readonly float[] Drops = BuildDrops();

    static float[] BuildDrops()
    {
        var drops = new float[Waves];
        for (int i = 1; i < Waves; i++)
            drops[i] = drops[i - 1] + (Thicknesses[i - 1] + Thicknesses[i]) * 0.5f + Gap;
        return drops;
    }

    // A wave that is still filling grows out of nothing to its full width, so
    // the growing is what the eye catches. This is how much of its width it has
    // the instant it appears: a wave that started at literally nothing spent the
    // first tenth of its filling invisible, which read as the gauge sticking.
    const float Seed = 0.18f;

    // The gauge's colours. `Cool` is where any wave starts, deep and dark;
    // `LiveFirst` and `LiveLast` are what the first and the last wave come to
    // when they are full, and every wave between takes its share of the way
    // between them. So intensity climbs down the taper — the last and smallest
    // wave is the brightest thing on the gauge, which is what makes "how far the
    // bright end has got" the same reading as "how many waves are full".
    //
    // The ramp is **blue the whole way**, from a deep blue up to a bright pale
    // one, and it keeps its green: a blue with the green taken out of it is an
    // indigo, and an indigo ramp went purple against the room's cold murk rather
    // than reading as the one bright colour on the screen. The hot end stops
    // short of white for a different reason — these are Lit surfaces under one
    // directional light, so nothing can be brighter than white, and a near-white
    // bar therefore reads as unlit paddle grey rather than as the fiercest thing
    // in the picture.
    //
    // These are written as the **sRGB colours they are meant to look like**, and
    // handed to the shader converted (see the end of Draw). The project renders
    // in linear colour space, so a value written straight into `_BaseColor` from
    // script is taken as already-linear and comes out considerably paler and
    // less saturated than the number reads: an authored 0.45 green landed at
    // about 0.70 on screen, which is how a strong blue ramp turned up looking
    // like a washed-out cyan one. The material Inspector does this conversion for
    // a colour picked by hand, and a property block does not.
    static readonly Color Cool = new Color(0f, 0.18f, 0.5f);
    static readonly Color LiveFirst = new Color(0f, 0.4f, 1f);
    // The hot end keeps its blue by having *less* green than red-and-blue would
    // suggest: taken up towards an even light cyan it came out reading teal
    // against the room's murk, which is the same wrong turn as the indigo at the
    // other end, only in the other direction. A light cornflower stays plainly
    // blue at any brightness.
    static readonly Color LiveLast = new Color(0.55f, 0.72f, 1f);
    static readonly Color Ready = Color.white;

    // The ramp between them is eased rather than even, so the middle wave sits
    // nearer the first than the last. Spaced evenly, the middle and the last
    // came out close enough to be taken for each other, and the last wave being
    // plainly the hottest is the whole of what the colour is for.
    const float RampEase = 1.5f;

    // How far towards white a full gauge is taken, and how much harder it
    // throbs. Most of "ready" is carried by the throb rather than by the
    // whitening, and deliberately: taken far towards white the topmost wave sits
    // pale directly under a pale paddle and stops being a wave again — the same
    // trap the hot end of the ramp falls into.
    const float ReadyGlow = 0.22f;
    const float ReadyPulse = 0.28f;

    // The light that runs down the stack: a gentle brightening, phase-shifted a
    // wave at a time so it reads as travelling rather than as three bars
    // flickering together. This is all that is left of the waves actually being
    // waves, and it is enough — a gauge that sat perfectly still would read as a
    // UI widget bolted under the paddle rather than as something being gathered.
    //
    // It only ever brightens, never dims, and it is kept well under the step
    // between one wave's colour and the next. A pulse swinging both ways was the
    // first version, and at the depth a full gauge throbs at it could put a
    // lower wave in a trough while the one above it was at a peak — which stands
    // the gauge's own ordering on its head for a fraction of a second at a time.
    // Whatever else it does, the last wave has to stay the brightest.
    const float PulseRate = 7f;
    const float PulsePhase = 1.15f;
    const float PulseDepth = 0.14f;

    // How long the gauge takes to fly apart once the charge is let go of. What
    // it spends is exactly what was standing there — the waves that were up
    // widen, thin away and are gone — because the release is the charge being
    // spent whole, and a gauge that merely blinked out would say the charge was
    // dropped rather than delivered. It plays whether or not there was a ball to
    // take it: a release that missed its window is the mistake the player most
    // needs to see they made.
    const float SpentTime = 0.22f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

    static Mesh cubeMesh;
    static Mesh sphereMesh;
    static MaterialPropertyBlock colorBlock;

    // One wave, built as a **stadium rather than a bar**: a box with a round cap
    // at each end, so its corners are round and stay round. That is why a wave
    // is three pieces and not one. A rounded-rectangle mesh — the kind the paddle
    // and the rounded brick are cut from — cannot serve here, because a wave's
    // width is not a fixed number: it grows as the wave fills, and scaling one
    // mesh across that range stretches the corner radius along with it, so every
    // wave would be rounded by a different amount and each one differently from
    // moment to moment. Caps scaled *uniformly* by the wave's own thickness are
    // round by construction, whatever the box between them is doing.
    class Wave
    {
        public GameObject root;
        public Transform body;
        public Transform capLeft;
        public Transform capRight;
        public Renderer[] skins;
    }

    Wave[] waves;

    // What was standing when the charge was let go of, and how far through
    // flying apart it is. Zero means there is nothing to spend.
    float spent;
    float spentAge;

    // A gauge of a paddle's own, made the first time that paddle has a charge to
    // show. It is an unparented root like every other cosmetic here rather than
    // a child of the paddle, because the paddle's transform is scaled on the
    // menu screen and everything the gauge is measured by is a world size; the
    // paddle drives its position instead, and switches it off when it goes away
    // itself.
    public static PowerWave Attach(Material material)
    {
        if (material == null) return null;

        var go = new GameObject("PowerWave");
        var gauge = go.AddComponent<PowerWave>();
        gauge.waves = new Wave[Waves];
        for (int i = 0; i < Waves; i++)
        {
            var root = new GameObject("Wave" + i);
            root.transform.SetParent(go.transform, false);
            var wave = new Wave
            {
                root = root,
                body = Piece(root.transform, "Body", CubeMesh, material),
                capLeft = Piece(root.transform, "CapLeft", SphereMesh, material),
                capRight = Piece(root.transform, "CapRight", SphereMesh, material),
            };
            wave.skins = new[]
            {
                wave.body.GetComponent<Renderer>(),
                wave.capLeft.GetComponent<Renderer>(),
                wave.capRight.GetComponent<Renderer>(),
            };
            root.SetActive(false);
            gauge.waves[i] = wave;
        }
        return gauge;
    }

    static Transform Piece(Transform parent, string name, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var meshRenderer = go.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        // Same reason the exhaust throws none: every shadow on either screen
        // lands on the backdrop behind it, and a wave's would be a dark bar
        // sliding about the fog — the one thing on screen that would say the
        // gauge is a row of solid boxes.
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    // Driven by the paddle every frame, charge or no charge, since the gauge has
    // a flying-apart to finish after the charge has gone. `centre` is the middle
    // of the paddle's drawn body and `width`/`height` are its world size — all
    // measured rather than assumed, since the menu's paddle is a scaled-down
    // copy of the round's and carries the same component.
    public void Tick(Vector3 centre, float width, float height, float charge, float delta)
    {
        if (charge > 0f)
        {
            spent = 0f;
            Draw(centre, width, height, charge, 0f);
            return;
        }

        if (spent > 0f)
        {
            spentAge += delta;
            if (spentAge >= SpentTime)
            {
                spent = 0f;
                Hide();
                return;
            }
            Draw(centre, width, height, spent, spentAge / SpentTime);
            return;
        }

        Hide();
    }

    // The charge has been let go of: whatever was standing is what flies apart.
    public void Spend(float charge)
    {
        spent = charge;
        spentAge = 0f;
    }

    public void Hide()
    {
        if (waves == null) return;
        foreach (var wave in waves)
            if (wave?.root != null) wave.root.SetActive(false);
    }

    // `spentT` runs 0 to 1 while the gauge flies apart, and is 0 for a charge
    // still being held.
    void Draw(Vector3 centre, float width, float height, float charge, float spentT)
    {
        float top = centre.y - height * 0.5f - height * Clearance;
        float stretch = Mathf.Lerp(1f, 1.35f, spentT);

        for (int i = 0; i < Waves; i++)
        {
            // Each wave owns its own third of the charge, so the second only
            // starts to appear once the first is at its full width. This one
            // line is the whole of the gauge being a gauge.
            float fill = Mathf.Clamp01(charge * Waves - i);
            var wave = waves[i];
            if (wave?.root == null) continue;
            if (fill <= 0f)
            {
                wave.root.SetActive(false);
                continue;
            }
            wave.root.SetActive(true);

            float thick = height * Thicknesses[i] * (1f - spentT);
            // Grows out of the middle as it fills, and — once the charge is let
            // go of — on out past its full width while thinning away, so the
            // charge reads as leaving rather than as being switched off.
            float span = width * Widths[i] * Mathf.Lerp(Seed, 1f, fill)
                * Mathf.Lerp(1f, 1.9f, spentT);

            wave.root.transform.position =
                new Vector3(centre.x, top - height * Drops[i] * stretch, centre.z);

            // The caps sit at the ends of the box and the box is shortened by
            // their diameter, so the wave's *whole* width is `span` however round
            // its ends are. A wave narrower than it is thick is all cap: the two
            // meet in the middle and it reads as a dot growing into a bar, which
            // is the right picture for a wave that has only just appeared.
            float bar = Mathf.Max(0f, span - thick);
            wave.body.localScale = new Vector3(bar, thick, thick);
            wave.capLeft.localScale = Vector3.one * thick;
            wave.capRight.localScale = Vector3.one * thick;
            wave.capLeft.localPosition = new Vector3(-bar * 0.5f, 0f, 0f);
            wave.capRight.localPosition = new Vector3(bar * 0.5f, 0f, 0f);

            // A filling wave is cold and brightens as it fills, and how hot it
            // gets when it is full is its place down the taper — so the gauge
            // says the same thing twice, in the count of full waves and in how
            // far the fierce end has reached. Written every frame rather than
            // once, because the whole point of the thing is that its colour is
            // telling the player something that changes.
            bool ready = charge >= 1f;
            float depth = ready ? ReadyPulse : PulseDepth;
            float pulse = 1f + depth * fill
                * (0.5f + 0.5f * Mathf.Sin(Time.time * PulseRate - i * PulsePhase));
            var hot = Color.Lerp(LiveFirst, LiveLast,
                Mathf.Pow(i / (Waves - 1f), RampEase));
            var colour = Color.Lerp(Cool, hot, fill);
            if (ready) colour = Color.Lerp(colour, Ready, ReadyGlow);
            colour *= Mathf.Lerp(pulse, 1.6f, spentT);

            colorBlock ??= new MaterialPropertyBlock();
            // Blended and brightened in sRGB, where the colours above are
            // written and where "halfway between these two blues" means what it
            // looks like it means, then converted once on the way to the shader.
            colorBlock.SetColor(BaseColorId, colour.linear);
            // Matte, for the reason the exhaust is: the paddle's own material is
            // glossy, and a highlight sliding across a wave gives away that it
            // is a box.
            colorBlock.SetFloat(SmoothnessId, 0f);
            foreach (var skin in wave.skins) skin.SetPropertyBlock(colorBlock);
        }
    }

    // A gauge belongs to its paddle, but it is an unparented root, so it has to
    // be swept up when the screen that owned the paddle changes — the same
    // reason the rubble and the exhaust are.
    public static void ClearAll()
    {
        foreach (var gauge in FindObjectsByType<PowerWave>())
            Destroy(gauge.gameObject);
    }

    // The stock meshes aren't loadable by name at runtime, so they are lifted off
    // throwaway primitives once and shared by every piece, exactly as JetTrail,
    // Ricochet and Debris do for the cube.
    static Mesh CubeMesh => cubeMesh ??= Lift(PrimitiveType.Cube);
    static Mesh SphereMesh => sphereMesh ??= Lift(PrimitiveType.Sphere);

    static Mesh Lift(PrimitiveType kind)
    {
        var template = GameObject.CreatePrimitive(kind);
        var mesh = template.GetComponent<MeshFilter>().sharedMesh;
        Destroy(template);
        return mesh;
    }
}
