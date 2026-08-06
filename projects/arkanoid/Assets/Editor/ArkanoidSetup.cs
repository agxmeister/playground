using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Builds the Arkanoid assets and scene as a resumable state machine: each stage
// creates one batch of assets and returns, letting the next domain reload see
// the result before the following stage runs. Safe to run on every reload —
// once everything exists it is a no-op.
public static class ArkanoidSetup
{
    const string SpritesFolder = "Assets/Sprites";
    const string PrefabsFolder = "Assets/Prefabs";
    const string PhysicsFolder = "Assets/Physics";
    const string SquareTexturePath = SpritesFolder + "/Square.png";
    const string CircleTexturePath = SpritesFolder + "/Circle.png";
    const string BouncyMaterialPath = PhysicsFolder + "/Bouncy.physicsMaterial2D";
    const string BallPrefabPath = PrefabsFolder + "/Ball.prefab";
    const string BrickPrefabPath = PrefabsFolder + "/Brick.prefab";

    [InitializeOnLoadMethod]
    static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        // Stage 1: source textures on disk.
        if (!File.Exists(ToAbsolute(SquareTexturePath)) || !File.Exists(ToAbsolute(CircleTexturePath)))
        {
            Directory.CreateDirectory(ToAbsolute(SpritesFolder));
            WriteSquareTexture();
            WriteCircleTexture();
            AssetDatabase.Refresh();
            Debug.Log("[ArkanoidSetup] Stage 1: wrote sprite textures.");
            return;
        }

        // Stage 2: import textures as sprites, 1 world unit each.
        bool squareReady = ConfigureSpriteImporter(SquareTexturePath, 32);
        bool circleReady = ConfigureSpriteImporter(CircleTexturePath, 64);
        if (!squareReady || !circleReady)
        {
            Debug.Log("[ArkanoidSetup] Stage 2: configured sprite importers.");
            return;
        }

        // Stage 3: frictionless, perfectly bouncy physics material.
        var bouncy = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(BouncyMaterialPath);
        if (bouncy == null)
        {
            Directory.CreateDirectory(ToAbsolute(PhysicsFolder));
            var material = new PhysicsMaterial2D("Bouncy") { bounciness = 1f, friction = 0f };
            AssetDatabase.CreateAsset(material, BouncyMaterialPath);
            Debug.Log("[ArkanoidSetup] Stage 3: created bouncy physics material.");
            return;
        }

        var squareSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SquareTexturePath);
        var circleSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CircleTexturePath);
        if (squareSprite == null || circleSprite == null)
        {
            Debug.Log("[ArkanoidSetup] Sprites not importable yet, waiting for next reload.");
            return;
        }

        // Stage 4: ball and brick prefabs.
        if (AssetDatabase.LoadAssetAtPath<GameObject>(BallPrefabPath) == null
            || AssetDatabase.LoadAssetAtPath<GameObject>(BrickPrefabPath) == null)
        {
            Directory.CreateDirectory(ToAbsolute(PrefabsFolder));
            CreateBallPrefab(circleSprite, bouncy);
            CreateBrickPrefab(squareSprite);
            Debug.Log("[ArkanoidSetup] Stage 4: created ball and brick prefabs.");
            return;
        }

        // Stage 5: scene content.
        if (GameObject.Find("GameManager") == null)
        {
            var ballPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BallPrefabPath);
            var brickPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BrickPrefabPath);
            if (ballPrefab == null || brickPrefab == null) return;

            BuildScene(squareSprite, ballPrefab, brickPrefab);
            Debug.Log("[ArkanoidSetup] Stage 5: built and saved the scene.");
            return;
        }

        // Stage 6: scoreboard UI canvas (screen-space overlay).
        if (GameObject.Find("ScoreBoard") == null)
        {
            BuildScoreBoard();
            Debug.Log("[ArkanoidSetup] Stage 6: built and saved the scoreboard UI.");
            return;
        }

        // Stage 7: records panel (hall of fame + name entry). Authored inactive,
        // so the guard must include inactive objects.
        if (Object.FindAnyObjectByType<RecordsPanel>(FindObjectsInactive.Include) == null)
        {
            BuildRecordsPanel();
            Debug.Log("[ArkanoidSetup] Stage 7: built the records panel (scene left dirty).");
            return;
        }

        // Stage 8: persist stage 7's panel once it exists in memory but not yet
        // in the scene file. Deferred out of the reload callback, where a direct
        // SaveScene raised the modal "scene changed on disk" dialog before.
        // delayCall proved unreliable in driven sessions; a self-removing
        // update handler fires on the next editor tick instead.
        var scene = SceneManager.GetActiveScene();
        if (!File.ReadAllText(ToAbsolute(scene.path)).Contains("RecordsPanel"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 8: queued scene save for the next editor tick.");
        }
    }

    // Runs once, on the first editor tick after the reload that registered it.
    static void SaveSceneOnce()
    {
        EditorApplication.update -= SaveSceneOnce;
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("[ArkanoidSetup] Stage 8: saved the scene with the records panel.");
    }

    static string ToAbsolute(string assetsRelativePath) =>
        Application.dataPath + assetsRelativePath.Substring("Assets".Length);

    static void WriteSquareTexture()
    {
        const int size = 32, border = 2;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool onBorder = x < border || y < border || x >= size - border || y >= size - border;
                float value = onBorder ? 0.72f : 1f;
                texture.SetPixel(x, y, new Color(value, value, value, 1f));
            }
        }
        texture.Apply();
        File.WriteAllBytes(ToAbsolute(SquareTexturePath), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }

    static void WriteCircleTexture()
    {
        const int size = 64;
        float radius = size / 2f - 1f, center = size / 2f - 0.5f;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                float alpha = Mathf.Clamp01(radius - distance + 0.5f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        texture.Apply();
        File.WriteAllBytes(ToAbsolute(CircleTexturePath), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }

    // Returns true when the texture is already imported as a 1-unit sprite.
    static bool ConfigureSpriteImporter(string path, int pixelsPerUnit)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return false;
        if (importer.textureType == TextureImporterType.Sprite
            && Mathf.Approximately(importer.spritePixelsPerUnit, pixelsPerUnit)) return true;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        return false;
    }

    static void CreateBallPrefab(Sprite circleSprite, PhysicsMaterial2D bouncy)
    {
        var go = new GameObject("Ball");
        go.transform.localScale = new Vector3(0.4f, 0.4f, 1f);
        go.AddComponent<SpriteRenderer>().sprite = circleSprite;
        go.AddComponent<CircleCollider2D>().sharedMaterial = bouncy;
        var body = go.AddComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        go.AddComponent<Ball>();
        PrefabUtility.SaveAsPrefabAsset(go, BallPrefabPath);
        Object.DestroyImmediate(go);
    }

    static void CreateBrickPrefab(Sprite squareSprite)
    {
        var go = new GameObject("Brick");
        go.transform.localScale = new Vector3(1.5f, 0.5f, 1f);
        go.AddComponent<SpriteRenderer>().sprite = squareSprite;
        go.AddComponent<BoxCollider2D>();
        go.AddComponent<Brick>();
        PrefabUtility.SaveAsPrefabAsset(go, BrickPrefabPath);
        Object.DestroyImmediate(go);
    }

    static void BuildScene(Sprite squareSprite, GameObject ballPrefab, GameObject brickPrefab)
    {
        var camera = Camera.main;
        if (camera != null)
        {
            camera.orthographic = true;
            camera.orthographicSize = 6f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.08f, 0.13f);
        }

        var walls = new GameObject("Walls");
        var wallColor = new Color(0.35f, 0.38f, 0.45f);
        CreateWall(walls.transform, "Left", new Vector3(-7.75f, 0f, 0f), new Vector3(0.5f, 13f, 1f), squareSprite, wallColor);
        CreateWall(walls.transform, "Right", new Vector3(7.75f, 0f, 0f), new Vector3(0.5f, 13f, 1f), squareSprite, wallColor);
        CreateWall(walls.transform, "Top", new Vector3(0f, 5.75f, 0f), new Vector3(16f, 0.5f, 1f), squareSprite, wallColor);

        var paddleGo = new GameObject("Paddle");
        paddleGo.transform.position = new Vector3(0f, -4.5f, 0f);
        paddleGo.transform.localScale = new Vector3(2f, 0.4f, 1f);
        var paddleRenderer = paddleGo.AddComponent<SpriteRenderer>();
        paddleRenderer.sprite = squareSprite;
        paddleRenderer.color = new Color(0.85f, 0.87f, 0.92f);
        paddleGo.AddComponent<BoxCollider2D>();
        var paddleBody = paddleGo.AddComponent<Rigidbody2D>();
        paddleBody.bodyType = RigidbodyType2D.Kinematic;
        var paddle = paddleGo.AddComponent<Paddle>();

        var managerGo = new GameObject("GameManager");
        var manager = managerGo.AddComponent<GameManager>();
        var so = new SerializedObject(manager);
        so.FindProperty("ballPrefab").objectReferenceValue = ballPrefab.GetComponent<Ball>();
        so.FindProperty("brickPrefab").objectReferenceValue = brickPrefab.GetComponent<Brick>();
        so.FindProperty("paddle").objectReferenceValue = paddle;
        so.ApplyModifiedPropertiesWithoutUndo();

        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static void BuildScoreBoard()
    {
        var canvasGo = new GameObject("ScoreBoard");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var board = canvasGo.AddComponent<ScoreBoard>();

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var scoreValue = CreateReadout(canvasGo.transform, "Score", "SCORE", 0f, 40f, TextAnchor.UpperLeft, font);
        var highScoreValue = CreateReadout(canvasGo.transform, "HighScore", "HIGH SCORE", 0.5f, 0f, TextAnchor.UpperCenter, font);
        var livesValue = CreateReadout(canvasGo.transform, "Lives", "LIVES", 1f, -40f, TextAnchor.UpperRight, font);

        var boardSo = new SerializedObject(board);
        boardSo.FindProperty("scoreValue").objectReferenceValue = scoreValue;
        boardSo.FindProperty("highScoreValue").objectReferenceValue = highScoreValue;
        boardSo.FindProperty("livesValue").objectReferenceValue = livesValue;
        boardSo.ApplyModifiedPropertiesWithoutUndo();

        var manager = Object.FindAnyObjectByType<GameManager>();
        var managerSo = new SerializedObject(manager);
        managerSo.FindProperty("scoreBoard").objectReferenceValue = board;
        managerSo.ApplyModifiedPropertiesWithoutUndo();

        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static void BuildRecordsPanel()
    {
        var canvas = GameObject.Find("ScoreBoard");
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var panelGo = new GameObject("RecordsPanel", typeof(RectTransform));
        var rect = panelGo.GetComponent<RectTransform>();
        rect.SetParent(canvas.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(760f, 560f);
        var background = panelGo.AddComponent<Image>();
        background.color = new Color(0.04f, 0.06f, 0.1f, 0.92f);
        var panel = panelGo.AddComponent<RecordsPanel>();

        var title = CreateText(panelGo.transform, "Title", "HALL OF FAME", 40, new Vector2(0.5f, 1f), new Vector2(0f, -36f), TextAnchor.UpperCenter, font, new Color(0.95f, 0.83f, 0.18f));
        var message = CreateText(panelGo.transform, "Message", "", 26, new Vector2(0.5f, 1f), new Vector2(0f, -100f), TextAnchor.UpperCenter, font, Color.white);
        message.rectTransform.sizeDelta = new Vector2(720f, 64f);
        var name = CreateText(panelGo.transform, "Name", "", 36, new Vector2(0.5f, 1f), new Vector2(0f, -168f), TextAnchor.UpperCenter, font, new Color(0.18f, 0.8f, 0.44f));
        var list = CreateText(panelGo.transform, "List", "", 26, new Vector2(0.5f, 1f), new Vector2(0f, -228f), TextAnchor.UpperCenter, font, new Color(0.85f, 0.87f, 0.92f));
        list.rectTransform.sizeDelta = new Vector2(720f, 300f);

        var panelSo = new SerializedObject(panel);
        panelSo.FindProperty("titleText").objectReferenceValue = title;
        panelSo.FindProperty("messageText").objectReferenceValue = message;
        panelSo.FindProperty("nameText").objectReferenceValue = name;
        panelSo.FindProperty("listText").objectReferenceValue = list;
        panelSo.ApplyModifiedPropertiesWithoutUndo();

        var manager = Object.FindAnyObjectByType<GameManager>();
        var managerSo = new SerializedObject(manager);
        managerSo.FindProperty("recordsPanel").objectReferenceValue = panel;
        managerSo.ApplyModifiedPropertiesWithoutUndo();

        panelGo.SetActive(false);

        // Mark dirty but let the user save: a programmatic SaveScene here raised
        // the modal "scene changed on disk" dialog and froze the Editor main thread.
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    // Builds a caption ("SCORE") with a value line under it, anchored to the top
    // edge of the canvas, and returns the value Text for runtime updates.
    static Text CreateReadout(Transform parent, string name, string caption, float anchorX, float offsetX, TextAnchor alignment, Font font)
    {
        CreateText(parent, name + "Caption", caption, 26, new Vector2(anchorX, 1f), new Vector2(offsetX, -20f), alignment, font, new Color(0.62f, 0.66f, 0.75f));
        return CreateText(parent, name + "Value", "0", 42, new Vector2(anchorX, 1f), new Vector2(offsetX, -52f), alignment, font, Color.white);
    }

    static Text CreateText(Transform parent, string name, string content, int fontSize, Vector2 anchor, Vector2 offset, TextAnchor alignment, Font font, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = offset;
        rect.sizeDelta = new Vector2(420f, 48f);
        var text = go.AddComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = alignment;
        text.color = color;
        return text;
    }

    static void CreateWall(Transform parent, string name, Vector3 position, Vector3 scale, Sprite sprite, Color color)
    {
        var wall = new GameObject(name);
        wall.transform.SetParent(parent);
        wall.transform.position = position;
        wall.transform.localScale = scale;
        var renderer = wall.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        wall.AddComponent<BoxCollider2D>();
    }
}
