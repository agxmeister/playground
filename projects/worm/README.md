# Worm

A grid-based worm (snake) game built in Unity.

## Gameplay

- Move with the arrow keys or WASD.
- Eating food grows the worm by one segment and increases the score.
- Hitting a wall or the worm's own body ends the game.
- Press R or Space to restart after game over.

## Requirements

- Unity `6000.5.6f1` (see `ProjectSettings/ProjectVersion.txt`).

## Opening the project

Open this folder (`projects/worm`) as a project in Unity Hub, then open `Assets/Scenes/SampleScene.unity` and press Play.

## How it's built

- `Assets/Scripts/WormGame.cs` — runtime gameplay: input, movement, collision, food spawning, score, game-over/restart.
- `Assets/Scripts/WormGameDefaults.cs` — shared grid/cell constants.
- `Assets/Editor/WormGameEditorSetup.cs` — editor-time setup that authors the camera, walls, sprite, and Segment/Food prefabs directly into the scene the first time it's opened, so they're visible and editable in the Hierarchy/Inspector without entering Play mode. Only the moving worm body and food are created at runtime, since that state doesn't exist until the game is running.
