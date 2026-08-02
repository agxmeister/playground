using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Builds the static, editable parts of the worm game (camera, walls,
// Segment/Food prefabs, the WormGame GameObject) directly into the scene
// the first time it's opened, so they show up in the Hierarchy and can be
// tweaked in the Inspector without entering Play mode. The moving worm
// body itself is still created at runtime by WormGame.cs, since it's
// gameplay state that doesn't exist until the game runs.
//
// Setup runs in resumable stages, one asset-creation step per domain reload:
// AssetDatabase writes made here are not reliably readable back via
// LoadAssetAtPath within the same call in every Editor session, even with
// ForceSynchronousImport. They are reliably readable on the *next*
// [InitializeOnLoadMethod] invocation (the next compile/reload), so each
// stage creates one thing and returns, letting the following reload pick it
// up and proceed.
public static class WormGameEditorSetup
{
    const string SpritesFolder = "Assets/Sprites";
    const string TexturePath = SpritesFolder + "/SquareTexture.asset";
    const string SpritePath = SpritesFolder + "/Square.asset";
    const string PrefabsFolder = "Assets/Prefabs";
    const string SegmentPrefabPath = PrefabsFolder + "/Segment.prefab";
    const string FoodPrefabPath = PrefabsFolder + "/Food.prefab";
    const float WallThickness = 0.3f;
    const int SpriteTextureSize = 4;

    [InitializeOnLoadMethod]
    static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        if (!FolderExists(SpritesFolder))
        {
            CreateFolder(SpritesFolder);
            EditorUtility.RequestScriptReload();
            return;
        }

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        if (sprite == null)
        {
            CreateSpriteAsset();
            EditorUtility.RequestScriptReload();
            return;
        }

        if (!FolderExists(PrefabsFolder))
        {
            CreateFolder(PrefabsFolder);
            EditorUtility.RequestScriptReload();
            return;
        }

        var segmentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SegmentPrefabPath);
        if (segmentPrefab == null)
        {
            CreatePrefab(SegmentPrefabPath, "Segment", new Color(0.3f, 0.85f, 0.3f), sprite);
            EditorUtility.RequestScriptReload();
            return;
        }

        var foodPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodPrefabPath);
        if (foodPrefab == null)
        {
            CreatePrefab(FoodPrefabPath, "Food", new Color(0.9f, 0.25f, 0.25f), sprite);
            EditorUtility.RequestScriptReload();
            return;
        }

        if (Object.FindAnyObjectByType<WormGame>() != null) return;

        EnsureCamera();
        EnsureBorder(sprite);
        EnsureWormGameObject(segmentPrefab, foodPrefab);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    // AssetDatabase.IsValidFolder/CreateFolder were not reliably in sync with the
    // actual filesystem across separate calls in this Editor session, leading to
    // repeated duplicate-folder creation ("Sprites 1", "Sprites 2", ...). Checking
    // and creating folders directly on disk, then letting AssetDatabase.Refresh
    // pick them up, sidesteps that.
    static string ToAbsolutePath(string assetsRelativePath) =>
        Application.dataPath + assetsRelativePath.Substring("Assets".Length);

    static bool FolderExists(string assetsRelativePath) => Directory.Exists(ToAbsolutePath(assetsRelativePath));

    static void CreateFolder(string assetsRelativePath)
    {
        Directory.CreateDirectory(ToAbsolutePath(assetsRelativePath));
        AssetDatabase.Refresh();
    }

    // Creates the sprite as a directly-serialized asset (Sprite.Create + CreateAsset)
    // rather than importing a PNG through a TextureImporter, since the importer
    // pipeline is asynchronous in some Editor sessions. Texture and sprite are saved
    // as two independent assets rather than sprite-as-sub-asset-of-texture, since
    // AddObjectToAsset did not reliably persist the sub-asset here either.
    static void CreateSpriteAsset()
    {
        var texture = new Texture2D(SpriteTextureSize, SpriteTextureSize, TextureFormat.RGBA32, false);
        texture.name = "SquareTexture";
        texture.filterMode = FilterMode.Point;
        var pixels = new Color[texture.width * texture.height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        texture.SetPixels(pixels);
        texture.Apply();

        AssetDatabase.CreateAsset(texture, TexturePath);

        // Match pixels-per-unit to the texture size so the sprite's native size is
        // exactly 1 world unit (matching prefab/wall transforms authored in world units).
        var sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            SpriteTextureSize);
        sprite.name = "Square";

        AssetDatabase.CreateAsset(sprite, SpritePath);
        AssetDatabase.SaveAssets();
    }

    static void CreatePrefab(string path, string name, Color color, Sprite sprite)
    {
        var go = new GameObject(name);
        go.transform.localScale = Vector3.one * WormGameDefaults.CellSize * 0.9f;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;

        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }

    static void EnsureCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
        }

        cam.orthographic = true;
        cam.orthographicSize = WormGameDefaults.GridHeight / 2f + 1f;
        cam.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
        cam.transform.position = new Vector3(
            (WormGameDefaults.GridWidth - 1) * WormGameDefaults.CellSize / 2f,
            (WormGameDefaults.GridHeight - 1) * WormGameDefaults.CellSize / 2f,
            -10f);
    }

    static void EnsureBorder(Sprite sprite)
    {
        if (GameObject.Find("Borders") != null) return;

        float cell = WormGameDefaults.CellSize;
        float minX = -cell / 2f;
        float maxX = (WormGameDefaults.GridWidth - 1) * cell + cell / 2f;
        float minY = -cell / 2f;
        float maxY = (WormGameDefaults.GridHeight - 1) * cell + cell / 2f;
        float centerX = (minX + maxX) / 2f;
        float centerY = (minY + maxY) / 2f;
        float width = maxX - minX;
        float height = maxY - minY;
        var wallColor = new Color(0.9f, 0.9f, 0.95f);

        var parent = new GameObject("Borders");

        CreateWall(parent.transform, sprite, "Wall_Bottom", wallColor,
            new Vector3(centerX, minY - WallThickness / 2f, 0f), new Vector2(width + WallThickness * 2f, WallThickness));
        CreateWall(parent.transform, sprite, "Wall_Top", wallColor,
            new Vector3(centerX, maxY + WallThickness / 2f, 0f), new Vector2(width + WallThickness * 2f, WallThickness));
        CreateWall(parent.transform, sprite, "Wall_Left", wallColor,
            new Vector3(minX - WallThickness / 2f, centerY, 0f), new Vector2(WallThickness, height + WallThickness * 2f));
        CreateWall(parent.transform, sprite, "Wall_Right", wallColor,
            new Vector3(maxX + WallThickness / 2f, centerY, 0f), new Vector2(WallThickness, height + WallThickness * 2f));
    }

    static void CreateWall(Transform parent, Sprite sprite, string name, Color color, Vector3 position, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = position;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = -1;
    }

    static void EnsureWormGameObject(GameObject segmentPrefab, GameObject foodPrefab)
    {
        var go = new GameObject("WormGame");
        var wormGame = go.AddComponent<WormGame>();

        var so = new SerializedObject(wormGame);
        so.FindProperty("segmentPrefab").objectReferenceValue = segmentPrefab;
        so.FindProperty("foodPrefab").objectReferenceValue = foodPrefab;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
