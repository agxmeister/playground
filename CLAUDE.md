# CLAUDE.md

This repository is a collection of independent Unity pet projects, one per directory under `projects/`. Each project is a standalone Unity project with its own `Assets/`, `ProjectSettings/`, and `Packages/` — there is no shared code or tooling between them.

## Projects

- `projects/worm` — grid-based worm (snake) game. See its `Assets/Scripts` for gameplay logic and `Assets/Editor` for editor-time scene/prefab setup.

## Working with Unity projects here

Use the `unity-game-design` skill when creating or modifying scenes, prefabs, or GameObjects through the Unity MCP bridge. Keep editor-authored content (camera, walls, prefabs, scene layout) separate from runtime-only gameplay state, per each project's existing convention.
