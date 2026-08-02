using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Grid-based worm (snake) game. Grid, camera, walls and prefab references
// are authored in the scene (see WormGameEditorSetup) so they can be
// inspected and edited without entering Play mode. Only the moving worm
// body and food are created at runtime, since that state doesn't exist
// until the game is actually running.
public class WormGame : MonoBehaviour
{
    [Header("Grid")]
    [SerializeField] int gridWidth = WormGameDefaults.GridWidth;
    [SerializeField] int gridHeight = WormGameDefaults.GridHeight;
    [SerializeField] float cellSize = WormGameDefaults.CellSize;

    [Header("Gameplay")]
    [SerializeField] float moveInterval = 0.12f;

    [Header("Prefabs")]
    [SerializeField] GameObject segmentPrefab;
    [SerializeField] GameObject foodPrefab;

    readonly List<Vector2Int> segments = new List<Vector2Int>();
    readonly List<Transform> segmentVisuals = new List<Transform>();

    Vector2Int direction = Vector2Int.right;
    Vector2Int pendingDirection = Vector2Int.right;
    Vector2Int foodPosition;
    Transform foodVisual;
    float moveTimer;
    int score;
    bool gameOver;

    void Awake()
    {
        StartNewGame();
    }

    Transform SpawnInstance(GameObject prefab)
    {
        return Instantiate(prefab, transform).transform;
    }

    void StartNewGame()
    {
        foreach (var t in segmentVisuals)
            if (t != null) Destroy(t.gameObject);
        segmentVisuals.Clear();
        segments.Clear();

        var start = new Vector2Int(gridWidth / 2, gridHeight / 2);
        for (int i = 0; i < 3; i++)
            segments.Add(start - new Vector2Int(i, 0));

        direction = Vector2Int.right;
        pendingDirection = direction;
        score = 0;
        gameOver = false;
        moveTimer = 0f;

        foreach (var _ in segments)
            segmentVisuals.Add(SpawnInstance(segmentPrefab));

        if (foodVisual == null)
            foodVisual = SpawnInstance(foodPrefab);
        SpawnFood();

        UpdateVisuals();
    }

    void SpawnFood()
    {
        var free = new List<Vector2Int>();
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
            {
                var p = new Vector2Int(x, y);
                if (!segments.Contains(p)) free.Add(p);
            }

        foodPosition = free[Random.Range(0, free.Count)];
        foodVisual.position = ToWorld(foodPosition);
    }

    Vector3 ToWorld(Vector2Int p) => new Vector3(p.x * cellSize, p.y * cellSize, 0f);

    void Update()
    {
        ReadInput();

        if (gameOver)
        {
            if (Keyboard.current != null &&
                (Keyboard.current.rKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame))
                StartNewGame();
            return;
        }

        moveTimer += Time.deltaTime;
        if (moveTimer >= moveInterval)
        {
            moveTimer -= moveInterval;
            Step();
        }
    }

    void ReadInput()
    {
        if (Keyboard.current == null) return;

        if ((Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.wKey.wasPressedThisFrame) && direction != Vector2Int.down)
            pendingDirection = Vector2Int.up;
        else if ((Keyboard.current.downArrowKey.wasPressedThisFrame || Keyboard.current.sKey.wasPressedThisFrame) && direction != Vector2Int.up)
            pendingDirection = Vector2Int.down;
        else if ((Keyboard.current.leftArrowKey.wasPressedThisFrame || Keyboard.current.aKey.wasPressedThisFrame) && direction != Vector2Int.right)
            pendingDirection = Vector2Int.left;
        else if ((Keyboard.current.rightArrowKey.wasPressedThisFrame || Keyboard.current.dKey.wasPressedThisFrame) && direction != Vector2Int.left)
            pendingDirection = Vector2Int.right;
    }

    void Step()
    {
        direction = pendingDirection;
        var newHead = segments[0] + direction;

        bool hitWall = newHead.x < 0 || newHead.x >= gridWidth || newHead.y < 0 || newHead.y >= gridHeight;
        if (hitWall || segments.Contains(newHead))
        {
            gameOver = true;
            return;
        }

        segments.Insert(0, newHead);

        if (newHead == foodPosition)
        {
            score++;
            segmentVisuals.Insert(0, SpawnInstance(segmentPrefab));
            SpawnFood();
        }
        else
        {
            segments.RemoveAt(segments.Count - 1);
        }

        UpdateVisuals();
    }

    void UpdateVisuals()
    {
        for (int i = 0; i < segments.Count; i++)
            segmentVisuals[i].position = ToWorld(segments[i]);
    }

    void OnGUI()
    {
        GUI.skin.label.fontSize = 20;
        GUI.color = Color.white;
        GUI.Label(new Rect(10, 10, 300, 30), $"Score: {score}");

        if (gameOver)
        {
            GUI.skin.label.fontSize = 28;
            GUI.Label(new Rect(Screen.width / 2f - 220, Screen.height / 2f - 20, 440, 60),
                "Game Over — Press R or Space to Restart");
        }
    }
}
