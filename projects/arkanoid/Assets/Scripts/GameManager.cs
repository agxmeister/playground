using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // True while the exit confirmation is up. Time is frozen behind it, but the
    // components that read the keyboard themselves have to hold off too.
    public bool Paused => exitPrompt;

    // Public so the editor's "clear records" command can wipe it alongside the
    // record book: the stored high score is the bar a round has to clear to be
    // asked for a name, so an emptied book with the old bar still standing
    // would never reach name entry at all.
    public const string HighScoreKey = "Arkanoid.HighScore";

    // What a caught chunk of rubble is worth. Small next to the 100–500 a brick
    // pays, because a brick drops six to nine of them: a whole shattered brick
    // caught is worth about as much again as breaking it was, and none of it is
    // free — the paddle has to leave the ball to be under the rubble.
    public const int DebrisPoints = 10;

    [SerializeField] Ball ballPrefab;
    [SerializeField] Brick brickPrefab;
    [SerializeField] Brick halfBrickPrefab;
    [SerializeField] Brick roundedBrickPrefab;
    [SerializeField] Brick roundBrickPrefab;
    // The nine block materials, in BlockMaterial's own order, wired by the
    // editor setup script. An array rather than nine slots because the enum is
    // the index and nothing here should be able to disagree with it.
    [SerializeField] Material[] blockMaterials;
    // How far one block of a material may drift from the next, for the materials
    // that have any drift in them at all. Keyed by value rather than by index —
    // a material with no entry wears its shared asset untouched, which is every
    // material but Polymer for now.
    [SerializeField] BlockVariety[] blockVarieties;

    [SerializeField] Paddle paddle;
    [SerializeField] ScoreBoard scoreBoard;
    [SerializeField] RecordsPanel recordsPanel;
    [SerializeField] MainMenuPanel mainMenuPanel;
    // The round's room, which measures the frame it has been dealt. The brick
    // grid is laid out against that rather than against a fixed rectangle, since
    // the field is the whole window now.
    [SerializeField] Playfield playfield;
    // The round's room and its paddle, switched off while the menu is up. 2D
    // physics ignores Z, so the playfield's borders would otherwise fence in the
    // menu's ball even though the menu screen hides them, and both paddles
    // would answer the arrow keys at once.
    [SerializeField] GameObject[] playfieldObjects;

    [SerializeField] int startingLives = 3;

    // The level is a demonstration board: every shape crossed with every
    // material, exactly once each. A row is a shape and a column is a material,
    // in the enum's own order, so what a block is made of can be read off where
    // it stands and the whole of what a block can be is on screen at once.
    //
    // Half and Round blocks stand one to a slot here rather than the two they
    // used to, because "one of each" is the point of the board; they are half a
    // slot wide, so they sit centred with daylight either side, which is its own
    // way of saying they are the small ones.
    enum BrickKind { Normal, Half, Rounded, Round }

    static readonly BrickKind[] boardKinds =
    {
        BrickKind.Normal, BrickKind.Rounded, BrickKind.Half, BrickKind.Round,
    };

    enum State { Menu, Ready, Playing, EnteringName, GameOver, Won, Bench }

    const int MaxNameLength = 12;

    // How long the view takes to travel from the menu's room to the playfield.
    // The same length as the menu's own slide between its two screens, so
    // starting a round reads as one more step to the right rather than as a
    // different kind of change.
    const float TravelDuration = 0.6f;

    // How far below the frame's bottom edge a ball has to fall to be lost. Far
    // enough that it has plainly gone rather than clipped the boundary.
    const float BallLostDrop = 0.7f;

    // The room the test bench stands in, off to the right of the playfield the
    // way the menu is off to its left, and switched off entirely while it is not
    // in use. Serialized rather than found, because it is authored inactive and
    // GameObject.Find cannot see it.
    [SerializeField] TestBench testBench;

    // Typed on the menu to open the bench. Letters are free there — the arrows,
    // SPACE and DOWN drive the paddle, ESC has nothing to leave, and name entry
    // is a state of its own — so a word can be a door without taking a key away
    // from anything. See "The test bench" in CLAUDE.md.
    static readonly Key[] BenchCode = { Key.B, Key.E, Key.N, Key.C, Key.H };
    int benchProgress;

    // How coarse a block's surface grain reads, as tiles of the grain texture
    // per world unit. One number settles it for all four block shapes, whose
    // meshes lay out UVs four different ways — Brick.grainUvPerUnit is what each
    // of them divides it by. Two puts a tile across half a unit, so a full
    // slab's face carries three of them side by side and one up its height,
    // which at the grain textures' own resolution leaves the moulding finer than
    // the screen can resolve rather than coarser.
    // Public because the test bench shows the same blocks a round does and has
    // to roll them the same way; a bench that moulded its grain at a different
    // size would be answering a question nobody asked.
    public const float GrainTilesPerUnit = 2f;

    State state;
    State endState;
    // True while the view is travelling from the menu to the playfield. The
    // round is already built and waiting behind it, so everything that reads
    // the keyboard has to hold off until the journey is over.
    bool traveling;
    Ball ball;
    Transform brickHolder;
    int score;
    int highScore;
    int previousRecord;
    int lives;
    int bricksLeft;
    string typedName = "";
    int transitionFrame = -1;
    bool exitPrompt;
    Texture2D dimTexture;

    void Awake()
    {
        Instance = this;
        highScore = PlayerPrefs.GetInt(HighScoreKey, 0);
    }

    void Start()
    {
        if (mainMenuPanel != null) mainMenuPanel.OptionChosen += OnMenuOptionChosen;
        ShowMenu();
    }

    // The paddle a shattering brick's rubble may be caught on, and null unless a
    // round is actually being played: the menu's slabs shatter through the same
    // code and there is no score on the menu screen to add to. Read once, when
    // the rubble is spawned — OnDebrisCaught asks the question again when a
    // chunk actually lands, since a round can end while rubble is still falling.
    public Paddle Catcher => state == State.Ready || state == State.Playing ? paddle : null;

    // A chunk of a broken brick landed on the paddle. Nothing is added once the
    // round is over: rubble goes on falling over the end screen and over name
    // entry, and a score being submitted to the hall of fame must not creep up
    // underneath it.
    public void OnDebrisCaught()
    {
        if (state != State.Ready && state != State.Playing) return;
        SetScore(score + DebrisPoints);
    }

    void ShowMenu() => ShowMenu(null);

    // `finalScore` is the score a round was just lost with, and null for every
    // other way in here. It says which of the menu's boards the player arrives
    // on: a lost round comes to rest on the game over board, which reads that
    // score out, and everything else opens on the title board. Either way this
    // is the same menu, in the same state, with the same ball to serve — the
    // game over board is a screen of the menu rather than an overlay over the
    // round, so there is no end-screen state to sit in and no key to press to
    // leave it. The player aims at an arrow: the hall of fame to the left, the
    // title board (and START) to the right.
    void ShowMenu(int? finalScore)
    {
        transitionFrame = Time.frameCount;
        // Nothing of the round survives into the menu. 2D physics ignores Z, so
        // a ball or a grid of bricks left behind the menu's backdrop would still
        // be in the menu ball's way even though nothing of them can be seen.
        ClearRound();
        // The score and lives readouts belong to a round in progress.
        if (scoreBoard != null) scoreBoard.SetVisible(false);
        SetPlayfieldActive(false);
        // Whatever brought us to the menu, the bench is not standing behind it:
        // this is the one place every path back to the menu passes through.
        if (testBench != null) testBench.gameObject.SetActive(false);
        if (recordsPanel != null) recordsPanel.Hide();
        // The menu is a room of its own, off to the left of the playfield, so
        // the view has to be over it before it is switched on. Coming back is a
        // cut rather than a journey: the menu's opaque backdrop comes up in the
        // same frame, and there is nothing to see travelling over.
        MoveViewTo(MenuViewX);
        if (mainMenuPanel != null)
        {
            if (finalScore.HasValue) mainMenuPanel.ShowGameOver(finalScore.Value);
            else mainMenuPanel.Show();
        }
        state = State.Menu;
    }

    void ClearRound()
    {
        if (ball != null)
        {
            Destroy(ball.gameObject);
            ball = null;
        }
        if (brickHolder != null)
        {
            Destroy(brickHolder.gameObject);
            brickHolder = null;
        }
        bricksLeft = 0;
        // A round's rubble and its ricochet sparks are unparented objects that
        // outlive whatever spawned them, so they would go on falling over the
        // menu that replaces the round — the same sweep the menu does on its
        // way out, now that a round throws sparks off its borders too.
        Debris.ClearAll();
        Ricochet.ClearAll();
        JetTrail.ClearAll();
        PowerWave.ClearAll();
    }

    // Only StartGame ever reaches here: the menu's hall of fame is a screen of
    // its own inside the menu, which slides to it and back without this.
    void OnMenuOptionChosen(MainMenuOption option)
    {
        if (state != State.Menu || option != MainMenuOption.StartGame) return;
        NewGame();
    }

    // The round is built first and the view travels to it afterwards, because
    // the point of the journey is arriving at something: the bricks and the
    // paddle are already standing in the playfield's room, and its borders laid
    // against the frame, before the menu starts sliding out of it.
    void NewGame()
    {
        previousRecord = highScore;
        SetPlayfieldActive(true);
        if (recordsPanel != null) recordsPanel.Hide();
        SetScore(0);
        SetLives(startingLives);
        BuildLevel();
        SpawnBall();
        StartCoroutine(TravelToPlayfield());
    }

    // START is the right-pointing arrow, so it goes right: the view leaves the
    // menu's room for the playfield's rather than cutting to it. The menu is
    // only switched off once the view is over the playfield — and in the same
    // frame, so its backdrop is never seen sitting on the round's screen.
    IEnumerator TravelToPlayfield()
    {
        traveling = true;
        float from = MenuViewX;
        for (float t = 0f; t < TravelDuration; t += Time.deltaTime)
        {
            MoveViewTo(Mathf.SmoothStep(from, PlayfieldViewX, t / TravelDuration));
            yield return null;
        }
        MoveViewTo(PlayfieldViewX);
        if (mainMenuPanel != null) mainMenuPanel.Hide();
        // The HUD belongs to the round, so it comes up on arrival rather than
        // hanging over the menu all the way across.
        if (scoreBoard != null) scoreBoard.SetVisible(true);
        traveling = false;
        // The SPACE that could not launch the ball on the way must not launch
        // it on the frame the journey ends either.
        transitionFrame = Time.frameCount;
    }

    // The playfield is the world's middle; the menu is wherever its screen was
    // authored, a screen's width to the left of it.
    const float PlayfieldViewX = 0f;

    float MenuViewX => mainMenuPanel != null ? mainMenuPanel.transform.position.x : PlayfieldViewX;

    static void MoveViewTo(float x)
    {
        var view = Camera.main;
        if (view == null) return;
        var position = view.transform.position;
        view.transform.position = new Vector3(x, position.y, position.z);
    }

    void SetPlayfieldActive(bool active)
    {
        if (playfieldObjects == null) return;
        foreach (var playfieldObject in playfieldObjects)
            if (playfieldObject != null) playfieldObject.SetActive(active);
    }

    void SetScore(int value)
    {
        score = value;
        if (score > highScore)
        {
            highScore = score;
            PlayerPrefs.SetInt(HighScoreKey, highScore);
        }
        if (scoreBoard != null) scoreBoard.SetScore(score);
    }

    void SetLives(int value)
    {
        lives = value;
        if (scoreBoard != null) scoreBoard.SetLives(lives);
    }

    void BuildLevel()
    {
        if (brickHolder != null) Destroy(brickHolder.gameObject);
        brickHolder = new GameObject("Bricks").transform;
        bricksLeft = 0;

        const float width = 1.5f, height = 0.5f, gap = 0.14f;
        const float y0 = 4.6f;

        // One column per material and one row per shape, so the board is
        // BlockMaterials.Count x boardKinds.Length blocks and nothing is cut to
        // fit: nine slots span 9 * 1.64 - 0.14 = 14.62 units, which clears the
        // frame's width at any aspect from 4:3 up. The grid used to be cut to
        // however many whole slots the window held, and a board that dropped
        // combinations on a narrow window would not be showing all of them.
        int columnCount = BlockMaterials.Count;
        float x0 = -(columnCount - 1) * (width + gap) / 2f;

        for (int row = 0; row < boardKinds.Length; row++)
        {
            var kind = boardKinds[row];
            int points = (boardKinds.Length - row) * 100;

            for (int column = 0; column < columnCount; column++)
            {
                var slot = new Vector3(x0 + column * (width + gap), y0 - row * (height + gap), 0f);
                SpawnBrick(kind, slot, points, (BlockMaterial)column);
            }
        }
    }

    void SpawnBrick(BrickKind kind, Vector3 position, int points, BlockMaterial material)
    {
        var brick = Instantiate(PrefabFor(kind), position, Quaternion.identity, brickHolder);
        brick.Points = points;
        brick.SetMaterial(material, MaterialAsset(material));
        var variety = VarietyOf(material);
        if (variety != null) brick.SetLook(variety.Roll(GrainTilesPerUnit));
        // A force field is never cleared, so a round that counted one would
        // wait for ever to be won.
        if (!brick.Unbreakable) bricksLeft++;
    }

    // Public so the test bench dresses a block exactly as a round does, rather
    // than carrying a second copy of the same wiring that could drift from it.
    // Falls back to null — and so to whatever the prefab was wearing — while a
    // material asset is not wired up, the same way PrefabFor does for shapes.
    public Material MaterialAsset(BlockMaterial material)
    {
        int index = (int)material;
        return blockMaterials != null && index < blockMaterials.Length ? blockMaterials[index] : null;
    }

    // Null for a material nothing was authored for, which is the ordinary case
    // and not a failure: no variety means every block of it is the shared asset
    // exactly as written, the way all nine were before Polymer got a grain.
    public BlockVariety VarietyOf(BlockMaterial material)
    {
        if (blockVarieties == null) return null;
        foreach (var variety in blockVarieties)
            if (variety != null && variety.material == material && variety.grains != null
                && variety.grains.Length > 0)
                return variety;
        return null;
    }

    // Falls back to the normal brick while a variant prefab is not wired up.
    Brick PrefabFor(BrickKind kind) => kind switch
    {
        BrickKind.Half when halfBrickPrefab != null => halfBrickPrefab,
        BrickKind.Rounded when roundedBrickPrefab != null => roundedBrickPrefab,
        BrickKind.Round when roundBrickPrefab != null => roundBrickPrefab,
        _ => brickPrefab,
    };

    void SpawnBall()
    {
        transitionFrame = Time.frameCount;
        if (ball == null) ball = Instantiate(ballPrefab);
        ball.AttachTo(paddle.transform);
        state = State.Ready;
    }

    void Update()
    {
        // The key press that caused this frame's transition must not act twice:
        // MainMenuPanel reads the keyboard in the same frame as this component,
        // in an undefined order, so ENTER on START would otherwise both start
        // the game and launch the ball.
        if (Time.frameCount == transitionFrame) return;
        // The round is built and waiting, but the view is still on its way to
        // it: no key means anything until it arrives.
        if (traveling) return;

        var keyboard = Keyboard.current;
        bool pressedSpace = keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
        bool pressedEscape = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;

        // The exit confirmation swallows every other key while it is up, and
        // the game is frozen behind it (see OpenExitPrompt).
        if (exitPrompt)
        {
            bool confirmed = keyboard != null
                && (keyboard.yKey.wasPressedThisFrame
                    || keyboard.enterKey.wasPressedThisFrame
                    || keyboard.numpadEnterKey.wasPressedThisFrame);
            if (confirmed) ExitToMenu();
            else if (pressedEscape || (keyboard != null && keyboard.nKey.wasPressedThisFrame)) CloseExitPrompt();
            return;
        }

        // ESC asks to abandon the round and go back to the menu. Only a round in
        // progress is worth confirming: the menu has nothing to leave, name
        // entry is a few keystrokes from being submitted with ENTER, and an end
        // screen leaves on ESC as readily as on SPACE, with nothing to lose.
        if (pressedEscape && (state == State.Ready || state == State.Playing))
        {
            OpenExitPrompt();
            return;
        }

        switch (state)
        {
            case State.Menu:
                // The menu drives itself entirely, hall of fame included —
                // there is no keyboard path into it but SPACE to launch, which
                // MainMenuPanel reads for itself. The one exception is the word
                // that opens the test bench.
                ReadBenchCode(keyboard);
                break;
            case State.Bench:
                // Everything else the bench does, it reads for itself. Only the
                // way out is here, since leaving is this component's business.
                if (pressedEscape) CloseBench();
                break;
            case State.Ready:
                if (pressedSpace)
                {
                    ball.Launch();
                    state = State.Playing;
                }
                break;
            case State.Playing:
                if (ball.transform.position.y < BallLostY) OnBallLost();
                break;
            case State.EnteringName:
                ReadNameKeys(keyboard);
                bool pressedEnter = keyboard != null
                    && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
                if (pressedEnter) SubmitName();
                break;
            // GameOver is an `endState` only: a lost round goes straight back to
            // the menu, onto the board that says so, and the menu drives itself
            // from there. Winning is still an overlay with a key to dismiss.
            case State.Won:
                if (pressedSpace || pressedEscape) ShowMenu();
                break;
        }
    }

    // The code has to be typed *in order*, and a wrong letter starts it over
    // rather than merely failing — except that the wrong letter may itself be
    // the first letter of a fresh attempt, which is why the reset re-tests
    // against position zero. Nothing here is timed: a door nobody is racing
    // through does not need to be.
    void ReadBenchCode(Keyboard keyboard)
    {
        if (keyboard == null || testBench == null) return;

        for (int i = 0; i < BenchCode.Length; i++)
        {
            if (!keyboard[BenchCode[i]].wasPressedThisFrame) continue;
            benchProgress = i == benchProgress ? benchProgress + 1 : (i == 0 ? 1 : 0);
            if (benchProgress < BenchCode.Length) return;
            benchProgress = 0;
            OpenBench();
            return;
        }
        // A letter that is nowhere in the word is as much a mistake as one out
        // of order.
        if (keyboard.anyKey.wasPressedThisFrame) benchProgress = 0;
    }

    // The bench is a room, not an overlay: the menu is switched off and the view
    // cuts to it, exactly the way coming back from a round cuts to the menu. The
    // round's room stays off — nothing about a bench is a round.
    void OpenBench()
    {
        transitionFrame = Time.frameCount;
        ClearRound();
        if (mainMenuPanel != null) mainMenuPanel.Hide();
        if (scoreBoard != null) scoreBoard.SetVisible(false);
        SetPlayfieldActive(false);
        state = State.Bench;
        testBench.gameObject.SetActive(true);
        MoveViewTo(testBench.transform.position.x);
    }

    // Public so the bench's own Q key can leave: see TestBench for why ESC
    // alone is not enough.
    public void CloseBench()
    {
        testBench.gameObject.SetActive(false);
        ShowMenu();
    }

    // The prompt freezes the game rather than overlaying a live one: a ball in
    // flight would otherwise go on being lost, and bricks broken, while the
    // player reads the question.
    void OpenExitPrompt()
    {
        exitPrompt = true;
        Time.timeScale = 0f;
    }

    void CloseExitPrompt()
    {
        exitPrompt = false;
        Time.timeScale = 1f;
    }

    // Abandoning the round: the score goes with it, however good it was — the
    // hall of fame is for rounds that were played out. SetScore has been
    // pushing the running score into the stored high score all round, so that
    // has to be wound back to what the round started with, or a walked-out-on
    // score would still raise the bar the next round has to beat.
    void ExitToMenu()
    {
        CloseExitPrompt();
        if (highScore > previousRecord)
        {
            highScore = previousRecord;
            PlayerPrefs.SetInt(HighScoreKey, highScore);
        }
        ShowMenu();
    }

    // The bottom edge of the frame is the one side the room is left open on, so
    // where a ball counts as lost follows it rather than a fixed depth.
    float BallLostY => playfield != null && playfield.HalfHeight > 0f
        ? -playfield.HalfHeight - BallLostDrop
        : -7f;

    void OnBallLost()
    {
        SetLives(lives - 1);
        if (lives > 0)
        {
            SpawnBall();
            return;
        }

        Destroy(ball.gameObject);
        ball = null;
        EndRound(State.GameOver);
    }

    public void OnBrickDestroyed(Brick brick)
    {
        // A block on the test bench is not part of a round. It pays nothing and
        // is counted in nothing — and this line is the one that matters, because
        // SetScore writes straight through to the stored high score: without it,
        // breaking a block on a bench would raise the bar a real round has to
        // clear, and could put a bench session in the hall of fame.
        if (state == State.Bench) return;

        SetScore(score + brick.Points);
        bricksLeft--;
        if (bricksLeft > 0 || state != State.Playing) return;

        if (ball != null)
        {
            Destroy(ball.gameObject);
            ball = null;
        }
        EndRound(State.Won);
    }

    void EndRound(State result)
    {
        endState = result;
        if (score > previousRecord && recordsPanel != null && Keyboard.current != null)
        {
            state = State.EnteringName;
            typedName = "";
            recordsPanel.ShowNameEntry(score);
            return;
        }
        ShowEndScreen();
    }

    void ShowEndScreen()
    {
        // A lost round ends on the menu's own game over board — a screen of the
        // same room the title board stands in, arrived at rather than overlaid
        // (see ShowMenu). Clearing the level still raises the old overlay: there
        // is no board for a win, and one screen of the two being scene content
        // is better than neither.
        if (endState == State.GameOver)
        {
            ShowMenu(score);
            return;
        }

        transitionFrame = Time.frameCount;
        state = endState;
        if (recordsPanel != null)
            recordsPanel.ShowRecords(RecordBook.Load(),
                $"YOU WIN! Score: {score} — press SPACE for the menu");
    }

    // The name is read key by key rather than through Keyboard.onTextInput,
    // which never delivered a character here and left the field looking broken.
    // Reading keys is also the honest path for what the name is *for*: the hall
    // of fame draws it in BlockText's 5 x 7 font, which has only the capitals
    // and the digits, so a champion who typed "Alex" was put on the plaque as
    // "A???". Only the glyphs that font can draw can be typed now.
    void ReadNameKeys(Keyboard keyboard)
    {
        if (keyboard == null) return;

        bool changed = false;

        void Append(char character)
        {
            // A leading space would be trimmed off the name anyway, and the
            // plaque has nothing to draw for it.
            if (typedName.Length >= MaxNameLength) return;
            if (character == ' ' && typedName.Length == 0) return;
            typedName += character;
            changed = true;
        }

        if (keyboard.backspaceKey.wasPressedThisFrame && typedName.Length > 0)
        {
            typedName = typedName.Substring(0, typedName.Length - 1);
            changed = true;
        }

        for (var key = Key.A; key <= Key.Z; key++)
            if (keyboard[key].wasPressedThisFrame) Append((char)('A' + (key - Key.A)));

        // Digit1..Digit9 run in order and Digit0 sits after them, as it does on
        // the row itself; the numpad's ten are plainly in order.
        for (var key = Key.Digit1; key <= Key.Digit9; key++)
            if (keyboard[key].wasPressedThisFrame) Append((char)('1' + (key - Key.Digit1)));
        if (keyboard.digit0Key.wasPressedThisFrame) Append('0');
        for (var key = Key.Numpad0; key <= Key.Numpad9; key++)
            if (keyboard[key].wasPressedThisFrame) Append((char)('0' + (key - Key.Numpad0)));

        if (keyboard.spaceKey.wasPressedThisFrame) Append(' ');

        if (changed && recordsPanel != null) recordsPanel.SetTypedName(typedName);
    }

    void SubmitName()
    {
        var name = typedName.Trim();
        RecordBook.Add(name.Length > 0 ? name : "???", score);
        ShowEndScreen();
    }

    void OnDestroy()
    {
        // The prompt freezes time, and timeScale outlives this component.
        Time.timeScale = 1f;
        if (dimTexture != null) Destroy(dimTexture);
        if (mainMenuPanel != null) mainMenuPanel.OptionChosen -= OnMenuOptionChosen;
    }

    void OnGUI()
    {
        if (exitPrompt)
        {
            DrawExitPrompt();
            return;
        }

        // The same prompt serves the round and the menu: both sit a ball on a
        // paddle and wait for SPACE, and on the menu it is the one thing that
        // isn't self-evident about aiming at an option.
        // Not while the view is still travelling to the round, though: the ball
        // is sitting on the paddle already, but SPACE does nothing yet.
        bool waiting = !traveling
            && (state == State.Ready
                || (state == State.Menu && mainMenuPanel != null && mainMenuPanel.BallWaiting));
        if (!waiting) return;

        var banner = new GUIStyle(GUI.skin.label)
        {
            fontSize = 32,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        banner.normal.textColor = Color.white;
        // Mid-screen during a round, but down by the paddle on the menu, where
        // the middle of the screen is where the option slabs are.
        float y = state == State.Ready ? Screen.height / 2f - 60f : Screen.height * 0.72f;
        GUI.Label(new Rect(0f, y, Screen.width, 120f), "Press SPACE to launch", banner);
    }

    void DrawExitPrompt()
    {
        // Dim whatever is frozen behind the question so the two lines read
        // against a bright playfield as well as against the menu. OnGUI runs
        // several times a frame, so the one-pixel texture is built once.
        if (dimTexture == null)
        {
            dimTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            dimTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.7f));
            dimTexture.Apply();
        }
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), dimTexture);

        var question = new GUIStyle(GUI.skin.label)
        {
            fontSize = 40,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        question.normal.textColor = Color.white;
        var hint = new GUIStyle(question) { fontSize = 24, fontStyle = FontStyle.Normal };
        hint.normal.textColor = new Color(0.8f, 0.8f, 0.8f);

        GUI.Label(new Rect(0f, Screen.height / 2f - 70f, Screen.width, 60f),
            "Leave the round for the menu?", question);
        GUI.Label(new Rect(0f, Screen.height / 2f + 5f, Screen.width, 40f),
            "Y or ENTER — main menu        N or ESC — keep playing", hint);
    }
}
