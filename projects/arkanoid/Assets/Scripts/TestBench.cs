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
    // A quarter, so the crack overlay's two stages can both be parked on: 0.25
    // and 0.5 of the way through show the light crack, 0.75 the heavy one, and
    // the fourth press breaks the block — which is worth seeing too.
    const float DamageStep = 0.25f;

    // Where the ball is put back when it falls out of the room. There is no
    // paddle here to serve it off, and a bench that quietly emptied itself of
    // the one moving thing on it would be a bench that stops answering.
    const float BallReturnHeight = 3.5f;

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

    public bool IsOpen => gameObject.activeSelf;

    void OnEnable()
    {
        FitToFrame();
        Rebuild();
    }

    void OnDisable()
    {
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

        // A ball the room has lost is put back rather than left gone, since the
        // bench's one moving part going missing would make it stop answering.
        if (ball != null && !waitingToServe
            && ball.transform.position.y < transform.position.y - 8f) ServeBall();
    }

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
        if (variety != null) block.SetLook(variety.Roll(GameManager.GrainTilesPerUnit));
    }

    // A quarter of every block's hardness at a time, applied through the same
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

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            normal = { textColor = Color.white },
        };
        var text = $"TEST BENCH\n{shapeName}  |  {kind}  {hardness}"
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
            + "\n\nZ/X material   C/V shape   N/M count"
            + "\nG damage   F rebuild   R re-roll"
            + "\nB ball   P paddle   SPACE serve   Q/ESC menu";

        var box = new Rect(24f, 24f, 420f, 190f);
        GUI.Box(box, GUIContent.none);
        GUI.Label(new Rect(box.x + 12f, box.y + 10f, box.width - 24f, box.height - 20f), text, style);
    }
}
