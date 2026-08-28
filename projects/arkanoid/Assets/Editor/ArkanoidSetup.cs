using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Builds the Arkanoid assets and scene as a resumable state machine: each stage
// (numbered to ninety-seven, ending with the world-UV box meshes a block's
// tiled grain needed) creates one batch of assets and returns, letting the next domain reload see
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
    // Polymer's surface grains. Three characters of injection-moulded plastic,
    // each written twice: a near-white albedo carrying only a whisper of value,
    // and a normal map carrying all the relief. Splitting them that way is what
    // lets the same grain serve a near-white block and a near-black one — a
    // dark block whose grain lived in its albedo would have nothing left to be
    // dark with, and the texture would vanish exactly where the block most
    // needs to not be mistaken for Neutronium.
    static readonly (string Name, GrainKind Kind)[] PolymerGrains =
    {
        ("PolymerPebble", GrainKind.Pebble),
        ("PolymerStipple", GrainKind.Stipple),
        ("PolymerCrumb", GrainKind.Crumb),
    };
    static string GrainTexturePath(string name) => TexturesFolder + "/" + name + ".png";
    static string GrainNormalPath(string name) => TexturesFolder + "/" + name + "Normal.png";
    const int GrainTextureSize = 256;

    // The band a Polymer block's batch colour is drawn from: the whole sheet,
    // near-white through near-black, which is what the reference for the
    // material actually looks like — moulded plastic comes in every value there
    // is. It does mean Polymer's dark end and Neutronium's are the same
    // brightness, so brightness alone stops being what tells the softest block
    // from the hardest. What tells them apart instead is the surface: Polymer is
    // a dielectric with a grain and a broad sheen, Neutronium is full metallic
    // at smoothness 0.05 in a scene with no reflection probe and so returns
    // nothing at all. That is a stronger cue at a glance than a value step was,
    // but it is a cue in the *relief*, which is why the grain is not optional
    // decoration here — it is the thing carrying the reading.
    static readonly Color PolymerDarkest = new Color(0.050f, 0.050f, 0.056f, 1f);
    static readonly Color PolymerLightest = new Color(0.930f, 0.928f, 0.910f, 1f);
    // Dark plastic is glossy plastic — the black tiles in the reference all
    // throw a highlight the white ones don't — so gloss rides along with the
    // roll rather than being a second independent die.
    const float PolymerDarkSmoothness = 0.45f;
    const float PolymerLightSmoothness = 0.12f;
    const float PolymerHueJitter = 0.03f;

    // How many UV units each block shape lays across one world unit of its
    // face — the divisor that makes one authored grain density come out the
    // same size on all four shapes. See Brick.grainUvPerUnit: the stock cube
    // and sphere map 0..1 across a face whatever its size, so their span is the
    // reciprocal of that size, while the rounded prism's mesh puts local XY
    // straight into the UV and so spans exactly one.
    static readonly (string Prefab, Vector2 UvPerUnit)[] BrickGrainUvSpans =
    {
        // The two box blocks wear world-UV meshes of their own rather than the
        // stock cube (stages 96 and 97), so like the rounded prism they measure
        // one UV unit to one world unit on every face.
        (BrickPrefabPath, Vector2.one),
        (HalfBrickPrefabPath, Vector2.one),
        (RoundedBrickPrefabPath, Vector2.one),
        // A stock sphere's u runs once around the equator and its v once from
        // pole to pole, so the two spans differ by the half-turn: pi*d across
        // u and pi*d/2 across v.
        (RoundBrickPrefabPath, new Vector2(
            1f / (Mathf.PI * RoundBrickDiameter), 2f / (Mathf.PI * RoundBrickDiameter))),
    };

    // Which relief a grain is. The three characters picked off the reference
    // sheet: the orange-peel that dominates it, the tight speckle of the pale
    // tiles, and the coarse granulate of the dark ones.
    enum GrainKind
    {
        Pebble,
        Stipple,
        Crumb,
    }
    const string BallPanelTexturePath = TexturesFolder + "/BallPanels.png";
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
    // World-UV box meshes for the two box-shaped blocks, replacing the stock
    // cube now that they wear a tiled grain: the stock cube maps 0..1 across
    // every face whatever it measures, which combs the grain into stripes on the
    // thin sides. Unit-size geometry with the block's proportions baked into the
    // UVs, so the prefabs keep the transform scale their crack overlay is
    // measured in.
    const string BrickBoxMeshPath = MeshesFolder + "/BrickBox.asset";
    const string HalfBrickBoxMeshPath = MeshesFolder + "/HalfBrickBox.asset";
    const float BrickWidth = 1.5f;
    const float BrickHeight = 0.5f;
    const float BrickDepth = 0.6f;
    // Two half bricks plus the level's 0.14 gap fill one normal slot: 2 * 0.68 + 0.14 = 1.5.
    const float HalfBrickWidth = 0.68f;
    // Rounded corners are physics, not trim: the collider is the same rounded
    // rectangle as the mesh, so a corner hit reflects off the curve. At 0.12 the
    // top face was 84% flat and five hits in six came off it like a box. 0.2 is
    // just under the half-height ceiling (BrickHeight / 2 = 0.25, at which the
    // ends are full semicircles and the shape is a stadium), so the curve is
    // plainly there while the face stays a face.
    const float RoundedBrickCornerRadius = 0.2f;
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
    // What the board a lost round ends on has written across it, in the same
    // block letters and the same masonry as the title. GAME OVER is a glyph
    // longer than ARKANOID — 53 cells against 47, the space between the two
    // words counting for one of them — and the arrows stand where they stand on
    // every board, so the word is drawn at whatever cell makes it span exactly
    // the width the title spans rather than at the title's own cell. That keeps
    // the 0.6 of a unit the 16:9 layout leaves between a word and the arrow
    // beside it, and it keeps the title the largest lettering on the menu.
    const string MenuOverWord = "GAME OVER";
    static readonly float MenuOverCell = MenuTitleCell
        * BlockText.WordColumns(MenuTitleWord) / (float)BlockText.WordColumns(MenuOverWord);
    // Where the slider's boards stand, and the order in X is the order the
    // arrows walk: the hall of fame is two screens left of the title board now,
    // with the board a lost round ends on between them, because every arrow
    // scrolls the way it points and that board's left arrow leads to the hall
    // while its right one leads back to the menu.
    const float MenuOverBoardX = -MainMenuPanel.ScreenSpacing;
    const float MenuHallBoardX = -2f * MainMenuPanel.ScreenSpacing;
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
    // The test bench's room, off to the *right* of the playfield — the mirror of
    // where the menu stands. Far enough that its borders and whatever is
    // standing on it are out of reach of the other rooms' 2D physics, which
    // ignores Z and so is only ever separated in X.
    //
    // **Two** screen widths rather than one, and that is not an aesthetic
    // choice. At one width the room stands at x 20, which serialises as
    // `m_LocalPosition: {x: 20, y: 0, z: 0}` — the exact string stage 53 greps
    // the scene file for to decide whether the hall of fame is still at its old
    // place. A room there made that gate true for ever and queued a scene save
    // on every single reload. The lesson is stage 53's rather than this room's
    // (a gate on residual damage is only as good as the scene's not containing
    // that pattern legitimately), but the cheap fix is to stand somewhere
    // nothing is looking: no gate in this file keys on x 40.
    const float BenchRoomX = 2f * MainMenuPanel.ScreenSpacing;
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

    // The nine block materials, one asset each. What a block is made of decides
    // both how it looks and how many hits it takes (BlockMaterials), so the look
    // is authored here beside the rest of the project's assets and the rule
    // lives with the enum.
    //
    // Colours are written in sRGB and converted once with .linear on the way to
    // the shader: the project renders in linear colour space, so a colour handed
    // to a material from script is taken as already-linear and comes out paler
    // and less saturated than the number reads.
    static readonly (BlockMaterial Kind, Color Color, float Metallic, float Smoothness, Color Emission)[]
        BlockMaterialLooks =
    {
        // Not armour but a rule, so it is the one block that does not look like
        // a solid: translucent and lit from within, which is the only thing on
        // the grid that says "this is not yours to break".
        (BlockMaterial.ForceField, new Color(0.20f, 0.60f, 1.00f, 0.45f), 0f, 0.90f, new Color(0.06f, 0.25f, 0.50f)),
        (BlockMaterial.Polymer, new Color(0.88f, 0.89f, 0.86f, 1f), 0f, 0.15f, Color.black),
        (BlockMaterial.Ceramics, new Color(0.93f, 0.90f, 0.80f, 1f), 0f, 0.08f, Color.black),
        // Magenta and lit, because it is the other block that behaves rather
        // than merely resists, and the two specials should be findable at a
        // glance in a grid of metals.
        (BlockMaterial.Antimatter, new Color(0.85f, 0.15f, 0.65f, 1f), 0f, 0.50f, new Color(0.55f, 0.05f, 0.40f)),
        (BlockMaterial.Titanium, new Color(0.62f, 0.64f, 0.68f, 1f), 0.85f, 0.45f, Color.black),
        (BlockMaterial.Crystal, new Color(0.35f, 0.85f, 0.80f, 0.62f), 0f, 0.95f, new Color(0.03f, 0.12f, 0.11f)),
        // Inconel and Waspaloy are both nickel alloys and would read as the same
        // block if they were only "grey metal": the gold heat-tint against the
        // near-black is what tells a x3 from a x4 across the field.
        (BlockMaterial.Inconel, new Color(0.66f, 0.58f, 0.44f, 1f), 0.9f, 0.55f, Color.black),
        (BlockMaterial.Waspaloy, new Color(0.38f, 0.37f, 0.43f, 1f), 0.80f, 0.60f, Color.black),
        // The top of the ladder is the block that returns nothing at all — and
        // getting there means making it a *metal*, which is the opposite of how
        // it was first written. A non-metal in URP always carries a dielectric
        // specular floor of about 4% reflectance, which is a good 0.22 in sRGB:
        // a matte block with an albedo of nothing still comes out mid-grey, and
        // the first attempt at this one came out lighter than the x4 below it.
        // A metal has no such floor — its reflectance *is* its base colour — so
        // full metallic over near-black, barely smooth enough to catch anything,
        // is the only way to a block that genuinely reflects nothing. Waspaloy
        // keeps a plain highlight precisely so this one can be the absence of
        // one.
        (BlockMaterial.Neutronium, new Color(0.020f, 0.020f, 0.026f, 1f), 1f, 0.05f, Color.black),
    };

    static string BlockMaterialPath(BlockMaterial kind) => MaterialsFolder + "/Block" + kind + ".mat";
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
    // The game over board is the plaque's shape — a word over a number, with the
    // arrows standing between the two lines — so it uses the hall's own heights
    // rather than authoring its own: the two screens read as one design, and
    // whatever keeps the plaque's shadow out of its score keeps this board's out
    // of its score too.
    const float MenuOverWordY = HallNameY;
    const float MenuOverScoreY = HallScoreY;
    const float MenuOverArrowY = HallArrowY;
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
        bool overLettersStale = !File.Exists(ToAbsolute(MenuOverLetterMeshPath(0)))
            || MeshWidthDiffers(MenuOverLetterMeshPath(0), BlockText.GlyphWidth * MenuOverCell);
        bool arrowsStale = !File.Exists(ToAbsolute(MenuArrowStartMeshPath))
            || !File.Exists(ToAbsolute(MenuInlayStartMeshPath))
            // The hall's arrow re-lettered from NEXT to PREV: same banner, same
            // width, so only the path it is written to says it has changed.
            || !File.Exists(ToAbsolute(MenuArrowPrevMeshPath))
            || MeshWidthDiffers(MenuArrowStartMeshPath, ArrowOutlineWidth());
        if (lettersStale || overLettersStale || arrowsStale)
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
            if (overLettersStale)
            {
                Directory.CreateDirectory(ToAbsolute(MenuLettersMeshFolder));
                for (int i = 0; i < MenuOverWord.Length; i++)
                {
                    // The space between the two words has nothing to draw, and
                    // nothing stands there — a block over the gap would bounce
                    // the ball off the middle of a word that isn't there.
                    if (BlockText.Blank(MenuOverWord[i])) continue;
                    AssetDatabase.CreateAsset(
                        BlockText.BuildMesh($"Over{i}", BlockText.GlyphCells(MenuOverWord[i]), MenuOverCell,
                            MenuTitleDepth, MenuTitleUvScale,
                            new Vector2(BlockText.GlyphCentreX(MenuOverWord, i, MenuOverCell), 0f)),
                        MenuOverLetterMeshPath(i));
                }
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
            foreach (var (drawn, mesh) in MenuLetters(drawnMenu.transform))
                drawn.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(mesh);
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
            // Saves on its own tick rather than through a paired disk-gated
            // stage — see stage 53 below for why it had to stop being one.
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 52: moved the hall of fame left of the board and queued a scene save.");
            return;
        }

        // Stage 53 was the disk-gated save for stage 52, and it is gone: its
        // gate was `the scene file still contains m_LocalPosition: {x: 20, y: 0,
        // z: 0}`, the hall's old place — a gate on residual damage, which is
        // only ever as good as that string not turning up in the scene for some
        // other perfectly good reason. It did: the test bench's room was first
        // stood one screen width right of the playfield, which is x 20, and the
        // gate went true for ever — queueing a save on every reload and, worse,
        // returning before every stage after it, so nothing downstream could
        // run at all. That is what makes this class of bug expensive rather than
        // merely noisy.
        //
        // Stage 52's own guard is an in-memory fact (a transform is where it is),
        // so it needs no disk-side gate: it registers the deferred save itself,
        // the way stages 70, 76, 85, 87 and 95 do. Nothing is lost — a stage that
        // saves only when it has just changed something cannot loop by
        // construction — and the aliasing is gone at the root rather than dodged.
        // The bench room moved to x 40 as well, since standing where no gate is
        // looking costs nothing (see BenchRoomX).

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

        // Stage 79: the ball's panels. A twisted ball turns (Ball.RollSpeed),
        // and a plain sphere turning is a sphere standing still — there has to
        // be something drawn on it for the roll to be visible at all.
        if (!File.Exists(ToAbsolute(BallPanelTexturePath)))
        {
            WriteBallPanelTexture();
            AssetDatabase.Refresh();
            Debug.Log("[ArkanoidSetup] Stage 79: wrote the ball's panel texture.");
            return;
        }

        // Stage 80: import it without mipmaps. The ball is a dozen-odd pixels
        // across on screen against a 64 px texture, so a mip chain would hand
        // the sphere an averaged-out level and the panels would be a flat grey
        // — the exact failure of drawing something on a ball to be seen.
        var panelImporter = AssetImporter.GetAtPath(BallPanelTexturePath) as TextureImporter;
        if (panelImporter != null && panelImporter.mipmapEnabled)
        {
            panelImporter.mipmapEnabled = false;
            panelImporter.filterMode = FilterMode.Bilinear;
            panelImporter.SaveAndReimport();
            Debug.Log("[ArkanoidSetup] Stage 80: turned the ball texture's mipmaps off.");
            return;
        }

        // Stage 81: put the panels on the ball's material. The tint stays what
        // it was and the texture is grayscale, the same bargain the brick wall
        // struck, so the ball is still the colour it was authored — panelled.
        var panelledBall = AssetDatabase.LoadAssetAtPath<Material>(BallMaterialPath);
        var panelTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(BallPanelTexturePath);
        if (panelledBall != null && panelTexture != null
            && panelledBall.GetTexture("_BaseMap") == null)
        {
            panelledBall.SetTexture("_BaseMap", panelTexture);
            EditorUtility.SetDirty(panelledBall);
            AssetDatabase.SaveAssets();
            Debug.Log("[ArkanoidSetup] Stage 81: panelled the ball's material.");
            return;
        }

        // Stage 82: let the ball turn. It was authored with its rotation frozen,
        // which is right for a ball that has nothing drawn on it and wrong for
        // one that does. Nothing about the rally moves: the collider is a
        // circle, so its rotation is not a fact any contact can read, and
        // `Ball.FixedUpdate` writes the angular velocity itself every step
        // rather than leaving it to the engine. The menu's ball is an instance
        // of this prefab and overrides nothing here, so it comes along.
        var turningBallRoot = PrefabUtility.LoadPrefabContents(BallPrefabPath);
        var turningBallBody = turningBallRoot != null ? turningBallRoot.GetComponent<Rigidbody2D>() : null;
        if (turningBallBody != null && turningBallBody.constraints != RigidbodyConstraints2D.None)
        {
            turningBallBody.constraints = RigidbodyConstraints2D.None;
            PrefabUtility.SaveAsPrefabAsset(turningBallRoot, BallPrefabPath);
            PrefabUtility.UnloadPrefabContents(turningBallRoot);
            Debug.Log("[ArkanoidSetup] Stage 82: unfroze the ball's rotation.");
            return;
        }
        if (turningBallRoot != null) PrefabUtility.UnloadPrefabContents(turningBallRoot);

        // Stage 83: the board a lost round comes to rest on. It is a screen of
        // the menu like the title board and the hall of fame — same room, same
        // fog, same ball still in play — standing between those two, because
        // every arrow scrolls the way it points and this board's arrows lead to
        // exactly them. The hall moves a screen further out to make the room.
        //
        // Nothing else of the menu changes, so this is an edit in place rather
        // than one of stage 50's wholesale rebuilds; stage 42's builder authors
        // the board directly, so a from-scratch run never reaches this stage.
        var menuWithBoards = FindRootObject("MenuScreen");
        var boardSlider = menuWithBoards != null
            ? menuWithBoards.transform.Find("MenuSlider")
            : null;
        if (boardSlider != null && boardSlider.Find("MenuOver") == null)
        {
            var standingHall = boardSlider.Find("MenuHall");
            if (standingHall != null)
                standingHall.localPosition = new Vector3(MenuHallBoardX, 0f, 0f);
            var builtOver = BuildGameOverBoard(boardSlider, menuTitleMaterial, menuLabelMaterial);
            var overPanel = Object.FindAnyObjectByType<MainMenuPanel>(FindObjectsInactive.Include);
            if (overPanel != null)
            {
                var overPanelSo = new SerializedObject(overPanel);
                overPanelSo.FindProperty("over").objectReferenceValue = builtOver;
                overPanelSo.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[ArkanoidSetup] Stage 83: built the game over board (scene left dirty).");
            return;
        }

        // Stage 84: persist stage 83, gated on the scene file not yet naming the
        // board — the new state's own signature rather than the hall's old
        // place, which is a position the slider's boards could legitimately
        // stand at again.
        if (!File.ReadAllText(ToAbsolute(scene.path)).Contains("MenuOver"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 84: queued scene save for the next editor tick.");
            return;
        }

        // Stage 85: the game over board's score goes white. Stage 83 cut it from
        // the title's masonry like the words above it, where the hall's plaque
        // has always drawn a name in stone and its score in the pale MenuLabel —
        // a name is lettering and a score is a readout, and what they are cut
        // from is what tells them apart. The board is the plaque's shape, so it
        // is the plaque's materials too.
        //
        // The guard is the material the line is wearing, which is an in-memory
        // fact, so like stages 70 and 76 this saves on its own tick rather than
        // through a paired stage: a disk-side gate would have to key on a
        // material guid the arrows' own lettering carries all over this scene.
        var builtOverBoard = FindRootObject("MenuScreen");
        var overScore = builtOverBoard != null
            ? builtOverBoard.transform.Find("MenuSlider/MenuOver/OverScore")
            : null;
        var overScoreRenderer = overScore != null ? overScore.GetComponent<MeshRenderer>() : null;
        if (overScoreRenderer != null && overScoreRenderer.sharedMaterial != menuLabelMaterial)
        {
            overScoreRenderer.sharedMaterial = menuLabelMaterial;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 85: made the game over board's score white and queued a scene save.");
            return;
        }

        // Stage 86: the nine block materials. A block's colour used to be
        // something the round painted on at spawn — one white Brick.mat tinted
        // per instance — which made the look and the hardness two unrelated
        // facts that a level had to remember to keep in step. They are one fact
        // now: the material is the look *and* the multiplier on the shape's own
        // hardness, so a player reads how many hits a block owes them off what
        // it is made of. Assets rather than tints, so blocks of a material still
        // batch, which is what the property block was there to protect.
        if (!File.Exists(ToAbsolute(BlockMaterialPath(BlockMaterial.Polymer))))
        {
            Directory.CreateDirectory(ToAbsolute(MaterialsFolder));
            foreach (var look in BlockMaterialLooks)
                CreateBlockMaterial(BlockMaterialPath(look.Kind),
                    look.Color, look.Metallic, look.Smoothness, look.Emission);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ArkanoidSetup] Stage 86: wrote {BlockMaterialLooks.Length} block materials.");
            return;
        }

        // Stage 87: hand them to the GameManager, in the enum's own order, since
        // BlockMaterial is the index into the array. This is a standing repair
        // like stage 70, not a one-off wiring: rewriting a material asset over
        // one already referenced destroys the object the scene was pointing at,
        // so the slot goes null rather than following the new material into the
        // same file — and retuning a block's look means exactly that rewrite.
        // The guard therefore reads the array's *contents*, not merely its
        // length, which a first version got wrong and which left a whole grid
        // wearing the prefabs' old white material with nine perfectly good
        // materials sitting beside it.
        //
        // An in-memory fact, so like stages 70, 76 and 85 this saves on its own
        // tick rather than through a paired stage — a disk-side gate would have
        // to key on material guids, and these nine sit in a scene that already
        // carries a dozen others.
        var materialManager = FindRootObject("GameManager");
        var materialManagerComponent = materialManager != null ? materialManager.GetComponent<GameManager>() : null;
        if (materialManagerComponent != null)
        {
            var managerSo = new SerializedObject(materialManagerComponent);
            var slot = managerSo.FindProperty("blockMaterials");
            var materials = LoadBlockMaterials();
            if (slot != null && materials != null && BlockMaterialsDiffer(slot, materials))
            {
                slot.arraySize = materials.Length;
                for (int i = 0; i < materials.Length; i++)
                    slot.GetArrayElementAtIndex(i).objectReferenceValue = materials[i];
                managerSo.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorApplication.update += SaveSceneOnce;
                Debug.Log("[ArkanoidSetup] Stage 87: wired the block materials and queued a scene save.");
                return;
            }
        }

        // Stage 88: the other half of the split. The shape carries a block's
        // *base* hardness and the material multiplies it, so the half-width and
        // round blocks are 1 where the full-slot slabs are 2 — small blocks are
        // flimsy, which also answers for there being two of them in every slot
        // they fill. The full-slot prefabs keep the field's own default of 2, so
        // only these two need writing.
        var smallBrickPaths = new[] { HalfBrickPrefabPath, RoundBrickPrefabPath };
        foreach (var path in smallBrickPaths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var brick = prefab != null ? prefab.GetComponent<Brick>() : null;
            if (brick == null) continue;
            if (new SerializedObject(brick).FindProperty("baseHardness").intValue == 1) continue;

            var root = PrefabUtility.LoadPrefabContents(path);
            var rootSo = new SerializedObject(root.GetComponent<Brick>());
            rootSo.FindProperty("baseHardness").intValue = 1;
            rootSo.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            Debug.Log($"[ArkanoidSetup] Stage 88: set {Path.GetFileName(path)}'s base hardness to 1.");
            return;
        }

        // Stage 89: rebuild the rounded brick's mesh at the wider corner radius.
        // Its corners were always real physics — a BoxCollider2D shrunk by the
        // radius with edgeRadius filling it back out, so a corner hit reflects
        // off the curve — but at 0.12 the top face was 84% flat and the curve
        // almost never got a hit. The guard measures the radius off the mesh
        // itself rather than trusting a file to be absent, since the path is
        // the one it has always been written to.
        if (RoundedBrickRadiusDiffers())
        {
            AssetDatabase.CreateAsset(
                BuildRoundedPrismMesh("BrickRounded", BrickWidth, BrickHeight, BrickDepth,
                    RoundedBrickCornerRadius, PaddleCornerSegments),
                RoundedBrickMeshPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ArkanoidSetup] Stage 89: rebuilt the rounded brick mesh at radius {RoundedBrickCornerRadius}.");
            return;
        }

        // Stage 90: put that mesh back on the prefab and resize the collider to
        // match it. Stage 89 wrote over an asset the prefab was pointing at,
        // which destroys the object rather than updating it — the reference goes
        // null instead of following the new mesh into the same file — so this is
        // the paired repair, and the collider is re-derived here because the
        // visual and the physical rounding must never be two different numbers.
        var roundedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoundedBrickPrefabPath);
        var roundedMeshAsset = AssetDatabase.LoadAssetAtPath<Mesh>(RoundedBrickMeshPath);
        if (roundedPrefab != null && roundedMeshAsset != null)
        {
            var filter = roundedPrefab.GetComponent<MeshFilter>();
            var box = roundedPrefab.GetComponent<BoxCollider2D>();
            bool meshLost = filter != null && filter.sharedMesh != roundedMeshAsset;
            bool colliderStale = box != null
                && Mathf.Abs(box.edgeRadius - RoundedBrickCornerRadius) > 0.001f;
            if (meshLost || colliderStale)
            {
                var root = PrefabUtility.LoadPrefabContents(RoundedBrickPrefabPath);
                root.GetComponent<MeshFilter>().sharedMesh = roundedMeshAsset;
                var collider = root.GetComponent<BoxCollider2D>();
                collider.size = new Vector2(
                    BrickWidth - 2f * RoundedBrickCornerRadius, BrickHeight - 2f * RoundedBrickCornerRadius);
                collider.edgeRadius = RoundedBrickCornerRadius;
                PrefabUtility.SaveAsPrefabAsset(root, RoundedBrickPrefabPath);
                PrefabUtility.UnloadPrefabContents(root);
                Debug.Log("[ArkanoidSetup] Stage 90: re-pointed the rounded brick's mesh and widened its collider.");
                return;
            }
        }

        // Stage 91: Polymer's three surface grains, six files — a near-white
        // albedo and a normal map each. The guard is the first albedo's absence,
        // so retuning a grain means deleting the PNGs and letting this write
        // them again, keeping their `.meta` files the way stage 79's ball
        // texture is retuned: a new guid would break the references stage 95
        // hands the GameManager.
        if (!File.Exists(ToAbsolute(GrainTexturePath(PolymerGrains[0].Name))))
        {
            Directory.CreateDirectory(ToAbsolute(TexturesFolder));
            foreach (var grain in PolymerGrains) WriteGrainTextures(grain.Name, grain.Kind);
            AssetDatabase.Refresh();
            Debug.Log($"[ArkanoidSetup] Stage 91: wrote {PolymerGrains.Length} polymer grains, albedo and normal.");
            return;
        }

        // Stage 92: import the normal maps *as* normal maps. A tangent-space
        // map left on the default importer type is read as colour and lit as
        // though the surface were painted lilac — which is exactly what it looks
        // like, and it is the one failure here that shows up as a wrong hue
        // rather than as flat geometry. The albedos need nothing: a PNG's
        // default import is already sRGB, repeat-wrapped and mipmapped, and the
        // mipmaps matter — a 256 px grain tiled twice a unit on a block a
        // hundred-odd pixels tall would shimmer without them.
        var strayNormal = FirstNonNormalMapGrain();
        if (strayNormal != null)
        {
            foreach (var grain in PolymerGrains)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(GrainNormalPath(grain.Name));
                if (importer == null || importer.textureType == TextureImporterType.NormalMap) continue;
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
            Debug.Log("[ArkanoidSetup] Stage 92: imported the polymer grain normals as normal maps.");
            return;
        }

        // Stage 93: give the shared BlockPolymer material the first grain, so a
        // block that never gets a per-instance look (a level that leaves the
        // varieties unwired, or anything spawning a brick outside BuildLevel)
        // still comes out moulded rather than flat. It is also what turns the
        // shader's normal-map path on: a keyword is a fact about the material and
        // cannot be overridden per instance, so every per-block grain in the game
        // is riding on this one call.
        var polymerMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            BlockMaterialPath(BlockMaterial.Polymer));
        var defaultGrain = AssetDatabase.LoadAssetAtPath<Texture2D>(
            GrainTexturePath(PolymerGrains[0].Name));
        var defaultGrainNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(
            GrainNormalPath(PolymerGrains[0].Name));
        if (polymerMaterial != null && defaultGrain != null && defaultGrainNormal != null
            && polymerMaterial.GetTexture("_BaseMap") != defaultGrain)
        {
            polymerMaterial.SetTexture("_BaseMap", defaultGrain);
            polymerMaterial.SetTexture("_BumpMap", defaultGrainNormal);
            polymerMaterial.EnableKeyword("_NORMALMAP");
            // The full slab's own tiling, which is what this fallback would most
            // likely be seen on. A per-instance look overrides both of these.
            var slab = new Vector2(
                PolymerGrainTiles * BrickWidth, PolymerGrainTiles * BrickHeight);
            polymerMaterial.SetTextureScale("_BaseMap", slab);
            polymerMaterial.SetTextureScale("_BumpMap", slab);
            EditorUtility.SetDirty(polymerMaterial);
            AssetDatabase.SaveAssets();
            Debug.Log("[ArkanoidSetup] Stage 93: put a grain and its normal on BlockPolymer.");
            return;
        }

        // Stage 94: tell each block prefab how its own mesh lays UVs out, which
        // is the one thing standing between one authored grain size and four
        // different ones. Nothing here touches the scene — the four prefabs are
        // assets and the grid is spawned from them — so no save is paired with
        // it.
        var strayUvSpan = FirstStaleGrainUvSpan();
        if (strayUvSpan != null)
        {
            foreach (var (path, uvPerUnit) in BrickGrainUvSpans)
            {
                if (!GrainUvSpanDiffers(path, uvPerUnit)) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                var brickSo = new SerializedObject(root.GetComponent<Brick>());
                brickSo.FindProperty("grainUvPerUnit").vector2Value = uvPerUnit;
                brickSo.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log("[ArkanoidSetup] Stage 94: set the block prefabs' grain UV spans.");
            return;
        }

        // Stage 95: hand Polymer's variety to the GameManager. A standing repair
        // for the same reason stage 87 is: stage 91 rewrites texture assets, and
        // rewriting one over a path already referenced destroys the object the
        // scene was pointing at rather than updating it, so these slots go null
        // instead of following the new PNG into the same file. The guard reads
        // the entry's *contents* — both texture arrays, element by element —
        // because a list that is the right length with a null grain in it is
        // exactly what that failure leaves behind, and VarietyOf would then
        // quietly hand every Polymer block the shared asset with no sign that
        // anything had been lost.
        //
        // An in-memory fact, so like stages 70, 76, 85 and 87 this saves on its
        // own tick rather than through a paired stage: there is no disk-side
        // signature to gate on, since a texture guid in the scene file says
        // nothing about which slot is holding it.
        var varietyManager = FindRootObject("GameManager");
        var varietyManagerComponent = varietyManager != null
            ? varietyManager.GetComponent<GameManager>() : null;
        var grainTextures = LoadGrains(GrainTexturePath);
        var grainNormalTextures = LoadGrains(GrainNormalPath);
        if (varietyManagerComponent != null && grainTextures != null && grainNormalTextures != null)
        {
            var varietySo = new SerializedObject(varietyManagerComponent);
            var varieties = varietySo.FindProperty("blockVarieties");
            if (PolymerVarietyDiffers(varieties, grainTextures, grainNormalTextures))
            {
                varieties.arraySize = 1;
                var entry = varieties.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("material").enumValueIndex = (int)BlockMaterial.Polymer;
                FillTextureArray(entry.FindPropertyRelative("grains"), grainTextures);
                FillTextureArray(entry.FindPropertyRelative("grainNormals"), grainNormalTextures);
                entry.FindPropertyRelative("darkest").colorValue = PolymerDarkest;
                entry.FindPropertyRelative("lightest").colorValue = PolymerLightest;
                entry.FindPropertyRelative("darkSmoothness").floatValue = PolymerDarkSmoothness;
                entry.FindPropertyRelative("lightSmoothness").floatValue = PolymerLightSmoothness;
                entry.FindPropertyRelative("hueJitter").floatValue = PolymerHueJitter;
                varietySo.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                EditorApplication.update += SaveSceneOnce;
                Debug.Log("[ArkanoidSetup] Stage 95: wired Polymer's variety and queued a scene save.");
                return;
            }
        }

        // Stage 96: world-UV box meshes for the two box blocks. Adding a tiled
        // grain is what made the stock cube untenable: it lays UVs 0..1 across
        // every face whatever that face measures, so the one tiling that suits a
        // 1.5-wide front is two and a half times too dense across a 0.6-deep
        // side, and the grain there came out visibly combed into stripes. No
        // tiling value can fix it — the whole point of these meshes is that a UV
        // unit is a world unit on all six faces, which is the same thing the
        // rounded prism already does and why that block never had the problem.
        //
        // The guard measures the winding off the mesh's own triangles rather
        // than trusting a file to be absent, for the reason stage 89's measures
        // the corner radius: the path is the one it has always been written to,
        // and the first cut of this builder wound every face the wrong way round
        // — which draws the far face and the inside of the box, and reads as a
        // stepped, hollow, corrupt block. A guard that only asked whether the
        // file existed could never have repaired that.
        if (!File.Exists(ToAbsolute(BrickBoxMeshPath)) || BlockBoxWindingIsInverted())
        {
            Directory.CreateDirectory(ToAbsolute(MeshesFolder));
            AssetDatabase.CreateAsset(
                BuildWorldUvBoxMesh("BrickBox", new Vector3(BrickWidth, BrickHeight, BrickDepth)),
                BrickBoxMeshPath);
            AssetDatabase.CreateAsset(
                BuildWorldUvBoxMesh("HalfBrickBox", new Vector3(HalfBrickWidth, BrickHeight, BrickDepth)),
                HalfBrickBoxMeshPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[ArkanoidSetup] Stage 96: wrote the world-UV block box meshes.");
            return;
        }

        // Stage 97: put them on the prefabs. The transform scale is left exactly
        // as it was — the geometry is a unit cube, so the scale still supplies
        // the size, and the crack overlay child that is measured in that scale
        // does not move. Nothing here touches the scene, so no save is paired
        // with it.
        var boxSwap = FirstStaleBlockBoxMesh();
        if (boxSwap != null)
        {
            foreach (var (path, meshPath) in BlockBoxMeshes)
            {
                var wanted = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (wanted == null || !BlockBoxMeshDiffers(path, meshPath)) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                root.GetComponent<MeshFilter>().sharedMesh = wanted;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                // And then make the Editor read the prefab back off disk. This
                // is the whole reason the first cut of this stage did nothing
                // visible: SaveAsPrefabAsset writes the file — the guid lands
                // correctly, and `git diff` looks like a clean repair — but the
                // Editor goes on serving the *imported* prefab it already had,
                // which was imported in the window where stage 96 had just
                // destroyed the mesh and so holds a null. Instantiate hands out
                // that imported copy, so every block in the round came up with
                // no mesh at all while the file on disk was perfect. Nothing
                // short of a forced reimport clears it: a plain Refresh, a
                // domain reload and re-opening the scene all leave it standing.
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            Debug.Log("[ArkanoidSetup] Stage 97: put the world-UV box meshes on the block prefabs.");
            return;
        }

        // Stage 98: the test bench's room (see "The test bench" in CLAUDE.md).
        // A room of its own off to the right of the playfield, the way the menu
        // is off to its left, so that whatever stands on it is out of reach of
        // either other room's 2D physics and needs no round to be running. It is
        // authored **inactive**: GameManager switches it on when the code is
        // typed, which is also why its reference is serialized rather than found
        // — GameObject.Find cannot see an inactive object.
        //
        // Only the room and its backdrop are authored. Everything else the bench
        // shows is spawned at runtime, which is the point: the arrangement is
        // meant to be rewritten per task, and a scratchpad whose contents lived
        // in the scene file would put every experiment into a scene diff.
        var benchBackdropMaterial = AssetDatabase.LoadAssetAtPath<Material>(MenuBackdropMaterialPath);
        if (FindRootObject("TestBench") == null && benchBackdropMaterial != null)
        {
            var benchRoom = new GameObject("TestBench");
            benchRoom.transform.position = new Vector3(BenchRoomX, 0f, PlayfieldPlaneZ);
            benchRoom.AddComponent<TestBench>();

            // The same wall the round stands in front of, at the same gap, so a
            // block photographed here throws the shadow it would throw in play —
            // a bench that lit its subject differently from the game would be
            // worse than no bench.
            var benchBackdrop = new GameObject("BenchBackdrop");
            benchBackdrop.transform.SetParent(benchRoom.transform, false);
            benchBackdrop.transform.localPosition = new Vector3(0f, 0f, PlayfieldBackdropZ);
            benchBackdrop.transform.localScale =
                new Vector3(MenuBackdropWidth, 24f, PlayfieldBackdropDepth);
            benchBackdrop.AddComponent<MeshFilter>().sharedMesh =
                Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var benchBackdropRenderer = benchBackdrop.AddComponent<MeshRenderer>();
            benchBackdropRenderer.sharedMaterial = benchBackdropMaterial;
            benchBackdropRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;

            benchRoom.SetActive(false);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[ArkanoidSetup] Stage 98: built the test bench room (scene left dirty).");
            return;
        }

        // The same stage, repairing a room that was built at the wrong X. The
        // first version of BenchRoomX put it one screen width out, where its
        // serialised position collided with stage 53's save gate (see
        // BenchRoomX); moving the constant alone would have left the room
        // standing in the old spot in every scene that had already saved it.
        var standingBench = FindRootObject("TestBench");
        if (standingBench != null
            && Mathf.Abs(standingBench.transform.position.x - BenchRoomX) > 0.01f)
        {
            standingBench.transform.position = new Vector3(BenchRoomX, 0f, PlayfieldPlaneZ);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorApplication.update += SaveSceneOnce;
            Debug.Log($"[ArkanoidSetup] Stage 98: moved the test bench room to x {BenchRoomX} and queued a scene save.");
            return;
        }

        // Stage 99: persist stage 98, gated on the scene file not yet naming the
        // room. The room's own name is the new state's signature, which is the
        // gate that cannot loop: it is a name nothing else in this scene carries.
        var benchScene = SceneManager.GetActiveScene();
        if (FindRootObject("TestBench") != null
            && !File.ReadAllText(ToAbsolute(benchScene.path)).Contains("TestBench"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 99: queued scene save for the next editor tick.");
            return;
        }

        // Stage 100: wire the bench — its four shape prefabs, the ball, and the
        // playfield's paddle as a template — and hand the room to the
        // GameManager. Only those: the materials and their varieties are read off
        // the GameManager at runtime rather than wired twice, so there is no
        // second copy to drift.
        //
        // An in-memory fact, so like stages 70, 76, 85, 87 and 95 this saves on
        // its own tick rather than through a paired disk-gated stage.
        var benchObject = FindRootObject("TestBench");
        var bench = benchObject != null ? benchObject.GetComponent<TestBench>() : null;
        var manager = FindRootObject("GameManager");
        var managerComponent = manager != null ? manager.GetComponent<GameManager>() : null;
        if (bench != null && managerComponent != null)
        {
            var benchSo = new SerializedObject(bench);
            var shapes = benchSo.FindProperty("shapePrefabs");
            var benchBall = benchSo.FindProperty("ballPrefab");
            var benchPaddle = benchSo.FindProperty("paddleTemplate");
            var shapePrefabs = LoadBenchShapes();
            var ballPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BallPrefabPath);
            // The playfield's paddle is authored scene content and has no prefab,
            // so the bench copies the object itself at runtime.
            var roundPaddleObject = FindRootObject("Paddle");
            var roundPaddle = roundPaddleObject != null
                ? roundPaddleObject.GetComponent<Paddle>() : null;

            var managerSo = new SerializedObject(managerComponent);
            var benchSlot = managerSo.FindProperty("testBench");

            bool shapesStale = shapePrefabs != null && BenchShapesDiffer(shapes, shapePrefabs);
            bool ballStale = ballPrefab != null
                && benchBall.objectReferenceValue != ballPrefab.GetComponent<Ball>();
            bool paddleStale = roundPaddle != null
                && benchPaddle.objectReferenceValue != roundPaddle;
            bool slotStale = benchSlot.objectReferenceValue != bench;

            if (shapesStale || ballStale || paddleStale || slotStale)
            {
                if (shapesStale)
                {
                    shapes.arraySize = shapePrefabs.Length;
                    for (int i = 0; i < shapePrefabs.Length; i++)
                        shapes.GetArrayElementAtIndex(i).objectReferenceValue = shapePrefabs[i];
                }
                if (ballStale) benchBall.objectReferenceValue = ballPrefab.GetComponent<Ball>();
                if (paddleStale) benchPaddle.objectReferenceValue = roundPaddle;
                benchSo.ApplyModifiedPropertiesWithoutUndo();

                if (slotStale)
                {
                    benchSlot.objectReferenceValue = bench;
                    managerSo.ApplyModifiedPropertiesWithoutUndo();
                }

                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                EditorApplication.update += SaveSceneOnce;
                Debug.Log("[ArkanoidSetup] Stage 100: wired the test bench and queued a scene save.");
                return;
            }
        }
    }

    // The four block shapes the bench offers, in the order the demonstration
    // board lays them out, so cycling through them on the bench walks the same
    // ladder a round shows. Null if any one is not readable back yet, so the
    // stage waits for a reload rather than wiring a hole.
    static Brick[] LoadBenchShapes()
    {
        var paths = new[]
        {
            BrickPrefabPath, RoundedBrickPrefabPath, HalfBrickPrefabPath, RoundBrickPrefabPath,
        };
        var shapes = new Brick[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
            shapes[i] = prefab != null ? prefab.GetComponent<Brick>() : null;
            if (shapes[i] == null) return null;
        }
        return shapes;
    }

    static bool BenchShapesDiffer(SerializedProperty slot, Brick[] shapes)
    {
        if (slot.arraySize != shapes.Length) return true;
        for (int i = 0; i < shapes.Length; i++)
            if (slot.GetArrayElementAtIndex(i).objectReferenceValue != shapes[i]) return true;
        return false;
    }

    // Each box block against the world-UV mesh it should be drawing. The same
    // shape of table MenuMeshes is, and for the same reason: stage 96 writes
    // these assets, and a rewrite over a path the prefab already references
    // destroys the object rather than updating it, so this is the standing
    // repair as well as the first wiring.
    static readonly (string Prefab, string Mesh)[] BlockBoxMeshes =
    {
        (BrickPrefabPath, BrickBoxMeshPath),
        (HalfBrickPrefabPath, HalfBrickBoxMeshPath),
    };

    // Whether the block box mesh on disk is inside out: every triangle of a
    // closed convex box should face away from its middle, so one that faces
    // back towards it is a face wound the wrong way round. Measured off the
    // first triangle, since the builder winds all twelve the same way — either
    // the whole box is right or the whole box is inverted.
    static bool BlockBoxWindingIsInverted()
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(BrickBoxMeshPath);
        if (mesh == null) return false;

        var vertices = mesh.vertices;
        var triangles = mesh.triangles;
        if (triangles.Length < 3) return false;

        var a = vertices[triangles[0]];
        var b = vertices[triangles[1]];
        var c = vertices[triangles[2]];
        var face = Vector3.Cross(b - a, c - a);
        // The box is centred on the origin, so a corner's own position is the
        // direction "outward" at that corner.
        return Vector3.Dot(face, a) < 0f;
    }

    static string FirstStaleBlockBoxMesh()
    {
        foreach (var (path, meshPath) in BlockBoxMeshes)
        {
            var wanted = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (wanted != null && BlockBoxMeshDiffers(path, meshPath)) return path;
        }
        return null;
    }

    // Compared by the asset the mesh *is*, not by object identity. Identity is
    // the wrong question here and asking it span this stage for ever: the mesh a
    // prefab resolves to is not guaranteed to be the same instance
    // LoadAssetAtPath hands back, so `sharedMesh != wanted` can be true of a
    // prefab that is already pointing at exactly the right asset — and then the
    // repair runs, and runs again on the next reload, and never settles. A path
    // is the durable fact, and it is also the one that answers the question the
    // stage actually has: is this prefab drawing the mesh at that path?
    static bool BlockBoxMeshDiffers(string prefabPath, string meshPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var filter = prefab != null ? prefab.GetComponent<MeshFilter>() : null;
        if (filter == null) return false;
        if (filter.sharedMesh == null) return true;
        return AssetDatabase.GetAssetPath(filter.sharedMesh) != meshPath;
    }

    // The grain density the shared Polymer material's fallback tiling is worked
    // out at. The same number GameManager.GrainTilesPerUnit holds for the
    // per-instance looks, written here because an editor script cannot read a
    // private const out of a runtime one — if either moves, both move.
    const float PolymerGrainTiles = 2f;

    // The first grain normal still sitting on the default importer type, or null
    // once every one of them has been imported as a normal map.
    static string FirstNonNormalMapGrain()
    {
        foreach (var grain in PolymerGrains)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(GrainNormalPath(grain.Name));
            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
                return grain.Name;
        }
        return null;
    }

    // The first block prefab whose authored UV span is not what its own mesh
    // actually does, or null once all four agree.
    static string FirstStaleGrainUvSpan()
    {
        foreach (var (path, uvPerUnit) in BrickGrainUvSpans)
            if (GrainUvSpanDiffers(path, uvPerUnit)) return path;
        return null;
    }

    static bool GrainUvSpanDiffers(string prefabPath, Vector2 uvPerUnit)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var brick = prefab != null ? prefab.GetComponent<Brick>() : null;
        if (brick == null) return false;
        var authored = new SerializedObject(brick).FindProperty("grainUvPerUnit").vector2Value;
        return (authored - uvPerUnit).sqrMagnitude > 0.0000001f;
    }

    // Every polymer grain map of one kind, in PolymerGrains' own order. Null if
    // any one of them is not readable back yet, so the stage that wants them
    // waits for a reload rather than wiring a hole.
    static Texture2D[] LoadGrains(System.Func<string, string> pathOf)
    {
        var textures = new Texture2D[PolymerGrains.Length];
        for (int i = 0; i < textures.Length; i++)
        {
            textures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(pathOf(PolymerGrains[i].Name));
            if (textures[i] == null) return null;
        }
        return textures;
    }

    // Whether the wired variety is anything other than exactly this one entry
    // holding exactly these textures — a wrong length anywhere, the wrong
    // material, or any slot holding the wrong asset or nothing at all.
    static bool PolymerVarietyDiffers(SerializedProperty varieties,
        Texture2D[] grains, Texture2D[] normals)
    {
        if (varieties.arraySize != 1) return true;
        var entry = varieties.GetArrayElementAtIndex(0);
        if (entry.FindPropertyRelative("material").enumValueIndex != (int)BlockMaterial.Polymer)
            return true;
        return TextureArrayDiffers(entry.FindPropertyRelative("grains"), grains)
            || TextureArrayDiffers(entry.FindPropertyRelative("grainNormals"), normals);
    }

    static bool TextureArrayDiffers(SerializedProperty slot, Texture2D[] textures)
    {
        if (slot.arraySize != textures.Length) return true;
        for (int i = 0; i < textures.Length; i++)
            if (slot.GetArrayElementAtIndex(i).objectReferenceValue != textures[i]) return true;
        return false;
    }

    static void FillTextureArray(SerializedProperty slot, Texture2D[] textures)
    {
        slot.arraySize = textures.Length;
        for (int i = 0; i < textures.Length; i++)
            slot.GetArrayElementAtIndex(i).objectReferenceValue = textures[i];
    }

    // Every menu mesh stage 40 writes over, against the object that draws it.
    // The authored words' letters are handled apart from this (MenuLetters),
    // since they are one mesh per letter against one child per letter.
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
        // The game over board's two arrows share their banners with the boards
        // above — the same two words on the same two shapes — so rewriting
        // either mesh nulls the reference here as well as there.
        ("MenuSlider/MenuOver/OptionHall", MenuArrowHallMeshPath),
        ("MenuSlider/MenuOver/OptionBack", MenuArrowBackMeshPath),
        ("MenuSlider/MenuOver/OptionHall/ArrowInlay", MenuInlayHallMeshPath),
        ("MenuSlider/MenuOver/OptionBack/ArrowInlay", MenuInlayBackMeshPath),
    };

    // Every block letter the menu draws, paired with the mesh asset it should be
    // drawing it with: the two authored words, ARKANOID on the title board and
    // GAME OVER on the game over board. (The hall's plaque and that board's
    // score are built at runtime, so there is nothing there to lose or put
    // back.) A word's blanks have no mesh, so the letters standing under it are
    // walked alongside the characters that have one rather than by child index.
    static List<(MeshFilter Drawn, string Mesh)> MenuLetters(Transform menu)
    {
        var letters = new List<(MeshFilter, string)>();
        Collect(menu.Find("MenuSlider/MenuBoard/MenuTitle"), MenuTitleWord, MenuLetterMeshPath);
        Collect(menu.Find("MenuSlider/MenuOver/MenuOverWord"), MenuOverWord, MenuOverLetterMeshPath);
        return letters;

        void Collect(Transform word, string text, System.Func<int, string> mesh)
        {
            if (word == null) return;
            int child = 0;
            for (int i = 0; i < text.Length && child < word.childCount; i++)
            {
                if (BlockText.Blank(text[i])) continue;
                var filter = word.GetChild(child++).GetComponent<MeshFilter>();
                if (filter != null) letters.Add((filter, mesh(i)));
            }
        }
    }

    // Whether anything the menu draws has lost the mesh it draws it with.
    static bool MissingMenuMesh(Transform menu)
    {
        foreach (var (drawn, _) in MenuLetters(menu))
            if (drawn.sharedMesh == null) return true;
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

    // The game over board's own letters, at its own cell. Numbered by the
    // letter's place in the word, blanks included, so the space between GAME and
    // OVER simply has no mesh of its own rather than shifting the numbering of
    // everything after it.
    static string MenuOverLetterMeshPath(int index) => $"{MenuLettersMeshFolder}/Over{index}.asset";

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

    // One grain, written as the two maps a block wears: a near-white albedo and
    // a normal map. Both come off the same height field, so the faint mottling
    // and the relief agree about where the bumps are.
    //
    // The height field is scattered discs with a smooth falloff, combined by
    // *max* rather than by sum — a sum of overlapping discs is a lumpy plateau
    // that normalises down to mush, where a max is a field of distinct rounded
    // caps, which is what moulded plastic is. Every disc is drawn wrapped, so
    // the tile is seamless in both directions and a random offset into it is as
    // good as any other (see BlockVariety.Roll).
    static void WriteGrainTextures(string name, GrainKind kind)
    {
        int size = GrainTextureSize;
        // Seeded off the name, so the three grains differ and any one of them
        // regenerates identically. Nothing downstream depends on the exact
        // pattern, but a texture that changed every reload would make every
        // screenshot comparison worthless.
        var random = new System.Random(name.GetHashCode());
        var height = new float[size * size];
        float normalStrength;

        switch (kind)
        {
            // Orange peel: one dense layer of medium caps, heavily overlapping,
            // which is the dominant look on the reference sheet.
            case GrainKind.Pebble:
                ScatterCaps(height, size, random, 220, 10f, 18f);
                normalStrength = 9f;
                break;
            // The tight even speckle of the pale tiles. Far more, far smaller:
            // at block size this reads as a softened sheen rather than as
            // countable bumps, which is the point of it.
            case GrainKind.Stipple:
                ScatterCaps(height, size, random, 1400, 3f, 6f);
                normalStrength = 4f;
                break;
            // Coarse granulate: big caps for the chunk, then a fine layer over
            // the top so the chunks themselves are not smooth.
            default:
                ScatterCaps(height, size, random, 90, 18f, 34f);
                ScatterCaps(height, size, random, 260, 5f, 10f);
                normalStrength = 13f;
                break;
        }

        WriteGrainAlbedo(GrainTexturePath(name), height, size);
        WriteGrainNormal(GrainNormalPath(name), height, size, normalStrength);
    }

    // Rounded caps laid down wrapped, combined by max. The falloff is
    // smoothstep on the radius, so a cap meets the flat around it without a
    // crease — a linear cone would put a hard rim in the normal map and read as
    // a field of spikes.
    static void ScatterCaps(float[] height, int size, System.Random random, int count,
        float minRadius, float maxRadius)
    {
        for (int i = 0; i < count; i++)
        {
            float cx = (float)random.NextDouble() * size;
            float cy = (float)random.NextDouble() * size;
            float radius = minRadius + (float)random.NextDouble() * (maxRadius - minRadius);
            // A cap's own height varies too, so the field is not one uniform
            // pebble repeated — the reference's grain is plainly uneven.
            float peak = 0.65f + 0.35f * (float)random.NextDouble();
            int reach = Mathf.CeilToInt(radius);

            for (int dy = -reach; dy <= reach; dy++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    if (distance > radius) continue;
                    // Wrapped, so the tile has no edge to seam at.
                    int x = (((int)cx + dx) % size + size) % size;
                    int y = (((int)cy + dy) % size + size) % size;
                    float value = peak * Mathf.SmoothStep(0f, 1f, 1f - distance / radius);
                    int index = y * size + x;
                    if (value > height[index]) height[index] = value;
                }
            }
        }
    }

    // Near white throughout: the grain's job in the albedo is only to keep the
    // surface from being one flat value, since the block's actual colour is the
    // per-instance tint multiplying this. Grayscale, like BrickWall.png, for the
    // same reason.
    static void WriteGrainAlbedo(string path, float[] height, int size)
    {
        const float contrast = 0.10f;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float value = 1f - contrast * (1f - height[y * size + x]);
                texture.SetPixel(x, y, new Color(value, value, value, 1f));
            }
        }
        texture.Apply();
        Directory.CreateDirectory(ToAbsolute(TexturesFolder));
        File.WriteAllBytes(ToAbsolute(path), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }

    // Tangent-space normals from the height field by central differences, taken
    // wrapped so the map tiles as seamlessly as the height did. `strength`
    // scales the gradient into the tilt a bump of that size should have: a cap
    // of radius r rises over r texels, so the slope is about 1/r and the
    // strength wanted is about r — which is why each grain carries its own.
    static void WriteGrainNormal(string path, float[] height, int size, float strength)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float left = height[y * size + (x - 1 + size) % size];
                float right = height[y * size + (x + 1) % size];
                float down = height[((y - 1 + size) % size) * size + x];
                float up = height[((y + 1) % size) * size + x];
                var normal = new Vector3(
                    -(right - left) * 0.5f * strength,
                    -(up - down) * 0.5f * strength,
                    1f).normalized;
                texture.SetPixel(x, y, new Color(
                    normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1f));
            }
        }
        texture.Apply();
        Directory.CreateDirectory(ToAbsolute(TexturesFolder));
        File.WriteAllBytes(ToAbsolute(path), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }

    // Four panels around the ball, so that a ball turning about the axis it
    // faces the camera down reads as turning rather than as sitting still.
    // Longitude is the texture's u on a stock sphere, so bands of u are wedges
    // meeting at the poles — a beach ball, seen from the side. Only half the
    // sphere is ever in view, which is 180° of that wrap: whatever the count,
    // the ball on screen is a light side and a dark side with a seam between
    // them, and the count decides how often that seam comes round. Four puts
    // one every quarter turn, which is what makes a short scuff read as a roll
    // at the dozen-odd pixels the ball is actually drawn at; more is finer than
    // those pixels can carry.
    //
    // Grayscale, so the material's tint keeps supplying the colour, exactly as
    // the brick wall does. How dark the dark panel goes is the whole of whether
    // this works: half brightness read as shading on a sphere rather than as a
    // mark on one, and the 0.22 tried after it took the dark side down to the
    // backdrop's own value, which loses half the ball against the murk it is
    // being tracked across. 0.38 is a mark that is unmistakably a mark and
    // still plainly lighter than anything behind it.
    static void WriteBallPanelTexture()
    {
        const int size = 64, panels = 4;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int x = 0; x < size; x++)
        {
            float value = x * panels / size % 2 == 0 ? 1f : 0.38f;
            for (int y = 0; y < size; y++)
                texture.SetPixel(x, y, new Color(value, value, value, 1f));
        }
        texture.Apply();
        Directory.CreateDirectory(ToAbsolute(TexturesFolder));
        File.WriteAllBytes(ToAbsolute(BallPanelTexturePath), texture.EncodeToPNG());
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

    // A box the size of a unit cube whose UVs are laid out in the *world* units
    // its faces will end up covering once the prefab's own scale is applied.
    // Geometry at unit size rather than at final size, deliberately: the block
    // prefabs carry their proportions in their transform scale and their crack
    // overlay is a child measured in that scale, so authoring the mesh at final
    // size and taking the scale to one would blow the crack up by the same
    // factor it shrank the box. The size is therefore baked into the UVs alone.
    //
    // This replaces the stock cube for the two box-shaped blocks, and it is the
    // only real fix for what the stock cube does to a tiled texture: it maps 0..1
    // across every face whatever that face measures, so one tiling that is right
    // for a 1.5-wide front is 2.5x too dense across a 0.6-deep side, and the
    // grain comes out combed into stripes there. No tiling value can fix that,
    // because the fault is that one number is being asked to serve faces of two
    // different sizes.
    static Mesh BuildWorldUvBoxMesh(string name, Vector3 size)
    {
        var mesh = new Mesh { name = name };
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        // The six outward normals, each paired with one in-plane axis; the other
        // is `Cross(normal, u)`, which is what keeps all six windings right
        // without six hand-checked sign conventions. That cross makes
        // `u x v == normal`, so with the corner order below every face comes out
        // clockwise as seen from outside it, which is Unity's front face.
        var faces = new[]
        {
            (Normal: Vector3.back, U: Vector3.right),
            (Normal: Vector3.forward, U: Vector3.left),
            (Normal: Vector3.right, U: Vector3.forward),
            (Normal: Vector3.left, U: Vector3.back),
            (Normal: Vector3.up, U: Vector3.right),
            (Normal: Vector3.down, U: Vector3.right),
        };

        foreach (var (normal, u) in faces)
        {
            var v = Vector3.Cross(normal, u);
            int start = vertices.Count;
            // How far the face reaches along each of its own axes once the
            // prefab's scale is on: the UV, and nothing else, is measured in it.
            float uExtent = Vector3.Scale(u, size).magnitude;
            float vExtent = Vector3.Scale(v, size).magnitude;

            for (int corner = 0; corner < 4; corner++)
            {
                float du = corner == 1 || corner == 2 ? 0.5f : -0.5f;
                float dv = corner >= 2 ? 0.5f : -0.5f;
                vertices.Add(normal * 0.5f + u * du + v * dv);
                uvs.Add(new Vector2(du * uExtent, dv * vExtent));
            }

            // The corner walk above already goes clockwise seen from outside the
            // face — `v` is `Cross(normal, u)`, so `u x v == normal` — and
            // clockwise-from-outside is Unity's front face. So the two triangles
            // are taken in that order and not reversed. Reversing them was the
            // first version of this and it turned every block inside out: with
            // back-face culling the near face is the one thrown away, so what is
            // drawn is the far face and the inside of the box, which reads as a
            // stepped, hollow, plainly corrupt shape rather than as a brick.
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }

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

    // One block material. Transparency is carried by the colour's alpha, which
    // is what ForceField and Crystal need and what nothing else uses, so the
    // opaque path stays the plain one.
    static void CreateBlockMaterial(string path, Color color, float metallic, float smoothness, Color emission)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", color.linear);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);

        if (emission != Color.black)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.linear);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        if (color.a < 1f)
        {
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            // URP defaults a transparent material to its preserve-specular path,
            // which sets _SrcBlend to One and blends the block *additively* — a
            // force field that came out a solid glowing cyan rather than a pane
            // anything could be seen through. Turned off, this is plain alpha.
            material.SetFloat("_BlendModePreserveSpecular", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        AssetDatabase.CreateAsset(material, path);
    }

    // Whether the wired array is anything other than exactly these materials in
    // exactly this order — a wrong length, or any slot holding the wrong asset
    // or nothing at all.
    static bool BlockMaterialsDiffer(SerializedProperty slot, Material[] materials)
    {
        if (slot.arraySize != materials.Length) return true;
        for (int i = 0; i < materials.Length; i++)
            if (slot.GetArrayElementAtIndex(i).objectReferenceValue != materials[i]) return true;
        return false;
    }

    // Every block material, loaded in BlockMaterial's own order for the array
    // GameManager holds. Null if any one of them is not readable back yet.
    static Material[] LoadBlockMaterials()
    {
        var materials = new Material[BlockMaterials.Count];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = AssetDatabase.LoadAssetAtPath<Material>(BlockMaterialPath((BlockMaterial)i));
            if (materials[i] == null) return null;
        }
        return materials;
    }

    // Whether the rounded brick's mesh was built at a corner radius other than
    // the one the constant now says. The outline's topmost vertices sit at
    // x = +-(width / 2 - radius), so the mesh reports its own radius.
    static bool RoundedBrickRadiusDiffers()
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(RoundedBrickMeshPath);
        if (mesh == null) return false;

        float top = float.MinValue, flat = 0f;
        foreach (var vertex in mesh.vertices) top = Mathf.Max(top, vertex.y);
        foreach (var vertex in mesh.vertices)
            if (vertex.y > top - 0.001f) flat = Mathf.Max(flat, Mathf.Abs(vertex.x));

        return Mathf.Abs(flat - (BrickWidth / 2f - RoundedBrickCornerRadius)) > 0.01f;
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
        var overBoard = BuildGameOverBoard(slider.transform, titleMaterial, labelMaterial);

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
        menuSo.FindProperty("over").objectReferenceValue = overBoard;
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
    // It sits to the *left* of the board, so that the left-pointing arrow that
    // leads here scrolls left to reach it and the right-pointing one on it
    // scrolls right to go back — two screens' width now, with the board a lost
    // round ends on standing between the two.
    static void BuildHallOfFame(Transform slider, Material nameMaterial, Material scoreMaterial)
    {
        var hall = new GameObject("MenuHall");
        hall.transform.SetParent(slider, false);
        hall.transform.localPosition = new Vector3(MenuHallBoardX, 0f, 0f);
        var component = hall.AddComponent<HallOfFame>();

        var nameLine = CreateBlockLine(hall.transform, "ChampionName", HallNameY, nameMaterial);
        var scoreLine = CreateBlockLine(hall.transform, "ChampionScore", HallScoreY, scoreMaterial);
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

    // A line of lettering nobody can author: a champion's name or score, or the
    // score a round was just lost with. Empty here — `BlockLine` hangs one
    // hittable block per symbol off it at runtime and takes the material for
    // them from this renderer, so the line object itself is the anchor and the
    // look of the line rather than a mesh of its own.
    static MeshFilter CreateBlockLine(Transform parent, string name, float y, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, y, MenuPlaneZ);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go.AddComponent<MeshFilter>();
    }

    // The board a lost round comes to rest on: GAME OVER over the score it ended
    // with, the hall of fame one arrow to the left and the menu one to the right.
    // It stands between those two inside the slider, because every arrow scrolls
    // the way it points and those are the two places this board leads.
    //
    // The words are authored here exactly as the title's letters are — one
    // hittable block per letter, the space between them skipped, since a
    // collider standing in the gap would bounce the ball off nothing. The score
    // line is left empty: the number is only known when a round ends, so
    // GameOverBoard builds it at runtime the way the hall builds a champion.
    static GameOverBoard BuildGameOverBoard(Transform slider, Material wordMaterial, Material labelMaterial)
    {
        var over = new GameObject("MenuOver");
        over.transform.SetParent(slider, false);
        over.transform.localPosition = new Vector3(MenuOverBoardX, 0f, 0f);
        var component = over.AddComponent<GameOverBoard>();

        // Tilted like the title, and for the title's reason: the letters' tops
        // and sides catch the light and read as blocks rather than as a cutout.
        // The tilt is about X, which 2D physics ignores.
        var word = new GameObject("MenuOverWord");
        word.transform.SetParent(over.transform, false);
        word.transform.localPosition = new Vector3(0f, MenuOverWordY, MenuPlaneZ);
        word.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);
        for (int i = 0; i < MenuOverWord.Length; i++)
        {
            if (BlockText.Blank(MenuOverWord[i])) continue;
            var letter = new GameObject($"Letter{i}-{MenuOverWord[i]}");
            letter.transform.SetParent(word.transform, false);
            letter.transform.localPosition =
                new Vector3(BlockText.GlyphCentreX(MenuOverWord, i, MenuOverCell), 0f, 0f);
            letter.AddComponent<MeshFilter>().sharedMesh =
                AssetDatabase.LoadAssetAtPath<Mesh>(MenuOverLetterMeshPath(i));
            letter.AddComponent<MeshRenderer>().sharedMaterial = wordMaterial;
            letter.AddComponent<BoxCollider2D>().size =
                new Vector2(BlockText.GlyphWidth * MenuOverCell, BlockText.GlyphHeight * MenuOverCell);
            letter.AddComponent<MenuTitleBlock>();
        }

        // The words are stone and the number is white, which is the split the
        // hall's plaque has always made: a name is lettering, a score is a
        // readout, and the two are told apart by what they are cut from.
        var scoreLine = CreateBlockLine(over.transform, "OverScore", MenuOverScoreY, labelMaterial);

        CreateArrowOption(over.transform, "OptionHall", MainMenuOption.HallOfFame,
            -MenuArrowX, MenuOverArrowY, false, MenuOptionRecordsMaterialPath, MenuArrowHallMeshPath,
            MenuInlayHallMeshPath, labelMaterial);
        CreateArrowOption(over.transform, "OptionBack", MainMenuOption.BackToMenu,
            MenuArrowX, MenuOverArrowY, true, MenuOptionStartMaterialPath, MenuArrowBackMeshPath,
            MenuInlayBackMeshPath, labelMaterial);

        var overSo = new SerializedObject(component);
        overSo.FindProperty("scoreLine").objectReferenceValue = scoreLine;
        overSo.FindProperty("wordCell").floatValue = MenuOverCell;
        overSo.ApplyModifiedPropertiesWithoutUndo();
        return component;
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
