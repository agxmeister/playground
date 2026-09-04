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

    // Ceramics' surface grains, written by the same machinery for the same
    // reason — one height field, read out twice as a near-white albedo and a
    // normal map — but built by carving rather than by scattering. Polymer's
    // three characters are all *raised*: moulded plastic is a field of caps
    // standing off a flat, and ScatterCaps is a field of caps. Every character
    // on the ceramic reference is the opposite of that — a smooth glaze with
    // something cut into it or stained through it: the craquelure net of a
    // crackle glaze, the veining of a marble tile, the cloud in the body of an
    // alabaster one. So these three start the height field at the surface
    // (Flood) and take material away (CarveCrackle, CarveVeins, Cloud), which
    // the two readouts already handle without a change: the albedo darkens
    // wherever the field is low and the normal tilts into it, so a groove comes
    // out dark and recessed exactly as a cap comes out pale and proud.
    //
    // The fine porcelain grit of the reference's matte tiles is deliberately
    // *not* one of the three. It sits under all three instead (ScatterGrit),
    // which is both what the reference shows — the speckle is there behind the
    // veining as much as it is on its own — and what keeps the two characters
    // that are nearly pure pigment from having no relief at all to be seen with
    // on a face the light does not reach.
    static readonly (string Name, GrainKind Kind)[] CeramicsGrains =
    {
        ("CeramicsCrackle", GrainKind.Crackle),
        ("CeramicsVein", GrainKind.Vein),
        ("CeramicsCloud", GrainKind.Cloud),
    };
    static string GrainTexturePath(string name) => TexturesFolder + "/" + name + ".png";
    static string GrainNormalPath(string name) => TexturesFolder + "/" + name + "Normal.png";
    // 1024 px laid at half a tile per unit (BlockGrainTiles) is the same
    // 512 texels a unit the original 256 px at two tiles a unit gave, but a
    // tile that spans two units is wider than any block face, so no face ever
    // shows the same patch of grain twice. At 256 px a full slab carried three
    // copies of the tile side by side, and with the coarse Crumb grain's few
    // big caps the repeat was plain to see.
    const int GrainTextureSize = 1024;
    // How much bigger that tile is than the 256 px the grains were authored
    // at: cap radii scale by it and cap counts by its square, so the grain
    // comes out the same size on a block as it always did.
    const float GrainTileScale = GrainTextureSize / 256f;
    // What every one of Polymer's three grains spends on pigment. One number
    // for the three of them because the trade behind it is the same for all
    // three (see WriteGrainAlbedo), and named rather than repeated so that
    // retuning the plastic's is one edit.
    const float PolymerContrast = 0.30f;
    // Texels to a world unit on a block's face: the tile's own pixels over the
    // world span BlockGrainTiles lays them across. Every *carved* feature is
    // sized against this rather than in texels, and the distinction is worth
    // the constant. A grain's caps are a property of the surface and are
    // authored in texels of the 256 px sheet they were drawn on (GrainTileScale
    // carries them to whatever size the tile is now); a crack or a vein is a
    // feature of the *block* — how many cells cross a slab, whether a vein
    // reads at the hundred-odd pixels a block is drawn at — and has to be given
    // in the units the block is measured in. The first vein written in texels
    // came out four times too long and the tile read as a bowl of noodles.
    const float GrainTexelsPerUnit = GrainTextureSize * BlockGrainTiles;

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

    // Ceramics' band. Narrow and bright where Polymer's is the whole sheet,
    // because the reference is one substance rather than every colour a pellet
    // is pressed in: fired white body under a clear glaze at the top, the grey
    // marble tiles at the bottom, and a warmth through all of it that the
    // plastic has none of. Stopping well short of black is the point of the
    // bottom end — Polymer already goes there, and a x1 ceramic that could come
    // out as dark as Neutronium would be throwing away the one thing a band is
    // for.
    static readonly Color CeramicsDarkest = new Color(0.400f, 0.395f, 0.375f, 1f);
    static readonly Color CeramicsLightest = new Color(0.955f, 0.945f, 0.915f, 1f);
    // A glaze is a glaze whatever it is over, so the two ends sit far closer
    // together than plastic's do, and both are glossier than the shared
    // BlockCeramics asset's own 0.08 — that number is unglazed bisque, and the
    // reference is glazed. The dark end still leads, for the same reason it
    // does in plastic: the grey tiles on the sheet are the polished ones.
    const float CeramicsDarkSmoothness = 0.50f;
    const float CeramicsLightSmoothness = 0.32f;
    // Half Polymer's. Fired clay varies from batch to batch in *warmth* rather
    // than in hue — a green ceramic is a different glaze, not the same one
    // twice — and the band is bright enough throughout that even this shows.
    const float CeramicsHueJitter = 0.015f;

    // Every material with a surface of its own: the grains it may be moulded
    // with and the band its batch colour is drawn from. A material with no
    // entry wears its shared asset untouched, which is still every material but
    // these two.
    //
    // This table is what stages 91-95 walk, and it is why they are plural. Each
    // of them held Polymer by name, and the second material could not be a
    // near-copy of the five of them: stage 95 wired the varieties by truncating
    // the list to its one entry, so a second stage appending its own would have
    // been undone on the following reload and the two would have taken turns for
    // ever. Adding a third surfaced material is now an entry here and nothing
    // else.
    //
    // Declared below the constants it reads because it reads them: static field
    // initialisers run in the order they are written, and a table standing above
    // the colours it names would be a table of black.
    static readonly (BlockMaterial Material, (string Name, GrainKind Kind)[] Grains,
        Color Darkest, Color Lightest, float DarkSmoothness, float LightSmoothness,
        float HueJitter)[] BlockSurfaces =
    {
        (BlockMaterial.Polymer, PolymerGrains, PolymerDarkest, PolymerLightest,
            PolymerDarkSmoothness, PolymerLightSmoothness, PolymerHueJitter),
        (BlockMaterial.Ceramics, CeramicsGrains, CeramicsDarkest, CeramicsLightest,
            CeramicsDarkSmoothness, CeramicsLightSmoothness, CeramicsHueJitter),
    };

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

    // Which relief a grain is, across both reference sheets. Polymer's three
    // are the characters that dominate a sheet of moulded plastic: the
    // orange-peel most of it wears, the tight speckle of the pale tiles, the
    // coarse granulate of the dark ones. Ceramics' three are carved rather than
    // scattered (see CeramicsGrains) and are the characters of a fired tile: a
    // crackle glaze's craquelure net, a marble's veining, an alabaster's cloud.
    enum GrainKind
    {
        Pebble,
        Stipple,
        Crumb,
        Crackle,
        Vein,
        Cloud,
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
    // How many steps of wear the net is drawn in. Must equal Brick.CrackStages
    // — the component indexes one flat array as `stage * variants + variant`
    // and this is the stride's other half.
    const int CrackStageCount = Brick.CrackStages;
    // 128 px at 128 pixels per unit, so the sprite's native size stays exactly
    // one world unit and the Cracks child goes on covering its brick by
    // inheriting the root's scale. The old net was 32 px, which was enough for
    // three or four hairlines and is not enough for a mesh of them.
    const int CrackTextureSize = 128;
    // Rows of cells down the net. The columns follow from the block's own
    // proportions (see WriteCrackTextures), so three rows across a 0.5-high
    // brick puts a cell at 1/6 of a unit — coarse enough that a whole cell is
    // visible from the player's seat, fine enough that a slab holds a couple
    // of dozen of them.
    const int CrackNetRows = 3;
    // What each stage of the net has grown to, as a share of the edges in it,
    // ordered outward from where the block was struck. The first showing is
    // already a third of the net rather than a single cell: one cell of
    // craquelure at this size reads as a speck, not as damage.
    static readonly float[] CrackStageShares = { 0.34f, 0.58f, 0.80f, 1f };

    // The chip decals: a flake of glaze gone at the point of impact, for the
    // materials brittle enough to lose one (see BlockMaterials.Chips). Four
    // shapes rather than four stages — a chip is one event at one place, and
    // Brick picks a shape per hit and turns it at a random angle, so four is
    // already more distinct chips than a block lives long enough to show.
    const int ChipVariantCount = 4;
    // 32 px at 32 pixels per unit, so a chip sprite is one world unit before
    // Brick scales it down to ChipSize. A quarter of the crack net's 128, and
    // for the same reason the net is 128: the texture is sized to the pixels
    // it is actually drawn across. A block is about 130 px wide on a 1080p
    // screen and a chip is a sixth of a unit, so a chip lands on some 15 px —
    // and these sprites are imported without mipmaps, so a texture far over
    // that size is minified detail turning into sparkle rather than detail.
    const int ChipTextureSize = 32;
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
    // How far a box block's edges are cut back, in world units. Cosmetic only:
    // the BoxCollider2D is untouched, so the ball still reflects off the square
    // outline the block used to draw — the bevel is a face the light catches,
    // not a shape the physics knows about. A hard 90-degree edge under this
    // scene's single head-on light returns the same value on both sides of
    // itself and reads as a printed rectangle; a chamfer that is one facet wide
    // puts a bright line down the top edge and a dark one down the bottom, which
    // is what says "moulded" rather than "drawn". 0.03 is roughly four pixels on
    // the block's edge at the framing the round is played at: enough to catch,
    // too little to change the silhouette anyone is aiming at.
    const float BlockBevel = 0.03f;
    // How far each end of a box block is drawn in, front to back — a draft
    // angle, the taper a moulded part carries so it can leave its tool. Here it
    // is doing a job the bevel could not.
    //
    // A square end face is the worst surface this scene can present. Its normal
    // is (+/-1, 0, 0), which is exactly perpendicular to the key light, so it
    // takes none of it; and the camera sees it at about 72 degrees of incidence,
    // where a dielectric's Fresnel term climbs towards a mirror. What is left is
    // a flat, pale, blue reflection of the ambient sky standing beside a warm
    // block face, and it reads as a different material rather than as the side
    // of the same one. Measured on a dark Polymer block, the strip's blue cast
    // was b−r of +17 against the face's −8.
    //
    // Neither of the obvious answers works on it. A fill light cannot: on a
    // near-black albedo the diffuse term is a couple of percent, and the blue is
    // a *reflection* rather than an absence of light (a white fill at 0.5 moved
    // it by 6 of 255). Tinting that fill warm cannot either, because a
    // normal-mapped front face catches a side light too, so anything added to
    // the end is added to the face beside it — measured, the gap came out a
    // shade wider.
    //
    // A taper answers both halves at once, and it is geometry rather than
    // lighting. At 0.2 over the 0.6 depth the end's normal becomes about
    // (0.96, 0, -0.29): it now takes roughly 29% of the key light, so it is lit,
    // warm and carries its grain; and the camera meets it at about 52 degrees
    // instead of 72, which is off the steep part of the Fresnel curve. **The
    // front face keeps its full size** — the taper runs backwards, away from the
    // player — so the silhouette a ball is aimed at is unchanged, and so is the
    // collider, which was only ever the front outline.
    // **Zero: a block is a box, and its back face is the same size as its
    // front.** The taper is kept as a dial rather than deleted because the
    // reasoning it came from is still sound — it is just no longer needed here.
    //
    // It was introduced to fix the end faces. A square end has a normal exactly
    // perpendicular to the key light, so it took none of it, and the camera met
    // it at about 72 degrees where a dielectric's Fresnel term climbs towards a
    // mirror; what was left was a flat pale reflection of the environment beside
    // a warm face. Tapering the block turned that end into a facet the key light
    // could reach and dropped the viewing angle off the steep part of the curve,
    // and it worked — the strip went from about eight pixels to one.
    //
    // What superseded it is the edge shading being flattened (see the normals
    // in BuildWorldUvBoxMesh): every edge surface now takes the *front* face's
    // normal, so an end face shades as the face whatever angle it is really at.
    // With that in place the taper buys no light at all and costs the block its
    // shape — a drafted box is visibly not a box, most of all on the translucent
    // materials where the far side shows through the near one.
    const float BlockDraft = 0f;
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
    //
    // 50 to begin with, then 30, now 12, and the direction of travel is the
    // point: every time this has been looked at with fresh eyes the answer has
    // been "less". At 30 a screen full of blocks reads as a screen full of hard
    // black rectangles — each shadow is a second copy of its block, offset far
    // enough to be seen as its own shape rather than as the block's. tan(12) is
    // about a fifth of tan(30), so the drop is now a fifth of what it was: it
    // reads as the block standing off the wall, which is all the shadow was ever
    // being asked to say.
    //
    // Chosen on the bench (see "Aiming the key light" in CLAUDE.md) rather than
    // reasoned to, and it travels with `KeyShadowStrength` below — the pair was
    // settled together and neither number means much without the other.
    const float LightPitch = 12f;

    // How dark the key light draws its shadows. Full black was never chosen; it
    // is simply what a Unity light does, and against this scene's dark backdrop
    // a full-strength shadow is the blackest thing on the screen — darker than
    // any block, so the eye is caught by the shadow before the object.
    //
    // 0.7 keeps it clearly a shadow while letting the murk go on showing through
    // it, which is the same argument `SoftShadows.Darkness` is written to. It
    // does not move the shadow: `LightPitch` says where it falls, this says how
    // heavily it lands, and the two were settled together on the bench.
    const float KeyShadowStrength = 0.7f;
    // Two fill lights stand either side of the rooms, aimed dead horizontally,
    // and they exist for one face: a block's ends. The main light points along
    // +Z and tilts down, so for a face whose normal is +/-X the dot product is
    // exactly zero — those faces get no direct light at all, and a grain that
    // lives in a normal map has nothing to catch. They came back as flat pale
    // panels beside every block off the centre of the screen, which at this
    // camera is most of them: a 0.6-deep block seen 28 degrees off axis shows
    // about a sixth of its width as end face.
    //
    // Aiming them *exactly* horizontally is what keeps them mostly off the faces
    // the main light already owns: a direction of (+/-1, 0, 0) has a zero dot
    // with the flat front, top and bottom faces, so what they reach is the ends,
    // the bevels and the curved shapes' flanks. "Mostly" and not "entirely",
    // measured: a normal-mapped face's shading normals are tilted off its
    // geometric one, so the grain on the front face catches these too. That is
    // wanted where it happens — a side light is what makes moulding read — but
    // it is why they cannot be used to change the ends' colour alone.
    //
    // Their shadows are off, which is the other half of it. A fill that cast
    // would put a second shadow of every object on the backdrop, and the
    // shadows in this scene are the depth cue the whole look rests on — one
    // light, one shadow. At 0.5 an end face comes out at a bit over half the
    // front's brightness, which reads as the shaded side of a solid object;
    // brighter and the block starts to look lit from three directions at once.
    const float FillLightIntensity = 0f;
    // A neutral sky, standing in for the URP template's procedural one.
    //
    // `m_AmbientMode` is Skybox, so the skybox is not scenery here — the camera
    // clears to a solid colour and never draws it — it is the *only* source of
    // ambient light and of the environment reflection. The template's is the
    // default procedural sky, and it is blue. That is where every blue cast
    // measured on this scene's blocks came from: an end face, a bevel, a
    // silhouette pixel — anywhere the key light does not reach, what is left is
    // a blue sky, and it sits beside a warm block face and reads as a foreign
    // material rather than as shading.
    //
    // Neutral rather than *dimmed*, deliberately. The four metal materials have
    // no reflection probe in this scene and take their entire look from this
    // probe (see "A block is a shape and a material"), so turning the ambient
    // down would flatten Titanium, Inconel, Waspaloy and Neutronium to nothing.
    // A grey sky of the same strength costs them no light at all and costs the
    // blocks their blue edge.
    const string NeutralEnvironmentPath = TexturesFolder + "/NeutralEnvironment.cubemap";
    // The flat ambient's own colour: neutral, at about the strength the blue
    // sky was giving. Neutral is the whole point — every "grey border" on a
    // block this session was this light, and it was only ever conspicuous
    // because it was *blue* beside a warm face, not because it was bright.
    // Written in sRGB and converted with .linear on the way in, like every other
    // colour here.
    static readonly Color AmbientColor = new Color(0.23f, 0.23f, 0.23f);
    // What the environment returns, in every direction. Proven to be the right
    // target before being built: `reflectionIntensity` is applied at shading
    // time and so cannot be stale, and dropping it to zero took the border
    // column count from 1 to 0 in a single reload. That named the environment
    // reflection as the source without any argument about caches.
    static readonly Color EnvironmentColor = new Color(0.30f, 0.30f, 0.30f);
    // White, and warm was tried and measured and abandoned. The end faces read
    // blue against a warm block face (b−r of +17 on the strip against −8 on the
    // face), and a warm fill looks like the obvious answer to that. It is not:
    // at 1.2 warm the strip's cast improved to +11 but the *face* went to −15,
    // so the gap between them came out a shade wider than it started. The
    // reason is the grain — a normal-mapped face's shading normals are tilted
    // off −Z, so a horizontal light lands on the front face's bumps too, and
    // anything added to the ends is added to the front along with it. No light
    // colour closes a gap it widens at both ends.
    static readonly Color FillLightColor = Color.white;

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

        // Stage 11: crack overlay textures on disk — the net at each of its
        // four stages, for each of the four random shape variants.
        bool crackTexturesMissing = false;
        for (int variant = 0; variant < CrackVariantCount; variant++)
            for (int stage = 0; stage < CrackStageCount; stage++)
                crackTexturesMissing |= !File.Exists(ToAbsolute(CrackTexturePath(stage, variant)));
        if (crackTexturesMissing)
        {
            WriteCrackTextures();
            AssetDatabase.Refresh();
            Debug.Log($"[ArkanoidSetup] Stage 11: wrote {CrackVariantCount * CrackStageCount} crack net textures.");
            return;
        }

        // Stage 12: import crack textures as 1-unit sprites. The pixels per
        // unit is the texture's own size on purpose: the sprite has to come out
        // exactly one world unit whatever resolution the net is drawn at, since
        // the Cracks child covers its brick by inheriting the root's scale.
        bool cracksImported = true;
        for (int variant = 0; variant < CrackVariantCount; variant++)
            for (int stage = 0; stage < CrackStageCount; stage++)
                cracksImported &= ConfigureSpriteImporter(CrackTexturePath(stage, variant), CrackTextureSize);
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
            var cracks = LoadCrackSprites();
            if (cracks == null)
            {
                Debug.Log("[ArkanoidSetup] Crack sprites not importable yet, waiting for next reload.");
                return;
            }
            AddCracksToBrickPrefab(cracks);
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

        // Stage 28: crack-net retrofit. Two overhauls now share this stage,
        // because the second supersedes the first rather than following it: the
        // brick prefab once predated the per-variant sprite arrays on Brick,
        // and every brick prefab has since had a light/heavy pair of arrays
        // where it now needs one flat stage-major array of the net. Both are
        // the same repair — put the current sprite set on whatever block
        // prefabs are standing — so the guard is written against the current
        // end state and fires on either.
        //
        // It covers all four block prefabs rather than only the first, and it
        // stands here, ahead of the stage that creates the other three, on
        // purpose: a from-scratch run reaches that stage later and builds them
        // with the right array in the first place, so the ones this finds are
        // always ones an older run left behind.
        var stalePrefabs = BrickPrefabsMissingCrackNet();
        if (stalePrefabs.Count > 0)
        {
            var cracks = LoadCrackSprites();
            if (cracks == null)
            {
                Debug.Log("[ArkanoidSetup] Crack sprites not importable yet, waiting for next reload.");
                return;
            }
            foreach (var prefabPath in stalePrefabs) WireCrackSpritesIntoPrefab(prefabPath, cracks);
            AssetDatabase.DeleteAsset(LegacyCrackLightTexturePath);
            AssetDatabase.DeleteAsset(LegacyCrackHeavyTexturePath);
            for (int variant = 0; variant < CrackVariantCount; variant++)
            {
                AssetDatabase.DeleteAsset(LegacyCrackTexturePath("Light", variant));
                AssetDatabase.DeleteAsset(LegacyCrackTexturePath("Heavy", variant));
            }
            Debug.Log($"[ArkanoidSetup] Stage 28: wired the crack net into {stalePrefabs.Count} block prefab(s).");
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
        var sceneLight = MainSceneLight();
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
            var cracks = LoadCrackSprites();
            if (brickMaterial == null || roundedMesh == null || cracks == null)
            {
                Debug.Log("[ArkanoidSetup] Brick variant dependencies not loadable yet, waiting for next reload.");
                return;
            }
            CreateHalfBrickPrefab(brickMaterial, cracks);
            CreateRoundedBrickPrefab(brickMaterial, roundedMesh, cracks);
            CreateRoundBrickPrefab(brickMaterial, cracks);
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

        // Stage 46: aim the key light at LightPitch. Written to flatten the 50
        // degrees stage 34 set, and since made a **standing repair**, which is
        // what makes LightPitch a live dial: it re-aims a light that does not
        // match the constant instead of only pulling down one that is steeper
        // than it. The old one-sided guard (`> LightPitch + 1`) would have
        // silently ignored the constant ever being *raised*, and a guard that
        // can only fail in one direction is the kind that reads as a passing
        // check while doing nothing.
        //
        // It saves on its own tick now, the way stages 70, 76, 85, 87, 95 and
        // 101 do, because the rotation is an in-memory fact and needs no disk
        // gate to be sure of. Stage 47 below still carries the gate for the
        // original 50-degree scene, which is a different question.
        if (sceneLight != null && Mathf.Abs(Mathf.DeltaAngle(sceneLight.transform.eulerAngles.x, LightPitch)) > 0.1f)
        {
            sceneLight.transform.rotation = Quaternion.Euler(LightPitch, 0f, 0f);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorApplication.update += SaveSceneOnce;
            Debug.Log($"[ArkanoidSetup] Stage 46: aimed the light at {LightPitch} degrees and queued a scene save.");
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

        // Stage 91: every surfaced material's grains, two PNGs each — a
        // near-white albedo and a normal map. The guard is any one of those
        // files being absent, so retuning a grain means deleting its PNGs and
        // letting this write them again, keeping their `.meta` files the way
        // stage 79's ball texture is retuned: a new guid would break the
        // references stage 95 hands the GameManager.
        //
        // Only the missing ones are written, which is what makes the guard worth
        // reading per file rather than per material: retuning one grain of one
        // material costs one grain, and a material appended to BlockSurfaces
        // needs no more than that to get its surface.
        if (FirstMissingGrain() != null)
        {
            Directory.CreateDirectory(ToAbsolute(TexturesFolder));
            int written = 0;
            foreach (var surface in BlockSurfaces)
                foreach (var grain in surface.Grains)
                {
                    if (!GrainMapsMissing(grain.Name)) continue;
                    WriteGrainTextures(grain.Name, grain.Kind);
                    written++;
                }
            AssetDatabase.Refresh();
            Debug.Log($"[ArkanoidSetup] Stage 91: wrote {written} grains, albedo and normal.");
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
            foreach (var surface in BlockSurfaces)
                foreach (var grain in surface.Grains)
                {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(GrainNormalPath(grain.Name));
                    if (importer == null || importer.textureType == TextureImporterType.NormalMap) continue;
                    importer.textureType = TextureImporterType.NormalMap;
                    importer.SaveAndReimport();
                }
            Debug.Log("[ArkanoidSetup] Stage 92: imported the grain normals as normal maps.");
            return;
        }

        // Stage 93: give each surfaced material's shared asset its own first
        // grain, so a block that never gets a per-instance look (a level that
        // leaves the varieties unwired, or anything spawning a brick outside
        // BuildLevel) still comes out surfaced rather than flat. It is also what
        // turns the shader's normal-map path on: a keyword is a fact about the
        // material and cannot be overridden per instance, so every per-block
        // grain in the game is riding on this one call — and a new surfaced
        // material whose asset this loop misses has no relief at all, whatever
        // its variety hands the renderer.
        if (FirstFlatSharedMaterial() != null)
        {
            foreach (var surface in BlockSurfaces)
            {
                var shared = AssetDatabase.LoadAssetAtPath<Material>(
                    BlockMaterialPath(surface.Material));
                var defaultGrain = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    GrainTexturePath(surface.Grains[0].Name));
                var defaultGrainNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    GrainNormalPath(surface.Grains[0].Name));
                if (shared == null || defaultGrain == null || defaultGrainNormal == null) continue;
                if (shared.GetTexture("_BaseMap") == defaultGrain) continue;
                shared.SetTexture("_BaseMap", defaultGrain);
                shared.SetTexture("_BumpMap", defaultGrainNormal);
                shared.EnableKeyword("_NORMALMAP");
                // The full slab's own tiling, which is what this fallback would
                // most likely be seen on. A per-instance look overrides both.
                var slab = new Vector2(
                    BlockGrainTiles * BrickWidth, BlockGrainTiles * BrickHeight);
                shared.SetTextureScale("_BaseMap", slab);
                shared.SetTextureScale("_BumpMap", slab);
                EditorUtility.SetDirty(shared);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ArkanoidSetup] Stage 93: put a grain and its normal on the surfaced materials.");
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

        // Stage 95: hand the surfaced materials' varieties to the GameManager,
        // one list entry per entry in BlockSurfaces and in its order — the list
        // is matched by material value rather than by index (GameManager.
        // VarietyOf), so the order is only ever this table's own.
        //
        // A standing repair for the same reason stage 87 is: stage 91 rewrites
        // texture assets, and
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
        if (varietyManagerComponent != null && AllGrainsLoadable())
        {
            var varietySo = new SerializedObject(varietyManagerComponent);
            var varieties = varietySo.FindProperty("blockVarieties");
            if (VarietiesDiffer(varieties))
            {
                varieties.arraySize = BlockSurfaces.Length;
                for (int i = 0; i < BlockSurfaces.Length; i++)
                {
                    var surface = BlockSurfaces[i];
                    var entry = varieties.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("material").enumValueIndex = (int)surface.Material;
                    FillTextureArray(entry.FindPropertyRelative("grains"),
                        LoadGrains(surface.Grains, GrainTexturePath));
                    FillTextureArray(entry.FindPropertyRelative("grainNormals"),
                        LoadGrains(surface.Grains, GrainNormalPath));
                    entry.FindPropertyRelative("darkest").colorValue = surface.Darkest;
                    entry.FindPropertyRelative("lightest").colorValue = surface.Lightest;
                    entry.FindPropertyRelative("darkSmoothness").floatValue = surface.DarkSmoothness;
                    entry.FindPropertyRelative("lightSmoothness").floatValue = surface.LightSmoothness;
                    entry.FindPropertyRelative("hueJitter").floatValue = surface.HueJitter;
                }
                varietySo.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                EditorApplication.update += SaveSceneOnce;
                Debug.Log($"[ArkanoidSetup] Stage 95: wired {BlockSurfaces.Length} varieties and queued a scene save.");
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
        //
        // The guard also fires when the mesh on disk is not chamfered to the
        // current BlockBevel, which is how the bevel is retuned: change the
        // constant and the meshes are rewritten, and stage 97 below — which is
        // already the standing repair for what an asset rewrite does to a
        // prefab's reference — puts them back on the two prefabs.
        if (!File.Exists(ToAbsolute(BrickBoxMeshPath))
            || BlockBoxWindingIsInverted()
            || BlockBoxBevelIsStale()
            || BlockBoxDraftIsStale()
            || BlockBoxIsFaceted())
        {
            Directory.CreateDirectory(ToAbsolute(MeshesFolder));
            AssetDatabase.CreateAsset(
                BuildWorldUvBoxMesh("BrickBox", new Vector3(BrickWidth, BrickHeight, BrickDepth)),
                BrickBoxMeshPath);
            AssetDatabase.CreateAsset(
                BuildWorldUvBoxMesh("HalfBrickBox", new Vector3(HalfBrickWidth, BrickHeight, BrickDepth)),
                HalfBrickBoxMeshPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ArkanoidSetup] Stage 96: wrote the world-UV block box meshes, bevelled {BlockBevel}, drafted {BlockDraft}.");
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

        // Stage 103: a neutral environment. Two settings, and between them they
        // are the last of the "grey border" on a block (see "A square block's
        // edges are cut").
        //
        // What the border actually is: the drafted end facet and the bevel take
        // almost no key light, so what they return is the environment — and the
        // environment was blue. Against a warm block face that reads as a
        // foreign material rather than as shading, at any width, down to the one
        // pixel it had shrunk to.
        //
        // **The reflection is a flat cubemap, not a sky, and that distinction is
        // the fix.** The first attempt hung a `Skybox/Procedural` material with a
        // grey tint and called it neutral. It is not: that shader simulates
        // Rayleigh scattering, which is *why* real skies are blue, so a grey
        // tint on it is still a blue sky — measured, the border column stayed at
        // b−r of +9.5 with it hung. Six grey faces have no gradient to be blue
        // in. `defaultReflectionMode` is therefore Custom rather than Skybox.
        //
        // Neutral rather than dimmer, deliberately: the four metals have no
        // reflection probe in this scene and take their entire look from this
        // one (see "A block is a shape and a material"), so turning the
        // environment down would flatten Titanium, Inconel, Waspaloy and
        // Neutronium to nothing. A grey environment of the same strength costs
        // them no light and costs the blocks their blue edge.
        //
        // A standing repair, and an in-memory fact, so it saves on its own tick.
        if (RenderSettings.ambientMode != UnityEngine.Rendering.AmbientMode.Flat
            || RenderSettings.ambientLight != AmbientColor.linear
            || RenderSettings.defaultReflectionMode
                != UnityEngine.Rendering.DefaultReflectionMode.Custom
            || RenderSettings.customReflectionTexture == null
            || AssetDatabase.GetAssetPath(RenderSettings.customReflectionTexture)
                != NeutralEnvironmentPath)
        {
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(NeutralEnvironmentPath);
            if (cube == null)
            {
                Directory.CreateDirectory(ToAbsolute(TexturesFolder));
                cube = BuildFlatCubemap(EnvironmentColor);
                AssetDatabase.CreateAsset(cube, NeutralEnvironmentPath);
                AssetDatabase.SaveAssets();
            }
            // Flat ambient, for the same reason the reflection is a cubemap:
            // with AmbientMode.Skybox the ambient probe is cached off the sky and
            // hanging a different sky does not refresh it. The neutral sky was
            // assigned, saved, and the rendered frame came back *byte for byte
            // identical* — as clear a "this did nothing" as a measurement gets,
            // and DynamicGI.UpdateEnvironment did not clear it either. Flat mode
            // reads this field directly, so it is live.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientColor.linear;
            RenderSettings.defaultReflectionMode =
                UnityEngine.Rendering.DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = cube;
            RenderSettings.reflectionIntensity = 1f;
            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 103: hung a neutral environment and queued a scene save.");
            return;
        }

        // Stage 101: the two horizontal fill lights (see FillLightIntensity).
        // Each is named for where it stands rather than for where it points, so
        // FillLeft is the one on the left of the rooms shining rightwards, and
        // it is the right-hand end faces it reaches. Directional lights are
        // global, so one pair serves the playfield, the menu and the bench
        // alike and none of the three needs its own.
        //
        // A standing repair rather than a one-off build, so FillLightIntensity
        // is a live dial: it re-aims and re-dims a fill that does not match the
        // constants instead of only creating a missing one. The guard is an
        // in-memory fact (a light is as bright as it is), so this saves on its
        // own tick the way stages 70, 76, 85, 87 and 95 do — but stage 102 below
        // still carries the disk gate for the *first* build, since a scene that
        // has never had the lights is a different question from one whose lights
        // have drifted.
        if (FillLightIsStale("FillLeft", 90f) || FillLightIsStale("FillRight", -90f))
        {
            SetUpFillLight("FillLeft", 90f);
            SetUpFillLight("FillRight", -90f);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorApplication.update += SaveSceneOnce;
            Debug.Log($"[ArkanoidSetup] Stage 101: stood the two horizontal fill lights at {FillLightIntensity} and queued a scene save.");
            return;
        }

        // Stage 102: persist stage 101, gated on the scene file not yet naming
        // the left one. The new state's own signature, per the rule the bench's
        // save gate is written to: a name nothing else in this scene carries,
        // rather than a pattern some other object could legitimately match.
        var fillScene = SceneManager.GetActiveScene();
        if (FindRootObject("FillLeft") != null
            && !File.ReadAllText(ToAbsolute(fillScene.path)).Contains("FillLeft"))
        {
            EditorApplication.update += SaveSceneOnce;
            Debug.Log("[ArkanoidSetup] Stage 102: queued scene save for the next editor tick.");
            return;
        }

        // Stage 104: how dark the key light's shadows are drawn (see
        // KeyShadowStrength). A standing repair on an in-memory fact — a light
        // is as strong as it is — so it saves on its own tick and carries no
        // disk gate, per the rule that a stage which saves only when it has just
        // changed something cannot loop by construction.
        //
        // Separate from stage 46 rather than folded into it, though the two
        // numbers were chosen together: they are independent facts about the
        // light, and a stage that wrote both would re-aim a light whose pitch
        // was fine because its strength had drifted, or the other way about.
        var strengthLight = MainSceneLight();
        if (strengthLight != null && Mathf.Abs(strengthLight.shadowStrength - KeyShadowStrength) > 0.001f)
        {
            strengthLight.shadowStrength = KeyShadowStrength;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorApplication.update += SaveSceneOnce;
            Debug.Log($"[ArkanoidSetup] Stage 104: set the key light's shadow strength to {KeyShadowStrength} and queued a scene save.");
            return;
        }

        // Stage 105: the chip decals on disk — one PNG per variant shape (see
        // WriteChipTextures). Retuning them is the same recipe the crack net is
        // retuned by: delete Assets/Sprites/Chip*.png, keep the .meta files so
        // the guids the block prefabs hold survive, and let this write them
        // again.
        bool chipTexturesMissing = false;
        for (int variant = 0; variant < ChipVariantCount; variant++)
            chipTexturesMissing |= !File.Exists(ToAbsolute(ChipTexturePath(variant)));
        if (chipTexturesMissing)
        {
            WriteChipTextures();
            AssetDatabase.Refresh();
            Debug.Log($"[ArkanoidSetup] Stage 105: wrote {ChipVariantCount} chip decals.");
            return;
        }

        // Stage 106: import them as 1-unit sprites, for the same reason the
        // crack net is imported that way — Brick scales a chip to the size it
        // wants in world units, which only works if the sprite's own size is
        // one.
        bool chipsImported = true;
        for (int variant = 0; variant < ChipVariantCount; variant++)
            chipsImported &= ConfigureSpriteImporter(ChipTexturePath(variant), ChipTextureSize);
        if (!chipsImported)
        {
            Debug.Log("[ArkanoidSetup] Stage 106: configured the chip sprite importers.");
            return;
        }

        // Stage 107: hand the chips to every block prefab. There is no child to
        // add and no prefab hierarchy to change — a chip is created at runtime
        // where the ball landed, so all a prefab carries is the bag of shapes
        // to pick from. Which is also why the four prefab builders are left
        // alone: this stage stands after the one that creates them, so a
        // from-scratch run reaches it with all four standing and wires them
        // here, and there is no second path to keep in step.
        var chipless = BlockPrefabsMissingChips();
        if (chipless.Count > 0)
        {
            var chips = LoadChipSprites();
            if (chips == null)
            {
                Debug.Log("[ArkanoidSetup] Chip sprites not importable yet, waiting for next reload.");
                return;
            }
            foreach (var prefabPath in chipless) WireChipSpritesIntoPrefab(prefabPath, chips);
            Debug.Log($"[ArkanoidSetup] Stage 107: wired the chip decals into {chipless.Count} block prefab(s).");
            return;
        }
    }

    // A cubemap of one colour on all six faces. Small on purpose: the shader
    // samples a blurred mip for a rough surface and the top one for a smooth
    // surface, and every mip of a flat colour is that colour.
    static Cubemap BuildFlatCubemap(Color color)
    {
        const int size = 16;
        var cube = new Cubemap(size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        foreach (CubemapFace face in System.Enum.GetValues(typeof(CubemapFace)))
        {
            if (face == CubemapFace.Unknown) continue;
            cube.SetPixels(pixels, face);
        }
        cube.Apply();
        return cube;
    }

    // One fill: a directional light lying flat, casting nothing. `yaw` is the
    // whole of its aim — no pitch at all, because a pitch is exactly what would
    // spill it onto the faces the main light already owns. Creates the object if
    // it is missing and otherwise corrects the one standing, so the constants
    // above are the single source of truth for both.
    static void SetUpFillLight(string name, float yaw)
    {
        var go = FindRootObject(name) ?? new GameObject(name);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        var light = go.GetComponent<Light>() ?? go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.shadows = LightShadows.None;
        light.intensity = FillLightIntensity;
        light.color = FillLightColor.linear;
    }

    static bool FillLightIsStale(string name, float yaw)
    {
        var go = FindRootObject(name);
        if (go == null) return true;
        var light = go.GetComponent<Light>();
        if (light == null) return true;
        var wanted = FillLightColor.linear;
        return Mathf.Abs(light.intensity - FillLightIntensity) > 0.001f
            || Mathf.Abs(light.color.r - wanted.r) > 0.005f
            || Mathf.Abs(light.color.g - wanted.g) > 0.005f
            || Mathf.Abs(light.color.b - wanted.b) > 0.005f
            || Quaternion.Angle(go.transform.rotation, Quaternion.Euler(0f, yaw, 0f)) > 0.5f;
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

    // Whether the block box mesh on disk carries a bevel of the current width.
    // Measured off the mesh's own vertices rather than gated on the file being
    // absent, for the same reason stage 89's guard measures the rounded block's
    // corner radius: the path is the one it has always been written to, so a
    // file-existence gate could never repair a mesh that is present and cut to
    // an older number.
    //
    // The measurement: every vertex sitting on one of the two side faces
    // (|x| = 0.5) reaches exactly the bevelled half-extent up the Y axis, so the
    // furthest of them is `0.5 - BlockBevel / BrickHeight` in local units. An
    // unbevelled box reaches the full 0.5 there and measures as a bevel of zero.
    static bool BlockBoxBevelIsStale()
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(BrickBoxMeshPath);
        if (mesh == null) return false;

        // Keyed on the *widest* vertices rather than on a literal 0.5, which is
        // what the draft below cost this guard the first time round: the taper
        // pulls every vertex in by some amount, so after it there is no vertex
        // at the full 0.5 any more, the search found nothing, and the guard
        // quietly answered "not stale" to every question. A guard that can only
        // fail silently is worse than no guard. The Y coordinates are untouched
        // by the taper, so measuring them against the widest X is exact.
        float widest = 0f;
        foreach (var vertex in mesh.vertices) widest = Mathf.Max(widest, Mathf.Abs(vertex.x));
        if (widest <= 0f) return false;

        float reach = 0f;
        foreach (var vertex in mesh.vertices)
            if (Mathf.Abs(Mathf.Abs(vertex.x) - widest) < 0.001f)
                reach = Mathf.Max(reach, Mathf.Abs(vertex.y));
        if (reach <= 0f) return false;

        return Mathf.Abs((0.5f - reach) * BrickHeight - BlockBevel) > 0.001f;
    }

    // Whether the block box mesh on disk carries the current draft, measured off
    // its own vertices the way the bevel above is, and for the same reason.
    //
    // The taper is linear in z, so it can be solved from two widths — but from
    // the right two, and the first cut of this got that wrong and left the stage
    // rewriting the meshes on every reload. The widest vertex is *not* at the
    // full 0.5: it sits on the front edge of a side face at z = -(0.5 - bevel/
    // depth), where the taper has already run a sliver of its course. Treating
    // it as untapered measured a draft of 0.192 where 0.2 was written, which is
    // outside any tolerance tight enough to be worth having.
    //
    // With b = bevel/depth, the two side-face edges sit at z + 0.5 = b and
    // 1 - b, so their widths are in the ratio r = (1 - k(1-b)) / (1 - k b) for a
    // local taper k. Inverting that gives k = (1 - r) / ((1 - b) - r b), and the
    // draft in world units is k * width / 2. An untapered box answers zero.
    // Whether the mesh on disk is flat-shaded across its bevel. A guard on the
    // *shading* is needed beside the two on the shape because smoothing the
    // chamfer moved no vertex at all: geometry-only guards would have looked at
    // a mesh that needed rewriting and seen nothing to complain about.
    //
    // The measurement is a count. With the edge shading flattened, every vertex
    // takes either the front normal or the back one, so the whole box holds
    // exactly **two** distinct normals. Earlier versions held six (one per face,
    // with the bevel smoothed into its neighbours) or twenty-six (one per
    // polygon, flat-shaded). Neither is a state this builder produces any more,
    // so the comparison is exact rather than a threshold — and it fires on both
    // older meshes, which is what makes this the repair as well as the check.
    static bool BlockBoxIsFaceted()
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(BrickBoxMeshPath);
        if (mesh == null) return false;

        var distinct = new HashSet<Vector3Int>();
        foreach (var normal in mesh.normals)
            distinct.Add(new Vector3Int(
                Mathf.RoundToInt(normal.x * 100f),
                Mathf.RoundToInt(normal.y * 100f),
                Mathf.RoundToInt(normal.z * 100f)));
        return distinct.Count != 2;
    }

    //
    // Both meshes are measured, not just the slab's. The taper is a proportion,
    // so the half-brick's expected draft in world units is its own share of the
    // slab's — and when the rule changed from "a fixed distance" to "a fixed
    // proportion" the slab's number did not move at all. A guard that only ever
    // looked at BrickBox would have seen nothing to do and left the half-brick
    // standing as the wedge that prompted the change.
    static bool BlockBoxDraftIsStale() =>
        DraftIsStale(BrickBoxMeshPath, BrickWidth)
        || DraftIsStale(HalfBrickBoxMeshPath, HalfBrickWidth);

    static bool DraftIsStale(string path, float width)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null) return false;

        float front = 0f, back = 0f;
        foreach (var vertex in mesh.vertices)
        {
            if (vertex.z < 0f) front = Mathf.Max(front, Mathf.Abs(vertex.x));
            else back = Mathf.Max(back, Mathf.Abs(vertex.x));
        }
        if (front <= 0f || back <= 0f) return false;

        float b = BlockBevel / BrickDepth;
        float ratio = back / front;
        float taper = (1f - ratio) / ((1f - b) - ratio * b);
        // The wanted draft for this mesh is its own share of the slab's, which
        // is what "the same proportion on every box block" means in world units.
        float wanted = BlockDraft * width / BrickWidth;
        return Mathf.Abs(taper * width / 2f - wanted) > 0.002f;
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

    // The grain density the shared materials' fallback tiling is worked out at.
    // The same number GameManager.GrainTilesPerUnit holds for the per-instance
    // looks, written here because an editor script cannot read a private const
    // out of a runtime one — if either moves, both move.
    const float BlockGrainTiles = 0.5f;

    // The first grain normal still sitting on the default importer type, or null
    // once every one of them has been imported as a normal map.
    static string FirstNonNormalMapGrain()
    {
        foreach (var surface in BlockSurfaces)
            foreach (var grain in surface.Grains)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(GrainNormalPath(grain.Name));
                if (importer != null && importer.textureType != TextureImporterType.NormalMap)
                    return grain.Name;
            }
        return null;
    }

    // Whether a grain's two maps are not both on disk. Either one missing is the
    // whole grain missing: they come off one height field and a half-written
    // pair would put a relief on a block that its albedo disagreed with.
    static bool GrainMapsMissing(string name) =>
        !File.Exists(ToAbsolute(GrainTexturePath(name)))
        || !File.Exists(ToAbsolute(GrainNormalPath(name)));

    // The first grain that has not been written yet, or null once every one of
    // them is on disk.
    static string FirstMissingGrain()
    {
        foreach (var surface in BlockSurfaces)
            foreach (var grain in surface.Grains)
                if (GrainMapsMissing(grain.Name)) return grain.Name;
        return null;
    }

    // The first surfaced material whose shared asset is not wearing its own
    // first grain, or null once every one of them is. Read off the base map
    // rather than off the keyword, because a material can carry `_NORMALMAP`
    // from any number of things and only the texture says *which* grain.
    static string FirstFlatSharedMaterial()
    {
        foreach (var surface in BlockSurfaces)
        {
            var shared = AssetDatabase.LoadAssetAtPath<Material>(
                BlockMaterialPath(surface.Material));
            var defaultGrain = AssetDatabase.LoadAssetAtPath<Texture2D>(
                GrainTexturePath(surface.Grains[0].Name));
            if (shared == null || defaultGrain == null) continue;
            if (shared.GetTexture("_BaseMap") != defaultGrain) return surface.Material.ToString();
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

    // One material's grain maps of one kind, in its own table's order. Null if
    // any one of them is not readable back yet, so the stage that wants them
    // waits for a reload rather than wiring a hole.
    static Texture2D[] LoadGrains((string Name, GrainKind Kind)[] grains,
        System.Func<string, string> pathOf)
    {
        var textures = new Texture2D[grains.Length];
        for (int i = 0; i < textures.Length; i++)
        {
            textures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(pathOf(grains[i].Name));
            if (textures[i] == null) return null;
        }
        return textures;
    }

    // Whether every grain of every surfaced material can be read back as an
    // asset. Stage 95 wires nothing until they all can: a variety wired around
    // one unloadable texture is the null slot that stage the guard exists to
    // catch, written deliberately.
    static bool AllGrainsLoadable()
    {
        foreach (var surface in BlockSurfaces)
            if (LoadGrains(surface.Grains, GrainTexturePath) == null
                || LoadGrains(surface.Grains, GrainNormalPath) == null) return false;
        return true;
    }

    // Whether the wired varieties are anything other than exactly BlockSurfaces
    // — a wrong length, an entry against the wrong material, a texture slot
    // holding the wrong asset or nothing at all, or a band that has since been
    // retuned.
    //
    // The bands are part of the guard and were not always: with only the
    // textures read, moving CeramicsLightest left the scene wearing the old
    // number with every file on disk agreeing, which is a look that cannot be
    // tuned. They are compared loosely because the round trip through the scene
    // file is decimal text — an exact float compare would fire on every reload
    // for ever and queue a save with it.
    static bool VarietiesDiffer(SerializedProperty varieties)
    {
        if (varieties.arraySize != BlockSurfaces.Length) return true;
        for (int i = 0; i < BlockSurfaces.Length; i++)
        {
            var surface = BlockSurfaces[i];
            var entry = varieties.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("material").enumValueIndex != (int)surface.Material)
                return true;
            if (TextureArrayDiffers(entry.FindPropertyRelative("grains"),
                    LoadGrains(surface.Grains, GrainTexturePath))
                || TextureArrayDiffers(entry.FindPropertyRelative("grainNormals"),
                    LoadGrains(surface.Grains, GrainNormalPath)))
                return true;
            if (ColorDiffers(entry.FindPropertyRelative("darkest").colorValue, surface.Darkest)
                || ColorDiffers(entry.FindPropertyRelative("lightest").colorValue, surface.Lightest)
                || FloatDiffers(entry.FindPropertyRelative("darkSmoothness").floatValue, surface.DarkSmoothness)
                || FloatDiffers(entry.FindPropertyRelative("lightSmoothness").floatValue, surface.LightSmoothness)
                || FloatDiffers(entry.FindPropertyRelative("hueJitter").floatValue, surface.HueJitter))
                return true;
        }
        return false;
    }

    static bool FloatDiffers(float wired, float authored) => Mathf.Abs(wired - authored) > 0.0005f;

    static bool ColorDiffers(Color wired, Color authored) =>
        FloatDiffers(wired.r, authored.r) || FloatDiffers(wired.g, authored.g)
        || FloatDiffers(wired.b, authored.b) || FloatDiffers(wired.a, authored.a);

    static bool TextureArrayDiffers(SerializedProperty slot, Texture2D[] textures)
    {
        // Nothing to compare against is not a difference: the caller has already
        // decided to wait for a reload (AllGrainsLoadable).
        if (textures == null) return false;
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

    static string CrackTexturePath(int stage, int variant) =>
        SpritesFolder + "/CrackNet" + stage + variant + ".png";

    // The light/heavy pair each variant used to be, kept only so the retrofit
    // that replaces them with the net has something to delete.
    static string LegacyCrackTexturePath(string weight, int variant) =>
        SpritesFolder + "/Crack" + weight + variant + ".png";

    // A block cracks in a *net*: a lattice of irregular cells whose edges are
    // the cracks, the way a glaze crazes or a windscreen goes. That is what
    // the four stages per variant draw, each one the same net grown further
    // out from the point the block was first struck — so a block that has been
    // hit four times shows the crack of a block hit once with more of it, and
    // not a different crack.
    //
    // Two things about the geometry are worth knowing before touching the
    // numbers:
    //
    // **The net is authored pre-squashed.** The sprite is one unit square and
    // the Cracks child inherits its brick's scale, so on a 1.5 x 0.5 slab
    // everything in this texture is stretched three to one. A square lattice
    // authored here would arrive as a course of wide bricks. So the columns
    // are the rows times the block's own aspect: cells that are three times
    // narrower than they are tall in texture space, and square on the screen.
    // The two full-slot shapes and the rounded one are all 1.5 x 0.5 and get
    // this exactly right; the half-width block (1.5:1) and the round one
    // (square) are stretched less than the correction assumes and show cells
    // that lean tall. One sprite set serves four shapes, which is the trade —
    // the alternative is a texture set per aspect, and the two that are off
    // are the two small blocks.
    //
    // **The perimeter of the lattice is not drawn.** Edges lying along the
    // outer boundary would frame the block in a rectangle, which reads as a
    // border rather than as damage; skipping them leaves a net whose cracks
    // still run out to the edges of the face.
    //
    // Written white with the coverage in the alpha, because the colour is not
    // a fact about the texture: Brick tints the renderer from the colour of
    // the block the net is drawn on (see Brick.CrackTint).
    static void WriteCrackTextures()
    {
        int rows = CrackNetRows;
        int columns = Mathf.RoundToInt(rows * (BrickWidth / BrickHeight));

        for (int variant = 0; variant < CrackVariantCount; variant++)
        {
            var random = new System.Random(2026 + variant);
            var nodes = BuildCrackNetNodes(random, columns, rows);
            var edges = BuildCrackNetEdges(random, nodes, columns, rows);

            for (int stage = 0; stage < CrackStageCount; stage++)
            {
                int drawn = Mathf.Max(1, Mathf.RoundToInt(edges.Count * CrackStageShares[stage]));
                WriteCrackTexture(CrackTexturePath(stage, variant), edges, drawn, stage);
            }
        }
    }

    // The lattice's corners, each pushed off its grid position by up to
    // CrackNodeJitter of a cell so no two cells come out the same shape. Nodes
    // on the boundary slide along it but never off it: a crack that stopped
    // short of the edge would leave a cell floating in the middle of an
    // untouched face.
    const float CrackNodeJitter = 0.34f;

    static Vector2[,] BuildCrackNetNodes(System.Random random, int columns, int rows)
    {
        float cellWidth = (float)CrackTextureSize / columns;
        float cellHeight = (float)CrackTextureSize / rows;
        var nodes = new Vector2[columns + 1, rows + 1];
        for (int i = 0; i <= columns; i++)
            for (int j = 0; j <= rows; j++)
            {
                bool edgeColumn = i == 0 || i == columns;
                bool edgeRow = j == 0 || j == rows;
                float jitterX = edgeColumn ? 0f : (float)(random.NextDouble() * 2 - 1) * CrackNodeJitter * cellWidth;
                float jitterY = edgeRow ? 0f : (float)(random.NextDouble() * 2 - 1) * CrackNodeJitter * cellHeight;
                nodes[i, j] = new Vector2(i * cellWidth + jitterX, j * cellHeight + jitterY);
            }
        return nodes;
    }

    // How many of the lattice's edges simply are not there. A complete net is
    // a grid; a net with holes in it is a crack. A sixth of them missing is
    // enough to break every straight run of three without opening a hole big
    // enough to read as a gap.
    const float CrackEdgeDropChance = 0.17f;

    // How far off centre the impact the net spreads from may sit, as a share
    // of the face. Kept inside the middle half so that no variant puts its
    // origin in a corner, where the first stage would be a scuff on one end of
    // the block rather than a crack in it.
    const float CrackOriginSpread = 0.25f;

    // The edges of the lattice, ordered outward from the impact — which is the
    // whole of how a stage knows what to draw: stage n takes the first share
    // of this list. The distance is roughed up by a random factor so the net
    // does not grow as a visible circle; a crack front is uneven.
    static List<(Vector2 From, Vector2 To)> BuildCrackNetEdges(
        System.Random random, Vector2[,] nodes, int columns, int rows)
    {
        var origin = new Vector2(
            CrackTextureSize * (0.5f + (float)(random.NextDouble() * 2 - 1) * CrackOriginSpread),
            CrackTextureSize * (0.5f + (float)(random.NextDouble() * 2 - 1) * CrackOriginSpread));

        var ranked = new List<(float Rank, Vector2 From, Vector2 To)>();
        void Consider(Vector2 from, Vector2 to)
        {
            if (random.NextDouble() < CrackEdgeDropChance) return;
            float distance = Vector2.Distance((from + to) * 0.5f, origin);
            ranked.Add((distance * (float)(0.75 + random.NextDouble() * 0.5), from, to));
        }

        for (int i = 0; i < columns; i++)
            for (int j = 1; j < rows; j++)
                Consider(nodes[i, j], nodes[i + 1, j]);
        for (int i = 1; i < columns; i++)
            for (int j = 0; j < rows; j++)
                Consider(nodes[i, j], nodes[i, j + 1]);

        ranked.Sort((a, b) => a.Rank.CompareTo(b.Rank));
        return ranked.ConvertAll(entry => (entry.From, entry.To));
    }

    // One stage's texture: the first `drawn` edges of the net, each broken
    // into three segments about a pair of points nudged off the straight line,
    // since a crack that runs true is a scored line and not a broken one.
    //
    // Edges that have been there for two stages are drawn a pixel wider. A
    // crack does not only spread, it opens, and this is the cheaper half of
    // saying so: the oldest cracks on a nearly-broken block are the heavy ones
    // and the newest are hairlines, which is the same reading the sprite's
    // rising opacity gives from further away.
    static void WriteCrackTexture(
        string path, List<(Vector2 From, Vector2 To)> edges, int drawn, int stage)
    {
        var texture = new Texture2D(CrackTextureSize, CrackTextureSize, TextureFormat.RGBA32, false);
        var coverage = new float[CrackTextureSize * CrackTextureSize];
        int widened = Mathf.RoundToInt(edges.Count * CrackStageShares[Mathf.Max(0, stage - 2)]);
        for (int i = 0; i < drawn && i < edges.Count; i++)
        {
            var (from, to) = edges[i];
            bool wide = stage >= 2 && i < widened;
            // Seeded off the edge's own index, so the jag on an edge is the
            // same jag at the next stage: this is one crack opening, not four
            // drawn one after another.
            foreach (var (a, b) in JagCrackEdge(new System.Random(2026 + i), from, to))
                DrawCoverageLine(coverage, a, b, wide);
        }

        for (int y = 0; y < CrackTextureSize; y++)
            for (int x = 0; x < CrackTextureSize; x++)
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(coverage[y * CrackTextureSize + x])));

        texture.Apply();
        File.WriteAllBytes(ToAbsolute(path), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }

    // How far a crack wanders off the straight line between two lattice
    // corners, as a share of that line's own length.
    const float CrackEdgeWander = 0.14f;

    // One edge as three segments, its two interior points pushed sideways off
    // the line — both to the *same* side, by a random share of the first's
    // offset, so the edge bows. Two independent offsets was the first version
    // and it put a hard zigzag in every other edge: a crack wanders, it does
    // not switchback twice in a sixth of an inch.
    static List<(Vector2, Vector2)> JagCrackEdge(System.Random random, Vector2 from, Vector2 to)
    {
        var along = to - from;
        var across = new Vector2(-along.y, along.x).normalized * (along.magnitude * CrackEdgeWander);
        float bow = (float)(random.NextDouble() * 2 - 1);
        var first = from + along / 3f + across * bow;
        var second = from + along * (2f / 3f) + across * bow * (float)(0.25 + random.NextDouble() * 0.75);
        return new List<(Vector2, Vector2)>
        {
            (from, first), (first, second), (second, to),
        };
    }

    // Half the width of a crack in pixels, hairline and opened.
    const float CrackHairlineRadius = 0.55f;
    const float CrackOpenedRadius = 1f;

    // A line laid into a coverage buffer rather than straight into pixels, so
    // that two cracks meeting at a lattice corner do not draw over one another
    // — the brightest of the two wins and the joint stays a joint.
    //
    // It is drawn by dragging a soft round brush along the line rather than by
    // setting the nearest pixel at each step, and that is not a refinement.
    // The nearest-pixel version came out as a **string of beads**: on anything
    // near a diagonal it leaves pixels touching only at their corners, and the
    // sprite is filtered bilinearly onto a block three times the texture's
    // size, which turns every one of those corners into a gap. A brush whose
    // edge falls off across a pixel is what makes the line continuous at the
    // size it is actually seen at.
    static void DrawCoverageLine(float[] coverage, Vector2 from, Vector2 to, bool wide)
    {
        float radius = wide ? CrackOpenedRadius : CrackHairlineRadius;
        // Three samples a pixel, so consecutive brush stamps overlap heavily
        // and the line has no scalloping along its length either.
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) * 3f));
        for (int i = 0; i <= steps; i++)
            Stamp(coverage, Vector2.Lerp(from, to, (float)i / steps), radius);
    }

    // One brush stamp: every pixel within a pixel of the brush's edge takes
    // coverage in proportion to how far inside it sits.
    static void Stamp(float[] coverage, Vector2 point, float radius)
    {
        int reach = Mathf.CeilToInt(radius) + 1;
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int x = Mathf.RoundToInt(point.x) + dx;
                int y = Mathf.RoundToInt(point.y) + dy;
                if (x < 0 || y < 0 || x >= CrackTextureSize || y >= CrackTextureSize) continue;
                float distance = Vector2.Distance(new Vector2(x, y), point);
                float amount = Mathf.Clamp01(radius + 0.5f - distance);
                int index = y * CrackTextureSize + x;
                if (coverage[index] < amount) coverage[index] = amount;
            }
    }


    static string ChipTexturePath(int variant) => SpritesFolder + "/Chip" + variant + ".png";

    // How much of the texture the flake itself spans, as a share of its width,
    // and how far its corners wander off that. The rest of the texture is the
    // room the splinters around it need.
    const float ChipFlakeRadius = 0.27f;
    const float ChipRadiusJitter = 0.34f;

    // How many corners a flake has, and how far each one may slide round the
    // circle from its even share of it. Eight straight facets rather than a
    // smooth outline, because that is how a glaze actually lets go: a fired
    // surface fractures in flats and corners, and an outline drawn as a
    // wandering radius comes out as a droplet — which was the first version,
    // and it read as a bead of water sitting on the block. The angle jitter is
    // what stops the flats coming out all the same length.
    const int ChipFacets = 8;
    const float ChipAngleJitter = 0.40f;

    // How far from round a flake is drawn, along an axis of its own: a share
    // of every point's offset added back along that axis. A spall is longer
    // one way than the other, and eight facets on a circle are still a circle.
    const float ChipStretch = 0.30f;

    // Four numbers make the flake a hollow rather than a blob, and all four are
    // about where the light in this room does or does not reach. The key light
    // comes down onto the field at LightPitch, so a pit is lit exactly one way:
    // the wall at the top of it shades the floor beneath, the wall at the
    // bottom faces up into the light, and the glaze's own broken edge is a hard
    // line all the way round. That asymmetry is the whole of what makes a
    // crater read as a crater on a photograph, and swapping it top for bottom
    // is what makes one read as a dome.
    //
    // - `ChipFloor` is the exposed body itself: below the face it was taken
    //   out of, because the floor of a hollow is a surface turned away from a
    //   light that is nearly head-on.
    // - `ChipUndercut` is what the floor loses directly under the break above
    //   it, reaching `ChipUndercutPixels` down into the flake.
    // - `ChipLip` is the far wall catching that light, within `ChipLipPixels`
    //   of the outline below.
    // - `ChipRimShade` is the break itself, a hairline of it — `ChipRimPixels`
    //   wide, and no wider. This was a third of the flake's radius in the first
    //   version, which left no flat floor at all, only one smooth ramp from
    //   edge to middle: a smooth ramp is a dome, and four variants of it read
    //   as four beads of water sitting on the block.
    const float ChipFloor = 0.70f;
    const float ChipUndercut = 0.30f;
    const float ChipUndercutPixels = 5f;
    const float ChipLip = 1f;
    const float ChipLipPixels = 3f;
    const float ChipRimShade = 0.30f;
    const float ChipRimPixels = 1.3f;

    // The scatter of much smaller flakes around the main one, thrown off the
    // same impact. Without them a chip is one clean shape and reads as a
    // sticker; with them the face is *damaged* around the place it was hit.
    const int ChipSplinters = 5;
    const float ChipSplinterNear = 1.15f;
    const float ChipSplinterFar = 1.45f;
    const float ChipSplinterSmallest = 0.12f;
    const float ChipSplinterLargest = 0.28f;

    // A ceramic loses a flake of glaze where it is struck, and that is a
    // different kind of damage from the net the same hit crazes into it (see
    // WriteCrackTextures): the net is a state the whole face is in, drawn once
    // across the block and grown stage by stage, while a chip is one event at
    // one place. So these are four *shapes* rather than four stages — Brick
    // picks one per hit, turns it to a random angle and stamps it where the
    // ball landed.
    //
    // **Written as pure shadow: black throughout, with the shading in the
    // alpha.** A chip needs modelling the crack net does not — a dark broken
    // rim, an undercut under the top edge, a lighter floor below it — and the
    // first version carried that as grey in the RGB, tinted by the block's own
    // colour. It came out *cold*: measured on the bench, a chip on a cream
    // ceramic rendered (123,122,121) against a face of (253,225,192). The
    // reason is worth keeping, because it is a fact about every unlit overlay
    // in this project and not about chips: the warmth on that face is the
    // *light*, not the albedo — the block is lit by the key light and the two
    // fills, and a sprite is lit by nothing at all. So a sprite painted with
    // the block's albedo is painted with a colour the block is never actually
    // seen to be, and the bigger the patch of it, the worse that reads. The
    // crack net gets away with it because a hairline is too thin to compare.
    //
    // Black at alpha `a` over a face leaves `face * (1 - a)`, so a texture
    // written this way is a *multiply* on whatever the block is: it keeps the
    // hue exactly, it keeps the light exactly, and it cannot come out brighter
    // than the face by construction — which is the same conclusion
    // Brick.CrackDarkness reaches from the other end (see "A crack is always
    // darker, never lighter"). What it costs is that nothing in a chip can be
    // *brighter* than the face either, so the lit far wall is drawn as the part
    // that is barely darkened rather than as a highlight. Against the rim and
    // the undercut beside it, that is still the brightest thing in the hollow.
    static void WriteChipTextures()
    {
        for (int variant = 0; variant < ChipVariantCount; variant++)
            WriteChipTexture(ChipTexturePath(variant), new System.Random(4096 + variant));
    }

    static void WriteChipTexture(string path, System.Random random)
    {
        float middle = ChipTextureSize * 0.5f;
        float radius = ChipTextureSize * ChipFlakeRadius;

        // The main flake first and the splinters after it, each with an outline
        // of its own so no two of them are the same shape.
        var flakes = new List<Vector2[]> { ChipFlake(random, new Vector2(middle, middle), radius) };
        for (int i = 0; i < ChipSplinters; i++)
        {
            float angle = (float)(random.NextDouble() * Mathf.PI * 2.0);
            float reach = radius * Mathf.Lerp(
                ChipSplinterNear, ChipSplinterFar, (float)random.NextDouble());
            float small = radius * Mathf.Lerp(
                ChipSplinterSmallest, ChipSplinterLargest, (float)random.NextDouble());
            flakes.Add(ChipFlake(
                random,
                new Vector2(middle + Mathf.Cos(angle) * reach, middle + Mathf.Sin(angle) * reach),
                small));
        }

        var shading = new float[ChipTextureSize * ChipTextureSize];
        var coverage = new float[ChipTextureSize * ChipTextureSize];
        // Nothing is drawn where there is no flake, so the field starts as the
        // face itself: a share of 1 is "leave this pixel alone".
        for (int i = 0; i < shading.Length; i++) shading[i] = 1f;
        foreach (var flake in flakes) StampFlake(shading, coverage, flake);

        var texture = new Texture2D(ChipTextureSize, ChipTextureSize, TextureFormat.RGBA32, false);
        for (int y = 0; y < ChipTextureSize; y++)
            for (int x = 0; x < ChipTextureSize; x++)
            {
                int index = y * ChipTextureSize + x;
                // Black at the alpha that leaves the wanted share of the face
                // standing, and only across the flake itself. The share is
                // raised to the gamma on the way in because the blend happens
                // in *linear* space while every number above was chosen by
                // eye: measured on the bench, a floor asked for at 0.70 came
                // out at 0.89 of the face — a chip so faint it read as a
                // smudge — because 0.7 of the linear value is 0.86 of the
                // sRGB one. So the shading is a perceptual share and this is
                // where it becomes a linear coverage.
                float share = Mathf.Pow(Mathf.Clamp01(shading[index]), 2.2f);
                texture.SetPixel(x, y, new Color(0f, 0f, 0f, (1f - share) * coverage[index]));
            }

        texture.Apply();
        File.WriteAllBytes(ToAbsolute(path), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }

    // One flake as a polygon: a corner per facet, each at a jittered angle and
    // a jittered radius, then the whole thing stretched along an axis of its
    // own. Wound counter-clockwise, which nothing downstream depends on but
    // which keeps the debugging of it sane.
    static Vector2[] ChipFlake(System.Random random, Vector2 middle, float radius)
    {
        float lean = (float)(random.NextDouble() * Mathf.PI);
        var along = new Vector2(Mathf.Cos(lean), Mathf.Sin(lean));
        float turn = (float)(random.NextDouble() * Mathf.PI * 2.0);

        var flake = new Vector2[ChipFacets];
        for (int i = 0; i < ChipFacets; i++)
        {
            float share = Mathf.PI * 2f / ChipFacets;
            float angle = turn + share * (i + (float)(random.NextDouble() * 2 - 1) * ChipAngleJitter);
            float reach = radius * (1f + (float)(random.NextDouble() * 2 - 1) * ChipRadiusJitter);
            var offset = new Vector2(Mathf.Cos(angle) * reach, Mathf.Sin(angle) * reach);
            float projected = offset.x * along.x + offset.y * along.y;
            offset = new Vector2(
                offset.x + along.x * projected * ChipStretch,
                offset.y + along.y * projected * ChipStretch);
            flake[i] = new Vector2(middle.x + offset.x, middle.y + offset.y);
        }
        return flake;
    }

    // One flake laid into the shading and coverage buffers — `shading` being
    // the share of the face left standing at each pixel, and `coverage` how
    // much of the pixel the flake covers at all. Coverage is full inside the
    // outline and falls off across the last pixel of it, since the sprite is
    // filtered onto the block and a hard edge would come back as a stepped
    // one. Where two flakes overlap the more covered pixel wins.
    static void StampFlake(float[] shading, float[] coverage, Vector2[] flake)
    {
        float left = float.MaxValue, right = float.MinValue;
        float bottom = float.MaxValue, top = float.MinValue;
        foreach (var corner in flake)
        {
            left = Mathf.Min(left, corner.x); right = Mathf.Max(right, corner.x);
            bottom = Mathf.Min(bottom, corner.y); top = Mathf.Max(top, corner.y);
        }

        for (int y = Mathf.Max(0, Mathf.FloorToInt(bottom) - 1);
            y <= Mathf.Min(ChipTextureSize - 1, Mathf.CeilToInt(top) + 1); y++)
            for (int x = Mathf.Max(0, Mathf.FloorToInt(left) - 1);
                x <= Mathf.Min(ChipTextureSize - 1, Mathf.CeilToInt(right) + 1); x++)
            {
                var point = new Vector2(x, y);
                float distance = DistanceToOutline(flake, point);
                float inward = Inside(flake, point) ? distance : -distance;
                float alpha = Mathf.Clamp01(inward + 0.5f);
                if (alpha <= 0f) continue;

                // The floor of the hollow, then the three things that are
                // brighter or darker than it, in the order light reaches them.
                float shade = ChipFloor;
                // The far wall, facing up into the key light.
                float below = DistanceAlongY(flake, point, -1, ChipLipPixels);
                shade = Mathf.Lerp(ChipLip, shade, Mathf.Clamp01(below / ChipLipPixels));
                // The undercut: how far above this pixel the glaze breaks.
                float above = DistanceAlongY(flake, point, 1, ChipUndercutPixels);
                shade *= Mathf.Lerp(ChipUndercut, 1f, Mathf.Clamp01(above / ChipUndercutPixels));
                // And the break itself, a hairline round the whole outline.
                shade *= Mathf.Lerp(ChipRimShade, 1f, Mathf.Clamp01(inward / ChipRimPixels));

                int index = y * ChipTextureSize + x;
                if (alpha < coverage[index]) continue;
                coverage[index] = alpha;
                shading[index] = shade;
            }
    }

    // Whether a point is inside the polygon, by counting the crossings of a
    // ray cast out of it.
    static bool Inside(Vector2[] flake, Vector2 point)
    {
        bool inside = false;
        for (int i = 0, j = flake.Length - 1; i < flake.Length; j = i++)
        {
            if (flake[i].y > point.y == flake[j].y > point.y) continue;
            float crossing = flake[j].x
                + (point.y - flake[j].y) / (flake[i].y - flake[j].y) * (flake[i].x - flake[j].x);
            if (point.x < crossing) inside = !inside;
        }
        return inside;
    }

    // How far the point is from the nearest facet, inside or out.
    static float DistanceToOutline(Vector2[] flake, Vector2 point)
    {
        float nearest = float.MaxValue;
        for (int i = 0, j = flake.Length - 1; i < flake.Length; j = i++)
        {
            var along = new Vector2(flake[i].x - flake[j].x, flake[i].y - flake[j].y);
            float length = along.x * along.x + along.y * along.y;
            var offset = new Vector2(point.x - flake[j].x, point.y - flake[j].y);
            float share = length > 0f
                ? Mathf.Clamp01((offset.x * along.x + offset.y * along.y) / length) : 0f;
            var to = new Vector2(offset.x - along.x * share, offset.y - along.y * share);
            nearest = Mathf.Min(nearest, to.magnitude);
        }
        return nearest;
    }

    // How far the outline is from the point straight up (`direction` 1) or
    // straight down (-1), or `reach` when the ray leaves the flake without
    // meeting it again — a pixel with nothing over it is a pixel in the open,
    // and the shading above is written so that `reach` means "no wall here".
    // Casting the ray against the outline rather than measuring off the
    // bounding box is what keeps the shadow the shape of the break: an outline
    // that dips low shades the floor further down.
    static float DistanceAlongY(Vector2[] flake, Vector2 point, int direction, float reach)
    {
        float nearest = reach;
        for (int i = 0, j = flake.Length - 1; i < flake.Length; j = i++)
        {
            if (flake[i].x > point.x == flake[j].x > point.x) continue;
            float share = (point.x - flake[j].x) / (flake[i].x - flake[j].x);
            float crossing = flake[j].y + share * (flake[i].y - flake[j].y);
            float away = (crossing - point.y) * direction;
            if (away > 0f) nearest = Mathf.Min(nearest, away);
        }
        return nearest;
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
    static void CreateHalfBrickPrefab(Material material, Sprite[] cracks)
    {
        var go = new GameObject("HalfBrick");
        go.transform.localScale = new Vector3(HalfBrickWidth, BrickHeight, BrickDepth);
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        go.AddComponent<BoxCollider2D>();
        go.AddComponent<Brick>();
        // The child inherits the root scale, so the 1-unit crack sprite covers
        // the brick; -0.51 lands just in front of the box face at z -0.5.
        AddCrackOverlay(go, new Vector3(0f, 0f, -0.51f), Vector3.one, cracks);
        PrefabUtility.SaveAsPrefabAsset(go, HalfBrickPrefabPath);
        Object.DestroyImmediate(go);
    }

    // Rounded-corner brick: the mesh is authored at final size (unit-scale
    // transform, like the paddle), and the collider is a box shrunk by the
    // corner radius on every side with edgeRadius filling it back out — the
    // same rounded rectangle, so corner hits reflect off the curve's normal.
    static void CreateRoundedBrickPrefab(Material material, Mesh roundedMesh, Sprite[] cracks)
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
            new Vector3(BrickWidth, BrickHeight, 1f), cracks);
        PrefabUtility.SaveAsPrefabAsset(go, RoundedBrickPrefabPath);
        Object.DestroyImmediate(go);
    }

    // Round brick: a half-size sphere whose CircleCollider2D matches its
    // silhouette exactly, so the ball reflects off the circle's radial
    // normal — glancing hits deflect sideways instead of bouncing flat.
    static void CreateRoundBrickPrefab(Material material, Sprite[] cracks)
    {
        var go = new GameObject("RoundBrick");
        go.transform.localScale = Vector3.one * RoundBrickDiameter;
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        go.AddComponent<CircleCollider2D>().radius = 0.5f;
        go.AddComponent<Brick>();
        // 0.7 ~ an inscribed square: keeps the square crack sprite's pixels
        // over the sphere's circular silhouette instead of floating past it.
        AddCrackOverlay(go, new Vector3(0f, 0f, -0.52f), Vector3.one * 0.7f, cracks);
        PrefabUtility.SaveAsPrefabAsset(go, RoundBrickPrefabPath);
        Object.DestroyImmediate(go);
    }

    // Adds the Cracks overlay child and wires it (and the sprite variant
    // arrays) into the Brick component on a prefab under construction.
    static void AddCrackOverlay(GameObject root, Vector3 localPosition, Vector3 localScale, Sprite[] cracks)
    {
        var cracksGo = new GameObject("Cracks");
        cracksGo.transform.SetParent(root.transform, false);
        cracksGo.transform.localPosition = localPosition;
        cracksGo.transform.localScale = localScale;
        var renderer = cracksGo.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = 1;

        var so = new SerializedObject(root.GetComponent<Brick>());
        so.FindProperty("crackRenderer").objectReferenceValue = renderer;
        SetObjectArray(so, "crackSprites", cracks);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // The whole net as one flat array, stage-major — `stage * variants +
    // variant`, which is exactly how Brick indexes it — or null while any one
    // of them is still unimportable.
    static Sprite[] LoadCrackSprites()
    {
        var sprites = new Sprite[CrackStageCount * CrackVariantCount];
        for (int stage = 0; stage < CrackStageCount; stage++)
            for (int variant = 0; variant < CrackVariantCount; variant++)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(CrackTexturePath(stage, variant));
                if (sprite == null) return null;
                sprites[stage * CrackVariantCount + variant] = sprite;
            }
        return sprites;
    }

    // Which block prefabs are not carrying the current crack net. A count is
    // the whole test: the array is the stages times the variants, so a prefab
    // built before either number moved has a different one — and one left over
    // from the light/heavy pair of arrays has none at all, since that field is
    // gone and its serialized data with it.
    static List<string> BrickPrefabsMissingCrackNet()
    {
        var stale = new List<string>();
        foreach (var path in new[]
            { BrickPrefabPath, HalfBrickPrefabPath, RoundedBrickPrefabPath, RoundBrickPrefabPath })
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var brick = root != null ? root.GetComponent<Brick>() : null;
            if (brick == null || root.transform.Find("Cracks") == null) continue;
            if (new SerializedObject(brick).FindProperty("crackSprites").arraySize
                != CrackStageCount * CrackVariantCount) stale.Add(path);
        }
        return stale;
    }

    // The child inherits the brick root's 1.5x0.5 scale, so the 1-unit crack
    // sprite stretches to cover the brick exactly. The renderer starts with no
    // sprite; Brick swaps in a light/heavy crack variant as damage accumulates.
    static void AddCracksToBrickPrefab(Sprite[] cracks)
    {
        var root = PrefabUtility.LoadPrefabContents(BrickPrefabPath);
        var cracksGo = new GameObject("Cracks");
        cracksGo.transform.SetParent(root.transform, false);
        var renderer = cracksGo.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = 1;

        var so = new SerializedObject(root.GetComponent<Brick>());
        so.FindProperty("crackRenderer").objectReferenceValue = renderer;
        SetObjectArray(so, "crackSprites", cracks);
        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, BrickPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    // Stage-28 retrofit path: the Cracks child already exists, only the sprite
    // array needs replacing. The forced reimport at the end is not optional —
    // SaveAsPrefabAsset writes the file, and Instantiate serves the *imported*
    // copy, which otherwise goes on handing out clones wearing the old array
    // while every file on disk reads correctly.
    static void WireCrackSpritesIntoPrefab(string prefabPath, Sprite[] cracks)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        var so = new SerializedObject(root.GetComponent<Brick>());
        SetObjectArray(so, "crackSprites", cracks);
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
    }

    // The four chip shapes, or null while any one of them is still
    // unimportable — same rule as the crack net: a prefab wired around a hole
    // would hand Brick a null sprite to stamp on a hit.
    static Sprite[] LoadChipSprites()
    {
        var sprites = new Sprite[ChipVariantCount];
        for (int variant = 0; variant < ChipVariantCount; variant++)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ChipTexturePath(variant));
            if (sprite == null) return null;
            sprites[variant] = sprite;
        }
        return sprites;
    }

    // Which block prefabs are not carrying the current set of chips. The count
    // is not enough on its own here, the way it is for the net: stage 105
    // rewrites the PNGs at the paths these prefabs already point at, and a
    // rewrite over a referenced path destroys the object the import was
    // holding — so a prefab can be left with an array of the right length full
    // of nulls. Reading the entries is what makes this the repair as well as
    // the check.
    static List<string> BlockPrefabsMissingChips()
    {
        var stale = new List<string>();
        foreach (var path in new[]
            { BrickPrefabPath, HalfBrickPrefabPath, RoundedBrickPrefabPath, RoundBrickPrefabPath })
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var brick = root != null ? root.GetComponent<Brick>() : null;
            if (brick == null) continue;
            var chips = new SerializedObject(brick).FindProperty("chipSprites");
            if (chips.arraySize != ChipVariantCount)
            {
                stale.Add(path);
                continue;
            }
            for (int i = 0; i < chips.arraySize; i++)
                if (chips.GetArrayElementAtIndex(i).objectReferenceValue == null)
                {
                    stale.Add(path);
                    break;
                }
        }
        return stale;
    }

    // The forced reimport at the end is not optional, for the reason
    // WireCrackSpritesIntoPrefab gives: SaveAsPrefabAsset writes the file and
    // Instantiate serves the *imported* copy.
    static void WireChipSpritesIntoPrefab(string prefabPath, Sprite[] chips)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        var so = new SerializedObject(root.GetComponent<Brick>());
        SetObjectArray(so, "chipSprites", chips);
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
    }

    static void SetObjectArray(SerializedObject so, string field, Object[] values)
    {
        var property = so.FindProperty(field);
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
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
        // How much of the grain is pigment rather than relief. Per-kind since
        // the ceramics arrived: see WriteGrainAlbedo for what it trades, and
        // note that the two carved characters that are nearly pure stain in the
        // reference — a vein, a cloud — have almost nothing *but* this.
        float contrast;

        switch (kind)
        {
            // Orange peel: one dense layer of medium caps, heavily overlapping,
            // which is the dominant look on the reference sheet.
            case GrainKind.Pebble:
                ScatterCaps(height, size, random, 220, 10f, 18f, GrainTileScale);
                normalStrength = 9f * GrainTileScale;
                contrast = PolymerContrast;
                break;
            // The tight even speckle of the pale tiles. Far more, far smaller:
            // at block size this reads as a softened sheen rather than as
            // countable bumps, which is the point of it.
            case GrainKind.Stipple:
                ScatterCaps(height, size, random, 1400, 3f, 6f, GrainTileScale);
                normalStrength = 4f * GrainTileScale;
                contrast = PolymerContrast;
                break;
            // Coarse granulate: big caps for the chunk, then a fine layer over
            // the top so the chunks themselves are not smooth.
            case GrainKind.Crumb:
                ScatterCaps(height, size, random, 90, 18f, 34f, GrainTileScale);
                ScatterCaps(height, size, random, 260, 5f, 10f, GrainTileScale);
                normalStrength = 13f * GrainTileScale;
                contrast = PolymerContrast;
                break;

            // A crackle glaze. The net is a jittered-grid Voronoi's cell
            // boundaries, which is what a glaze shrinking over a body it no
            // longer fits actually cracks into: cells of one rough size, no two
            // the same shape, every junction a meeting of three. The clouding
            // under it is faint on purpose — a crackle tile's interest is the
            // net, and a body mottled as hard as the alabaster's would fight it.
            case GrainKind.Crackle:
                Flood(height, 1f);
                ScatterGrit(height, size, random, GrainTileScale);
                Cloud(height, size, 1103, 0.08f, 4);
                CarveCrackle(height, size, random, 0.14f, 0.010f, 0.85f);
                // The crack's own half-width in texels, by WriteGrainNormal's
                // rule — and *not* scaled by the tile, which is the correction
                // the first render earned. At 5 × GrainTileScale the grooves
                // came out as near-vertical canyons and the cells between them
                // as domed cobbles: a photograph of dried mud rather than of a
                // glaze. A crackle glaze is a *flat* surface with a hairline
                // crease in it, and the darkness of the line is the albedo's
                // job (contrast, below) rather than the relief's.
                normalStrength = 2.5f;
                // The highest of the six: the net is the entire character, and
                // it has to survive both the end faces the light misses and the
                // mip levels a block a hundred-odd pixels tall is drawn from.
                contrast = 0.45f;
                break;

            // A marble tile. Two heavy veins per authored tile carrying the eye
            // and a tangle of hairlines that only show close up, which is the
            // proportion the reference's veined tiles keep — one or two veins a
            // face doing the work. Walked rather than drawn (CarveVeins): a vein
            // is a crack that was filled, so it wanders the way a crack
            // propagates and not the way a line is laid down.
            case GrainKind.Vein:
                Flood(height, 1f);
                ScatterGrit(height, size, random, GrainTileScale);
                Cloud(height, size, 5077, 0.10f, 3);
                CarveVeins(height, size, random, 6, 0.026f, 2.5f, 0.70f);
                CarveVeins(height, size, random, 14, 0.008f, 0.8f, 0.40f);
                // Deliberately a fraction of what the vein's own radius would
                // ask for: a vein is a stain and not a ditch, and a marble tile
                // is polished flat straight across it. That leaves the grit
                // under it reading as pigment rather than as relief, which is
                // the same fact said twice — polished means the grit is under
                // the glaze and not in the surface.
                normalStrength = 3f;
                contrast = 0.55f;
                break;

            // An alabaster tile: the cloud in the body seen through a clear
            // glaze. No edges anywhere, only a slow drift with two finer
            // octaves over it, and the amplitudes falling steeply because the
            // reference's clouding is *large* — detail at the pixel would come
            // out as the grit it already has.
            default:
                Flood(height, 1f);
                ScatterGrit(height, size, random, GrainTileScale);
                Cloud(height, size, 8821, 0.40f, 3);
                Cloud(height, size, 4409, 0.18f, 7);
                Cloud(height, size, 6151, 0.07f, 15);
                // The one ceramic character with nothing in relief of its own —
                // a cloud is in the body, not on the face — so here the grit's
                // own caps are what the strength is sized for, and it is the
                // only one of the three that keeps the tile's scaling.
                normalStrength = 3.5f * GrainTileScale;
                contrast = 0.45f;
                break;
        }

        WriteGrainAlbedo(GrainTexturePath(name), height, size, contrast);
        WriteGrainNormal(GrainNormalPath(name), height, size, normalStrength);
    }

    // Every ceramic grain starts here: a flat surface, which is the one thing
    // Polymer's do not have. A scattered grain builds up from nothing and its
    // flat is the zero it never filled; a carved one is a surface first and its
    // character is what has been taken out of it.
    static void Flood(float[] height, float value)
    {
        for (int i = 0; i < height.Length; i++) height[i] = value;
    }

    // The fine porcelain grit under all three ceramic characters (see
    // CeramicsGrains). Scattered with Polymer's own ScatterCaps, into a buffer
    // of its own and then pressed into the surface rather than laid on it,
    // because a ceramic's field runs the other way up: max-combining caps
    // straight into a flooded field would find every cap lower than the flat
    // and change nothing at all.
    //
    // Shallow — a tenth of the field — and that is the whole of it. Grit deep
    // enough to see on its own would be sand rather than fired clay, and the
    // reference's matte tiles are only just not smooth.
    static void ScatterGrit(float[] height, int size, System.Random random, float scale)
    {
        const float depth = 0.10f;
        var caps = new float[size * size];
        ScatterCaps(caps, size, random, 1600, 2f, 5f, scale);
        for (int i = 0; i < height.Length; i++)
            height[i] = Mathf.Min(height[i], 1f - depth * (1f - caps[i]));
    }

    // Wrapped value noise taken out of the surface: the body of a tile, which
    // is a thing with no edges in it. Subtracted rather than min-combined, so
    // octaves stack and a groove carved afterwards still lands on whatever the
    // body was doing underneath it.
    //
    // It is nearly all pigment by construction and that is the point of using
    // it: a gradient this slow has essentially no gradient, so the normal map
    // barely registers it while the albedo carries all of it. A cloud is
    // something seen *in* a surface rather than on it.
    static void Cloud(float[] height, int size, int seed, float amplitude, int period)
    {
        var lattice = NoiseLattice(period, seed);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float value = SampleNoiseLattice(lattice, (float)x / size, (float)y / size);
                int index = y * size + x;
                height[index] = Mathf.Clamp01(height[index] - amplitude * (1f - value));
            }
    }

    // A craquelure net cut into the surface: the cell boundaries of a Voronoi
    // whose seeds are one per grid cell, jittered inside it.
    //
    // The jittered grid rather than a free scatter is not tidiness. Free seeds
    // clump, and a clump is a knot of tiny cells among big ones, which reads as
    // a tile somebody dropped rather than as a glaze that crazed. Bounding the
    // jitter inside its own cell is also what makes the search below correct:
    // only a pixel's own grid neighbourhood can hold the seeds nearest to it.
    //
    // `cell` is how wide one cell of the net is and `width` how wide a crack
    // is, both in world units (GrainTexelsPerUnit) — a cell is a thing counted
    // across a block, so the block is what it has to be measured against. The
    // first pass sized it in texels and came out at eighteen cells across a
    // slab, which is a texture rather than a craze; ten is where the eye reads
    // individual cells at the size a block is actually drawn.
    static void CarveCrackle(float[] height, int size, System.Random random, float cellSize,
        float width, float depth)
    {
        int divisions = Mathf.Max(2, Mathf.RoundToInt(size / (cellSize * GrainTexelsPerUnit)));
        width *= GrainTexelsPerUnit;
        float cell = (float)size / divisions;

        var seeds = new Vector2[divisions * divisions];
        for (int gy = 0; gy < divisions; gy++)
            for (int gx = 0; gx < divisions; gx++)
                seeds[gy * divisions + gx] = new Vector2(
                    (gx + 0.2f + 0.6f * (float)random.NextDouble()) * cell,
                    (gy + 0.2f + 0.6f * (float)random.NextDouble()) * cell);

        // How hard the glaze pulled where a crack runs, so the net has major
        // cracks and hairlines in it rather than one width the whole way over.
        // Coarse and slow: it varies the net by region, which is what the
        // reference shows, and not crack by crack, which would put a step in the
        // middle of a line.
        var pull = NoiseLattice(12, 90563);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nearest = float.MaxValue, second = float.MaxValue;
                int cx = (int)(x / cell), cy = (int)(y / cell);
                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int gx = cx + dx, gy = cy + dy;
                        // Wrapped, with the seed carried across the seam along
                        // with the index it was fetched by, so the net closes on
                        // itself and the tile has no edge to seam at.
                        var seed = seeds[
                            ((gy % divisions + divisions) % divisions) * divisions
                            + ((gx % divisions + divisions) % divisions)];
                        float sx = seed.x + Mathf.Floor((float)gx / divisions) * size;
                        float sy = seed.y + Mathf.Floor((float)gy / divisions) * size;
                        // Squared while they are being sorted; only the two that
                        // win are worth a square root.
                        float distance = (sx - x) * (sx - x) + (sy - y) * (sy - y);
                        if (distance < nearest) { second = nearest; nearest = distance; }
                        else if (distance < second) second = distance;
                    }
                }

                // The gap between the two nearest seeds' claims, not the
                // distance to either: it is zero exactly on a boundary and grows
                // away from it, which is a net of lines. The distance to the
                // nearest seed alone would be a field of dots.
                float edge = Mathf.Sqrt(second) - Mathf.Sqrt(nearest);
                if (edge >= width) continue;
                float groove = 1f - Mathf.SmoothStep(0f, 1f, edge / width);
                float local = 0.45f + 0.55f * SampleNoiseLattice(pull, (float)x / size, (float)y / size);
                int index = y * size + x;
                height[index] = Mathf.Min(height[index], 1f - depth * local * groove);
            }
        }
    }

    // Veins walked across the tile and stamped as chains of soft grooves. A
    // vein is a crack the ground filled back in, so it is walked rather than
    // drawn: the heading takes a small random step at every stamp, which is
    // smooth over a few texels and unpredictable over the length of the tile. A
    // heading drawn fresh each step would be a cloud of noise, and a straight
    // line with a curve fitted to it would be a drawn line.
    //
    // `count` is veins across the whole tile; `radius` and `length` are in
    // world units, like everything else carved here. Counted per tile rather
    // than per area because a vein is not a density — a face carries one or two
    // of them and the question is how many cross a slab, which is a count.
    static void CarveVeins(float[] height, int size, System.Random random, int count,
        float radius, float length, float depth)
    {
        radius *= GrainTexelsPerUnit;
        for (int i = 0; i < count; i++)
        {
            float x = (float)random.NextDouble() * size;
            float y = (float)random.NextDouble() * size;
            float heading = (float)random.NextDouble() * Mathf.PI * 2f;
            int steps = Mathf.Max(1, Mathf.RoundToInt(length * GrainTexelsPerUnit / VeinStep));
            for (int step = 0; step < steps; step++)
            {
                heading += ((float)random.NextDouble() - 0.5f) * VeinWander;
                x += Mathf.Cos(heading) * VeinStep;
                y += Mathf.Sin(heading) * VeinStep;
                // Faded in and out over the first and last fifth, so a vein
                // thins away to nothing rather than stopping at a blunt end
                // nothing in stone would explain — and, since the walk wraps,
                // so that the two ends meeting somewhere in the tile do not
                // read as a join.
                float along = (step + 0.5f) / steps;
                float taper = Mathf.SmoothStep(0f, 1f, Mathf.Min(along, 1f - along) / 0.2f);
                // A slow swell along the run on top of that, because a vein is
                // not one width for its whole length either. Offset by the
                // vein's own index so two of them do not swell in step.
                float swell = 0.65f + 0.35f * Mathf.Sin(along * 7f + i);
                StampGroove(height, size, x, y, radius * taper * swell, depth * taper);
            }
        }
    }

    // How far a vein walks between stamps and how far its heading may wander at
    // each one. The step is short enough that consecutive stamps overlap even at
    // the thinnest radius a taper leaves, or a vein would come out as a dotted
    // line; the wander is what makes it a vein rather than a ray.
    //
    // The wander is small and has to be. It compounds — a walk of n steps ends
    // up about `wander × sqrt(n / 12)` radians off where it started — and a vein
    // is a thousand-odd steps long, so 0.5 ends up several turns of heading and
    // draws a curl. 0.12 works out to under a radian across a whole vein, which
    // is a line that meanders.
    const float VeinStep = 1.5f;
    const float VeinWander = 0.12f;

    // One stamp of a vein, pressed into the surface: smoothstepped from its rim
    // to its middle for the same reason ScatterCaps' falloff is, wrapped for the
    // same reason, and min-combined so the overlapping stamps along a walk make
    // one even groove rather than a string of ever-deeper pits.
    static void StampGroove(float[] height, int size, float cx, float cy, float radius, float depth)
    {
        if (radius <= 0f || depth <= 0f) return;
        int reach = Mathf.CeilToInt(radius);
        for (int dy = -reach; dy <= reach; dy++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                if (distance > radius) continue;
                int x = (((int)cx + dx) % size + size) % size;
                int y = (((int)cy + dy) % size + size) % size;
                float value = 1f - depth * Mathf.SmoothStep(0f, 1f, 1f - distance / radius);
                int index = y * size + x;
                if (value < height[index]) height[index] = value;
            }
        }
    }

    // Rounded caps laid down wrapped, combined by max. The falloff is
    // smoothstep on the radius, so a cap meets the flat around it without a
    // crease — a linear cone would put a hard rim in the normal map and read as
    // a field of spikes. Count and radii are authored for a 256 px tile;
    // `scale` is how much bigger the tile actually is, and grows the radii by
    // it and the count by its square so the grain keeps its density.
    static void ScatterCaps(float[] height, int size, System.Random random, int count,
        float minRadius, float maxRadius, float scale)
    {
        count = Mathf.RoundToInt(count * scale * scale);
        minRadius *= scale;
        maxRadius *= scale;
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

    // Near white throughout: the block's actual colour is the per-instance tint
    // multiplying this, so what the albedo carries is only how far the grain
    // darkens it. Grayscale, like BrickWall.png, for the same reason.
    //
    // `contrast` is how much of the grain is pigment rather than relief, and it
    // is the dial for the one thing the relief cannot do: **a face with no light
    // on it has no relief.** The scene's single light stands head-on and tilts
    // down, so a block's two end faces (normal ±X) get essentially nothing, and
    // at 0.10 they came back as flat ambient-blue panels while the front and top
    // carried the full moulding — the two read as different materials from any
    // oblique angle. Pigment does not need a light, so raising this is what puts
    // the grain back on those faces.
    //
    // It is a trade rather than a free win, and the cost is the reason it was
    // 0.10 to begin with: pigment multiplies the tint, so it eats the range a
    // dark block has left to be dark with, and it is *added* to the relief on
    // the faces that are lit rather than swapped for it. Polymer's 0.30 is where
    // the end faces stop reading as blank without the lit faces reading as
    // painted; `normalStrength` was deliberately left alone alongside it, since
    // the moulding on the front face is what the surface is judged on and it was
    // already right.
    //
    // **It is the caller's number rather than a constant here** since the
    // ceramics arrived, because the trade lands differently for a carved
    // character: a marble's vein is pigment in the world too — the tile is
    // polished flat across it — and its band is bright enough throughout to
    // spend more than plastic's can (see CeramicsDarkest).
    static void WriteGrainAlbedo(string path, float[] height, int size, float contrast)
    {
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
        var coarse = NoiseLattice(3, 20240816);
        var middle = NoiseLattice(6, 90210);
        var fine = NoiseLattice(12, 4711);

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float value = 0.55f * SampleNoiseLattice(coarse, u, v)
                    + 0.3f * SampleNoiseLattice(middle, u, v)
                    + 0.15f * SampleNoiseLattice(fine, u, v);
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

    // A grid of random values that wraps, which is what makes anything sampled
    // off it tile: the sample at 1 is the sample at 0. Seeded, so a
    // from-scratch build reproduces the same weather — and the same marble.
    // Written for the menu's fog and since borrowed by the ceramic grains'
    // clouding (Cloud), which wants exactly the same thing: soft, tiling,
    // reproducible, and with no edge anywhere in it.
    static float[,] NoiseLattice(int period, int seed)
    {
        var random = new System.Random(seed);
        var values = new float[period, period];
        for (int y = 0; y < period; y++)
            for (int x = 0; x < period; x++)
                values[x, y] = (float)random.NextDouble();
        return values;
    }

    static float SampleNoiseLattice(float[,] values, float u, float v)
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
    //
    // The box is chamfered rather than square-edged (BlockBevel): six cut-back
    // faces, twelve bevel strips and eight corner triangles, 96 vertices where
    // the plain box had 24. That is cosmetic only and deliberately so — the
    // BoxCollider2D is left on the square outline, so the ball reflects off the
    // shape the block used to be and a bevel can be retuned without retuning
    // the game.
    static Mesh BuildWorldUvBoxMesh(string name, Vector3 size)
    {
        var mesh = new Mesh { name = name };
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        // Which of the six faces each vertex takes its normal from. This is what
        // makes the bevel a rounded edge rather than a facet of its own: a
        // strip's two vertices along one face are given *that face's* normal and
        // its other two the neighbour's, so the shading grades across the
        // chamfer instead of holding one value across it.
        //
        // Flat-shading it was the first version and it is wrong here, for a
        // reason that is about this surface rather than about bevels. A chamfer
        // 0.03 wide is far too narrow to carry any grain, so its specular stays
        // coherent where the face's is broken up by the normal map — and a
        // uniform sheen 6 px wide running the length of every edge does not read
        // as an edge, it reads as a drawn border around the block. Measured on a
        // light block: the band's saturation was 5 against the face's 12, and it
        // sat 10% off the face's value. Interpolated normals have no such band,
        // because there is no step to hold.
        var normalSource = new List<int>();
        var faces = new[]
        {
            (Normal: Vector3.back, U: Vector3.right),
            (Normal: Vector3.forward, U: Vector3.left),
            (Normal: Vector3.right, U: Vector3.forward),
            (Normal: Vector3.left, U: Vector3.back),
            (Normal: Vector3.up, U: Vector3.right),
            (Normal: Vector3.down, U: Vector3.right),
        };

        // The half-extent each face is left with once the bevel has cut it back.
        // The geometry is a unit cube and the prefab's scale is not uniform, so
        // a chamfer of a fixed width in *local* units would come out three
        // different widths in the world: on a block scaled (1.5, 0.5, 0.6) the
        // same number is three times wider down the height than across the
        // width. Dividing the world bevel by the world size per axis is what
        // makes it one constant width all the way round, which is the only kind
        // a single light can read consistently.
        var inset = new Vector3(
            0.5f - BlockBevel / size.x,
            0.5f - BlockBevel / size.y,
            0.5f - BlockBevel / size.z);

        // World units per local unit along an axis-aligned direction, and the
        // bevelled half-extent along it. Both take the magnitude, so a negative
        // axis (Vector3.left, Vector3.back) answers the same as its positive.
        float WorldPer(Vector3 axis) => Vector3.Scale(axis, size).magnitude;
        float InsetAlong(Vector3 axis) => Vector3.Scale(axis, inset).magnitude;

        // A face's UV is its world position along the face's two in-plane axes,
        // which is the whole point of this mesh: a UV unit is a world unit on
        // all six faces. The bevel strips and corners borrow the frame of one of
        // the faces they touch rather than being unwrapped in their own right —
        // they are one bevel wide, so the UV seam that leaves along the strip's
        // far side is 0.03 of a world unit across and no grain at this scale
        // reads it.
        Vector2 FaceUv(Vector3 point, Vector3 u, Vector3 v) => new Vector2(
            Vector3.Dot(point, u) * WorldPer(u),
            Vector3.Dot(point, v) * WorldPer(v));

        Vector2[] RingUvs(Vector3[] ring, Vector3 u, Vector3 v)
        {
            var result = new Vector2[ring.Length];
            for (int i = 0; i < ring.Length; i++) result[i] = FaceUv(ring[i], u, v);
            return result;
        }

        // Appends a convex ring as a triangle fan, wound so that it faces the
        // way `outward` points. The winding is *measured* here rather than
        // reasoned out per polygon, because this builder now emits six faces,
        // twelve bevel strips and eight corner triangles, and a box wound the
        // wrong way round is not a subtle fault: with back-face culling the near
        // face is the one thrown away, so what is drawn is the far face and the
        // inside of the box, which reads as a stepped, hollow, plainly corrupt
        // shape rather than as a brick. Clockwise seen from outside is Unity's
        // front face, and for the corner order below that is the winding whose
        // `(b - a) x (c - a)` points along the outward normal.
        void AddPolygon(Vector3[] ring, Vector2[] ringUvs, int[] ringSources, Vector3 outward)
        {
            var facing = Vector3.Cross(ring[1] - ring[0], ring[2] - ring[0]);
            bool flip = Vector3.Dot(facing, outward) < 0f;
            int start = vertices.Count;
            for (int i = 0; i < ring.Length; i++)
            {
                int source = flip ? ring.Length - 1 - i : i;
                vertices.Add(ring[source]);
                uvs.Add(ringUvs[source]);
                normalSource.Add(ringSources[source]);
            }
            for (int i = 1; i < ring.Length - 1; i++)
            {
                triangles.Add(start);
                triangles.Add(start + i);
                triangles.Add(start + i + 1);
            }
        }

        // The six faces, each cut back by the bevel on all four sides. They are
        // emitted first and in order, so face f owns vertices 4f..4f+3 — which
        // is what lets the normals below be read straight off them.
        for (int f = 0; f < faces.Length; f++)
        {
            var (normal, u) = faces[f];
            var v = Vector3.Cross(normal, u);
            float su = InsetAlong(u), sv = InsetAlong(v);
            var ring = new[]
            {
                normal * 0.5f - u * su - v * sv,
                normal * 0.5f + u * su - v * sv,
                normal * 0.5f + u * su + v * sv,
                normal * 0.5f - u * su + v * sv,
            };
            AddPolygon(ring, RingUvs(ring, u, v), new[] { f, f, f, f }, normal);
        }

        // The twelve bevel strips, one per pair of faces that meet: the quad
        // spanning from where one face now stops to where the other one does.
        // Pairs are walked rather than edges enumerated, so the twelve come out
        // of the same six normals the faces did and there is no second table to
        // keep in step. Opposite faces meet nowhere and are skipped.
        for (int i = 0; i < faces.Length; i++)
        {
            for (int j = i + 1; j < faces.Length; j++)
            {
                var (aNormal, aU) = faces[i];
                var (bNormal, _) = faces[j];
                if (Mathf.Abs(Vector3.Dot(aNormal, bNormal)) > 0.5f) continue;

                var along = Vector3.Cross(aNormal, bNormal);
                float half = InsetAlong(along);
                var aEdge = aNormal * 0.5f + bNormal * InsetAlong(bNormal);
                var bEdge = bNormal * 0.5f + aNormal * InsetAlong(aNormal);
                var ring = new[]
                {
                    aEdge - along * half,
                    aEdge + along * half,
                    bEdge + along * half,
                    bEdge - along * half,
                };
                // The first two sit against face i and the last two against
                // face j, so the strip interpolates from one face's normal to
                // the other's — a rounded edge, continuous with both.
                AddPolygon(ring, RingUvs(ring, aU, Vector3.Cross(aNormal, aU)),
                    new[] { i, i, j, j }, aNormal + bNormal);
            }
        }

        // The eight corner triangles, closing the three strips that meet there.
        // Each vertex is the corner pulled in on two axes and left full on the
        // third, which is the same point the face and both strips already put
        // there.
        for (int corner = 0; corner < 8; corner++)
        {
            var sign = new Vector3(
                (corner & 1) == 0 ? -1f : 1f,
                (corner & 2) == 0 ? -1f : 1f,
                (corner & 4) == 0 ? -1f : 1f);
            var ring = new[]
            {
                new Vector3(sign.x * 0.5f, sign.y * inset.y, sign.z * inset.z),
                new Vector3(sign.x * inset.x, sign.y * 0.5f, sign.z * inset.z),
                new Vector3(sign.x * inset.x, sign.y * inset.y, sign.z * 0.5f),
            };
            // Each corner vertex is full along one axis and drawn in on the
            // other two, so it belongs to the face of that axis — the indices
            // are the faces table's own order.
            var sources = new[]
            {
                sign.x > 0f ? 2 : 3,
                sign.y > 0f ? 4 : 5,
                sign.z > 0f ? 1 : 0,
            };
            AddPolygon(ring, RingUvs(ring, Vector3.right, Vector3.up), sources, sign);
        }

        // The draft, applied last: every vertex's X is drawn in by an amount
        // that grows linearly from nothing at the front face (z = -0.5, the side
        // facing the player) to the full BlockDraft at the back. Done here
        // rather than in each polygon's own corner arithmetic because it is one
        // rule about the whole solid, and doing it once cannot get out of step
        // between the faces, the strips and the corners.
        //
        // The UVs are deliberately left on the untapered projection. A world-UV
        // mesh maps a UV unit to a world unit, and after the taper the back is
        // narrower than the front — so there is no single mapping that is true
        // of both. Keeping the front's is the right choice: the front face is
        // the one a player looks at, and the alternative compresses the grain
        // across the ends instead, which is the face that just got fixed.
        // Measured against the *normal* block's width, not this mesh's own, so
        // every box block tapers by the same proportion rather than by the same
        // number of world units. A fixed 0.2 per side is a quarter of the full
        // slab's half-width and over half of the half-brick's: the half-brick
        // came out at 41% of its front width at the back where the slab was at
        // 73%, which is a wedge rather than a brick — and on the translucent
        // materials, where the far side of the block is visible through the near
        // one, it read as plainly broken. The taper is a look, and a look wants
        // to be the same proportion on both shapes.
        float draft = 2f * BlockDraft / BrickWidth;
        for (int i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            vertex.x *= 1f - draft * (vertex.z + 0.5f);
            vertices[i] = vertex;
        }

        // Normals are assigned rather than recalculated. Each of the six faces
        // is planar even after the taper — the front and back keep a constant z,
        // the top and bottom a constant y, and the two ends are a straight line
        // in XZ extruded along Y — so a face's normal can be read off its own
        // first triangle, which the emission order above guarantees is at 4f.
        // Taking them post-taper is what matters: the ends are no longer
        // (+/-1, 0, 0) and assuming they were would light them as though they
        // had never been drafted.
        var faceNormals = new Vector3[faces.Length];
        for (int f = 0; f < faces.Length; f++)
        {
            var a = vertices[4 * f];
            faceNormals[f] = Vector3.Cross(vertices[4 * f + 1] - a, vertices[4 * f + 2] - a).normalized;
        }
        // And then every one of them except the true back is given the *front*
        // face's normal, which is what stops the edge reading as a border.
        //
        // A border and an edge are the same phenomenon: the bevel and the
        // drafted end are angled away from the face, so they catch a different
        // amount of light, and that difference is the band around the block.
        // Recolouring it, narrowing it and neutralising it were each tried and
        // each left a band — because a surface that shades differently from the
        // face *is* a band, whatever colour it is. The only way to have a
        // softened silhouette with no band at all is for the edge geometry to
        // shade as though it were the face.
        //
        // So the geometry still rounds the outline and the collider is still
        // untouched, but nothing around the edge is lit as its own surface. The
        // back keeps its own normal because it is a real face, and it is culled
        // from every view this game has anyway.
        var normals = new List<Vector3>(vertices.Count);
        foreach (var source in normalSource)
            normals.Add(faceNormals[source == 1 ? 1 : 0]);

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.SetNormals(normals);
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

    // The one light stages 32, 34 and 46 are about: the shadow-casting key
    // light, which is the only one whose *direction* those stages have an
    // opinion about.
    //
    // This used to be a plain `FindAnyObjectByType<Light>`, and stage 101's two
    // fills broke it the moment they were added — in exactly the way this
    // project's notes on save gates warn about, and worth keeping as the
    // worked example. A fill lies flat, so its `forward.x` is 1, so stage 34's
    // guard ("is the key light yawed off centre?") was true of it; stage 34 then
    // re-aimed a *fill* to the key light's pitch, stage 101 aimed it back on the
    // next reload, and the two sat there undoing each other. And because every
    // stage returns, a gate that is permanently true means **every stage after
    // it never runs again** — the presenting symptom is not "the light keeps
    // moving" but "the stage I just added stopped firing".
    //
    // Excluding the fills by name is what makes the older guards mean what they
    // have always been written to mean. The alternative — teaching each of those
    // three guards to skip horizontal lights — would spread the same fact across
    // three places and still be wrong for the next light anyone adds.
    static Light MainSceneLight()
    {
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
        {
            if (light.name == "FillLeft" || light.name == "FillRight") continue;
            return light;
        }
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
