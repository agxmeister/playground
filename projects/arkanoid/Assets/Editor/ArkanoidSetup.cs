using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Builds the Arkanoid assets and scene as a resumable state machine: each stage
// (currently fifty-seven of them, ending with the playfield opened out to the
// frame the way the menu screens always were)
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
    const string FogTexturePath = TexturesFolder + "/Fog.png";
    const string MenuFogMaterialPath = MaterialsFolder + "/MenuFog.mat";
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
    // The main menu's 3D content: an opaque backdrop box across the whole
    // frame, and in front of it a small playable scene — the word ARKANOID as
    // one hittable brick per letter, two option slabs, a paddle and a ball.
    // The one-piece title mesh the letters replaced.
    const string LegacyMenuTitleMeshPath = MeshesFolder + "/MenuTitle.asset";
    const string MenuLettersMeshFolder = MeshesFolder + "/MenuLetters";
    const string MenuTitleMaterialPath = MaterialsFolder + "/MenuTitle.mat";
    const string MenuBackdropMaterialPath = MaterialsFolder + "/MenuBackdrop.mat";
    const string MenuTitleWord = "ARKANOID";
    // One glyph cell is 0.2 world units, so the 47-cell-wide word spans 9.4
    // units, with an option arrow either side of it filling the rest of the
    // camera's frame out to its edges (see MenuArrowX — the whole width of a
    // 16:9 screen is the menu's field, rather than the 4:3 box that used to
    // hold the layout in and kept the word small). The doubled UV scale puts
    // one texture brick per cell width, two rows per height.
    const float MenuTitleCell = 0.2f;
    const float MenuTitleDepth = 0.4f;
    const float MenuTitleUvScale = 2f;
    const float MenuTitleY = 2.3f;
    // Everything hittable sits on one plane in front of the backdrop. 2D
    // physics ignores Z entirely, so this is only about what the camera sees;
    // the plane is what keeps the menu's bricks clear of the backdrop's
    // surface, and what a screen arriving rises into.
    const float MenuPlaneZ = -3.7f;
    // Where the camera stands and the plane a round is played on. The menu's
    // plane is nearer the camera than the round's, so the same world size looks
    // bigger there: everything the player drives on the menu — the paddle and
    // the ball — is scaled by the ratio of the two depths, and its speed with
    // it, so a paddle is the same width and a ball crosses the screen at the
    // same rate whichever screen it is on.
    const float CameraZ = -13.5f;
    const float PlayfieldPlaneZ = 0f;
    const float MenuPlayScale = (MenuPlaneZ - CameraZ) / (PlayfieldPlaneZ - CameraZ);
    // The labelled slabs the arrows replaced.
    const string LegacyMenuOptionStartMeshPath = MeshesFolder + "/MenuOptionStart.asset";
    const string LegacyMenuOptionRecordsMeshPath = MeshesFolder + "/MenuOptionRecords.asset";
    const string LegacyMenuLabelRecordsMeshPath = MeshesFolder + "/MenuLabelRecords.asset";
    // The two shared arrow meshes and the four separate label meshes that stood
    // on them, from when a label was a block of letters parked in front of the
    // banner's face rather than cut into it.
    const string LegacyMenuArrowRightMeshPath = MeshesFolder + "/MenuArrowRight.asset";
    const string LegacyMenuArrowLeftMeshPath = MeshesFolder + "/MenuArrowLeft.asset";
    const string LegacyMenuLabelStartMeshPath = MeshesFolder + "/MenuLabelStart.asset";
    const string LegacyMenuLabelHallMeshPath = MeshesFolder + "/MenuLabelHall.asset";
    const string LegacyMenuLabelNextMeshPath = MeshesFolder + "/MenuLabelNext.asset";
    const string LegacyMenuLabelBackMeshPath = MeshesFolder + "/MenuLabelBack.asset";
    // The hall's own arrow when it read NEXT and the plaque cycled its names.
    // It walks down the book now and the arrow says PREV, which is a different
    // word in the same banner: new meshes at new paths, so the change is a
    // rebuild the size checks can see rather than a silent rewrite of a mesh
    // that would look identical to them.
    const string LegacyMenuArrowNextMeshPath = MeshesFolder + "/MenuArrowNext.asset";
    const string LegacyMenuInlayNextMeshPath = MeshesFolder + "/MenuInlayNext.asset";
    // An arrow carries the pocket its own lettering is set into, so there is one
    // mesh per option rather than one per direction — and one block of white
    // lettering per option to seat in it.
    const string MenuArrowStartMeshPath = MeshesFolder + "/MenuArrowStart.asset";
    const string MenuArrowHallMeshPath = MeshesFolder + "/MenuArrowHall.asset";
    const string MenuArrowPrevMeshPath = MeshesFolder + "/MenuArrowPrev.asset";
    const string MenuArrowBackMeshPath = MeshesFolder + "/MenuArrowBack.asset";
    const string MenuInlayStartMeshPath = MeshesFolder + "/MenuInlayStart.asset";
    const string MenuInlayHallMeshPath = MeshesFolder + "/MenuInlayHall.asset";
    const string MenuInlayPrevMeshPath = MeshesFolder + "/MenuInlayPrev.asset";
    const string MenuInlayBackMeshPath = MeshesFolder + "/MenuInlayBack.asset";
    const string MenuOptionStartMaterialPath = MaterialsFolder + "/MenuOptionStart.mat";
    const string MenuOptionRecordsMaterialPath = MaterialsFolder + "/MenuOptionRecords.mat";
    const string MenuLabelMaterialPath = MaterialsFolder + "/MenuLabel.mat";
    // Both options are arrow banners rather than labelled slabs: a body wide
    // enough to carry the option's name, drawn out to a point at one end, with
    // rounded corners. START points right, out of the word, and HALL OF FAME
    // points left, at the screen it slides to. They stand either side of the
    // title with the word between them, so a ball launched straight up picks
    // nothing and the player has to aim. Green means "on you go", blue means
    // "the other way", on both screens.
    const float MenuArrowWidth = 2.3f;
    const float MenuArrowHeight = 1.6f;
    // How much of the width the pointed end takes; the rest is the body the
    // label sits in, which is why the label is offset back by half of it. The
    // body has to stay wider than HALL OF, the longer of the two label lines.
    const float MenuArrowPoint = 0.5f;
    const float MenuArrowCornerRadius = 0.16f;
    const int MenuArrowCornerSegments = 6;
    const float MenuArrowDepth = 0.5f;
    // The title's 9.4 units, then a gap of about 0.6 either side, then half an
    // arrow. That leaves the same gap again between each arrow and the edge of
    // a 16:9 frame (half-width 8.12 at this depth): the layout breathes at both
    // ends rather than crowding the word or the frame.
    const float MenuArrowX = 6.45f;
    // Small enough that HALL OF fits across the banner's body with room to
    // spare; the arrows are sized for the gaps around them first, and the
    // lettering follows.
    const float MenuArrowLabelCell = 0.038f;
    const int MenuArrowLabelGapRows = 2;
    // The lettering is *set into* the banner rather than cut out of it: the
    // arrow's face carries a pocket the shape of the word, and small white
    // blocks — the same block-font geometry the title is built from — sit down
    // in it. A groove on its own was tried first and read as a smudge: its floor
    // faces the camera exactly as the face around it does, is lit exactly the
    // same, and so at this size the letters had nothing but two hairlines of
    // chamfer to be seen by. What the pocket is for now is the shadow line that
    // says the white blocks are below the surface rather than on it.
    //
    // A third of a cell is how far the pocket's mouth flares past the blocks,
    // and how deep they sit: enough to read as a chamfer, and short of the half
    // cell at which the pockets of two strokes one cell apart would meet.
    const int MenuArrowPocketSubdivisions = 3;
    const float MenuArrowPocketDepth = MenuArrowLabelCell / MenuArrowPocketSubdivisions;
    // Thick enough to be a block rather than a decal, and well short of the
    // banner it is seated in.
    const float MenuArrowInlayDepth = 0.08f;
    // How far the blocks stand off the floor they cover, which is nothing to see
    // and everything to the depth buffer: the two are the same shape, so without
    // it they would fight for the same pixels.
    const float MenuArrowInlayLift = 0.1f * MenuArrowPocketDepth;
    static readonly string[] MenuStartLabel = { "START" };
    // Two lines: HALL OF FAME across one would be either wider than the banner
    // or too small to read.
    static readonly string[] MenuHallLabel = { "HALL OF", "FAME" };
    static readonly string[] MenuPrevLabel = { "PREV" };
    static readonly string[] MenuBackLabel = { "MENU" };
    // The paddle simply starts under the middle, between the two arrows.
    const float MenuPaddleRestX = 0f;
    // How wide the menu room's backdrop is: enough to fill that room's frame at
    // its own depth on any sane aspect ratio (~25 units across at 21:9). It may
    // overhang the playfield's room freely — the menu is switched off in the
    // same frame the view arrives there, so the only time this backdrop is ever
    // seen from outside its own room is on the journey between the two.
    const float MenuBackdropWidth = 28f;
    const float MenuBackdropDepth = 0.4f;
    // The backdrop is the far side of the fog a menu screen rises out of (see
    // ScreenChange): its front face stands ScreenChange.FogWall behind the
    // playing plane, so its centre is half its own depth further back again.
    // It is close on purpose. Every shadow on the menu is thrown onto this
    // surface and a shadow's drop is its object's gap from it times the tangent
    // of LightPitch, so the gap is the length of every shadow the menu has:
    // a fifth of a unit at this depth. It was stood a unit and a half back for
    // a while, to leave a screen room to fly in behind the plane and still be
    // seen; what gives an arriving screen somewhere to be now is the fog rather
    // than the room, and the shadows came back in with the wall.
    const float MenuBackdropZ = MenuPlaneZ + ScreenChange.FogWall + MenuBackdropDepth / 2f;
    // The two banks of haze that hang in the fog itself, between the playing
    // plane and that backdrop (see MenuFog). Two rather than one, at different
    // depths and different sizes, because a single sheet sliding along reads as
    // a picture being pulled past and two passing over each other read as fog.
    // Both stand clear of the plane by more than the half-depth of anything on
    // it — an option arrow, at a quarter of a unit, is the thickest — so the
    // menu is always in front of its own weather.
    const float MenuFogFarDepth = 0.42f;
    const float MenuFogNearDepth = 0.3f;
    // The round's room stands in the same fog — a round is a continuation of
    // the menu screens — so its backdrop is the same wall at the same gap:
    // front face ScreenChange.FogWall behind the playing plane, centre half its
    // own depth further back. The shadows a round throws come out the same
    // length as the menu's for it.
    const float PlayfieldBackdropDepth = 0.2f;
    const float PlayfieldBackdropZ =
        PlayfieldPlaneZ + ScreenChange.FogWall + PlayfieldBackdropDepth / 2f;
    // The round's own banks of haze hang deeper than the menu's 0.42 and 0.3:
    // the thickest thing on the menu's plane is an option arrow at a quarter of
    // a unit of half-depth, but a brick carries 0.3, so the menu's near bank
    // would lie exactly on every brick's back face. Between that and the wall
    // at 0.55 there is less room, and these two share it.
    const float PlayfieldFogFarDepth = 0.46f;
    const float PlayfieldFogNearDepth = 0.37f;
    // The hall of fame's two lines and the arrows that drive them, a screen's
    // width to the left of the title board inside the slider. The lines stand
    // 1.75 apart rather than the 1.5 they were authored at, so the name's
    // shadow falls in the gap rather than across the score. They were 2.25
    // apart while the backdrop stood well back and that shadow was a whole unit
    // long. HallOfFame sizes the lettering to whatever gap it finds, so this
    // spacing is also what caps the cell: 1.75 puts it at exactly the title's
    // own 0.2, which is as large as the plaque should ever be.
    const float HallNameY = 2.75f;
    const float HallScoreY = 1f;
    const float HallArrowY = 1.85f;
    const float MenuPaddleY = -3.6f;
    // What Ball.cs authors as its default, which is what the ball prefab — and
    // so every round — plays at. The menu's ball takes the same rate: it was
    // given a gentler 6.5 for a while, on the theory that the menu is steered
    // rather than fought, and the seam showed — a round is a continuation of
    // the menu screens, and a ball that picks up speed on the way into one says
    // they are two different games.
    const float PlayfieldBallSpeed = 8f;
    // Scaled down with the ball itself, so it is the same speed on the screen
    // and not merely in world units — the menu's plane covers less ground per
    // unit than the round's does.
    const float MenuBallSpeed = PlayfieldBallSpeed * MenuPlayScale;
    // Downward tilt of the directional light. Everything in this game casts its
    // shadow onto a surface *behind* it (the playfield backdrop, the menu
    // backdrop), not onto a floor, so the shadow's offset from the object is
    // gap x tan(pitch) — a steeper, "higher" light smears the shadow further
    // down, and a shallower one tucks it back in behind the object.
    const float LightPitch = 30f;

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

        // Stages 9 and 10 authored the main menu as a UGUI panel and saved it.
        // The menu is scene content now (stage 42 builds it, MainMenuPanel
        // rides on the MenuScreen root), so both are gone; the numbering of
        // everything after them is left alone rather than shuffled.

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
        // referencing the side-wall material asset — and on there still being
        // walls to reference it, since stage 56 takes them out of the scene
        // again and an absence gate would otherwise queue a save every reload.
        if (leftWall != null
            && !File.ReadAllText(ToAbsolute(scene.path)).Contains(AssetDatabase.AssetPathToGUID(WallSideMaterialPath)))
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
        // keeping a downward tilt so the shadows — the main depth cue — stay
        // visible below the objects instead of hiding behind them.
        if (sceneLight != null && Mathf.Abs(sceneLight.transform.forward.x) > 0.001f)
        {
            sceneLight.transform.rotation = Quaternion.Euler(LightPitch, 0f, 0f);
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

        // Stage 77: rename the hall's arrow from OptionNext to OptionPrev. It
        // used to fetch the next champion in a cycle and now walks back through
        // the records one at a time, so the name it was authored under says the
        // wrong thing about it.
        //
        // **It is numbered with the stages it arrived alongside but stands here
        // on purpose**, ahead of stage 40 rather than at the end of the list:
        // stage 40 is about to write the arrow's re-lettered meshes and delete
        // the ones it holds, and stage 70 puts those back by looking the object
        // up under its path (see MenuMeshes). Left until after them, the rename
        // would move the object out from under that path in the one window
        // where its mesh reference is null — a renderer with no mesh and no
        // stage able to find it again.
        var namedMenu = FindRootObject("MenuScreen");
        var legacyNextOption = namedMenu != null
            ? namedMenu.transform.Find("MenuSlider/MenuHall/OptionNext")
            : null;
        if (legacyNextOption != null)
        {
            legacyNextOption.name = "OptionPrev";
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 77: renamed the hall's arrow to OptionPrev (scene left dirty).");
            return;
        }

        // Stage 78: persist stage 77, gated on the scene file still knowing the
        // arrow under its old name.
        if (File.ReadAllText(ToAbsolute(scene.path)).Contains("m_Name: OptionNext"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 78: queued scene save for the next editor tick.");
            return;
        }

        // Stages 40-43 build the main menu screen: the ARKANOID word as
        // brick-textured blocks in front of an opaque backdrop, plus the
        // playable half — option slabs, paddle and ball — that the player
        // steers to pick an option.

        // Stage 40: the menu's block geometry. The title is one mesh per
        // letter, not one for the word, so the ball can knock the letters out
        // one at a time; each letter's UVs carry its place in the word, which
        // keeps the masonry running across the joins.
        // The option arrows are built here too — the pocket their lettering is
        // set into, and the block of white lettering that sits in it — and the
        // labelled slabs they replaced, along with the separate label meshes
        // that used to stand proud of them, are deleted. The size checks are
        // what bring this stage back to life on an already-built project when a
        // constant is retuned, rather than only when a mesh is missing.
        //
        // The title's letters and the arrows are rebuilt independently, because
        // writing a mesh over one already standing in the scene costs the scene
        // its reference to it (see the repair stage just below). Retuning the
        // arrows should not make the title pay for it.
        bool lettersStale = !File.Exists(ToAbsolute(MenuLetterMeshPath(0)))
            || MeshWidthDiffers(MenuLetterMeshPath(0), BlockText.GlyphWidth * MenuTitleCell);
        bool arrowsStale = !File.Exists(ToAbsolute(MenuArrowStartMeshPath))
            || !File.Exists(ToAbsolute(MenuInlayStartMeshPath))
            // The hall's arrow re-lettered from NEXT to PREV: same banner, same
            // width, so only the path it is written to says it has changed.
            || !File.Exists(ToAbsolute(MenuArrowPrevMeshPath))
            || MeshWidthDiffers(MenuArrowStartMeshPath, ArrowOutlineWidth());
        if (lettersStale || arrowsStale)
        {
            if (lettersStale)
            {
                Directory.CreateDirectory(ToAbsolute(MenuLettersMeshFolder));
                for (int i = 0; i < MenuTitleWord.Length; i++)
                    AssetDatabase.CreateAsset(
                        BlockText.BuildMesh($"Letter{i}", BlockText.GlyphCells(MenuTitleWord[i]), MenuTitleCell,
                            MenuTitleDepth, MenuTitleUvScale,
                            new Vector2(BlockText.GlyphCentreX(MenuTitleWord, i, MenuTitleCell), 0f)),
                        MenuLetterMeshPath(i));
            }
            if (arrowsStale)
            {
                CreateArrowMesh("MenuArrowStart", true, MenuStartLabel, MenuArrowStartMeshPath);
                CreateArrowMesh("MenuArrowHall", false, MenuHallLabel, MenuArrowHallMeshPath);
                CreateArrowMesh("MenuArrowPrev", false, MenuPrevLabel, MenuArrowPrevMeshPath);
                CreateArrowMesh("MenuArrowBack", true, MenuBackLabel, MenuArrowBackMeshPath);
                CreateInlayMesh("MenuInlayStart", MenuStartLabel, MenuInlayStartMeshPath);
                CreateInlayMesh("MenuInlayHall", MenuHallLabel, MenuInlayHallMeshPath);
                CreateInlayMesh("MenuInlayPrev", MenuPrevLabel, MenuInlayPrevMeshPath);
                CreateInlayMesh("MenuInlayBack", MenuBackLabel, MenuInlayBackMeshPath);
            }
            AssetDatabase.DeleteAsset(LegacyMenuTitleMeshPath);
            AssetDatabase.DeleteAsset(LegacyMenuOptionStartMeshPath);
            AssetDatabase.DeleteAsset(LegacyMenuOptionRecordsMeshPath);
            AssetDatabase.DeleteAsset(LegacyMenuLabelRecordsMeshPath);
            AssetDatabase.DeleteAsset(LegacyMenuArrowRightMeshPath);
            AssetDatabase.DeleteAsset(LegacyMenuArrowLeftMeshPath);
            AssetDatabase.DeleteAsset(LegacyMenuLabelStartMeshPath);
            AssetDatabase.DeleteAsset(LegacyMenuLabelHallMeshPath);
            AssetDatabase.DeleteAsset(LegacyMenuLabelNextMeshPath);
            AssetDatabase.DeleteAsset(LegacyMenuLabelBackMeshPath);
            AssetDatabase.DeleteAsset(LegacyMenuArrowNextMeshPath);
            AssetDatabase.DeleteAsset(LegacyMenuInlayNextMeshPath);
            Debug.Log("[ArkanoidSetup] Stage 40: created the menu block meshes.");
            return;
        }

        // Stage 70: put back any mesh stage 40 has just knocked out from under
        // the scene. Writing an asset over one already standing there destroys
        // the object the scene was pointing at, and the reference goes null
        // rather than following the new one into the same file — so a re-run of
        // stage 40 blanks whatever it rewrote. It went unseen for a long time
        // because stage 50 used to follow it and author the whole screen afresh;
        // the first re-run that did not left ARKANOID missing from the middle of
        // its own menu.
        //
        // **It is numbered with the stages it arrived alongside but stands here
        // on purpose**, directly behind the stage whose damage it repairs and
        // ahead of every stage that saves the scene: a save taken while a
        // reference is null writes the damage to disk, which is exactly how the
        // missing title came to be committed. It saves itself on the same tick
        // rather than leaving that to a paired stage, because a disk-side gate
        // for it is the one thing that cannot be written safely — a renderer
        // holding no mesh is a permanent, legitimate feature of this scene (the
        // hall's two champion lines are anchors and draw nothing), so a gate on
        // one would queue a save on every reload for ever.
        var drawnMenu = FindRootObject("MenuScreen");
        if (drawnMenu != null && MissingMenuMesh(drawnMenu.transform))
        {
            var title = drawnMenu.transform.Find("MenuSlider/MenuBoard/MenuTitle");
            for (int i = 0; title != null && i < MenuTitleWord.Length && i < title.childCount; i++)
                title.GetChild(i).GetComponent<MeshFilter>().sharedMesh =
                    AssetDatabase.LoadAssetAtPath<Mesh>(MenuLetterMeshPath(i));
            foreach (var (path, mesh) in MenuMeshes)
            {
                var drawn = drawnMenu.transform.Find(path);
                if (drawn != null)
                    drawn.GetComponent<MeshFilter>().sharedMesh =
                        AssetDatabase.LoadAssetAtPath<Mesh>(mesh);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 70: put the menu's meshes back and queued a scene save.");
            return;
        }

        // Stage 41: the menu materials — brick masonry for the title, flat
        // dark for the backdrop it stands in front of, and the option slabs in
        // two of the playfield's brick colours with pale letters on top.
        var menuTitleMaterial = AssetDatabase.LoadAssetAtPath<Material>(MenuTitleMaterialPath);
        var menuBackdropMaterial = AssetDatabase.LoadAssetAtPath<Material>(MenuBackdropMaterialPath);
        var menuLabelMaterial = AssetDatabase.LoadAssetAtPath<Material>(MenuLabelMaterialPath);
        if (menuTitleMaterial == null || menuBackdropMaterial == null || menuLabelMaterial == null)
        {
            var brickWall = AssetDatabase.LoadAssetAtPath<Texture2D>(BrickWallTexturePath);
            if (brickWall == null) return;
            if (menuTitleMaterial == null)
                CreateTexturedMaterial(MenuTitleMaterialPath, brickWall, new Color(0.86f, 0.36f, 0.26f));
            if (menuBackdropMaterial == null)
                CreateFogMaterial(MenuBackdropMaterialPath);
            if (menuLabelMaterial == null)
            {
                CreateLitMaterial(MenuOptionStartMaterialPath, new Color(0.18f, 0.80f, 0.44f));
                CreateLitMaterial(MenuOptionRecordsMaterialPath, new Color(0.20f, 0.60f, 0.86f));
                CreateLitMaterial(MenuLabelMaterialPath, new Color(0.94f, 0.95f, 0.98f));
            }
            Debug.Log("[ArkanoidSetup] Stage 41: created the menu materials.");
            return;
        }

        // Stage 42: the MenuScreen scene content, MainMenuPanel and all.
        if (FindRootObject("MenuScreen") == null)
        {
            BuildMenuScreen();
            Debug.Log("[ArkanoidSetup] Stage 42: built the main menu screen (scene left dirty).");
            return;
        }

        // Stage 43: persist stage 42, with the same tick-deferred save as the
        // earlier save stages, gated on the scene file not yet naming the
        // menu screen.
        if (!File.ReadAllText(ToAbsolute(scene.path)).Contains("MenuScreen"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 43: queued scene save for the next editor tick.");
            return;
        }

        // Stage 44: drop the HUD's high score readout — the menu screen shows
        // that — and move what is left under a Hud child the ScoreBoard can
        // switch off for the menu without hiding the panels on the same canvas.
        var scoreBoardGo = GameObject.Find("ScoreBoard");
        if (scoreBoardGo != null && scoreBoardGo.transform.Find("Hud") == null)
        {
            RebuildScoreBoardHud(scoreBoardGo);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 44: grouped the HUD and removed its high score (scene left dirty).");
            return;
        }

        // Stage 45: persist stage 44, gated on the removed readout still being
        // in the scene file.
        if (File.ReadAllText(ToAbsolute(scene.path)).Contains("HighScoreCaption"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 45: queued scene save for the next editor tick.");
            return;
        }

        // Stage 46: shrink the shadows by flattening the light's tilt from the
        // 50 degrees stage 34 set to LightPitch. See LightPitch: the shadow
        // lands on the surface behind the object, so less tilt means a tighter
        // shadow — most visibly the ARKANOID title's, which was dropping two
        // fifths of a letter-height below the word.
        if (sceneLight != null && sceneLight.transform.eulerAngles.x > LightPitch + 1f)
        {
            sceneLight.transform.rotation = Quaternion.Euler(LightPitch, 0f, 0f);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 46: flattened the light's tilt (scene left dirty).");
            return;
        }

        // Stage 47: persist stage 46, gated on the scene file still holding the
        // old 50-degree rotation quaternion.
        if (File.ReadAllText(ToAbsolute(scene.path)).Contains("m_LocalRotation: {x: 0.4226183,"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 47: queued scene save for the next editor tick.");
            return;
        }

        // Stage 48: strip the menu panel down to its option list — the high
        // score line and the key hint both go, leaving the 3D title and the two
        // options. The panel is authored inactive, so it needs the
        // inactive-inclusive lookup.
        var menuPanel = Object.FindAnyObjectByType<MainMenuPanel>(FindObjectsInactive.Include);
        if (menuPanel != null && menuPanel.transform.Find("Hint") != null)
        {
            Object.DestroyImmediate(menuPanel.transform.Find("Hint").gameObject);
            var highScoreLine = menuPanel.transform.Find("HighScore");
            if (highScoreLine != null) Object.DestroyImmediate(highScoreLine.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 48: removed the menu's high score line and key hint (scene left dirty).");
            return;
        }

        // Stage 49: persist stage 48, gated on the hint's text still being in
        // the scene file.
        if (File.ReadAllText(ToAbsolute(scene.path)).Contains("UP / DOWN"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 49: queued scene save for the next editor tick.");
            return;
        }

        // Stage 50: rebuild the menu whenever its shape is behind stage 42's
        // builder — first when the UGUI option list became a playable screen,
        // now that the screen has become two screens on a slider with arrows
        // for options. Nothing of the old shape survives either time, so it
        // goes wholesale and the builder authors the current one.
        var builtMenuScreen = FindRootObject("MenuScreen");
        var builtStartOption = builtMenuScreen != null
            ? builtMenuScreen.transform.Find("MenuSlider/MenuBoard/OptionStart")
            : null;
        if (builtMenuScreen != null
            && (builtStartOption == null
                || !Mathf.Approximately(builtStartOption.localPosition.x, MenuArrowX)))
        {
            Object.DestroyImmediate(builtMenuScreen);
            var uiMenu = Object.FindAnyObjectByType<MainMenuPanel>(FindObjectsInactive.Include);
            if (uiMenu != null) Object.DestroyImmediate(uiMenu.gameObject);
            BuildMenuScreen();
            Debug.Log("[ArkanoidSetup] Stage 50: rebuilt the menu screen (scene left dirty).");
            return;
        }

        // Stage 51: persist stage 50, gated on the scene file not yet holding
        // the START arrow where the current layout puts it. The pair therefore
        // keys on the arrows' placement: retuning the layout without moving
        // them would rebuild the scene but leave saving it to the user.
        if (!File.ReadAllText(ToAbsolute(scene.path))
            .Contains($"m_LocalPosition: {{x: {MenuArrowX}, y: {MenuTitleY}, z: {MenuPlaneZ}}}"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 51: queued scene save for the next editor tick.");
            return;
        }

        // Stage 52: move the hall of fame to the other side of the title board.
        // It was authored to its right, which had the left-pointing arrow that
        // leads to it scrolling the view right and the right-pointing one on it
        // scrolling back left — every arrow travelling against its own point.
        // From its left, each arrow scrolls the way it points. Nothing else of
        // the screen changes, so this moves the one object rather than going
        // through stage 50's wholesale rebuild.
        var builtHall = FindRootObject("MenuScreen") != null
            ? FindRootObject("MenuScreen").transform.Find("MenuSlider/MenuHall")
            : null;
        if (builtHall != null && builtHall.localPosition.x > 0f)
        {
            builtHall.localPosition = new Vector3(-MainMenuPanel.ScreenSpacing, 0f, 0f);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 52: moved the hall of fame left of the board (scene left dirty).");
            return;
        }

        // Stage 53: persist stage 52, gated on the scene file still holding the
        // hall where it used to stand.
        if (File.ReadAllText(ToAbsolute(scene.path))
            .Contains($"m_LocalPosition: {{x: {MainMenuPanel.ScreenSpacing}, y: 0, z: 0}}"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 53: queued scene save for the next editor tick.");
            return;
        }

        // Stage 54: move the whole menu into a room of its own, a screen's
        // width left of the playfield, and pull its backdrop in to the width
        // that room needs. It used to stand on top of the playfield with a
        // backdrop wide enough to hide it, which left nothing for START to
        // travel across — the menu could only be switched off. Apart in X, the
        // view can scroll from one to the other.
        var menuRoom = FindRootObject("MenuScreen");
        if (menuRoom != null && menuRoom.transform.position.x > -MainMenuPanel.ScreenSpacing + 1f)
        {
            menuRoom.transform.position = new Vector3(-MainMenuPanel.ScreenSpacing, 0f, 0f);
            var roomBackdrop = menuRoom.transform.Find("MenuBackdrop");
            if (roomBackdrop != null)
                roomBackdrop.localScale =
                    new Vector3(MenuBackdropWidth, 24f, MenuBackdropDepth);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 54: moved the menu into its own room (scene left dirty).");
            return;
        }

        // Stage 55: persist stage 54, gated on the scene file still holding the
        // backdrop at the width it had while it was hiding the playfield.
        if (File.ReadAllText(ToAbsolute(scene.path)).Contains("m_LocalScale: {x: 40, y: 24, z: 0.4}"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 55: queued scene save for the next editor tick.");
            return;
        }

        // Stage 56: give a round the same screen the menu has always had. The
        // playfield was a bordered box in the middle of the window — three
        // masonry walls with the frame's edges well outside them — while the
        // menu ran the full width with no walls in the picture at all. The
        // walls go, the room takes a Playfield component that lays invisible
        // borders along the frame's edges instead, and with them the paddle's
        // travel, the backdrop's size and the brick grid all follow the window.
        // Two things the widening displaces come with it: the score and lives
        // readouts drop to the bottom corners, where the field's new width
        // leaves no room for them at the top, and the menu's paddle and ball
        // are scaled to the size a round's are on screen, which the player has
        // to be able to read as the same object across the two rooms.
        if (Object.FindAnyObjectByType<Playfield>(FindObjectsInactive.Include) == null)
        {
            OpenPlayfieldToTheFrame();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 56: opened the playfield out to the frame (scene left dirty).");
            return;
        }

        // Stage 57: persist stage 56, gated on the walls it took out still
        // standing in the scene file.
        if (File.ReadAllText(ToAbsolute(scene.path)).Contains("m_Name: Left"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 57: queued scene save for the next editor tick.");
            return;
        }

        // Stage 58: stand the menu's backdrop back off the playing plane, and
        // widen it for the frame its new depth has. It was a fifth of a unit
        // behind the letters, which is fine for a screen that only ever moves
        // across the frame and no use at all to one that arrives from behind
        // it: there was nowhere for a screen to be under the plane and still be
        // seen. One transform, so it edits in place rather than going through
        // stage 50's rebuild. Stage 62 brings it back in again — a screen rises
        // out of the fog in place now and needs no room behind the plane — so
        // all that is left of this stage is the width, and its guard, written
        // against a MenuBackdropZ that has since moved, is only ever true of a
        // backdrop standing nearer the plane than the fog's own far side.
        var sunkBackdrop = FindRootObject("MenuScreen") != null
            ? FindRootObject("MenuScreen").transform.Find("MenuBackdrop")
            : null;
        if (sunkBackdrop != null && sunkBackdrop.localPosition.z < MenuBackdropZ - 0.01f)
        {
            sunkBackdrop.localPosition = new Vector3(0f, 0f, MenuBackdropZ);
            sunkBackdrop.localScale = new Vector3(MenuBackdropWidth, 24f, MenuBackdropDepth);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 58: stood the menu backdrop back (scene left dirty).");
            return;
        }

        // Stage 59: persist stage 58, gated on the scene file still holding the
        // backdrop at the width it had while it stood against the plane.
        if (File.ReadAllText(ToAbsolute(scene.path)).Contains("m_LocalScale: {x: 24, y: 24, z: 0.4}"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 59: queued scene save for the next editor tick.");
            return;
        }

        // Stage 60: stand the hall of fame's two lines further apart, for the
        // shadow the name throws now that the board stands out in front of its
        // backdrop (see HallNameY). Two transforms, and the lettering resizes
        // itself to the gap it finds.
        var hallBoard = FindRootObject("MenuScreen") != null
            ? FindRootObject("MenuScreen").transform.Find("MenuSlider/MenuHall")
            : null;
        var championName = hallBoard != null ? hallBoard.Find("ChampionName") : null;
        var championScore = hallBoard != null ? hallBoard.Find("ChampionScore") : null;
        if (championName != null && championScore != null
            && championName.localPosition.y < HallNameY - 0.01f)
        {
            championName.localPosition = new Vector3(0f, HallNameY, MenuPlaneZ);
            championScore.localPosition = new Vector3(0f, HallScoreY, MenuPlaneZ);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 60: spaced the plaque's lines out (scene left dirty).");
            return;
        }

        // Stage 61: persist stage 60, gated on the scene file still holding the
        // score line where it used to stand.
        if (File.ReadAllText(ToAbsolute(scene.path))
            .Contains($"m_LocalPosition: {{x: 0, y: 1.1, z: {MenuPlaneZ}}}"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 61: queued scene save for the next editor tick.");
            return;
        }

        // Stage 62: bring the menu's backdrop back in against the playing plane
        // and make it the far side of a fog rather than a wall. Standing it back
        // (stage 58) bought an arriving screen room to be seen under the plane
        // and paid for it in shadows: at a unit and a half of clearance every
        // letter on the menu trailed one. A screen rises out of the fog in place
        // now (see ScreenChange), which needs no room at all, so the wall comes
        // back to ScreenChange.FogWall — a fifth of a unit of shadow — and the
        // plaque's lines come back with it, from the 2.25 that long shadow
        // forced them to down to 1.75. The material is matted at the same time:
        // a sheen is what says "wall". Three transforms and one material, so it
        // edits in place rather than going through stage 50's rebuild.
        var wallBackdrop = FindRootObject("MenuScreen") != null
            ? FindRootObject("MenuScreen").transform.Find("MenuBackdrop")
            : null;
        if (wallBackdrop != null && wallBackdrop.localPosition.z > MenuBackdropZ + 0.01f)
        {
            wallBackdrop.localPosition = new Vector3(0f, 0f, MenuBackdropZ);
            var fogMaterial = AssetDatabase.LoadAssetAtPath<Material>(MenuBackdropMaterialPath);
            if (fogMaterial != null)
            {
                fogMaterial.SetColor("_BaseColor", ScreenChange.FogColor);
                fogMaterial.SetFloat("_Smoothness", 0f);
                EditorUtility.SetDirty(fogMaterial);
                AssetDatabase.SaveAssets();
            }
            var plaque = FindRootObject("MenuScreen").transform.Find("MenuSlider/MenuHall");
            var plaqueName = plaque != null ? plaque.Find("ChampionName") : null;
            var plaqueScore = plaque != null ? plaque.Find("ChampionScore") : null;
            if (plaqueName != null) plaqueName.localPosition = new Vector3(0f, HallNameY, MenuPlaneZ);
            if (plaqueScore != null) plaqueScore.localPosition = new Vector3(0f, HallScoreY, MenuPlaneZ);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 62: brought the menu backdrop in as fog (scene left dirty).");
            return;
        }

        // Stage 63: persist stage 62, gated on the scene file still holding the
        // backdrop out where it stood back from the plane.
        if (File.ReadAllText(ToAbsolute(scene.path)).Contains("m_LocalPosition: {x: 0, y: 0, z: -1.95}"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 63: queued scene save for the next editor tick.");
            return;
        }

        // Stage 64: the cloud the fog is made of — one tileable sheet of soft
        // patches, walked across the menu's haze banks rather than simulated.
        if (!File.Exists(ToAbsolute(FogTexturePath)))
        {
            WriteFogTexture();
            AssetDatabase.Refresh();
            Debug.Log("[ArkanoidSetup] Stage 64: wrote the fog texture.");
            return;
        }

        // Stage 65: the material the haze banks wear. Transparent, unlit and
        // never in shadow: haze is the light in the air rather than a surface
        // the light falls on.
        if (!File.Exists(ToAbsolute(MenuFogMaterialPath)))
        {
            var fogTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(FogTexturePath);
            if (fogTexture == null)
            {
                Debug.Log("[ArkanoidSetup] Fog texture not importable yet, waiting for next reload.");
                return;
            }
            CreateHazeMaterial(MenuFogMaterialPath, fogTexture);
            Debug.Log("[ArkanoidSetup] Stage 65: created the fog material.");
            return;
        }

        // Stage 66: hang the two banks of haze in the menu's fog. Outside the
        // slider with the backdrop, since the fog is the room's weather rather
        // than anything a screen carries with it.
        var fogRoom = FindRootObject("MenuScreen");
        if (fogRoom != null && fogRoom.transform.Find("MenuFogFar") == null)
        {
            var hazeMaterial = AssetDatabase.LoadAssetAtPath<Material>(MenuFogMaterialPath);
            if (hazeMaterial == null) return;
            CreateFogBank(fogRoom.transform, "MenuFogFar", MenuPlaneZ + MenuFogFarDepth,
                new Vector2(1.7f, 1.45f), new Vector2(0.01f, 0.004f), hazeMaterial,
                new Vector2(MenuBackdropWidth, 24f));
            CreateFogBank(fogRoom.transform, "MenuFogNear", MenuPlaneZ + MenuFogNearDepth,
                new Vector2(2.6f, 2.2f), new Vector2(-0.007f, 0.005f), hazeMaterial,
                new Vector2(MenuBackdropWidth, 24f));
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 66: hung the menu's banks of haze (scene left dirty).");
            return;
        }

        // Stage 67: persist stage 66, gated on the scene file not yet knowing
        // about the banks it hung.
        if (!File.ReadAllText(ToAbsolute(scene.path)).Contains("m_Name: MenuFogFar"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 67: queued scene save for the next editor tick.");
            return;
        }

        // Stage 68: cut each option's label into its arrow instead of standing
        // it in front of the face. A label used to be a second object with a
        // mesh and a pale material of its own, parked a little way towards the
        // camera; it is part of the banner's own mesh now (see
        // BlockText.Engraving), so the object goes and the arrow takes the mesh
        // that carries its lettering. Four transforms and four mesh references
        // rather than another wholesale rebuild.
        var engravedMenu = FindRootObject("MenuScreen");
        if (engravedMenu != null && FindOption(engravedMenu.transform, "ArrowLabel") != null)
        {
            EngraveOption(engravedMenu.transform, "MenuSlider/MenuBoard/OptionStart", MenuArrowStartMeshPath);
            EngraveOption(engravedMenu.transform, "MenuSlider/MenuBoard/OptionRecords", MenuArrowHallMeshPath);
            EngraveOption(engravedMenu.transform, "MenuSlider/MenuHall/OptionPrev", MenuArrowPrevMeshPath);
            EngraveOption(engravedMenu.transform, "MenuSlider/MenuHall/OptionBack", MenuArrowBackMeshPath);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 68: engraved the option arrows (scene left dirty).");
            return;
        }

        // Stage 69: persist stage 68, gated on the scene file still knowing
        // about the label objects it took out.
        if (File.ReadAllText(ToAbsolute(scene.path)).Contains("m_Name: ArrowLabel"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 69: queued scene save for the next editor tick.");
            return;
        }

        // Stage 72: seat a block of white lettering in each arrow's pocket. An
        // empty pocket was what stage 68 left — the letters were the cut itself,
        // and at this size a cut lit exactly as the face around it read as a
        // smudge rather than a word. The blocks are the same geometry the title
        // is built from, in the pale material the old raised labels wore, sunk
        // to the floor of the pocket instead of parked in front of the face.
        var inlaidMenu = FindRootObject("MenuScreen");
        if (inlaidMenu != null && FindOption(inlaidMenu.transform, "ArrowInlay") == null)
        {
            var inlayMaterial = AssetDatabase.LoadAssetAtPath<Material>(MenuLabelMaterialPath);
            InlayOption(inlaidMenu.transform, "MenuSlider/MenuBoard/OptionStart",
                MenuInlayStartMeshPath, true, inlayMaterial);
            InlayOption(inlaidMenu.transform, "MenuSlider/MenuBoard/OptionRecords",
                MenuInlayHallMeshPath, false, inlayMaterial);
            InlayOption(inlaidMenu.transform, "MenuSlider/MenuHall/OptionPrev",
                MenuInlayPrevMeshPath, false, inlayMaterial);
            InlayOption(inlaidMenu.transform, "MenuSlider/MenuHall/OptionBack",
                MenuInlayBackMeshPath, true, inlayMaterial);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 72: seated the arrows' lettering (scene left dirty).");
            return;
        }

        // Stage 73: persist stage 72, gated on the scene file not yet knowing
        // about the blocks it seated.
        if (!File.ReadAllText(ToAbsolute(scene.path)).Contains("m_Name: ArrowInlay"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 73: queued scene save for the next editor tick.");
            return;
        }

        // Stage 74: stand the round's room in the menu's fog. A round is a
        // continuation of the menu screens, so the space behind its plane
        // becomes the same space the menu's is: the backdrop moves in to the
        // fog wall's gap and puts on the fog's own material — matte, the murk's
        // colour, shared with the menu's wall so the two rooms can never drift
        // apart — and two banks of haze go up in front of it, root scenery like
        // the backdrop itself, standing while the room's colliders are off so
        // the weather is already there when the camera travels in. Playfield
        // takes their reins to size each to the frame at its own depth.
        if (FindRootObject("PlayfieldFogFar") == null)
        {
            var roundHazeMaterial = AssetDatabase.LoadAssetAtPath<Material>(MenuFogMaterialPath);
            var fogWallMaterial = AssetDatabase.LoadAssetAtPath<Material>(MenuBackdropMaterialPath);
            if (roundHazeMaterial == null || fogWallMaterial == null) return;

            var roundBackdrop = FindRootObject("Backdrop");
            if (roundBackdrop != null)
            {
                var at = roundBackdrop.transform.position;
                roundBackdrop.transform.position = new Vector3(at.x, at.y, PlayfieldBackdropZ);
                roundBackdrop.transform.localScale = new Vector3(
                    roundBackdrop.transform.localScale.x,
                    roundBackdrop.transform.localScale.y, PlayfieldBackdropDepth);
                roundBackdrop.GetComponent<MeshRenderer>().sharedMaterial = fogWallMaterial;
            }

            // Sized to the frame at runtime; authored generously so nothing
            // shows an edge before the first fit.
            var farBank = CreateFogBank(null, "PlayfieldFogFar",
                PlayfieldPlaneZ + PlayfieldFogFarDepth,
                new Vector2(1.4f, 0.8f), new Vector2(0.01f, 0.004f),
                roundHazeMaterial, new Vector2(32f, 16f));
            var nearBank = CreateFogBank(null, "PlayfieldFogNear",
                PlayfieldPlaneZ + PlayfieldFogNearDepth,
                new Vector2(2.1f, 1.2f), new Vector2(-0.007f, 0.005f),
                roundHazeMaterial, new Vector2(32f, 16f));

            var foggedRoom = Object.FindAnyObjectByType<Playfield>(FindObjectsInactive.Include);
            if (foggedRoom != null)
            {
                var roomSo = new SerializedObject(foggedRoom);
                var banks = roomSo.FindProperty("fogBanks");
                banks.arraySize = 2;
                banks.GetArrayElementAtIndex(0).objectReferenceValue = farBank.transform;
                banks.GetArrayElementAtIndex(1).objectReferenceValue = nearBank.transform;
                roomSo.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 74: stood the round's room in the fog (scene left dirty).");
            return;
        }

        // Stage 75: persist stage 74, gated on the scene file not yet knowing
        // about the banks it hung.
        if (!File.ReadAllText(ToAbsolute(scene.path)).Contains("m_Name: PlayfieldFogFar"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 75: queued scene save for the next editor tick.");
            return;
        }

        // Stage 76: pace the menu's ball to the round's. It was authored at a
        // gentler 6.5 against the round's 8, and the seam showed on the way in.
        // The guard is the serialized speed itself — a reliable in-memory fact —
        // so the save rides the same tick rather than a disk-side gate: a bare
        // float in the scene file is nothing to grep for.
        var pacedMenu = Object.FindAnyObjectByType<MainMenuPanel>(FindObjectsInactive.Include);
        var pacedBall = pacedMenu != null
            ? pacedMenu.transform.Find("MenuPlay/MenuBall") : null;
        if (pacedBall != null && pacedBall.GetComponent<Ball>() != null)
        {
            var pacedSo = new SerializedObject(pacedBall.GetComponent<Ball>());
            var pacedSpeed = pacedSo.FindProperty("speed");
            if (Mathf.Abs(pacedSpeed.floatValue - MenuBallSpeed) > 0.001f)
            {
                pacedSpeed.floatValue = MenuBallSpeed;
                pacedSo.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorApplication.update += SaveSceneOnce;
                Debug.Log("[ArkanoidSetup] Stage 76: paced the menu's ball to the round's (save queued).");
                return;
            }
        }
    }

    // Every menu mesh stage 40 writes over, against the object that draws it.
    // The title's eight letters are handled apart from this, since they are one
    // mesh per letter against one child per letter.
    static readonly (string Object, string Mesh)[] MenuMeshes =
    {
        ("MenuSlider/MenuBoard/OptionStart", MenuArrowStartMeshPath),
        ("MenuSlider/MenuBoard/OptionRecords", MenuArrowHallMeshPath),
        ("MenuSlider/MenuHall/OptionPrev", MenuArrowPrevMeshPath),
        ("MenuSlider/MenuHall/OptionBack", MenuArrowBackMeshPath),
        ("MenuSlider/MenuBoard/OptionStart/ArrowInlay", MenuInlayStartMeshPath),
        ("MenuSlider/MenuBoard/OptionRecords/ArrowInlay", MenuInlayHallMeshPath),
        ("MenuSlider/MenuHall/OptionPrev/ArrowInlay", MenuInlayPrevMeshPath),
        ("MenuSlider/MenuHall/OptionBack/ArrowInlay", MenuInlayBackMeshPath),
    };

    // Whether anything the menu draws has lost the mesh it draws it with.
    static bool MissingMenuMesh(Transform menu)
    {
        var title = menu.Find("MenuSlider/MenuBoard/MenuTitle");
        if (title != null)
            foreach (var letter in title.GetComponentsInChildren<MeshFilter>(true))
                if (letter.sharedMesh == null) return true;
        foreach (var (path, _) in MenuMeshes)
        {
            var drawn = menu.Find(path);
            if (drawn != null && drawn.GetComponent<MeshFilter>().sharedMesh == null) return true;
        }
        return false;
    }

    // The first of the menu's options carrying a child of the given name.
    static Transform FindOption(Transform menu, string child)
    {
        foreach (var option in menu.GetComponentsInChildren<MenuOption>(true))
        {
            var found = option.transform.Find(child);
            if (found != null) return found;
        }
        return null;
    }

    // One built arrow given the mesh carrying the pocket its lettering goes in,
    // and rid of the label that used to stand in front of its face.
    static void EngraveOption(Transform menu, string path, string arrowMeshPath)
    {
        var option = menu.Find(path);
        if (option == null) return;
        option.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(arrowMeshPath);
        var label = option.Find("ArrowLabel");
        if (label != null) Object.DestroyImmediate(label.gameObject);
    }

    // One built arrow given the blocks that sit in its pocket.
    static void InlayOption(Transform menu, string path, string inlayMeshPath,
        bool pointingRight, Material material)
    {
        var option = menu.Find(path);
        if (option == null) return;
        CreateArrowInlay(option, inlayMeshPath, pointingRight, material);
    }

    // The white lettering seated in a banner's pocket: the same block geometry
    // the title is built from, dropped to the floor the pocket's chamfer narrows
    // down to, so what stands above it is the chamfer alone.
    static void CreateArrowInlay(Transform option, string meshPath, bool pointingRight, Material material)
    {
        var inlay = new GameObject("ArrowInlay");
        inlay.transform.SetParent(option, false);
        var centre = ArrowLabelCentre(pointingRight);
        inlay.transform.localPosition = new Vector3(centre.x, centre.y,
            -MenuArrowDepth / 2f + MenuArrowPocketDepth - MenuArrowInlayLift + MenuArrowInlayDepth / 2f);
        inlay.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        inlay.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    // One sheet of drifting haze, as wide as the backdrop it hangs in front of
    // so its own edges are never in the frame. A quad rather than a box: it has
    // no thickness to show, and nothing is ever behind it but the backdrop.
    // The room is the menu's screen root, or nothing at all for the round's
    // banks, which are root scenery like the backdrop they hang in front of.
    static GameObject CreateFogBank(Transform room, string name, float z,
        Vector2 tiling, Vector2 drift, Material material, Vector2 size)
    {
        var bank = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bank.name = name;
        // A primitive brings a 3D collider with it. Gameplay is 2D so it could
        // do no harm, but a sheet of weather is not something to run into.
        Object.DestroyImmediate(bank.GetComponent<Collider>());
        bank.transform.SetParent(room, false);
        bank.transform.localPosition = new Vector3(0f, 0f, z);
        bank.transform.localScale = new Vector3(size.x, size.y, 1f);

        var sheet = bank.GetComponent<MeshRenderer>();
        sheet.sharedMaterial = material;
        // Haze has nothing to cast and nothing to catch.
        sheet.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sheet.receiveShadows = false;

        var fog = bank.AddComponent<MenuFog>();
        var fogSo = new SerializedObject(fog);
        fogSo.FindProperty("tiling").vector2Value = tiling;
        fogSo.FindProperty("drift").vector2Value = drift;
        fogSo.ApplyModifiedPropertiesWithoutUndo();
        return bank;
    }

    // Stage-56 retrofit. Everything it touches is a scene edit, so it can all
    // happen in one pass — nothing here is an asset that has to be read back.
    static void OpenPlayfieldToTheFrame()
    {
        var room = FindRootObject("Walls") ?? FindRootObject("Playfield");
        if (room == null) return;
        room.name = "Playfield";

        // The masonry walls were the border. The border is the screen's edge
        // now, and Playfield lays invisible ones along it at runtime.
        foreach (var wallName in new[] { "Left", "Right", "Top" })
        {
            var wall = room.transform.Find(wallName);
            if (wall != null) Object.DestroyImmediate(wall.gameObject);
        }

        var backdrop = FindRootObject("Backdrop");
        var playfieldPaddle = FindRootObject("Paddle");
        var playfield = room.AddComponent<Playfield>();
        var playfieldSo = new SerializedObject(playfield);
        if (backdrop != null)
            playfieldSo.FindProperty("backdrop").objectReferenceValue = backdrop.transform;
        if (playfieldPaddle != null)
            playfieldSo.FindProperty("paddle").objectReferenceValue = playfieldPaddle.GetComponent<Paddle>();
        playfieldSo.ApplyModifiedPropertiesWithoutUndo();

        // Sized to the frame at runtime; this is only so the Editor shows
        // something the width of the room rather than of the old box.
        if (backdrop != null)
            backdrop.transform.localScale = new Vector3(26f, 14f, backdrop.transform.localScale.z);

        var manager = Object.FindAnyObjectByType<GameManager>();
        if (manager != null)
        {
            var managerSo = new SerializedObject(manager);
            managerSo.FindProperty("playfield").objectReferenceValue = playfield;
            managerSo.ApplyModifiedPropertiesWithoutUndo();
        }

        MoveHudReadoutsToTheBottom();
        ScaleMenuPlayToPlayfieldSize();
    }

    // The four HUD texts were authored hanging from the top corners, either
    // side of a field that stopped short of them. Re-anchored to the bottom
    // ones, at the mirror of the offsets they had.
    static void MoveHudReadoutsToTheBottom()
    {
        var board = Object.FindAnyObjectByType<ScoreBoard>(FindObjectsInactive.Include);
        if (board == null) return;
        var hud = board.transform.Find("Hud");
        if (hud == null) return;

        foreach (var name in new[] { "ScoreCaption", "ScoreValue", "LivesCaption", "LivesValue" })
        {
            var readout = hud.Find(name) as RectTransform;
            if (readout == null) continue;
            bool caption = name.EndsWith("Caption");
            float anchorX = name.StartsWith("Score") ? 0f : 1f;
            readout.anchorMin = readout.anchorMax = readout.pivot = new Vector2(anchorX, 0f);
            readout.anchoredPosition = new Vector2(anchorX == 0f ? 40f : -40f,
                caption ? ReadoutCaptionY : ReadoutValueY);
            var text = readout.GetComponent<Text>();
            if (text != null)
                text.alignment = anchorX == 0f ? TextAnchor.LowerLeft : TextAnchor.LowerRight;
        }
    }

    // The menu's plane stands nearer the camera than a round's, so its paddle
    // and ball were authored the same world size and came out bigger on screen.
    // Scaled by the ratio of the two depths they read as the same objects, and
    // the ball's speed goes with them so it crosses the menu's screen at the
    // same rate a round's ball crosses its own.
    static void ScaleMenuPlayToPlayfieldSize()
    {
        var menu = Object.FindAnyObjectByType<MainMenuPanel>(FindObjectsInactive.Include);
        if (menu == null) return;

        var menuPaddle = menu.transform.Find("MenuPlay/MenuPaddle");
        if (menuPaddle != null) menuPaddle.localScale = Vector3.one * MenuPlayScale;

        var menuBall = menu.transform.Find("MenuPlay/MenuBall");
        if (menuBall != null)
        {
            var ballPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BallPrefabPath);
            if (ballPrefab != null)
                menuBall.localScale = ballPrefab.transform.localScale * MenuPlayScale;
            var ballSo = new SerializedObject(menuBall.GetComponent<Ball>());
            ballSo.FindProperty("speed").floatValue = MenuBallSpeed;
            ballSo.ApplyModifiedPropertiesWithoutUndo();
        }

        // Wide enough to cover its own room's frame; it may overhang the
        // playfield's freely, since the menu is switched off the moment the
        // view arrives there.
        var menuBackdrop = menu.transform.Find("MenuBackdrop");
        if (menuBackdrop != null)
            menuBackdrop.localScale = new Vector3(
                MenuBackdropWidth, menuBackdrop.localScale.y, menuBackdrop.localScale.z);
    }

    static string MenuLetterMeshPath(int index) => $"{MenuLettersMeshFolder}/Letter{index}.asset";

    // Whether a built mesh is still the size the constants now call for, which
    // is how the menu's geometry stage notices that a size was retuned.
    static bool MeshWidthDiffers(string path, float width)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        return mesh != null && Mathf.Abs(mesh.bounds.size.x - width) > 0.01f;
    }

    // What an arrow mesh should measure — taken from the outline rather than
    // from MenuArrowWidth, because rounding the point pulls the tip back in and
    // leaves the mesh a little narrower than its nominal width.
    static float ArrowOutlineWidth()
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var point in ArrowOutline(true))
        {
            min = Mathf.Min(min, point.x);
            max = Mathf.Max(max, point.x);
        }
        return max - min;
    }

    // The one shape the mesh, the collider and the label placement all read.
    static Vector2[] ArrowOutline(bool pointingRight) => BlockText.ArrowOutline(
        MenuArrowWidth, MenuArrowHeight, MenuArrowPoint,
        MenuArrowCornerRadius, MenuArrowCornerSegments, pointingRight);

    // Where a banner's lettering sits: centred on the *body* rather than on the
    // whole shape, so the point stays empty and the letters don't run out past
    // the slants.
    static Vector2 ArrowLabelCentre(bool pointingRight) =>
        new Vector2(pointingRight ? -MenuArrowPoint / 2f : MenuArrowPoint / 2f, 0f);

    // One option's banner, with the pocket its own name is set into.
    static void CreateArrowMesh(string name, bool pointingRight, string[] lines, string path) =>
        AssetDatabase.CreateAsset(
            BlockText.BuildArrowMesh(name, ArrowOutline(pointingRight), MenuArrowDepth,
                BlockText.Pocket(BlockText.LinesCells(lines, MenuArrowLabelGapRows),
                    MenuArrowLabelCell, ArrowLabelCentre(pointingRight), MenuArrowPocketSubdivisions)),
            path);

    // The blocks that sit in that pocket, built at the same cell size from the
    // same cells, so they fit the floor the pocket's chamfer narrows down to.
    static void CreateInlayMesh(string name, string[] lines, string path) =>
        AssetDatabase.CreateAsset(
            BlockText.BuildMesh(name, BlockText.LinesCells(lines, MenuArrowLabelGapRows),
                MenuArrowLabelCell, MenuArrowInlayDepth, 1f, Vector2.zero),
            path);

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

    static void SetObjectArray(SerializedObject so, string field, Object[] values)
    {
        var property = so.FindProperty(field);
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
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

    // The cloud the menu's fog is made of: soft patches that tile, white
    // throughout with the shape carried entirely in the alpha, so a haze bank
    // is one colour thickening and thinning rather than a picture of clouds.
    // Three octaves of value noise, the coarsest carrying most of it — fog is
    // large and slow, and detail at the pixel would read as static.
    static void WriteFogTexture()
    {
        const int size = 256;
        var coarse = FogLattice(3, 20240816);
        var middle = FogLattice(6, 90210);
        var fine = FogLattice(12, 4711);

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float value = 0.55f * SampleFogLattice(coarse, u, v)
                    + 0.3f * SampleFogLattice(middle, u, v)
                    + 0.15f * SampleFogLattice(fine, u, v);
                // Cut the thin half away and ease the rest in, which leaves
                // banks with clear air between them rather than an even veil.
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.36f, 0.78f, value));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        texture.Apply();
        Directory.CreateDirectory(ToAbsolute(TexturesFolder));
        File.WriteAllBytes(ToAbsolute(FogTexturePath), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }

    // A grid of random values that wraps, which is what makes the cloud tile:
    // the sample at 1 is the sample at 0. Seeded, so a from-scratch build
    // reproduces the same weather.
    static float[,] FogLattice(int period, int seed)
    {
        var random = new System.Random(seed);
        var values = new float[period, period];
        for (int y = 0; y < period; y++)
            for (int x = 0; x < period; x++)
                values[x, y] = (float)random.NextDouble();
        return values;
    }

    static float SampleFogLattice(float[,] values, float u, float v)
    {
        int period = values.GetLength(0);
        float x = u * period, y = v * period;
        int x0 = Mathf.FloorToInt(x) % period, y0 = Mathf.FloorToInt(y) % period;
        int x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
        // Smoothstepped rather than straight, so the lattice's own grid doesn't
        // show through as creases.
        float fx = Mathf.SmoothStep(0f, 1f, x - Mathf.Floor(x));
        float fy = Mathf.SmoothStep(0f, 1f, y - Mathf.Floor(y));
        return Mathf.Lerp(
            Mathf.Lerp(values[x0, y0], values[x1, y0], fx),
            Mathf.Lerp(values[x0, y1], values[x1, y1], fx),
            fy);
    }

    // The haze the fog's banks wear: unlit, because haze is light in the air
    // rather than a surface catching it, and transparent, because the whole of
    // it is how much of what is behind it still shows. A little brighter than
    // the fog's own colour — murk with nothing lighting it would be a stain.
    static void CreateHazeMaterial(string path, Texture2D texture)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetTexture("_BaseMap", texture);
        // Lighter and bluer than the fog's own colour, or there is nothing to
        // see: haze the same darkness as the murk behind it changes nothing
        // when it drifts across it. Where the cloud is at its thickest this
        // lifts the backdrop by about half again, which is a bank of fog
        // passing rather than a light coming on.
        material.SetColor("_BaseColor", new Color(0.22f, 0.26f, 0.36f, 0.6f));
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        AssetDatabase.CreateAsset(material, path);
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

    // Like CreateTexturedWallMaterial but with the tint spelled out and 1:1
    // tiling, for meshes that already carry world-scaled UVs.
    static void CreateTexturedMaterial(string path, Texture2D texture, Color color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", color);
        material.SetTexture("_BaseMap", texture);
        AssetDatabase.CreateAsset(material, path);
    }

    // The menu's backdrop, which is the far side of the fog rather than a wall
    // standing behind the screens (see ScreenChange). It wears the fog's own
    // colour, so that a screen fully in the fog and the murk it is coming out of
    // have nothing to tell them apart, and it is matte: a sheen running across
    // it is the one thing that reads as a surface at a distance rather than as
    // air with nothing in it.
    static void CreateFogMaterial(string path)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", ScreenChange.FogColor);
        material.SetFloat("_Smoothness", 0f);
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

        // The round's room. Its three walls are visible boxes here and stay that
        // way through the stages that texture and re-mesh them; stage 56 is
        // where they go, and the room's border becomes the frame itself.
        var walls = new GameObject("Playfield");
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
        // The readouts hang off a Hud child so the whole HUD can be hidden for
        // the menu while the panels on this canvas stay usable.
        var hud = CreateFullScreenChild(canvasGo.transform, "Hud");
        var scoreValue = CreateReadout(hud.transform, "Score", "SCORE", 0f, 40f, TextAnchor.LowerLeft, font);
        var livesValue = CreateReadout(hud.transform, "Lives", "LIVES", 1f, -40f, TextAnchor.LowerRight, font);

        var boardSo = new SerializedObject(board);
        boardSo.FindProperty("hud").objectReferenceValue = hud;
        boardSo.FindProperty("scoreValue").objectReferenceValue = scoreValue;
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

    // Stage-44 retrofit: the HUD predates the menu screen, so it still carries
    // a high score readout and its texts hang straight off the canvas.
    static void RebuildScoreBoardHud(GameObject canvasGo)
    {
        foreach (var name in new[] { "HighScoreCaption", "HighScoreValue" })
        {
            var stale = canvasGo.transform.Find(name);
            if (stale != null) Object.DestroyImmediate(stale.gameObject);
        }

        var hud = CreateFullScreenChild(canvasGo.transform, "Hud");
        // First sibling, so the records and main menu panels keep drawing over it.
        hud.transform.SetAsFirstSibling();

        foreach (var name in new[] { "ScoreCaption", "ScoreValue", "LivesCaption", "LivesValue" })
        {
            var readout = canvasGo.transform.Find(name);
            if (readout != null) readout.SetParent(hud.transform, false);
        }

        var boardSo = new SerializedObject(canvasGo.GetComponent<ScoreBoard>());
        boardSo.FindProperty("hud").objectReferenceValue = hud;
        boardSo.ApplyModifiedPropertiesWithoutUndo();
    }

    // A RectTransform child stretched over its parent, so anything moved into
    // it keeps the anchors and offsets it had on the parent.
    static GameObject CreateFullScreenChild(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return go;
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

    // The whole main menu, which is scene content rather than a UI panel: an
    // opaque box filling the camera's frame well short of the playfield, and in
    // front of it a small playable scene — the title as one hittable brick per
    // letter, an option arrow either side of it, a paddle and a ball.
    // MainMenuPanel rides on the root, so showing the menu is switching this
    // subtree on. Authored inactive, like the other panels.
    //
    // It is two screens, not one: the title board and, a screen's width to its
    // right inside MenuSlider, the hall of fame. Picking the hall of fame
    // slides the slider left rather than cutting to another view. The paddle
    // and ball hang off the root instead of the slider, so they stay where they
    // are while the world behind them moves.
    static void BuildMenuScreen()
    {
        var titleMaterial = AssetDatabase.LoadAssetAtPath<Material>(MenuTitleMaterialPath);
        var backdropMaterial = AssetDatabase.LoadAssetAtPath<Material>(MenuBackdropMaterialPath);
        var labelMaterial = AssetDatabase.LoadAssetAtPath<Material>(MenuLabelMaterialPath);
        var paddleMaterial = AssetDatabase.LoadAssetAtPath<Material>(PaddleMaterialPath);
        var paddleMesh = AssetDatabase.LoadAssetAtPath<Mesh>(PaddleMeshPath);
        var ballPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BallPrefabPath);

        var root = new GameObject("MenuScreen");
        // The menu is a room of its own, a screen's width to the left of the
        // playfield, so that starting a round is the view travelling right to
        // the playfield rather than the menu being switched off in front of it.
        root.transform.position = new Vector3(-MainMenuPanel.ScreenSpacing, 0f, 0f);
        var menu = root.AddComponent<MainMenuPanel>();

        // Covers this room's frame at any sane aspect ratio (at its depth that
        // frame is ~11 units tall), and stands far enough back for a screen to
        // arrive under the playing plane in front of it.
        var backdrop = new GameObject("MenuBackdrop");
        backdrop.transform.SetParent(root.transform, false);
        backdrop.transform.localPosition = new Vector3(0f, 0f, MenuBackdropZ);
        backdrop.transform.localScale = new Vector3(MenuBackdropWidth, 24f, MenuBackdropDepth);
        backdrop.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var backdropRenderer = backdrop.AddComponent<MeshRenderer>();
        backdropRenderer.sharedMaterial = backdropMaterial;
        backdropRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // The two screens ride on this; MainMenuPanel slides it sideways.
        var slider = new GameObject("MenuSlider");
        slider.transform.SetParent(root.transform, false);

        var board = new GameObject("MenuBoard");
        board.transform.SetParent(slider.transform, false);

        // Tilted back a little so the letters' tops and sides read as solid
        // blocks rather than a flat cutout. The tilt is about X, which 2D
        // physics ignores, so the letters' colliders are unaffected by it.
        var title = new GameObject("MenuTitle");
        title.transform.SetParent(board.transform, false);
        title.transform.localPosition = new Vector3(0f, MenuTitleY, MenuPlaneZ);
        title.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);
        for (int i = 0; i < MenuTitleWord.Length; i++)
        {
            var letter = new GameObject($"Letter{i}-{MenuTitleWord[i]}");
            letter.transform.SetParent(title.transform, false);
            letter.transform.localPosition =
                new Vector3(BlockText.GlyphCentreX(MenuTitleWord, i, MenuTitleCell), 0f, 0f);
            letter.AddComponent<MeshFilter>().sharedMesh =
                AssetDatabase.LoadAssetAtPath<Mesh>(MenuLetterMeshPath(i));
            letter.AddComponent<MeshRenderer>().sharedMaterial = titleMaterial;
            // The glyph's whole 5 x 7 box, holes included: the letters read as
            // blocks, and a collider tracing the strokes would let the ball
            // rattle around inside an O.
            letter.AddComponent<BoxCollider2D>().size =
                new Vector2(BlockText.GlyphWidth * MenuTitleCell, BlockText.GlyphHeight * MenuTitleCell);
            letter.AddComponent<MenuTitleBlock>();
        }

        // START points right, out of the word and into the game; HALL OF FAME
        // points left, the way the screen travels to reach it.
        CreateArrowOption(board.transform, "OptionStart", MainMenuOption.StartGame,
            MenuArrowX, MenuTitleY, true, MenuOptionStartMaterialPath, MenuArrowStartMeshPath,
            MenuInlayStartMeshPath, labelMaterial);
        CreateArrowOption(board.transform, "OptionRecords", MainMenuOption.HallOfFame,
            -MenuArrowX, MenuTitleY, false, MenuOptionRecordsMaterialPath, MenuArrowHallMeshPath,
            MenuInlayHallMeshPath, labelMaterial);

        BuildHallOfFame(slider.transform, titleMaterial, labelMaterial);

        // Everything the player actually drives, outside the slider so it stays
        // put while the screens move behind it.
        var play = new GameObject("MenuPlay");
        play.transform.SetParent(root.transform, false);

        // Scaled down by the ratio of the two planes' depths, so the menu's
        // paddle is exactly as wide on screen as the one a round is played with
        // even though it stands nearer the camera. The collider is authored at
        // full size and scales with the transform, as does the ball's.
        var paddleGo = new GameObject("MenuPaddle");
        paddleGo.transform.SetParent(play.transform, false);
        paddleGo.transform.localPosition = new Vector3(MenuPaddleRestX, MenuPaddleY, MenuPlaneZ);
        paddleGo.transform.localScale = Vector3.one * MenuPlayScale;
        paddleGo.AddComponent<MeshFilter>().sharedMesh = paddleMesh;
        paddleGo.AddComponent<MeshRenderer>().sharedMaterial = paddleMaterial;
        var menuPaddleCollider = paddleGo.AddComponent<BoxCollider2D>();
        menuPaddleCollider.size = new Vector2(
            PaddleWidth - 2f * PaddleCornerRadius, PaddleHeight - 2f * PaddleCornerRadius);
        menuPaddleCollider.edgeRadius = PaddleCornerRadius;
        paddleGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        // How far it may travel is measured off the frame at runtime, so there
        // is no limit to author: the menu's field is the whole window.
        var menuPaddle = paddleGo.AddComponent<Paddle>();

        var ballGo = (GameObject)PrefabUtility.InstantiatePrefab(ballPrefab);
        ballGo.name = "MenuBall";
        ballGo.transform.SetParent(play.transform, false);
        ballGo.transform.localPosition = new Vector3(MenuPaddleRestX, MenuPaddleY + 0.5f, MenuPlaneZ);
        ballGo.transform.localScale = ballPrefab.transform.localScale * MenuPlayScale;
        var menuBall = ballGo.GetComponent<Ball>();
        var ballSo = new SerializedObject(menuBall);
        ballSo.FindProperty("speed").floatValue = MenuBallSpeed;
        ballSo.ApplyModifiedPropertiesWithoutUndo();

        var menuSo = new SerializedObject(menu);
        menuSo.FindProperty("playGroup").objectReferenceValue = play;
        menuSo.FindProperty("slider").objectReferenceValue = slider.transform;
        menuSo.FindProperty("title").objectReferenceValue = title.transform;
        menuSo.FindProperty("hall").objectReferenceValue =
            slider.transform.Find("MenuHall").GetComponent<HallOfFame>();
        menuSo.FindProperty("paddle").objectReferenceValue = menuPaddle;
        menuSo.FindProperty("ball").objectReferenceValue = menuBall;
        menuSo.ApplyModifiedPropertiesWithoutUndo();

        var manager = Object.FindAnyObjectByType<GameManager>();
        var managerSo = new SerializedObject(manager);
        managerSo.FindProperty("mainMenuPanel").objectReferenceValue = menu;
        SetObjectArray(managerSo, "playfieldObjects",
            new Object[] { FindRootObject("Playfield"), FindRootObject("Paddle") });
        managerSo.ApplyModifiedPropertiesWithoutUndo();

        root.SetActive(false);

        // Mark dirty but defer the save: a programmatic SaveScene here raised
        // the modal "scene changed on disk" dialog before.
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    // One option: an arrow banner the ball can hit, its label set into a pocket
    // in the front face as white blocks rather than stood in front of it. The
    // collider is the arrow's own rounded outline rather than a box around it,
    // so a hit anywhere reflects off the shape the player can see.
    static GameObject CreateArrowOption(Transform parent, string name, MainMenuOption option,
        float x, float y, bool pointingRight, string materialPath, string arrowMeshPath,
        string inlayMeshPath, Material inlayMaterial)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(x, y, MenuPlaneZ);
        go.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(arrowMeshPath);
        go.AddComponent<MeshRenderer>().sharedMaterial =
            AssetDatabase.LoadAssetAtPath<Material>(materialPath);

        go.AddComponent<PolygonCollider2D>().points = ArrowOutline(pointingRight);

        var optionSo = new SerializedObject(go.AddComponent<MenuOption>());
        optionSo.FindProperty("option").enumValueIndex = (int)option;
        optionSo.ApplyModifiedPropertiesWithoutUndo();

        CreateArrowInlay(go.transform, inlayMeshPath, pointingRight, inlayMaterial);
        return go;
    }

    // The hall of fame screen: a champion's name over their score, with the
    // arrow to the record before theirs on the left and the way back to the
    // menu on the right. Both lines are empty here — the names are only known at
    // runtime, so HallOfFame builds their meshes itself.
    //
    // It sits a screen's width to the *left* of the board, so that the
    // left-pointing arrow that leads here scrolls left to reach it and the
    // right-pointing one on it scrolls right to go back.
    static void BuildHallOfFame(Transform slider, Material nameMaterial, Material scoreMaterial)
    {
        var hall = new GameObject("MenuHall");
        hall.transform.SetParent(slider, false);
        hall.transform.localPosition = new Vector3(-MainMenuPanel.ScreenSpacing, 0f, 0f);
        var component = hall.AddComponent<HallOfFame>();

        var nameLine = CreateChampionLine(hall.transform, "ChampionName", HallNameY, nameMaterial);
        var scoreLine = CreateChampionLine(hall.transform, "ChampionScore", HallScoreY, scoreMaterial);
        var previous = CreateArrowOption(hall.transform, "OptionPrev", MainMenuOption.PreviousRecord,
            -MenuArrowX, HallArrowY, false, MenuOptionRecordsMaterialPath, MenuArrowPrevMeshPath,
            MenuInlayPrevMeshPath, scoreMaterial);
        CreateArrowOption(hall.transform, "OptionBack", MainMenuOption.BackToMenu,
            MenuArrowX, HallArrowY, true, MenuOptionStartMaterialPath, MenuArrowBackMeshPath,
            MenuInlayBackMeshPath, scoreMaterial);

        var hallSo = new SerializedObject(component);
        hallSo.FindProperty("nameLine").objectReferenceValue = nameLine;
        hallSo.FindProperty("scoreLine").objectReferenceValue = scoreLine;
        hallSo.FindProperty("prevOption").objectReferenceValue = previous;
        hallSo.ApplyModifiedPropertiesWithoutUndo();
    }

    // A line of the plaque: empty here, since the champion is only known at
    // runtime. HallOfFame hangs one hittable block per symbol off it and takes
    // the material for them from this renderer, so the line object itself is the
    // anchor and the look of the line rather than a mesh of its own.
    static MeshFilter CreateChampionLine(Transform parent, string name, float y, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, y, MenuPlaneZ);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go.AddComponent<MeshFilter>();
    }

    static GameObject FindRootObject(string name)
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == name) return root;
        return null;
    }

    // Builds a caption ("SCORE") with a value line under it and returns the
    // value Text for runtime updates.
    //
    // Both readouts hang off the bottom corners of the screen. They used to sit
    // in the top ones, either side of a field that stopped well short of them;
    // the field is the whole window now, and the top of it is where the bricks
    // are, so the caption and its value were dropped to the one strip of screen
    // a round has nothing standing in.
    static Text CreateReadout(Transform parent, string name, string caption, float anchorX, float offsetX, TextAnchor alignment, Font font)
    {
        CreateText(parent, name + "Caption", caption, 26, new Vector2(anchorX, 0f), new Vector2(offsetX, ReadoutCaptionY), alignment, font, new Color(0.62f, 0.66f, 0.75f));
        return CreateText(parent, name + "Value", "0", 42, new Vector2(anchorX, 0f), new Vector2(offsetX, ReadoutValueY), alignment, font, Color.white);
    }

    // Where the two lines of a readout stand above the bottom edge, the mirror
    // of the 20/52 they hung below the top one.
    const float ReadoutCaptionY = 70f;
    const float ReadoutValueY = 20f;

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
