# CLAUDE.md

Classic Arkanoid (Breakout) game. Unity `6000.5.6f1`, Universal Render Pipeline, new Input System.

## Layout

- `Assets/Scripts/Paddle.cs` — keyboard paddle movement (`Keyboard.current`, arrows/A-D), clamped to the playfield.
- `Assets/Scripts/Ball.cs` — constant-speed ball. Kinematic and paddle-following while attached; dynamic after launch. `FixedUpdate` renormalizes velocity to `speed` and enforces a minimum vertical component so the ball can't get stuck bouncing horizontally. Paddle bounces override the physics reflection with an angle based on where the ball hit the paddle.
- `Assets/Scripts/Brick.cs` — destroys itself on ball impact and reports its `Points` to `GameManager`.
- `Assets/Scripts/GameManager.cs` — state machine (Ready/Playing/EnteringName/GameOver/Won), score, lives, high score persisted via `PlayerPrefs` (`Arkanoid.HighScore`), runtime brick-grid construction. When a round ends above the record held at round start, switches to `EnteringName`: captures the player's name via `Keyboard.onTextInput` (backspace edits, ENTER submits, 12-char cap) and appends a `RecordBook` entry. Pushes values into the `ScoreBoard` and drives the `RecordsPanel`; OnGUI is only used for the "press SPACE to launch" banner. Serialized references are wired by the editor setup script.
- `Assets/Scripts/ScoreBoard.cs` — display-only UGUI HUD component on the `ScoreBoard` canvas (Score / High Score / Lives); `GameManager` pushes values in.
- `Assets/Scripts/RecordBook.cs` — static store for the chronological record-holder list (date/name/score), persisted in `PlayerPrefs` (`Arkanoid.Records`) as JSON.
- `Assets/Scripts/RecordsPanel.cs` — display-only end-of-round overlay under the `ScoreBoard` canvas: name entry after a new record, then the hall-of-fame list. Authored inactive; `GameManager` shows/hides it.
- `Assets/Editor/ArkanoidSetup.cs` — `[InitializeOnLoadMethod]` script that generates the sprite textures (`Assets/Sprites`), configures their importers, creates the bouncy `PhysicsMaterial2D` (`Assets/Physics`) and the `Ball`/`Brick` prefabs (`Assets/Prefabs`), then authors the camera, walls, paddle, and wired `GameManager` into the scene, and finally the `ScoreBoard` screen-space canvas. Editor-authored content stays inspectable without Play mode; only the ball and bricks exist at runtime.

## Editor setup is staged and resumable

`ArkanoidSetup.Setup()` performs **one** stage per domain reload and returns, relying on the next reload to observe the previous stage's output (textures → importers → physics material → prefabs → scene → scoreboard UI → records panel). Stage 7 only marks the scene dirty instead of saving it, to avoid the modal dialog described below. This works around `AssetDatabase` writes not being reliably readable back via `LoadAssetAtPath` within the same call. If you touch this file, preserve the one-stage-per-reload structure. The scene stage is guarded by `GameObject.Find("GameManager")`, so once set up the whole method is a no-op.

Folder existence is checked on disk (`System.IO.Directory`) rather than via `AssetDatabase.IsValidFolder`, which can go stale in driven sessions.

**Caution:** the scene stage calls `EditorSceneManager.SaveScene` from `[InitializeOnLoadMethod]`; in one session Unity then detected the open scene's file change and raised a *modal* "scene changed on disk" dialog that blocked the Editor main thread (and the Uplink bridge) until dismissed by hand. Either button is safe (disk and memory content are identical), but be aware programmatic scene saves can trigger it.

Use the `unity-game-design` skill for the general pattern this file follows when making further Editor-script changes here.
