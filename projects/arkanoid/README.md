# Arkanoid

A classic Arkanoid (Breakout) game built in Unity.

## Gameplay

- Move the paddle with the arrow keys or A/D.
- Press Space to launch the ball.
- Break all the bricks to win; higher rows are worth more points.
- You have 3 lives — losing the ball below the paddle costs one.
- Press Space to restart after game over or victory.

## Requirements

- Unity `6000.5.6f1` (see `ProjectSettings/ProjectVersion.txt`).

## Opening the project

Open this folder (`projects/arkanoid`) as a project in Unity Hub, then open `Assets/Scenes/SampleScene.unity` and press Play.

## How it's built

- `Assets/Scripts/Paddle.cs` — keyboard-driven paddle movement, clamped to the playfield.
- `Assets/Scripts/Ball.cs` — constant-speed ball: attaches to the paddle before launch, deflects off the paddle based on hit position, and never flattens into a horizontal trajectory.
- `Assets/Scripts/Brick.cs` — destroys itself on ball impact and reports its points to the game manager.
- `Assets/Scripts/GameManager.cs` — score, lives, brick-wall construction, win/lose states, and the OnGUI HUD.
- `Assets/Editor/ArkanoidSetup.cs` — editor-time setup that generates the sprites, physics material, and Ball/Brick prefabs, and authors the camera, walls, paddle, and a wired GameManager into the scene, so everything is visible and editable in the Hierarchy/Inspector without entering Play mode. Only the ball and bricks are created at runtime, since that state doesn't exist until the game is running.
