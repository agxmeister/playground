using UnityEngine;
using UnityEngine.InputSystem;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    const string HighScoreKey = "Arkanoid.HighScore";

    [SerializeField] Ball ballPrefab;
    [SerializeField] Brick brickPrefab;
    [SerializeField] Paddle paddle;
    [SerializeField] ScoreBoard scoreBoard;

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

    enum State { Ready, Playing, GameOver, Won }

    State state;
    Ball ball;
    Transform brickHolder;
    int score;
    int highScore;
    int lives;
    int bricksLeft;

    void Awake()
    {
        Instance = this;
        highScore = PlayerPrefs.GetInt(HighScoreKey, 0);
    }

    void Start()
    {
        NewGame();
    }

    void NewGame()
    {
        SetScore(0);
        SetLives(startingLives);
        BuildLevel();
        SpawnBall();
    }

    void SetScore(int value)
    {
        score = value;
        if (score > highScore)
        {
            highScore = score;
            PlayerPrefs.SetInt(HighScoreKey, highScore);
        }
        if (scoreBoard == null) return;
        scoreBoard.SetScore(score);
        scoreBoard.SetHighScore(highScore);
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
        float x0 = -(columns - 1) * (width + gap) / 2f;
        const float y0 = 4.6f;

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                var position = new Vector3(x0 + column * (width + gap), y0 - row * (height + gap), 0f);
                var brick = Instantiate(brickPrefab, position, Quaternion.identity, brickHolder);
                brick.Points = (rows - row) * 100;
                brick.GetComponent<SpriteRenderer>().color = rowColors[row % rowColors.Length];
                bricksLeft++;
            }
        }
    }

    void SpawnBall()
    {
        if (ball == null) ball = Instantiate(ballPrefab);
        ball.AttachTo(paddle.transform);
        state = State.Ready;
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        bool pressedSpace = keyboard != null && keyboard.spaceKey.wasPressedThisFrame;

        switch (state)
        {
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
            case State.GameOver:
            case State.Won:
                if (pressedSpace) NewGame();
                break;
        }
    }

    void OnBallLost()
    {
        SetLives(lives - 1);
        if (lives > 0)
        {
            SpawnBall();
            return;
        }

        state = State.GameOver;
        Destroy(ball.gameObject);
        ball = null;
    }

    public void OnBrickDestroyed(Brick brick)
    {
        SetScore(score + brick.Points);
        bricksLeft--;
        if (bricksLeft > 0 || state != State.Playing) return;

        state = State.Won;
        if (ball != null)
        {
            Destroy(ball.gameObject);
            ball = null;
        }
    }

    void OnGUI()
    {
        string message = state switch
        {
            State.Ready => "Press SPACE to launch",
            State.GameOver => $"GAME OVER\nScore: {score} — press SPACE to restart",
            State.Won => $"YOU WIN!\nScore: {score} — press SPACE to play again",
            _ => null,
        };

        if (message == null) return;

        var banner = new GUIStyle(GUI.skin.label)
        {
            fontSize = 32,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        banner.normal.textColor = Color.white;
        GUI.Label(new Rect(0f, Screen.height / 2f - 60f, Screen.width, 120f), message, banner);
    }
}
