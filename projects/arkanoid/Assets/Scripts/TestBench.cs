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
public class TestBench : MonoBehaviour
{
    // The shapes a block can be, wired in the same order the demonstration
    // board lays them out. Named off the prefab, so the readout says what the
    // thing on screen actually is.
    [SerializeField] Brick[] shapePrefabs;
    // Spawned only when the ball is asked for, since a bench with nothing moving
    // on it cannot time out, cannot lose and cannot end.
    [SerializeField] Ball ballPrefab;

    // The materials and their varieties are read off the GameManager rather than
    // wired again here, deliberately: two copies of that wiring would be two
    // things to keep in step, and a bench showing a material the round no longer
    // uses is worse than no bench at all.

    // The grid the blocks stand in. The same slot pitch the round's board uses,
    // so a block sits at the spacing it was designed to be seen at.
    const float SlotWidth = 1.64f;
    const float SlotHeight = 0.64f;
    const int Columns = 6;

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
    Transform blockHolder;
    Transform serveAnchor;

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
        if (blockHolder != null)
        {
            Destroy(blockHolder.gameObject);
            blockHolder = null;
        }
        Debris.ClearAll();
        Ricochet.ClearAll();
        JetTrail.ClearAll();
    }

    void Update()
    {
        if (fittedTo.x != Screen.width || fittedTo.y != Screen.height) FitToFrame();

        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        // Deliberately not the arrow keys: a ball switched on brings a paddle's
        // worth of arrow-reading with it, and a control that means two things
        // depending on what else is switched on is a control nobody can use.
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
        if (keyboard.dKey.wasPressedThisFrame) Damage();
        if (keyboard.bKey.wasPressedThisFrame) ToggleBall();
        // Q leaves, and it exists *because* ESC cannot be driven from outside:
        // Uplink's /input delivers letters happily and never delivers escape at
        // all, under either the short name or `<Keyboard>/escape` — measured,
        // twice, with a log on the frame to prove nothing arrived. A bench whose
        // only exit is a key no script can press is a bench that traps whatever
        // is driving it, so ESC stays for hands and Q is the one for scripts.
        if (keyboard.qKey.wasPressedThisFrame && GameManager.Instance != null)
            GameManager.Instance.CloseBench();

        // The ball has no paddle to be served off here, so it is simply put back
        // when the room loses it.
        if (ball != null && ball.transform.position.y < transform.position.y - 8f) ServeBall();
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
            return;
        }
        if (ballPrefab == null) return;
        ball = Instantiate(ballPrefab);
        ServeBall();
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
        ball.AttachTo(ServeAnchor());
        ball.Launch();
    }

    void FitToFrame()
    {
        var camera = Camera.main;
        if (camera == null) return;
        fittedTo = new Vector2Int(Screen.width, Screen.height);
        float planeZ = transform.position.z;
        Border.Fit(transform, new Vector2(transform.position.x, camera.transform.position.y),
            Border.FrameExtents(camera, planeZ), planeZ);
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
            + $"\nball {(ball != null ? "on" : "off")}"
            + "\n\nZ/X material   C/V shape   N/M count"
            + "\nD damage   F rebuild   R re-roll   B ball   Q/ESC menu";

        var box = new Rect(24f, 24f, 420f, 190f);
        GUI.Box(box, GUIContent.none);
        GUI.Label(new Rect(box.x + 12f, box.y + 10f, box.width - 24f, box.height - 20f), text, style);
    }
}
