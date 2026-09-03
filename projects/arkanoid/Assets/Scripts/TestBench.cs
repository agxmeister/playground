using UnityEngine;
using UnityEngine.InputSystem;

// A room with nothing in it but the thing being looked at.
//
// **This screen is a scratchpad, not a feature.** It exists so that whoever is
// working on how a block *looks* or *behaves* can put one on a stand, light it,
// and photograph it, without playing a round to get there. It is expected to be
// rewritten to suit whatever is being worked on — swap the grid for a single
// block, add a key that drives some new mechanic, tear out what is in the way.
// Changing it needs no more justification than "this is what I need to see".
//
// What it must never do is affect a round. It spawns blocks directly rather than
// through GameManager.SpawnBrick, so nothing it puts down is counted in
// `bricksLeft`; `GameManager.OnBrickDestroyed` refuses to score anything while
// the bench is open, which is what stops a block broken here from raising the
// stored high score and walking into the hall of fame. Those two facts are the
// whole of the contract, and both belong to GameManager rather than to this
// file — a bench that could be trusted only as long as nobody edited it would
// not be worth having.
//
// Reached by typing BENCH on the menu (GameManager.BenchCode). The room stands
// off to the right of the playfield, out of reach of either other room's
// physics, and is switched off entirely while it is not in use.
//
// A ball and a paddle can both be switched on, which is what makes the
// *behaving* half of a block testable here and not only the looking half: with
// a paddle in the room the whole rally is available — the arcade bounce, the
// twist, the charge and its gauge, the rocket and its exhaust, rubble worth
// catching — because the paddle is the round's own `Paddle` component with the
// round's own keys, not a stand-in for it.
public class TestBench : MonoBehaviour
{
    // The shapes a block can be, wired in the same order the demonstration
    // board lays them out. Named off the prefab, so the readout says what the
    // thing on screen actually is.
    [SerializeField] Brick[] shapePrefabs;
    // Spawned only when the ball is asked for, since a bench with nothing moving
    // on it cannot time out, cannot lose and cannot end.
    [SerializeField] Ball ballPrefab;
    // The playfield's own paddle, used as a *template* rather than driven: the
    // paddle is authored scene content and has no prefab, so the bench copies it
    // and switches the copy on. Copying it rather than writing a bench paddle is
    // the point — every mechanic the paddle carries (the thrust, the charge, the
    // gauge, the exhaust, the crash) comes along for free and can never drift
    // from what a round does.
    [SerializeField] Paddle paddleTemplate;

    // The materials and their varieties are read off the GameManager rather than
    // wired again here, deliberately: two copies of that wiring would be two
    // things to keep in step, and a bench showing a material the round no longer
    // uses is worse than no bench at all.

    // The grid the blocks stand in. The same slot pitch the round's board uses,
    // so a block sits at the spacing it was designed to be seen at.
    const float SlotWidth = 1.64f;
    const float SlotHeight = 0.64f;
    const int Columns = 6;

    // How far below the room's middle the bench's paddle stands. Low enough to
    // leave the grid room above it, as a round's does.
    const float PaddleDrop = 4.5f;

    // How much of a block's own hardness one press of the damage key spends.
    // A fifth, so every stage of the crack net can be parked on: the overlay
    // takes its stage from `floor(fraction x 4)`, which puts 0.2, 0.4, 0.6 and
    // 0.8 of the way through on stages 0 to 3, and the fifth press breaks the
    // block — which is worth seeing too. A quarter, which this was while the
    // overlay had two stages, would land on 1, 2, 3 and never show the first.
    const float DamageStep = 0.2f;

    // Where the ball is put back when it falls out of the room. There is no
    // paddle here to serve it off, and a bench that quietly emptied itself of
    // the one moving thing on it would be a bench that stops answering.
    const float BallReturnHeight = 3.5f;

    // How much one press moves each of the three lighting dials (see "The
    // lighting can be tried on the bench" in CLAUDE.md). Coarse on purpose: the
    // bench is for telling one answer from another, and a step so fine that two
    // presses look identical makes the comparison harder rather than finer.
    const float ShadowStep = 0.1f;
    const float PitchStep = 5f;
    const float RimStep = 2f;

    // How far one press moves the design mode's point on the material's band.
    // A twentieth, so the whole band is twenty presses end to end: fine enough
    // that two neighbouring steps are worth telling apart, coarse enough that
    // walking from near-white to near-black is not a chore.
    const float DesignStep = 0.05f;

    // How fast the arrows swing the key light while it is being aimed, in
    // degrees a second. Continuous rather than a step per press because aiming a
    // light is a thing done by eye: the shadow slides across the backdrop and is
    // stopped where it looks right, which is a different act from stepping a
    // number and looking at the result.
    const float AimRate = 45f;

    // How far off head-on the light may be swung. Not a taste limit: at 90 the
    // light is exactly edge-on to every block's front face — the face the camera
    // sees — and the whole screen goes black, which is a hole to fall into
    // rather than a setting to try.
    const float YawLimit = 80f;

    // Design mode: the grid stops rolling and every block is the *same* chosen
    // look, so a candidate can be walked one step at a time and photographed.
    // Off by default, because the bench's ordinary job is showing what a round
    // shows and a round rolls.
    bool designing;
    int designGrain;
    float designT = 0.5f;
    // Which named design is standing, or -1 while the dials have been moved off
    // one. Kept so the readout can say "Chalk" instead of leaving whoever is
    // looking to recognise a grain and a number as a name they already have.
    int namedDesign = -1;

    int shape;
    int material = (int)BlockMaterial.Polymer;
    int count = 12;
    int seed = 1;
    int damageSteps;
    Ball ball;
    Paddle paddle;
    // True while the ball is sitting on the paddle waiting for SPACE, which is
    // how a round serves and therefore the only way the charge can be practised:
    // a push is paid to a ball that is *caught*, so a bench that launched for
    // you would have nothing to catch.
    bool waitingToServe;
    Transform blockHolder;
    Transform serveAnchor;
    float halfWidth;

    // The room's own borders, so a ball let loose in here ricochets off the
    // frame exactly as it does in a round rather than sailing out of view.
    Vector2Int fittedTo;

    // The room's own ring of perimeter lamps, stood up and fitted exactly as
    // `Playfield` stands up its own. Without one the bench would be lighting
    // blocks by the key light alone and quietly showing them under lighting no
    // round has — which is the one thing a bench for looking at blocks must not
    // do — and the rim dial below would have nothing to move.
    RimLights rim;

    // The drawn alternative to the key light's shadows, which 7 switches to.
    SoftShadows soft;

    // The key light, and what it was set to when the bench opened. The dials
    // below move a light that belongs to the whole game, so what they move has
    // to be put back: see OnDisable.
    Light keyLight;
    float authoredShadowStrength;
    float authoredPitch;
    float authoredYaw;
    float authoredRim;

    // Where the key light is pointed, kept here as two numbers rather than read
    // back off the transform each time. Euler angles do not survive the round
    // trip cleanly — a pitch stepped down and back up returns a rounding error
    // away from where it started, and a negative yaw comes back as 300-odd — so
    // the bench keeps what it meant and writes it, instead of asking the
    // transform what it thinks it was told.
    float pitch;
    float yaw;

    // True while the arrows are aiming the light instead of driving the paddle.
    // Latched by L; SHIFT does the same thing for as long as it is held.
    bool aiming;
    LightShadows authoredShadowType;
    ShadowMode authoredShadowMode;

    public bool IsOpen => gameObject.activeSelf;

    void OnEnable()
    {
        // Read before anything is touched, so the numbers put back on the way
        // out are the ones the game was standing in when the bench opened rather
        // than whatever this file happens to believe they are.
        keyLight = FindKeyLight();
        if (keyLight != null)
        {
            authoredShadowStrength = keyLight.shadowStrength;
            authoredPitch = Wrapped(keyLight.transform.eulerAngles.x);
            authoredYaw = Wrapped(keyLight.transform.eulerAngles.y);
            pitch = authoredPitch;
            yaw = authoredYaw;
            // The kind of shadow the key light casts, so that switching to the
            // soft mode and back puts hard or soft shadows back as they were
            // rather than as this file guessed they were.
            authoredShadowType = keyLight.shadows;
        }
        authoredRim = RimLights.RestLevel;
        authoredShadowMode = SoftShadows.Mode;

        FitToFrame();
        Rebuild();
    }

    // The shadow-casting directional light, and *not* simply the first `Light`
    // in the scene. There are three standing: the key light and the two
    // horizontal fills, and a search that took the first one it found would as
    // happily hand back a fill — which is the exact mistake `ArkanoidSetup` made
    // once and left a note about, where a stage re-aimed a fill on every reload.
    // Casting shadows is the thing that makes the key light the key light, so
    // that is what this asks about, and it stays true if the fills are ever
    // renamed. The rim's own eight lamps are points and cast nothing, so they
    // cannot be picked up here either.
    static Light FindKeyLight()
    {
        foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            if (light.type == LightType.Directional && light.shadows != LightShadows.None) return light;
        return null;
    }

    // Everything the dials moved, put back. The bench's whole contract is that
    // it cannot affect a round, and these three are the only things it touches
    // that outlive the room: the key light is scene content shared with both
    // other rooms, and `RimLights.RestLevel` is a static. A setting worth keeping
    // is kept by editing the constant it came from, not by leaving it standing
    // here — the readout prints the numbers so they can be written down.
    void RestoreLighting()
    {
        aiming = false;
        if (paddle != null) paddle.enabled = true;
        if (keyLight != null)
        {
            keyLight.shadowStrength = authoredShadowStrength;
            pitch = authoredPitch;
            yaw = authoredYaw;
            Aim();
        }
        RimLights.SetRestLevel(authoredRim);
        SoftShadows.SetMode(authoredShadowMode, keyLight, authoredShadowType);
        if (keyLight != null) keyLight.shadows = authoredShadowType;
    }

    void OnDisable()
    {
        RestoreLighting();

        // Nothing of the bench outlives it: rubble and sparks are unparented
        // roots that would otherwise rain over whatever screen comes next, the
        // same reason MainMenuPanel.Hide sweeps them.
        if (ball != null)
        {
            Destroy(ball.gameObject);
            ball = null;
        }
        // The paddle is a copy of the round's, unparented like the ball, so it
        // has to be destroyed rather than left to go away with the room.
        if (paddle != null)
        {
            Destroy(paddle.gameObject);
            paddle = null;
        }
        waitingToServe = false;
        if (blockHolder != null)
        {
            Destroy(blockHolder.gameObject);
            blockHolder = null;
        }
        Debris.ClearAll();
        Ricochet.ClearAll();
        JetTrail.ClearAll();
        // The charge gauge is an unparented root too, and a gauge left glowing
        // under a paddle that is no longer there is the exact thing PowerWave's
        // own OnDisable exists to prevent.
        PowerWave.ClearAll();
    }

    void Update()
    {
        if (fittedTo.x != Screen.width || fittedTo.y != Screen.height) FitToFrame();

        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        // Deliberately clear of every key the paddle reads, because the paddle
        // can be standing here: it takes the arrows *and* A/D to move, S and DOWN
        // to charge, and SPACE to thrust and serve. A control that means two
        // things depending on what else is switched on is a control nobody can
        // use — which is why damage is G and not the D it started as, D being the
        // paddle's own "right".
        if (keyboard.zKey.wasPressedThisFrame) Step(ref material, BlockMaterials.Count, -1);
        if (keyboard.xKey.wasPressedThisFrame) Step(ref material, BlockMaterials.Count, 1);
        if (keyboard.cKey.wasPressedThisFrame) Step(ref shape, shapePrefabs.Length, -1);
        if (keyboard.vKey.wasPressedThisFrame) Step(ref shape, shapePrefabs.Length, 1);
        if (keyboard.nKey.wasPressedThisFrame) { count = Mathf.Max(1, count - 1); Rebuild(); }
        if (keyboard.mKey.wasPressedThisFrame) { count = Mathf.Min(48, count + 1); Rebuild(); }
        // A fresh draw of the same material's variety, so two shots of one
        // block can be compared and a whole new set asked for on purpose.
        if (keyboard.rKey.wasPressedThisFrame) { seed++; Rebuild(); }
        // Put the grid back exactly as it was, which is also how damage is
        // undone — there is no un-break, and a rebuild is honest about that.
        if (keyboard.fKey.wasPressedThisFrame) Rebuild();
        if (keyboard.gKey.wasPressedThisFrame) Damage();
        ReadDesignKeys(keyboard);
        if (keyboard.bKey.wasPressedThisFrame) ToggleBall();
        if (keyboard.pKey.wasPressedThisFrame) TogglePaddle();
        // The serve, exactly as a round's: the launch takes the press and the
        // paddle's own thrust takes the hold, so the one key never argues with
        // itself (see Paddle).
        if (waitingToServe && ball != null && keyboard.spaceKey.wasPressedThisFrame)
        {
            ball.Launch();
            waitingToServe = false;
        }
        // Q leaves, and it exists *because* ESC cannot be driven from outside:
        // Uplink's /input delivers letters happily and never delivers escape at
        // all, under either the short name or `<Keyboard>/escape` — measured,
        // twice, with a log on the frame to prove nothing arrived. A bench whose
        // only exit is a key no script can press is a bench that traps whatever
        // is driving it, so ESC stays for hands and Q is the one for scripts.
        if (keyboard.qKey.wasPressedThisFrame && GameManager.Instance != null)
            GameManager.Instance.CloseBench();

        ReadLightingKeys(keyboard);

        // A ball the room has lost is put back rather than left gone, since the
        // bench's one moving part going missing would make it stop answering.
        if (ball != null && !waitingToServe
            && ball.transform.position.y < transform.position.y - 8f) ServeBall();
    }

    // Block design mode. T switches it on, J/K walk the grain and U/I walk the
    // band; every block in the grid then wears the same chosen look, which is
    // what makes a candidate judgeable — twelve blocks of one look show it on
    // every shape and every neighbour, where twelve rolls show twelve looks and
    // no two of them can be compared.
    //
    // Letters again rather than digits, and these four are genuinely free: the
    // paddle takes the arrows, A/D, S and SPACE, the bench has Z/X/C/V/N/M/R/F/
    // G/B/P/Q/L, the digits are the lighting dials, Y answers the exit prompt.
    //
    // The seed still matters in design mode even though nothing is rolled: the
    // grain offset is still drawn per block (see BlockVariety.Compose), so R
    // slides the same look around the tile, which is a real thing to want to
    // see and not a leftover.
    void ReadDesignKeys(Keyboard keyboard)
    {
        if (keyboard.tKey.wasPressedThisFrame) { designing = !designing; Rebuild(); }
        if (!designing) return;

        // O walks the named designs, which is how one is looked at rather than
        // rebuilt from its numbers by hand. It loads the design's grain and
        // value into the dials, so the next J or U carries on from where that
        // design stands — a named look is a starting point as much as it is an
        // answer.
        //
        // **And it switches the material with them**, which it did not have to
        // while Polymer owned every design. A design names its material as much
        // as its grain does — the grain is an *index* into that material's own
        // grains — so walking to a Ceramics design with Polymer standing used to
        // hand a ceramic's grain number to the plastic and show a look that is
        // in no table anywhere.
        if (keyboard.oKey.wasPressedThisFrame && BlockDesigns.Count > 0)
        {
            namedDesign = (namedDesign + 1) % BlockDesigns.Count;
            var definition = BlockDesigns.Of((BlockDesign)namedDesign);
            material = (int)definition.Material;
            designGrain = definition.Grain;
            designT = definition.Value;
            Rebuild();
        }

        var variety = GameManager.Instance != null
            ? GameManager.Instance.VarietyOf((BlockMaterial)material) : null;
        int grains = variety != null ? variety.GrainCount : 0;
        if (grains > 0)
        {
            if (keyboard.jKey.wasPressedThisFrame)
            {
                designGrain = (designGrain - 1 + grains) % grains;
                namedDesign = -1;
                Rebuild();
            }
            if (keyboard.kKey.wasPressedThisFrame)
            {
                designGrain = (designGrain + 1) % grains;
                namedDesign = -1;
                Rebuild();
            }
        }

        // Clamped rather than wrapped: the ends of a band are places to sit and
        // look, and a dial that jumped from near-black to near-white on one
        // press past the end would throw away the thing being judged.
        if (keyboard.uKey.wasPressedThisFrame)
        {
            designT = Mathf.Clamp01(designT - DesignStep);
            namedDesign = -1;
            Rebuild();
        }
        if (keyboard.iKey.wasPressedThisFrame)
        {
            designT = Mathf.Clamp01(designT + DesignStep);
            namedDesign = -1;
            Rebuild();
        }
    }

    // The three lighting dials, on digits rather than letters. Every letter pair
    // on this bench is already spoken for by the bench or by the paddle standing
    // in it, and two of the obvious spare ones are worse than spare: Y answers
    // the exit prompt and H is a letter of the BENCH code. The digits are used
    // by nothing that can be open at the same time as this room.
    //
    // What each one is for is in "The edge of the frame is lit" and "How could
    // there still be a shadow" in CLAUDE.md; in short, 1/2 greys the shadow out
    // where it stands, 3/4 moves where it falls, and 5/6 lifts the whole
    // backdrop around it. They are three different answers to one complaint and
    // the point of putting them here is that they can be told apart.
    void ReadLightingKeys(Keyboard keyboard)
    {
        // How dark the key light's shadow is drawn, without moving it. The
        // gentlest of the three: at 0 the shadows are gone and nothing else
        // about the picture has changed.
        if (keyboard.digit1Key.wasPressedThisFrame) StepShadowStrength(-ShadowStep);
        if (keyboard.digit2Key.wasPressedThisFrame) StepShadowStrength(ShadowStep);

        // The key light's downward tilt — `ArkanoidSetup.LightPitch`, 30 as
        // authored. Everything here casts onto a surface *behind* it rather than
        // onto a floor, so the shadow's drop is `gap x tan(pitch)`: shallower
        // tucks it back in behind the block, steeper smears it further down.
        if (keyboard.digit3Key.wasPressedThisFrame) StepPitch(-PitchStep);
        if (keyboard.digit4Key.wasPressedThisFrame) StepPitch(PitchStep);

        // The rim's resting level. This one does not touch the shadow at all —
        // it raises the light everywhere around it, the shadow included, so what
        // it changes is how much the shadow stands out rather than how dark it
        // is. Past about 15 it flattens the murk, which is worth seeing once.
        if (keyboard.digit5Key.wasPressedThisFrame) RimLights.SetRestLevel(RimLights.RestLevel - RimStep);
        if (keyboard.digit6Key.wasPressedThisFrame) RimLights.SetRestLevel(RimLights.RestLevel + RimStep);

        // The two kinds of shadow, which is a switch rather than a dial: the key
        // light's own hard offset one, or a soft patch drawn squarely behind
        // every object with no direction in it (see SoftShadows). They are
        // mutually exclusive on purpose — both at once gives every block two
        // shadows, which is neither of the things being compared.
        if (keyboard.digit7Key.wasPressedThisFrame)
            SoftShadows.SetMode(
                SoftShadows.Mode == ShadowMode.Directional ? ShadowMode.Soft : ShadowMode.Directional,
                keyLight, authoredShadowType);

        ReadAiming(keyboard);

        // Back to what the game is written to, so a picture can always be
        // compared against the one everybody else is looking at.
        if (keyboard.digit0Key.wasPressedThisFrame) RestoreLighting();
    }

    void StepShadowStrength(float by)
    {
        if (keyLight == null) return;
        keyLight.shadowStrength = Mathf.Clamp01(keyLight.shadowStrength + by);
    }

    void StepPitch(float by)
    {
        // Kept the right way up: a pitch past vertical would put the key light
        // under the room and turn every shadow upside down, which is a different
        // question from the one being asked here.
        pitch = Mathf.Clamp(pitch + by, 0f, 85f);
        Aim();
    }

    void Aim()
    {
        if (keyLight != null) keyLight.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    // Aiming the key light by hand, which is the one thing here that wants to be
    // *swung* rather than stepped.
    //
    // Two ways in, and the second is not a duplicate. **SHIFT held** is what a
    // hand wants: press it, swing the light, let go. But a held modifier cannot
    // be driven from outside — `/input` plays one control at a time, so no
    // script can ever hold SHIFT *and* press an arrow — and this bench already
    // carries the scar from that lesson: `Q` exists because ESC is undeliverable.
    // So **L latches** the same mode, and anything driving the bench uses that.
    //
    // The arrows move *the light*, not the shadow: UP lifts it, so the shadow it
    // throws slides further down the backdrop, and RIGHT walks it to the right,
    // so the shadow goes left. Aiming a lamp is the thing being pictured, and a
    // control that moved the shadow directly would have the light going the
    // wrong way whenever anybody thought about it.
    void ReadAiming(Keyboard keyboard)
    {
        if (keyboard.lKey.wasPressedThisFrame) aiming = !aiming;
        bool aim = aiming || keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

        // The paddle reads the arrows too, and it can be standing right here.
        // Switching it off for the duration is the whole of the arbitration —
        // and it is the bench's own copy of the paddle, so nothing a round uses
        // is touched. A paddle switched off mid-boost keeps its drive and coasts
        // it off when it comes back, which is the same thing that happens to one
        // whose keys are simply let go of.
        if (paddle != null) paddle.enabled = !aim;
        if (!aim || keyLight == null) return;

        float step = AimRate * Time.deltaTime;
        float liftedBy =
            (keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.downArrowKey.isPressed ? 1f : 0f);
        // Right arrow walks the light right, which is a *negative* yaw: a light
        // yawed positively has its beam heading towards +X, which means it is
        // standing on the left.
        float walkedBy =
            (keyboard.leftArrowKey.isPressed ? 1f : 0f) - (keyboard.rightArrowKey.isPressed ? 1f : 0f);
        if (liftedBy == 0f && walkedBy == 0f) return;

        pitch = Mathf.Clamp(pitch + liftedBy * step, 0f, 85f);
        yaw = Mathf.Clamp(yaw + walkedBy * step, -YawLimit, YawLimit);
        Aim();
    }

    // Euler angles come back as 0..360; this is the same angle written the way a
    // person reads it, so a light swung a little to the right says -12 rather
    // than 348.
    static float Wrapped(float degrees) => degrees > 180f ? degrees - 360f : degrees;

    void Step(ref int value, int length, int by)
    {
        if (length <= 0) return;
        value = (value + by + length) % length;
        Rebuild();
    }

    // The grid, laid from the seed so the same seed gives the same picture. Unity's
    // Random is global state, which is exactly why this is worth knowing about:
    // the bench reseeds it, so anything else drawing from it while the bench is
    // open is drawing from a sequence the bench chose.
    void Rebuild()
    {
        if (blockHolder != null) Destroy(blockHolder.gameObject);
        blockHolder = new GameObject("BenchBlocks").transform;
        blockHolder.SetParent(transform, false);
        damageSteps = 0;

        if (shapePrefabs == null || shapePrefabs.Length == 0) return;
        Random.InitState(seed);

        int rows = Mathf.CeilToInt(count / (float)Columns);
        for (int i = 0; i < count; i++)
        {
            int row = i / Columns;
            int column = i % Columns;
            int inRow = Mathf.Min(Columns, count - row * Columns);
            var offset = new Vector3(
                (column - (inRow - 1) / 2f) * SlotWidth,
                ((rows - 1) / 2f - row) * SlotHeight * 2f,
                0f);
            Spawn(transform.position + offset);
        }
    }

    void Spawn(Vector3 position)
    {
        var kind = (BlockMaterial)material;
        var game = GameManager.Instance;
        var block = Instantiate(shapePrefabs[shape], position, Quaternion.identity, blockHolder);
        block.SetMaterial(kind, game != null ? game.MaterialAsset(kind) : null);
        var variety = game != null ? game.VarietyOf(kind) : null;
        if (variety == null) return;
        block.SetLook(designing
            ? variety.Compose(designGrain, designT, GameManager.GrainTilesPerUnit)
            : variety.Roll(GameManager.GrainTilesPerUnit));
    }

    // A fifth of every block's hardness at a time, applied through the same
    // TakeDamage the ball calls, so what is shown is the wear the game would
    // actually show rather than a crack sprite set by hand. An unbreakable
    // material refuses it and stays clean, which is itself worth seeing.
    void Damage()
    {
        if (blockHolder == null) return;
        damageSteps++;
        foreach (var block in blockHolder.GetComponentsInChildren<Brick>())
            block.TakeDamage(block.Hardness * DamageStep);
    }

    void ToggleBall()
    {
        if (ball != null)
        {
            Destroy(ball.gameObject);
            ball = null;
            waitingToServe = false;
            return;
        }
        if (ballPrefab == null) return;
        ball = Instantiate(ballPrefab);
        ServeBall();
    }

    // The paddle is copied out of the playfield rather than built, and it is
    // Instantiate's *positioning* overload that makes that safe: `Paddle.Awake`
    // reads `homeX` off its own transform and clamps its travel to either side
    // of it, so a paddle created at the template's place and moved afterwards
    // would spend the whole session trying to get back to the playfield. Handing
    // the position to Instantiate puts it there before Awake ever runs.
    void TogglePaddle()
    {
        if (paddle != null)
        {
            Destroy(paddle.gameObject);
            paddle = null;
            // The ball loses what it was resting on, so it is served afresh off
            // the anchor rather than left attached to a destroyed transform.
            if (ball != null) ServeBall();
            return;
        }
        if (paddleTemplate == null) return;

        var stand = new Vector3(transform.position.x,
            transform.position.y - PaddleDrop, paddleTemplate.transform.position.z);
        paddle = Instantiate(paddleTemplate, stand, paddleTemplate.transform.rotation);
        paddle.name = "BenchPaddle";
        // The template is switched off for as long as the bench is up, so the
        // copy arrives switched off too.
        paddle.gameObject.SetActive(true);
        paddle.FitTo(halfWidth);
        if (ball != null) ServeBall();
    }

    // Served off an anchor rather than placed by hand, because `Ball.Launch`
    // refuses to fire a ball that is not attached to something — it is written
    // for a ball rolled off a paddle, and quietly does nothing otherwise, which
    // would leave the bench's one moving part sitting still. A bare transform is
    // enough: `AttachTo` measures its paddle's collider and renderer and falls
    // back cleanly when there is neither, so the bench needs no paddle to serve.
    Transform ServeAnchor()
    {
        if (serveAnchor == null)
        {
            var anchor = new GameObject("BenchServePoint");
            anchor.transform.SetParent(transform, false);
            anchor.transform.localPosition = new Vector3(0f, -BallReturnHeight, 0f);
            serveAnchor = anchor.transform;
        }
        return serveAnchor;
    }

    void ServeBall()
    {
        if (ball == null) return;

        // With a paddle standing, the ball is rolled off it and waits for SPACE,
        // which is the only shape in which the charge means anything: a push is
        // paid to a ball at the moment it is caught.
        if (paddle != null)
        {
            ball.AttachTo(paddle.transform);
            waitingToServe = true;
            return;
        }

        // Without one, it launches itself, so an unattended bench keeps moving.
        ball.AttachTo(ServeAnchor());
        ball.Launch();
        waitingToServe = false;
    }

    // A star beside a dial that has been moved off the value the game is written
    // to. Compared loosely, since a pitch stepped down and back up again lands a
    // rounding error away from where it started and a bench that then insisted
    // the light had been changed would be lying about the more important half.
    static string Moved(float now, float authored) => Mathf.Abs(now - authored) > 0.001f ? "*" : "";

    // What design mode is currently holding, printed in full — because the
    // whole product of a design session is these numbers. A screenshot that
    // shows a look somebody likes and does not say which grain and which point
    // on the band produced it is a screenshot that cannot be turned into an
    // entry in CLAUDE.md, which is the one thing this mode is for.
    //
    // The tint and the smoothness are printed *derived* rather than as the t
    // alone, since they are what would actually be written down if the look is
    // kept, and recomputing them by hand off a band's two ends is exactly the
    // step where a transcription goes wrong.
    string DesignLine(BlockMaterial kind)
    {
        if (!designing) return "";
        var variety = GameManager.Instance != null ? GameManager.Instance.VarietyOf(kind) : null;
        // A material with no variety wears its shared asset untouched, which is
        // the ordinary case and not a failure — but design mode has nothing to
        // pin there, and a line of zeroes would look like an answer.
        if (variety == null) return $"\nDESIGN: {kind} has no variety to pin";

        float t = designT;
        var tint = Color.Lerp(variety.darkest, variety.lightest, t);
        float smoothness = Mathf.Lerp(variety.darkSmoothness, variety.lightSmoothness, t);
        return $"\nDESIGN  {variety.GrainName(designGrain)}   t {t:0.00}"
            + (namedDesign >= 0 ? $"   [{BlockDesigns.NameOf((BlockDesign)namedDesign)}]" : "")
            + $"   rgb {tint.r:0.000} {tint.g:0.000} {tint.b:0.000}"
            + $"   smooth {smoothness:0.00}";
    }

    void FitToFrame()
    {
        var camera = Camera.main;
        if (camera == null) return;
        fittedTo = new Vector2Int(Screen.width, Screen.height);
        float planeZ = transform.position.z;
        var extents = Border.FrameExtents(camera, planeZ);
        halfWidth = extents.x;
        Border.Fit(transform, new Vector2(transform.position.x, camera.transform.position.y),
            extents, planeZ);
        // The ring, fitted to this room's frame the same way `Playfield` fits
        // its own — and stood up here rather than authored for the same reason:
        // a perimeter is only known once there is a window.
        if (rim == null) rim = gameObject.AddComponent<RimLights>();
        rim.FitTo(new Vector2(transform.position.x, camera.transform.position.y), extents, planeZ);
        // And the room's own soft-shadow manager, dormant until 7 says otherwise.
        if (soft == null) soft = gameObject.AddComponent<SoftShadows>();
        soft.FitTo(planeZ);
        // A window resized under a standing paddle would otherwise leave its
        // travel measured against the frame it was born in.
        if (paddle != null) paddle.FitTo(halfWidth);
    }

    // The readout is the point of the bench as much as the blocks are: a
    // screenshot that does not say which material and which shape it is showing
    // is a screenshot somebody has to come back and ask about.
    void OnGUI()
    {
        var kind = (BlockMaterial)material;
        var traits = BlockMaterials.Of(kind);
        string shapeName = shapePrefabs != null && shapePrefabs.Length > 0
            ? shapePrefabs[shape].name : "none";
        string hardness = traits.Unbreakable
            ? "unbreakable" : $"x{traits.Multiplier}";

        float shadowStrength = keyLight != null ? keyLight.shadowStrength : 0f;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            normal = { textColor = Color.white },
        };
        var text = $"TEST BENCH\n{shapeName}  |  {kind}  {hardness}"
            + DesignLine(kind)
            + $"\ncount {count}   seed {seed}   damage {damageSteps} x {DamageStep:0.00}"
            + $"\nball {(ball != null ? "on" : "off")}   paddle {(paddle != null ? "on" : "off")}"
            // The paddle's own numbers, because the mechanics it carries are
            // invisible until they fire: a charge that is not filling and a
            // paddle that is not drifting look exactly like a paddle standing
            // still, and one of those is a bug.
            + (paddle != null
                ? $"   charge {paddle.Charge:0.00}   drift {paddle.Drift:0}"
                : "")
            // Time itself, because a frozen bench looks exactly like a broken
            // one: everything driven by deltaTime stands still while the keys go
            // on being read, which is a very confusing pair of symptoms to be
            // handed without this number. GameManager's exit prompt sets
            // timeScale to 0 and timeScale outlives the round that set it.
            + $"\ntimeScale {Time.timeScale:0.00}"
            // The three lighting numbers, because the whole point of the dials
            // is comparing one setting against another and a screenshot that
            // does not say which setting it is showing cannot be compared with
            // anything. A star marks a value moved off what the game is written
            // to, so a picture never quietly claims to be the stock look.
            + $"\nshadow {shadowStrength:0.00}{Moved(shadowStrength, authoredShadowStrength)}"
            // One decimal, because these two are swung by hand at 45 degrees a
            // second and land on fractions. Printed as whole degrees they can
            // read `yaw 0*` — starred as moved, showing the value it was moved
            // from — which is a readout arguing with itself.
            + $"   pitch {pitch:0.0}{Moved(pitch, authoredPitch)}"
            + $"   yaw {yaw:0.0}{Moved(yaw, authoredYaw)}"
            + $"   rim {RimLights.RestLevel:0.0}{Moved(RimLights.RestLevel, RimLights.AuthoredRestLevel)}"
            + $"\nshadows {(SoftShadows.Mode == ShadowMode.Directional ? "directional" : "soft, behind")}"
            + (SoftShadows.Mode != ShadowMode.Directional ? "*" : "")
            + "\n\nZ/X material   C/V shape   N/M count"
            + "\nG damage   F rebuild   R re-roll   T design"
            + (designing ? "   J/K grain   U/I value   O named" : "")
            + "\nB ball   P paddle   SPACE serve   Q/ESC menu"
            + "\n1/2 shadow   3/4 pitch   5/6 rim   7 mode   0 reset"
            // Said loudly while it is on, because a latched mode that has been
            // forgotten about presents as a paddle that has stopped answering
            // its keys — which reads as a bug and not as a mode.
            + (aiming ? "\nAIMING (L off)   arrows swing the light"
                      : "\nSHIFT+arrows or L: aim the light");

        // A line taller while design mode is printing its numbers, since a
        // readout clipped by its own box is a readout that loses exactly the
        // digits the mode exists to hand over.
        var box = new Rect(24f, 24f, 420f, designing ? 272f : 250f);
        GUI.Box(box, GUIContent.none);
        GUI.Label(new Rect(box.x + 12f, box.y + 10f, box.width - 24f, box.height - 20f), text, style);
    }
}
