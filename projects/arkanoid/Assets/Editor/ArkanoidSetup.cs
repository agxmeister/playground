using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Builds the Arkanoid assets and scene as a resumable state machine: each stage
// (currently thirty-nine of them, ending with the brick shape variants)
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
    const string TexturesFolder = "Assets/Textures";
    const string BrickWallTexturePath = TexturesFolder + "/BrickWall.png";
    const string WallSideMaterialPath = MaterialsFolder + "/WallSide.mat";
    const string WallTopMaterialPath = MaterialsFolder + "/WallTop.mat";
    const string MeshesFolder = "Assets/Meshes";
    const string WallSideMeshPath = MeshesFolder + "/WallSide.asset";
    const string WallTopMeshPath = MeshesFolder + "/WallTop.asset";
    const string PaddleMeshPath = MeshesFolder + "/Paddle.asset";
    // The paddle's world size and the radius of its rounded corners. The
    // collider is a box shrunk by the radius on every side with edgeRadius
    // filling it back out, which is exactly the same rounded rectangle.
    const float PaddleWidth = 2f;
    const float PaddleHeight = 0.4f;
    const float PaddleDepth = 0.5f;
    const float PaddleCornerRadius = 0.15f;
    const int PaddleCornerSegments = 8;
    const string SquareTexturePath = SpritesFolder + "/Square.png";
    const string CircleTexturePath = SpritesFolder + "/Circle.png";
    // Pre-variant crack textures, removed by the stage-28 retrofit.
    const string LegacyCrackLightTexturePath = SpritesFolder + "/CrackLight.png";
    const string LegacyCrackHeavyTexturePath = SpritesFolder + "/CrackHeavy.png";
    const int CrackVariantCount = 4;
    const string BouncyMaterialPath = PhysicsFolder + "/Bouncy.physicsMaterial2D";
    const string BallPrefabPath = PrefabsFolder + "/Ball.prefab";
    const string BrickPrefabPath = PrefabsFolder + "/Brick.prefab";
    // Brick shape variants: each collider matches its visual outline exactly,
    // so the ball's reflection off any brick uses the shape's true normal.
    const string HalfBrickPrefabPath = PrefabsFolder + "/HalfBrick.prefab";
    const string RoundedBrickPrefabPath = PrefabsFolder + "/RoundedBrick.prefab";
    const string RoundBrickPrefabPath = PrefabsFolder + "/RoundBrick.prefab";
    const string RoundedBrickMeshPath = MeshesFolder + "/BrickRounded.asset";
    const float BrickWidth = 1.5f;
    const float BrickHeight = 0.5f;
    const float BrickDepth = 0.6f;
    // Two half bricks plus the level's 0.14 gap fill one normal slot: 2 * 0.68 + 0.14 = 1.5.
    const float HalfBrickWidth = 0.68f;
    const float RoundedBrickCornerRadius = 0.12f;
    const float RoundBrickDiameter = 0.5f;

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

        // Stage 11: crack overlay textures on disk, one light/heavy pair per
        // random shape variant.
        bool crackTexturesMissing = false;
        for (int variant = 0; variant < CrackVariantCount; variant++)
            crackTexturesMissing |= !File.Exists(ToAbsolute(CrackTexturePath("Light", variant)))
                || !File.Exists(ToAbsolute(CrackTexturePath("Heavy", variant)));
        if (crackTexturesMissing)
        {
            WriteCrackTextures();
            AssetDatabase.Refresh();
            Debug.Log("[ArkanoidSetup] Stage 11: wrote the crack texture variants.");
            return;
        }

        // Stage 12: import crack textures as 1-unit sprites.
        bool cracksImported = true;
        for (int variant = 0; variant < CrackVariantCount; variant++)
        {
            cracksImported &= ConfigureSpriteImporter(CrackTexturePath("Light", variant), 32);
            cracksImported &= ConfigureSpriteImporter(CrackTexturePath("Heavy", variant), 32);
        }
        if (!cracksImported)
        {
            Debug.Log("[ArkanoidSetup] Stage 12: configured crack sprite importers.");
            return;
        }

        // Stage 13: crack overlay child on the brick prefab, with the crack
        // sprite variants wired into the Brick component.
        var brickPrefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(BrickPrefabPath);
        if (brickPrefabRoot != null && brickPrefabRoot.transform.Find("Cracks") == null)
        {
            var lightCracks = LoadCrackSprites("Light");
            var heavyCracks = LoadCrackSprites("Heavy");
            if (lightCracks == null || heavyCracks == null)
            {
                Debug.Log("[ArkanoidSetup] Crack sprites not importable yet, waiting for next reload.");
                return;
            }
            AddCracksToBrickPrefab(lightCracks, heavyCracks);
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
            return;
        }

        // Stage 19: shorten the side walls so they butt against the top
        // wall's underside (y 5.5) instead of overlapping it in the corners —
        // coplanar overlap z-fights and shows through. 12 units tall keeps a
        // whole number of 0.25-unit brick rows, so the side material's
        // vertical tiling follows suit when it already exists.
        var leftWall = GameObject.Find("Left");
        if (leftWall != null && leftWall.transform.localScale.y > 12.1f)
        {
            foreach (var wallName in new[] { "Left", "Right" })
            {
                var wall = GameObject.Find(wallName).transform;
                wall.position = new Vector3(wall.position.x, -0.5f, wall.position.z);
                wall.localScale = new Vector3(wall.localScale.x, 12f, wall.localScale.z);
            }
            var sideMaterial = AssetDatabase.LoadAssetAtPath<Material>(WallSideMaterialPath);
            if (sideMaterial != null)
            {
                sideMaterial.SetTextureScale("_BaseMap", new Vector2(0.5f, 12f));
                EditorUtility.SetDirty(sideMaterial);
                AssetDatabase.SaveAssets();
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 19: butted the side walls under the top wall (scene left dirty).");
            return;
        }

        // Stage 20: persist stage 19, gated on the old wall scale still being
        // in the scene file.
        if (File.ReadAllText(ToAbsolute(scene.path)).Contains("{x: 0.5, y: 13, z: 0.6}"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 20: queued scene save for the next editor tick.");
            return;
        }

        // Stage 21: tileable brick-masonry texture for the walls.
        if (!File.Exists(ToAbsolute(BrickWallTexturePath)))
        {
            WriteBrickWallTexture();
            AssetDatabase.Refresh();
            Debug.Log("[ArkanoidSetup] Stage 21: wrote the brick wall texture.");
            return;
        }

        // Stage 22: textured wall materials. Tiling stays 1:1 — the wall
        // meshes carry world-unit UVs (stage 26), so the 1-unit texture maps
        // at brick scale on every face without per-material tiling.
        if (!File.Exists(ToAbsolute(WallSideMaterialPath)) || !File.Exists(ToAbsolute(WallTopMaterialPath)))
        {
            var brickWallTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(BrickWallTexturePath);
            if (brickWallTexture == null)
            {
                Debug.Log("[ArkanoidSetup] Brick wall texture not importable yet, waiting for next reload.");
                return;
            }
            CreateTexturedWallMaterial(WallSideMaterialPath, brickWallTexture, Vector2.one);
            CreateTexturedWallMaterial(WallTopMaterialPath, brickWallTexture, Vector2.one);
            Debug.Log("[ArkanoidSetup] Stage 22: created the textured wall materials.");
            return;
        }

        // Stage 23: swap the walls onto the textured materials.
        var leftWallRenderer = leftWall != null ? leftWall.GetComponent<MeshRenderer>() : null;
        if (leftWallRenderer != null && leftWallRenderer.sharedMaterial != null
            && leftWallRenderer.sharedMaterial.name == "Wall")
        {
            var wallSideMaterial = AssetDatabase.LoadAssetAtPath<Material>(WallSideMaterialPath);
            var wallTopMaterial = AssetDatabase.LoadAssetAtPath<Material>(WallTopMaterialPath);
            if (wallSideMaterial == null || wallTopMaterial == null) return;
            leftWallRenderer.sharedMaterial = wallSideMaterial;
            GameObject.Find("Right").GetComponent<MeshRenderer>().sharedMaterial = wallSideMaterial;
            GameObject.Find("Top").GetComponent<MeshRenderer>().sharedMaterial = wallTopMaterial;
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 23: applied the brick texture to the walls (scene left dirty).");
            return;
        }

        // Stage 24: persist stage 23, gated on the scene file not yet
        // referencing the side-wall material asset.
        if (!File.ReadAllText(ToAbsolute(scene.path)).Contains(AssetDatabase.AssetPathToGUID(WallSideMaterialPath)))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 24: queued scene save for the next editor tick.");
            return;
        }

        // Stage 25: wall meshes with world-unit UVs. A scaled stock cube maps
        // its material tiling onto all six faces, so small faces (the top
        // wall's end caps, the side walls' tops) show the texture squeezed
        // into stripes. These meshes are authored at final size with each
        // face's UVs equal to its world dimensions, so the 1-unit brick
        // texture is brick-scale on every face.
        if (!File.Exists(ToAbsolute(WallSideMeshPath)) || !File.Exists(ToAbsolute(WallTopMeshPath)))
        {
            Directory.CreateDirectory(ToAbsolute(MeshesFolder));
            AssetDatabase.CreateAsset(BuildWallMesh("WallSide", 0.5f, 12f, 0.6f), WallSideMeshPath);
            AssetDatabase.CreateAsset(BuildWallMesh("WallTop", 16f, 0.5f, 0.6f), WallTopMeshPath);
            Debug.Log("[ArkanoidSetup] Stage 25: created the wall meshes with world-unit UVs.");
            return;
        }

        // Stage 26: swap the walls onto the world-UV meshes. The meshes are
        // authored at final size, so the transforms drop to unit scale and the
        // 2D colliders get their sizes set explicitly (they no longer inherit
        // them from the scale). Also resets the materials to 1:1 tiling.
        var leftWallFilter = leftWall != null ? leftWall.GetComponent<MeshFilter>() : null;
        if (leftWallFilter != null && leftWallFilter.sharedMesh != null && leftWallFilter.sharedMesh.name == "Cube")
        {
            var sideMesh = AssetDatabase.LoadAssetAtPath<Mesh>(WallSideMeshPath);
            var topMesh = AssetDatabase.LoadAssetAtPath<Mesh>(WallTopMeshPath);
            if (sideMesh == null || topMesh == null) return;

            foreach (var wallName in new[] { "Left", "Right" })
                ApplyWallMesh(GameObject.Find(wallName), sideMesh, new Vector2(0.5f, 12f));
            ApplyWallMesh(GameObject.Find("Top"), topMesh, new Vector2(16f, 0.5f));

            foreach (var materialPath in new[] { WallSideMaterialPath, WallTopMaterialPath })
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                material.SetTextureScale("_BaseMap", Vector2.one);
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 26: applied the world-UV meshes to the walls (scene left dirty).");
            return;
        }

        // Stage 27: persist stage 26, gated on the old scaled-cube wall
        // transform still being in the scene file.
        if (File.ReadAllText(ToAbsolute(scene.path)).Contains("{x: 0.5, y: 12, z: 0.6}"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 27: queued scene save for the next editor tick.");
            return;
        }

        // Stage 28: randomized-crack retrofit — the brick prefab predates the
        // per-variant sprite arrays on Brick, so wire them in and drop the old
        // single light/heavy texture pair.
        if (brickPrefabRoot != null && brickPrefabRoot.transform.Find("Cracks") != null
            && new SerializedObject(brickPrefabRoot.GetComponent<Brick>())
                .FindProperty("lightCrackSprites").arraySize == 0)
        {
            var lightCracks = LoadCrackSprites("Light");
            var heavyCracks = LoadCrackSprites("Heavy");
            if (lightCracks == null || heavyCracks == null)
            {
                Debug.Log("[ArkanoidSetup] Crack sprites not importable yet, waiting for next reload.");
                return;
            }
            WireCrackSpritesIntoBrickPrefab(lightCracks, heavyCracks);
            AssetDatabase.DeleteAsset(LegacyCrackLightTexturePath);
            AssetDatabase.DeleteAsset(LegacyCrackHeavyTexturePath);
            Debug.Log("[ArkanoidSetup] Stage 28: wired the crack variants into the brick prefab.");
            return;
        }

        // Stage 29: rounded-corner paddle mesh, authored at final size like
        // the wall meshes.
        if (!File.Exists(ToAbsolute(PaddleMeshPath)))
        {
            Directory.CreateDirectory(ToAbsolute(MeshesFolder));
            AssetDatabase.CreateAsset(
                BuildRoundedPrismMesh("Paddle", PaddleWidth, PaddleHeight, PaddleDepth, PaddleCornerRadius, PaddleCornerSegments),
                PaddleMeshPath);
            Debug.Log("[ArkanoidSetup] Stage 29: created the rounded paddle mesh.");
            return;
        }

        // Stage 30: swap the paddle onto the rounded mesh and round its
        // collider to match, so the ball's bounce reflects off the corner
        // curve's true normal. The mesh is authored at final size, so the
        // transform drops to unit scale and the collider is sized explicitly,
        // like the walls in stage 26.
        var paddleGo = GameObject.Find("Paddle");
        var paddleFilter = paddleGo != null ? paddleGo.GetComponent<MeshFilter>() : null;
        if (paddleFilter != null && paddleFilter.sharedMesh != null && paddleFilter.sharedMesh.name == "Cube")
        {
            var paddleMesh = AssetDatabase.LoadAssetAtPath<Mesh>(PaddleMeshPath);
            if (paddleMesh == null) return;
            paddleFilter.sharedMesh = paddleMesh;
            paddleGo.transform.localScale = Vector3.one;
            var paddleCollider = paddleGo.GetComponent<BoxCollider2D>();
            paddleCollider.size = new Vector2(
                PaddleWidth - 2f * PaddleCornerRadius, PaddleHeight - 2f * PaddleCornerRadius);
            paddleCollider.edgeRadius = PaddleCornerRadius;
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 30: rounded the paddle's corners (scene left dirty).");
            return;
        }

        // Stage 31: persist stage 30, with the same tick-deferred save as
        // stages 8 and 10, gated on the scene file not yet referencing the
        // paddle mesh asset.
        if (!File.ReadAllText(ToAbsolute(scene.path)).Contains(AssetDatabase.AssetPathToGUID(PaddleMeshPath)))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 31: queued scene save for the next editor tick.");
            return;
        }

        // Stage 32: center the directional light. The URP template authored
        // it at (0, 3, 0); move it to the scene origin. Position doesn't
        // affect a directional light's illumination (only rotation does), so
        // this is purely about where its gizmo sits in the hierarchy/scene
        // view. A from-scratch rebuild already creates the light at the
        // origin, so this stage only fires on the template-authored scene.
        var sceneLight = Object.FindAnyObjectByType<Light>(FindObjectsInactive.Include);
        if (sceneLight != null && sceneLight.transform.position != Vector3.zero)
        {
            sceneLight.transform.position = Vector3.zero;
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 32: moved the light to the scene center (scene left dirty).");
            return;
        }

        // Stage 33: persist stage 32, with the same tick-deferred save as the
        // earlier save stages, gated on the scene file still holding the
        // light's old template position.
        if (File.ReadAllText(ToAbsolute(scene.path)).Contains("m_LocalPosition: {x: 0, y: 3, z: 0}"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 33: queued scene save for the next editor tick.");
            return;
        }

        // Stage 34: center the light *direction*. The template's -30° yaw
        // lit the playfield from the top right, which is what actually shows
        // up in the shadows (a directional light only has a direction). Drop
        // the yaw so the light faces the playfield head-on horizontally,
        // keeping the 50° downward tilt so the shadows — the main depth cue —
        // stay visible below the objects instead of hiding behind them.
        if (sceneLight != null && Mathf.Abs(sceneLight.transform.forward.x) > 0.001f)
        {
            sceneLight.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 34: centered the light's direction (scene left dirty).");
            return;
        }

        // Stage 35: persist stage 34, with the same tick-deferred save as the
        // earlier save stages, gated on the scene file still holding the old
        // yawed rotation quaternion.
        if (File.ReadAllText(ToAbsolute(scene.path)).Contains("m_LocalRotation: {x: 0.40821788"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 35: queued scene save for the next editor tick.");
            return;
        }

        // Stages 36-39 add the brick shape variants: a half-width brick, a
        // rounded-corner brick and a round brick. Each prefab's 2D collider
        // matches its visual outline exactly, so the ball reflects off the
        // shape's true contact normal (flat face, corner curve, or circle).

        // Stage 36: rounded-corner brick mesh, the same rounded-rectangle
        // prism as the paddle, authored at final size.
        if (!File.Exists(ToAbsolute(RoundedBrickMeshPath)))
        {
            Directory.CreateDirectory(ToAbsolute(MeshesFolder));
            AssetDatabase.CreateAsset(
                BuildRoundedPrismMesh("BrickRounded", BrickWidth, BrickHeight, BrickDepth,
                    RoundedBrickCornerRadius, PaddleCornerSegments),
                RoundedBrickMeshPath);
            Debug.Log("[ArkanoidSetup] Stage 36: created the rounded brick mesh.");
            return;
        }

        // Stage 37: the three brick-variant prefabs.
        if (!File.Exists(ToAbsolute(HalfBrickPrefabPath)) || !File.Exists(ToAbsolute(RoundedBrickPrefabPath))
            || !File.Exists(ToAbsolute(RoundBrickPrefabPath)))
        {
            var brickMaterial = AssetDatabase.LoadAssetAtPath<Material>(BrickMaterialPath);
            var roundedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(RoundedBrickMeshPath);
            var lightCracks = LoadCrackSprites("Light");
            var heavyCracks = LoadCrackSprites("Heavy");
            if (brickMaterial == null || roundedMesh == null || lightCracks == null || heavyCracks == null)
            {
                Debug.Log("[ArkanoidSetup] Brick variant dependencies not loadable yet, waiting for next reload.");
                return;
            }
            CreateHalfBrickPrefab(brickMaterial, lightCracks, heavyCracks);
            CreateRoundedBrickPrefab(brickMaterial, roundedMesh, lightCracks, heavyCracks);
            CreateRoundBrickPrefab(brickMaterial, lightCracks, heavyCracks);
            Debug.Log("[ArkanoidSetup] Stage 37: created the brick variant prefabs.");
            return;
        }

        // Stage 38: wire the variant prefabs into the GameManager.
        var gameManager = Object.FindAnyObjectByType<GameManager>();
        if (gameManager != null)
        {
            var managerSo = new SerializedObject(gameManager);
            if (managerSo.FindProperty("halfBrickPrefab").objectReferenceValue == null)
            {
                var halfBrick = AssetDatabase.LoadAssetAtPath<GameObject>(HalfBrickPrefabPath);
                var roundedBrick = AssetDatabase.LoadAssetAtPath<GameObject>(RoundedBrickPrefabPath);
                var roundBrick = AssetDatabase.LoadAssetAtPath<GameObject>(RoundBrickPrefabPath);
                if (halfBrick == null || roundedBrick == null || roundBrick == null) return;
                managerSo.FindProperty("halfBrickPrefab").objectReferenceValue = halfBrick.GetComponent<Brick>();
                managerSo.FindProperty("roundedBrickPrefab").objectReferenceValue = roundedBrick.GetComponent<Brick>();
                managerSo.FindProperty("roundBrickPrefab").objectReferenceValue = roundBrick.GetComponent<Brick>();
                managerSo.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                Debug.Log("[ArkanoidSetup] Stage 38: wired the brick variants into the GameManager (scene left dirty).");
                return;
            }
        }

        // Stage 39: persist stage 38, with the same tick-deferred save as the
        // earlier save stages, gated on the scene file not yet referencing the
        // half-brick prefab asset.
        if (!File.ReadAllText(ToAbsolute(scene.path)).Contains(AssetDatabase.AssetPathToGUID(HalfBrickPrefabPath)))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 39: queued scene save for the next editor tick.");
            return;
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

    static string CrackTexturePath(string weight, int variant) =>
        SpritesFolder + "/Crack" + weight + variant + ".png";

    // Each variant is a light/heavy pair of jagged dark polylines on a
    // transparent background, generated from a per-variant seeded random walk
    // so every variant has its own shape while a from-scratch rebuild
    // reproduces the same set. The heavy texture extends the light one, so
    // escalating damage reads as the same crack spreading.
    static void WriteCrackTextures()
    {
        for (int variant = 0; variant < CrackVariantCount; variant++)
        {
            var random = new System.Random(2026 + variant);
            var polylines = new List<Vector2Int[]>();

            // Light: one main crack running down from the top edge, plus a
            // short side branch.
            var main = RandomCrackPolyline(random, new Vector2Int(random.Next(9, 24), 31), random.Next(3, 5));
            polylines.Add(main);
            polylines.Add(RandomCrackBranch(random, main));
            WriteCrackTexture(CrackTexturePath("Light", variant), polylines.ToArray());

            // Heavy: two more branches off the main crack plus an independent
            // secondary crack from the top or bottom edge.
            polylines.Add(RandomCrackBranch(random, main));
            polylines.Add(RandomCrackBranch(random, main));
            bool fromBottom = random.Next(2) == 0;
            polylines.Add(RandomCrackPolyline(
                random, new Vector2Int(random.Next(3, 29), fromBottom ? 0 : 31), 2, fromBottom));
            WriteCrackTexture(CrackTexturePath("Heavy", variant), polylines.ToArray());
        }
    }

    // A jagged crack path: from `start`, each segment drops 5-9 px (rises
    // instead when `up`) with a random horizontal swing.
    static Vector2Int[] RandomCrackPolyline(System.Random random, Vector2Int start, int segments, bool up = false)
    {
        var points = new Vector2Int[segments + 1];
        points[0] = start;
        var point = start;
        for (int i = 1; i <= segments; i++)
        {
            point = new Vector2Int(
                Mathf.Clamp(point.x + random.Next(-6, 7), 1, 30),
                Mathf.Clamp(point.y + (up ? 1 : -1) * random.Next(5, 10), 0, 31));
            points[i] = point;
        }
        return points;
    }

    // A short branch splitting sideways off a random interior vertex of the
    // parent crack.
    static Vector2Int[] RandomCrackBranch(System.Random random, Vector2Int[] parent)
    {
        var from = parent[random.Next(1, parent.Length - 1)];
        int direction = random.Next(2) == 0 ? -1 : 1;
        int segments = random.Next(1, 3);
        var points = new Vector2Int[segments + 1];
        points[0] = from;
        var point = from;
        for (int i = 1; i <= segments; i++)
        {
            point = new Vector2Int(
                Mathf.Clamp(point.x + direction * random.Next(4, 8), 1, 30),
                Mathf.Clamp(point.y - random.Next(-2, 6), 0, 31));
            points[i] = point;
        }
        return points;
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

    // Half-width brick: a scaled stock cube like the normal brick (the plain
    // color material makes the cube's UV squeeze irrelevant). The default
    // 1x1 BoxCollider2D inherits the transform scale, so flat-face impacts
    // reflect exactly like the normal brick's.
    static void CreateHalfBrickPrefab(Material material, Sprite[] lightCracks, Sprite[] heavyCracks)
    {
        var go = new GameObject("HalfBrick");
        go.transform.localScale = new Vector3(HalfBrickWidth, BrickHeight, BrickDepth);
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        go.AddComponent<BoxCollider2D>();
        go.AddComponent<Brick>();
        // The child inherits the root scale, so the 1-unit crack sprite covers
        // the brick; -0.51 lands just in front of the box face at z -0.5.
        AddCrackOverlay(go, new Vector3(0f, 0f, -0.51f), Vector3.one, lightCracks, heavyCracks);
        PrefabUtility.SaveAsPrefabAsset(go, HalfBrickPrefabPath);
        Object.DestroyImmediate(go);
    }

    // Rounded-corner brick: the mesh is authored at final size (unit-scale
    // transform, like the paddle), and the collider is a box shrunk by the
    // corner radius on every side with edgeRadius filling it back out — the
    // same rounded rectangle, so corner hits reflect off the curve's normal.
    static void CreateRoundedBrickPrefab(Material material, Mesh roundedMesh, Sprite[] lightCracks, Sprite[] heavyCracks)
    {
        var go = new GameObject("RoundedBrick");
        go.AddComponent<MeshFilter>().sharedMesh = roundedMesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        var collider = go.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(
            BrickWidth - 2f * RoundedBrickCornerRadius, BrickHeight - 2f * RoundedBrickCornerRadius);
        collider.edgeRadius = RoundedBrickCornerRadius;
        go.AddComponent<Brick>();
        AddCrackOverlay(go, new Vector3(0f, 0f, -BrickDepth / 2f - 0.01f),
            new Vector3(BrickWidth, BrickHeight, 1f), lightCracks, heavyCracks);
        PrefabUtility.SaveAsPrefabAsset(go, RoundedBrickPrefabPath);
        Object.DestroyImmediate(go);
    }

    // Round brick: a half-size sphere whose CircleCollider2D matches its
    // silhouette exactly, so the ball reflects off the circle's radial
    // normal — glancing hits deflect sideways instead of bouncing flat.
    static void CreateRoundBrickPrefab(Material material, Sprite[] lightCracks, Sprite[] heavyCracks)
    {
        var go = new GameObject("RoundBrick");
        go.transform.localScale = Vector3.one * RoundBrickDiameter;
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        go.AddComponent<CircleCollider2D>().radius = 0.5f;
        go.AddComponent<Brick>();
        // 0.7 ~ an inscribed square: keeps the square crack sprite's pixels
        // over the sphere's circular silhouette instead of floating past it.
        AddCrackOverlay(go, new Vector3(0f, 0f, -0.52f), Vector3.one * 0.7f, lightCracks, heavyCracks);
        PrefabUtility.SaveAsPrefabAsset(go, RoundBrickPrefabPath);
        Object.DestroyImmediate(go);
    }

    // Adds the Cracks overlay child and wires it (and the sprite variant
    // arrays) into the Brick component on a prefab under construction.
    static void AddCrackOverlay(GameObject root, Vector3 localPosition, Vector3 localScale,
        Sprite[] lightCracks, Sprite[] heavyCracks)
    {
        var cracksGo = new GameObject("Cracks");
        cracksGo.transform.SetParent(root.transform, false);
        cracksGo.transform.localPosition = localPosition;
        cracksGo.transform.localScale = localScale;
        var renderer = cracksGo.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = 1;

        var so = new SerializedObject(root.GetComponent<Brick>());
        so.FindProperty("crackRenderer").objectReferenceValue = renderer;
        SetCrackSpriteArrays(so, lightCracks, heavyCracks);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // All sprites of one weight ("Light"/"Heavy"), or null while any variant
    // is still unimportable.
    static Sprite[] LoadCrackSprites(string weight)
    {
        var sprites = new Sprite[CrackVariantCount];
        for (int variant = 0; variant < CrackVariantCount; variant++)
        {
            sprites[variant] = AssetDatabase.LoadAssetAtPath<Sprite>(CrackTexturePath(weight, variant));
            if (sprites[variant] == null) return null;
        }
        return sprites;
    }

    // The child inherits the brick root's 1.5x0.5 scale, so the 1-unit crack
    // sprite stretches to cover the brick exactly. The renderer starts with no
    // sprite; Brick swaps in a light/heavy crack variant as damage accumulates.
    static void AddCracksToBrickPrefab(Sprite[] lightCracks, Sprite[] heavyCracks)
    {
        var root = PrefabUtility.LoadPrefabContents(BrickPrefabPath);
        var cracksGo = new GameObject("Cracks");
        cracksGo.transform.SetParent(root.transform, false);
        var renderer = cracksGo.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = 1;

        var so = new SerializedObject(root.GetComponent<Brick>());
        so.FindProperty("crackRenderer").objectReferenceValue = renderer;
        SetCrackSpriteArrays(so, lightCracks, heavyCracks);
        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, BrickPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    // Stage-28 retrofit path: the Cracks child already exists, only the
    // variant arrays need wiring.
    static void WireCrackSpritesIntoBrickPrefab(Sprite[] lightCracks, Sprite[] heavyCracks)
    {
        var root = PrefabUtility.LoadPrefabContents(BrickPrefabPath);
        var so = new SerializedObject(root.GetComponent<Brick>());
        SetCrackSpriteArrays(so, lightCracks, heavyCracks);
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(root, BrickPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    static void SetCrackSpriteArrays(SerializedObject so, Sprite[] lightCracks, Sprite[] heavyCracks)
    {
        foreach (var (field, sprites) in new[]
        {
            ("lightCrackSprites", lightCracks),
            ("heavyCrackSprites", heavyCracks),
        })
        {
            var property = so.FindProperty(field);
            property.arraySize = sprites.Length;
            for (int i = 0; i < sprites.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
        }
    }

    // 128 px = 1 world unit: four rows of 64x32 px bricks (0.5 x 0.25 units)
    // in a running bond, drawn in grayscale so the material tint supplies the
    // color. Brightness varies per brick; mortar lines are darker.
    static void WriteBrickWallTexture()
    {
        const int size = 128, rowHeight = 32, brickWidth = 64, mortar = 3;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            int row = y / rowHeight;
            int offset = row % 2 * (brickWidth / 2);
            for (int x = 0; x < size; x++)
            {
                int shifted = (x + offset) % size;
                bool isMortar = y % rowHeight < mortar || shifted % brickWidth < mortar;
                float value = 0.55f;
                if (!isMortar)
                {
                    int brickIndex = shifted / brickWidth + row * 3;
                    value = 0.82f + 0.18f * (brickIndex * 37 % 5 / 4f);
                }
                texture.SetPixel(x, y, new Color(value, value, value, 1f));
            }
        }
        texture.Apply();
        Directory.CreateDirectory(ToAbsolute(TexturesFolder));
        File.WriteAllBytes(ToAbsolute(BrickWallTexturePath), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }

    static void ApplyWallMesh(GameObject wall, Mesh mesh, Vector2 colliderSize)
    {
        wall.GetComponent<MeshFilter>().sharedMesh = mesh;
        wall.transform.localScale = Vector3.one;
        wall.GetComponent<BoxCollider2D>().size = colliderSize;
    }

    // A box authored at final size whose UVs equal each face's world
    // dimensions, so a 1-world-unit texture tiles at natural scale on every
    // face. Corner positions are listed as seen from outside the face
    // (bottom-left, bottom-right, top-right, top-left).
    static Mesh BuildWallMesh(string name, float width, float height, float depth)
    {
        float hw = width / 2f, hh = height / 2f, hd = depth / 2f;
        var mesh = new Mesh { name = name };
        var vertices = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var triangles = new System.Collections.Generic.List<int>();

        void Face(Vector3 bottomLeft, Vector3 bottomRight, Vector3 topRight, Vector3 topLeft, float u, float v)
        {
            int start = vertices.Count;
            vertices.Add(bottomLeft); vertices.Add(bottomRight); vertices.Add(topRight); vertices.Add(topLeft);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(u, 0f)); uvs.Add(new Vector2(u, v)); uvs.Add(new Vector2(0f, v));
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
            triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
        }

        Face(new Vector3(-hw, -hh, -hd), new Vector3(hw, -hh, -hd), new Vector3(hw, hh, -hd), new Vector3(-hw, hh, -hd), width, height);  // front (-Z)
        Face(new Vector3(hw, -hh, hd), new Vector3(-hw, -hh, hd), new Vector3(-hw, hh, hd), new Vector3(hw, hh, hd), width, height);      // back (+Z)
        Face(new Vector3(-hw, hh, -hd), new Vector3(hw, hh, -hd), new Vector3(hw, hh, hd), new Vector3(-hw, hh, hd), width, depth);       // top (+Y)
        Face(new Vector3(-hw, -hh, hd), new Vector3(hw, -hh, hd), new Vector3(hw, -hh, -hd), new Vector3(-hw, -hh, -hd), width, depth);   // bottom (-Y)
        Face(new Vector3(hw, -hh, -hd), new Vector3(hw, -hh, hd), new Vector3(hw, hh, hd), new Vector3(hw, hh, -hd), depth, height);      // right (+X)
        Face(new Vector3(-hw, -hh, hd), new Vector3(-hw, -hh, -hd), new Vector3(-hw, hh, -hd), new Vector3(-hw, hh, hd), depth, height);  // left (-X)

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // A rounded-rectangle prism (paddle, rounded brick), authored at final
    // size like the wall meshes: the XY outline is a rectangle with
    // quarter-circle corners, extruded along Z into front and back faces plus
    // an outward rim. The materials are plain colors, so the UVs just map
    // local XY on the faces and outline length by depth on the rim.
    static Mesh BuildRoundedPrismMesh(string name, float width, float height, float depth, float cornerRadius, int cornerSegments)
    {
        float hd = depth / 2f;
        var mesh = new Mesh { name = name };
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        // Counterclockwise outline: quarter-circle arcs around the four inset
        // corner centers; the straight edges emerge between consecutive arcs.
        var centers = new[]
        {
            new Vector2(width / 2f - cornerRadius, height / 2f - cornerRadius),
            new Vector2(cornerRadius - width / 2f, height / 2f - cornerRadius),
            new Vector2(cornerRadius - width / 2f, cornerRadius - height / 2f),
            new Vector2(width / 2f - cornerRadius, cornerRadius - height / 2f),
        };
        var outline = new List<Vector2>();
        for (int c = 0; c < 4; c++)
        {
            for (int i = 0; i <= cornerSegments; i++)
            {
                float angle = Mathf.PI / 2f * (c + (float)i / cornerSegments);
                outline.Add(centers[c] + cornerRadius * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
            }
        }

        // Front (-Z) and back (+Z) faces: fans from the center of the outline.
        foreach (var (z, front) in new[] { (-hd, true), (hd, false) })
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(0f, 0f, z));
            uvs.Add(Vector2.zero);
            foreach (var point in outline)
            {
                vertices.Add(new Vector3(point.x, point.y, z));
                uvs.Add(point);
            }
            for (int i = 0; i < outline.Count; i++)
            {
                int a = start + 1 + i, b = start + 1 + (i + 1) % outline.Count;
                triangles.Add(start);
                triangles.Add(front ? b : a);
                triangles.Add(front ? a : b);
            }
        }

        // Rim: one outward-facing quad per outline segment.
        float length = 0f;
        for (int i = 0; i < outline.Count; i++)
        {
            var from = outline[i];
            var to = outline[(i + 1) % outline.Count];
            int start = vertices.Count;
            vertices.Add(new Vector3(from.x, from.y, -hd));
            vertices.Add(new Vector3(from.x, from.y, hd));
            vertices.Add(new Vector3(to.x, to.y, hd));
            vertices.Add(new Vector3(to.x, to.y, -hd));
            float next = length + Vector2.Distance(from, to);
            uvs.Add(new Vector2(length, 0f)); uvs.Add(new Vector2(length, depth));
            uvs.Add(new Vector2(next, depth)); uvs.Add(new Vector2(next, 0f));
            length = next;
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
            triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
        }

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static void CreateTexturedWallMaterial(string path, Texture2D texture, Vector2 tiling)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", new Color(0.35f, 0.38f, 0.45f));
        material.SetTexture("_BaseMap", texture);
        material.SetTextureScale("_BaseMap", tiling);
        AssetDatabase.CreateAsset(material, path);
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
            lightGo.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
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
        // Side walls butt against the top wall's underside (y 5.5) instead of
        // overlapping it: coplanar overlap z-fights, and anything taller than
        // the top wall pokes into the camera's view.
        CreateWall(walls.transform, "Left", new Vector3(-7.75f, -0.5f, 0f), new Vector3(0.5f, 12f, 1f), squareSprite, wallColor);
        CreateWall(walls.transform, "Right", new Vector3(7.75f, -0.5f, 0f), new Vector3(0.5f, 12f, 1f), squareSprite, wallColor);
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
