# CLAUDE.md

Grid-based worm (snake) game. Unity `6000.5.6f1`, Universal Render Pipeline, new Input System.

## Layout

- `Assets/Scripts/WormGame.cs` — all runtime gameplay logic (movement, input, collision, food, score, game over/restart). `Update`-driven with a fixed `moveInterval` tick.
- `Assets/Scripts/WormGameDefaults.cs` — shared constants (grid size, cell size) used by both the runtime script and the editor setup, so they stay in sync.
- `Assets/Editor/WormGameEditorSetup.cs` — `[InitializeOnLoadMethod]` script that authors the camera, border walls, sprite asset, and `Segment`/`Food` prefabs into the scene on load, and wires a `WormGame` GameObject to them via `SerializedObject`. This keeps editor-authored content (camera, walls, prefab references) inspectable/editable in the Hierarchy without needing Play mode, while only the moving worm body and food position are created at runtime.

## Editor setup is staged and resumable

`WormGameEditorSetup.Setup()` does **one** asset-creation step per invocation, then calls `EditorUtility.RequestScriptReload()` and returns, relying on the *next* domain reload to observe the result and proceed to the next step (folder → sprite texture → sprite → prefabs folder → Segment prefab → Food prefab → camera/walls/WormGame object). This works around `AssetDatabase` writes (`CreateAsset`, folder creation) not being reliably readable back via `LoadAssetAtPath` within the same call in every Editor session. If you touch this file, preserve that one-step-per-reload structure — collapsing steps or reading back an asset synchronously in the same call is the failure mode this file already worked around once.

Folder existence is checked directly on disk (`Directory.Exists`) rather than via `AssetDatabase.IsValidFolder`, for the same reason — `AssetDatabase` state was observed out of sync with the filesystem across separate calls, causing duplicate `Sprites 1`, `Sprites 2`, ... folders.

Use the `unity-game-design` skill for the general pattern this file follows when making further Editor-script changes here.
