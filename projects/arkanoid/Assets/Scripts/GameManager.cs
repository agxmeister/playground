using UnityEngine;
using UnityEngine.InputSystem;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // True while the quit confirmation is up. Time is frozen behind it, but the
    // components that read the keyboard themselves have to hold off too.
    public bool Paused => quitPrompt;

    const string HighScoreKey = "Arkanoid.HighScore";

    [SerializeField] Ball ballPrefab;
    [SerializeField] Brick brickPrefab;
    [SerializeField] Brick halfBrickPrefab;
    [SerializeField] Brick roundedBrickPrefab;
    [SerializeField] Brick roundBrickPrefab;
    [SerializeField] Paddle paddle;
    [SerializeField] ScoreBoard scoreBoard;
    [SerializeField] RecordsPanel recordsPanel;
    [SerializeField] MainMenuPanel mainMenuPanel;
    // The walls and the paddle, switched off while the menu is up. 2D physics
    // ignores Z, so the playfield's colliders would otherwise fence in the
    // menu's ball even though the menu screen hides them, and both paddles
    // would answer the arrow keys at once.
    [SerializeField] GameObject[] playfieldObjects;

    [SerializeField] int rows = 5;
    [SerializeField] int columns = 8;
    [SerializeField] int startingLives = 3;

    static readonly Color[] rowColors =
    {
        new Color(0.91f, 0.30f, 0.24f),
        new Color(0.95f, 0.61f, 0.07f),
        new Color(0.95f, 0.83f, 0.18f),
        new Color(0.18f, 0.80f, 0.44f),
        new Color(0.20f, 0.60f, 0.86f),
    };

    // Hardness per row, matching rowColors: red bricks take 3 hits, blue take 1.
    static readonly int[] rowHardness = { 3, 2, 2, 1, 1 };

    // The brick shape used by each row. Half and Round bricks are half a slot
    // wide, so those rows get two bricks per slot at half the points each.
    enum BrickKind { Normal, Half, Rounded, Round }

    static readonly BrickKind[] rowKinds =
    {
        BrickKind.Rounded, BrickKind.Normal, BrickKind.Half, BrickKind.Round, BrickKind.Normal,
    };

    enum State { Menu, Ready, Playing, EnteringName, GameOver, Won }

    const int MaxNameLength = 12;

    State state;
    State endState;
    Ball ball;
    Transform brickHolder;
    int score;
    int highScore;
    int previousRecord;
    int lives;
    int bricksLeft;
    string typedName = "";
    bool menuShowingRecords;
    int transitionFrame = -1;
    bool quitPrompt;
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

    void ShowMenu()
    {
        transitionFrame = Time.frameCount;
        menuShowingRecords = false;
        // The score and lives readouts belong to a round in progress.
        if (scoreBoard != null) scoreBoard.SetVisible(false);
        SetPlayfieldActive(false);
        if (recordsPanel != null) recordsPanel.Hide();
        if (mainMenuPanel != null) mainMenuPanel.Show();
        state = State.Menu;
    }

    void OnMenuOptionChosen(MainMenuOption option)
    {
        if (state != State.Menu || menuShowingRecords) return;

        if (option == MainMenuOption.StartGame)
        {
            NewGame();
            return;
        }

        transitionFrame = Time.frameCount;
        menuShowingRecords = true;
        // Only the options go away — the 3D menu screen stays up behind the
        // records panel.
        if (mainMenuPanel != null) mainMenuPanel.HideOptions();
        if (recordsPanel != null) recordsPanel.ShowRecords(RecordBook.Load(), "ESC — back to menu");
    }

    void NewGame()
    {
        previousRecord = highScore;
        SetPlayfieldActive(true);
        if (scoreBoard != null) scoreBoard.SetVisible(true);
        if (recordsPanel != null) recordsPanel.Hide();
        if (mainMenuPanel != null) mainMenuPanel.Hide();
        SetScore(0);
        SetLives(startingLives);
        BuildLevel();
        SpawnBall();
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
        // Two half-width bricks plus the same gap fill one slot exactly:
        // 2 * 0.68 + 0.14 = 1.5, with their centers at slot center ± 0.41.
        const float halfOffset = (width + gap) / 4f;
        float x0 = -(columns - 1) * (width + gap) / 2f;
        const float y0 = 4.6f;

        for (int row = 0; row < rows; row++)
        {
            var kind = rowKinds[row % rowKinds.Length];
            int points = (rows - row) * 100;
            int hardness = rowHardness[row % rowHardness.Length];
            var color = rowColors[row % rowColors.Length];

            for (int column = 0; column < columns; column++)
            {
                var slot = new Vector3(x0 + column * (width + gap), y0 - row * (height + gap), 0f);
                if (kind == BrickKind.Half || kind == BrickKind.Round)
                {
                    SpawnBrick(kind, slot + Vector3.left * halfOffset, points / 2, hardness, color);
                    SpawnBrick(kind, slot + Vector3.right * halfOffset, points / 2, hardness, color);
                }
                else
                {
                    SpawnBrick(kind, slot, points, hardness, color);
                }
            }
        }
    }

    void SpawnBrick(BrickKind kind, Vector3 position, int points, int hardness, Color color)
    {
        var brick = Instantiate(PrefabFor(kind), position, Quaternion.identity, brickHolder);
        brick.Points = points;
        brick.Hardness = hardness;
        brick.SetColor(color);
        bricksLeft++;
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

        var keyboard = Keyboard.current;
        bool pressedSpace = keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
        bool pressedEscape = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;

        // The quit confirmation swallows every other key while it is up, and
        // the game is frozen behind it (see OpenQuitPrompt).
        if (quitPrompt)
        {
            bool confirmed = keyboard != null
                && (keyboard.yKey.wasPressedThisFrame
                    || keyboard.enterKey.wasPressedThisFrame
                    || keyboard.numpadEnterKey.wasPressedThisFrame);
            if (confirmed) QuitGame();
            else if (pressedEscape || (keyboard != null && keyboard.nKey.wasPressedThisFrame)) CloseQuitPrompt();
            return;
        }

        // ESC asks to quit everywhere except the hall of fame, where it already
        // means "back to the menu", and name entry, which ENTER submits.
        if (pressedEscape && state != State.EnteringName && !(state == State.Menu && menuShowingRecords))
        {
            OpenQuitPrompt();
            return;
        }

        switch (state)
        {
            case State.Menu:
                // The menu's own options are driven by MainMenuPanel; only the
                // way back from its hall of fame view is handled here.
                if (menuShowingRecords)
                {
                    bool pressedBack = pressedSpace || (keyboard != null
                        && (keyboard.escapeKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame));
                    if (pressedBack) ShowMenu();
                }
                break;
            case State.Ready:
                if (pressedSpace)
                {
                    ball.Launch();
                    state = State.Playing;
                }
                break;
            case State.Playing:
                if (ball.transform.position.y < -7f) OnBallLost();
                break;
            case State.EnteringName:
                bool pressedEnter = keyboard != null
                    && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
                if (pressedEnter) SubmitName();
                break;
            case State.GameOver:
            case State.Won:
                if (pressedSpace) ShowMenu();
                break;
        }
    }

    // The prompt freezes the game rather than overlaying a live one: a ball in
    // flight would otherwise go on being lost, and bricks broken, while the
    // player reads the question.
    void OpenQuitPrompt()
    {
        quitPrompt = true;
        Time.timeScale = 0f;
    }

    void CloseQuitPrompt()
    {
        quitPrompt = false;
        Time.timeScale = 1f;
    }

    void QuitGame()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

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
        var keyboard = Keyboard.current;
        if (score > previousRecord && recordsPanel != null && keyboard != null)
        {
            state = State.EnteringName;
            typedName = "";
            recordsPanel.ShowNameEntry(score);
            keyboard.onTextInput += OnTextInput;
            return;
        }
        ShowEndScreen();
    }

    void ShowEndScreen()
    {
        transitionFrame = Time.frameCount;
        state = endState;
        string message = endState == State.Won
            ? $"YOU WIN! Score: {score} — press SPACE for the menu"
            : $"GAME OVER — Score: {score} — press SPACE for the menu";
        if (recordsPanel != null) recordsPanel.ShowRecords(RecordBook.Load(), message);
    }

    void OnTextInput(char character)
    {
        if (state != State.EnteringName) return;
        if (character == '\b')
        {
            if (typedName.Length > 0) typedName = typedName.Substring(0, typedName.Length - 1);
        }
        else if (!char.IsControl(character) && typedName.Length < MaxNameLength)
        {
            typedName += character;
        }
        recordsPanel.SetTypedName(typedName);
    }

    void SubmitName()
    {
        if (Keyboard.current != null) Keyboard.current.onTextInput -= OnTextInput;
        var name = typedName.Trim();
        RecordBook.Add(name.Length > 0 ? name : "???", score);
        ShowEndScreen();
    }

    void OnDestroy()
    {
        // The prompt freezes time, and timeScale outlives this component.
        Time.timeScale = 1f;
        if (dimTexture != null) Destroy(dimTexture);
        if (Keyboard.current != null) Keyboard.current.onTextInput -= OnTextInput;
        if (mainMenuPanel != null) mainMenuPanel.OptionChosen -= OnMenuOptionChosen;
    }

    void OnGUI()
    {
        if (quitPrompt)
        {
            DrawQuitPrompt();
            return;
        }

        // The same prompt serves the round and the menu: both sit a ball on a
        // paddle and wait for SPACE, and on the menu it is the one thing that
        // isn't self-evident about aiming at an option.
        bool waiting = state == State.Ready
            || (state == State.Menu && !menuShowingRecords
                && mainMenuPanel != null && mainMenuPanel.BallWaiting);
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

    void DrawQuitPrompt()
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

        GUI.Label(new Rect(0f, Screen.height / 2f - 70f, Screen.width, 60f), "Quit the game?", question);
        GUI.Label(new Rect(0f, Screen.height / 2f + 5f, Screen.width, 40f),
            "Y or ENTER — quit        N or ESC — keep playing", hint);
    }
}
