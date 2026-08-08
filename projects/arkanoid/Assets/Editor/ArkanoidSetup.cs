using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Builds the Arkanoid assets and scene as a resumable state machine: each stage
// (currently eighteen of them, ending with the 3D-look retrofit)
// creates one batch of assets and returns, letting the next domain reload see
// the result before the following stage runs. Safe to run on every reload —
// once everything exists it is a no-op.
public static class ArkanoidSetup
{
    const string SpritesFolder = "Assets/Sprites";
    const string PrefabsFolder = "Assets/Prefabs";
    const string PhysicsFolder = "Assets/Physics";
    const string MaterialsFolder = "Assets/Materials";
    const string BallMaterialPath = MaterialsFolder + "/Ball.mat";
    const string BrickMaterialPath = MaterialsFolder + "/Brick.mat";
    const string PaddleMaterialPath = MaterialsFolder + "/Paddle.mat";
    const string WallMaterialPath = MaterialsFolder + "/Wall.mat";
    const string BackdropMaterialPath = MaterialsFolder + "/Backdrop.mat";
    const string SquareTexturePath = SpritesFolder + "/Square.png";
    const string CircleTexturePath = SpritesFolder + "/Circle.png";
    const string CrackLightTexturePath = SpritesFolder + "/CrackLight.png";
    const string CrackHeavyTexturePath = SpritesFolder + "/CrackHeavy.png";
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
            return;
        }

        // Stage 9: main menu panel (title screen). Authored inactive, like the
        // records panel, so the guard must include inactive objects.
        if (Object.FindAnyObjectByType<MainMenuPanel>(FindObjectsInactive.Include) == null)
        {
            BuildMainMenu();
            Debug.Log("[ArkanoidSetup] Stage 9: built the main menu panel (scene left dirty).");
            return;
        }

        // Stage 10: persist stage 9's panel, with the same tick-deferred save
        // as stage 8.
        if (!File.ReadAllText(ToAbsolute(scene.path)).Contains("MainMenuPanel"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 10: queued scene save for the next editor tick.");
            return;
        }

        // Stage 11: crack overlay textures on disk.
        if (!File.Exists(ToAbsolute(CrackLightTexturePath)) || !File.Exists(ToAbsolute(CrackHeavyTexturePath)))
        {
            WriteCrackTextures();
            AssetDatabase.Refresh();
            Debug.Log("[ArkanoidSetup] Stage 11: wrote crack textures.");
            return;
        }

        // Stage 12: import crack textures as 1-unit sprites.
        bool crackLightReady = ConfigureSpriteImporter(CrackLightTexturePath, 32);
        bool crackHeavyReady = ConfigureSpriteImporter(CrackHeavyTexturePath, 32);
        if (!crackLightReady || !crackHeavyReady)
        {
            Debug.Log("[ArkanoidSetup] Stage 12: configured crack sprite importers.");
            return;
        }

        // Stage 13: crack overlay child on the brick prefab, with the crack
        // sprites wired into the Brick component.
        var brickPrefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(BrickPrefabPath);
        if (brickPrefabRoot != null && brickPrefabRoot.transform.Find("Cracks") == null)
        {
            var crackLight = AssetDatabase.LoadAssetAtPath<Sprite>(CrackLightTexturePath);
            var crackHeavy = AssetDatabase.LoadAssetAtPath<Sprite>(CrackHeavyTexturePath);
            if (crackLight == null || crackHeavy == null)
            {
                Debug.Log("[ArkanoidSetup] Crack sprites not importable yet, waiting for next reload.");
                return;
            }
            AddCracksToBrickPrefab(crackLight, crackHeavy);
            Debug.Log("[ArkanoidSetup] Stage 13: added the crack overlay to the brick prefab.");
            return;
        }

        // Stages 14-18 retrofit a 3D look onto the 2D game: URP Lit materials,
        // mesh visuals on the prefabs and scene objects, a perspective camera
        // and a shadow-catching backdrop. Physics and gameplay stay 2D.

        // Stage 14: shared materials.
        if (!File.Exists(ToAbsolute(BallMaterialPath)) || !File.Exists(ToAbsolute(BrickMaterialPath))
            || !File.Exists(ToAbsolute(PaddleMaterialPath)) || !File.Exists(ToAbsolute(WallMaterialPath))
            || !File.Exists(ToAbsolute(BackdropMaterialPath)))
        {
            Directory.CreateDirectory(ToAbsolute(MaterialsFolder));
            CreateLitMaterial(BallMaterialPath, new Color(0.9f, 0.9f, 0.95f));
            CreateLitMaterial(BrickMaterialPath, Color.white); // tinted per brick at runtime
            CreateLitMaterial(PaddleMaterialPath, new Color(0.85f, 0.87f, 0.92f));
            CreateLitMaterial(WallMaterialPath, new Color(0.35f, 0.38f, 0.45f));
            CreateLitMaterial(BackdropMaterialPath, new Color(0.09f, 0.11f, 0.17f));
            Debug.Log("[ArkanoidSetup] Stage 14: created URP Lit materials.");
            return;
        }

        // Stage 15: ball prefab gets a sphere mesh instead of the circle sprite.
        var ballPrefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(BallPrefabPath);
        if (ballPrefabRoot != null && ballPrefabRoot.GetComponent<SpriteRenderer>() != null)
        {
            var ballMaterial = AssetDatabase.LoadAssetAtPath<Material>(BallMaterialPath);
            if (ballMaterial == null) return;
            ConvertPrefabToMesh(BallPrefabPath, "New-Sphere.fbx", ballMaterial, 0.4f);
            Debug.Log("[ArkanoidSetup] Stage 15: converted the ball prefab to a sphere mesh.");
            return;
        }

        // Stage 16: brick prefab gets a box mesh; the crack overlay child moves
        // onto the box's front face.
        if (brickPrefabRoot != null && brickPrefabRoot.GetComponent<SpriteRenderer>() != null)
        {
            var brickMaterial = AssetDatabase.LoadAssetAtPath<Material>(BrickMaterialPath);
            if (brickMaterial == null) return;
            ConvertPrefabToMesh(BrickPrefabPath, "Cube.fbx", brickMaterial, 0.6f);
            Debug.Log("[ArkanoidSetup] Stage 16: converted the brick prefab to a box mesh.");
            return;
        }

        // Stage 17: perspective camera, mesh paddle and walls, backdrop plane.
        var mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.orthographic)
        {
            var paddleMaterial = AssetDatabase.LoadAssetAtPath<Material>(PaddleMaterialPath);
            var wallMaterial = AssetDatabase.LoadAssetAtPath<Material>(WallMaterialPath);
            var backdropMaterial = AssetDatabase.LoadAssetAtPath<Material>(BackdropMaterialPath);
            if (paddleMaterial == null || wallMaterial == null || backdropMaterial == null) return;
            ConvertSceneTo3D(mainCamera, paddleMaterial, wallMaterial, backdropMaterial);
            Debug.Log("[ArkanoidSetup] Stage 17: converted the scene to the 3D look (scene left dirty).");
            return;
        }

        // Stage 18: persist stage 17, with the same tick-deferred save as
        // stages 8 and 10. The scene file still says "orthographic: 1" until
        // the perspective camera is saved.
        if (File.ReadAllText(ToAbsolute(scene.path)).Contains("orthographic: 1"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 18: queued scene save for the next editor tick.");
        }
    }

    // Runs once, on the first editor tick after the reload that registered it.
    static void SaveSceneOnce()
    {
        EditorApplication.update -= SaveSceneOnce;
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("[ArkanoidSetup] Saved the scene on a deferred editor tick.");
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

    // Two damage states drawn as jagged dark polylines on a transparent
    // background: the heavy texture extends the light one, so escalating
    // damage reads as the same crack spreading.
    static void WriteCrackTextures()
    {
        var lightPolylines = new[]
        {
            new[] { new Vector2Int(18, 31), new Vector2Int(14, 23), new Vector2Int(19, 15), new Vector2Int(13, 6) },
            new[] { new Vector2Int(14, 23), new Vector2Int(8, 19) },
        };
        var heavyPolylines = new[]
        {
            lightPolylines[0],
            new[] { new Vector2Int(14, 23), new Vector2Int(8, 19), new Vector2Int(3, 12) },
            new[] { new Vector2Int(19, 15), new Vector2Int(25, 11), new Vector2Int(29, 4) },
            new[] { new Vector2Int(13, 6), new Vector2Int(17, 0) },
            new[] { new Vector2Int(5, 31), new Vector2Int(9, 25), new Vector2Int(6, 18) },
            new[] { new Vector2Int(27, 30), new Vector2Int(23, 24), new Vector2Int(26, 18) },
        };
        WriteCrackTexture(CrackLightTexturePath, lightPolylines);
        WriteCrackTexture(CrackHeavyTexturePath, heavyPolylines);
    }

    static void WriteCrackTexture(string path, Vector2Int[][] polylines)
    {
        const int size = 32;
        var crackColor = new Color(0.03f, 0.04f, 0.07f, 0.85f);
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var clear = new Color(0f, 0f, 0f, 0f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                texture.SetPixel(x, y, clear);

        foreach (var polyline in polylines)
            for (int i = 0; i < polyline.Length - 1; i++)
                DrawLine(texture, polyline[i], polyline[i + 1], crackColor);

        texture.Apply();
        File.WriteAllBytes(ToAbsolute(path), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }

    static void DrawLine(Texture2D texture, Vector2Int from, Vector2Int to, Color color)
    {
        int steps = Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y));
        for (int i = 0; i <= steps; i++)
        {
            float t = steps == 0 ? 0f : (float)i / steps;
            int x = Mathf.RoundToInt(Mathf.Lerp(from.x, to.x, t));
            int y = Mathf.RoundToInt(Mathf.Lerp(from.y, to.y, t));
            texture.SetPixel(x, y, color);
        }
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

    // The child inherits the brick root's 1.5x0.5 scale, so the 1-unit crack
    // sprite stretches to cover the brick exactly. The renderer starts with no
    // sprite; Brick swaps in light/heavy crack sprites as damage accumulates.
    static void AddCracksToBrickPrefab(Sprite crackLight, Sprite crackHeavy)
    {
        var root = PrefabUtility.LoadPrefabContents(BrickPrefabPath);
        var cracksGo = new GameObject("Cracks");
        cracksGo.transform.SetParent(root.transform, false);
        var renderer = cracksGo.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = 1;

        var so = new SerializedObject(root.GetComponent<Brick>());
        so.FindProperty("crackRenderer").objectReferenceValue = renderer;
        so.FindProperty("lightCrackSprite").objectReferenceValue = crackLight;
        so.FindProperty("heavyCrackSprite").objectReferenceValue = crackHeavy;
        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, BrickPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    static void CreateLitMaterial(string path, Color color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(material, path);
    }

    // Swaps a prefab root's SpriteRenderer for a built-in mesh with a Lit
    // material and gives it real depth via the Z scale. Colliders stay 2D, so
    // gameplay is unaffected; the mesh is purely visual.
    static void ConvertPrefabToMesh(string prefabPath, string builtinMeshName, Material material, float depth)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        ConvertToMesh(root, builtinMeshName, material, depth);

        // Keep the crack overlay sprite visible on the box's front face.
        var cracks = root.transform.Find("Cracks");
        if (cracks != null) cracks.localPosition = new Vector3(0f, 0f, -0.51f);

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    static void ConvertToMesh(GameObject go, string builtinMeshName, Material material, float depth)
    {
        var spriteRenderer = go.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null) Object.DestroyImmediate(spriteRenderer);
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>(builtinMeshName);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        var scale = go.transform.localScale;
        scale.z = depth;
        go.transform.localScale = scale;
    }

    static void ConvertSceneTo3D(Camera camera, Material paddleMaterial, Material wallMaterial, Material backdropMaterial)
    {
        // Same framing as the old orthographic size 6: half-height at the
        // gameplay plane is distance * tan(fov/2) = 13.5 * tan(25) ~ 6.3.
        camera.orthographic = false;
        camera.fieldOfView = 50f;
        camera.transform.position = new Vector3(0f, 0f, -13.5f);

        ConvertToMesh(GameObject.Find("Paddle"), "Cube.fbx", paddleMaterial, 0.5f);
        foreach (var wallName in new[] { "Left", "Right", "Top" })
            ConvertToMesh(GameObject.Find(wallName), "Cube.fbx", wallMaterial, 0.6f);

        // A plane behind the playfield that catches the objects' shadows —
        // the main depth cue. No collider: it is scenery only.
        var backdrop = new GameObject("Backdrop");
        backdrop.transform.position = new Vector3(0f, 0f, 0.6f);
        backdrop.transform.localScale = new Vector3(17f, 14f, 0.2f);
        backdrop.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        backdrop.AddComponent<MeshRenderer>().sharedMaterial = backdropMaterial;

        // The URP template scene ships with a directional light; create one
        // only if this scene somehow lacks it.
        if (Object.FindAnyObjectByType<Light>(FindObjectsInactive.Include) == null)
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
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

    static void BuildMainMenu()
    {
        var canvas = GameObject.Find("ScoreBoard");
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var panelGo = new GameObject("MainMenuPanel", typeof(RectTransform));
        var rect = panelGo.GetComponent<RectTransform>();
        rect.SetParent(canvas.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var background = panelGo.AddComponent<Image>();
        background.color = new Color(0.04f, 0.06f, 0.1f, 0.97f);
        var menu = panelGo.AddComponent<MainMenuPanel>();

        var title = CreateText(panelGo.transform, "Title", "ARKANOID", 96, new Vector2(0.5f, 1f), new Vector2(0f, -260f), TextAnchor.UpperCenter, font, new Color(0.95f, 0.83f, 0.18f));
        title.rectTransform.sizeDelta = new Vector2(900f, 110f);
        var highScore = CreateText(panelGo.transform, "HighScore", "", 30, new Vector2(0.5f, 1f), new Vector2(0f, -410f), TextAnchor.UpperCenter, font, new Color(0.85f, 0.87f, 0.92f));
        highScore.rectTransform.sizeDelta = new Vector2(720f, 48f);
        var prompts = CreateText(panelGo.transform, "Prompts", "SPACE — start game\nR — hall of fame", 30, new Vector2(0.5f, 1f), new Vector2(0f, -510f), TextAnchor.UpperCenter, font, new Color(0.18f, 0.8f, 0.44f));
        prompts.rectTransform.sizeDelta = new Vector2(720f, 120f);

        var menuSo = new SerializedObject(menu);
        menuSo.FindProperty("highScoreText").objectReferenceValue = highScore;
        menuSo.ApplyModifiedPropertiesWithoutUndo();

        var manager = Object.FindAnyObjectByType<GameManager>();
        var managerSo = new SerializedObject(manager);
        managerSo.FindProperty("mainMenuPanel").objectReferenceValue = menu;
        managerSo.ApplyModifiedPropertiesWithoutUndo();

        panelGo.SetActive(false);

        // Mark dirty but defer the save to stage 10: a programmatic SaveScene
        // here raised the modal "scene changed on disk" dialog before.
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
