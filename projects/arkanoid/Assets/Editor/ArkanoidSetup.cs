using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        if (GameObject.Find("GameManager") != null) return;

        var ballPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BallPrefabPath);
        var brickPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BrickPrefabPath);
        if (ballPrefab == null || brickPrefab == null) return;

        BuildScene(squareSprite, ballPrefab, brickPrefab);
        Debug.Log("[ArkanoidSetup] Stage 5: built and saved the scene.");
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
