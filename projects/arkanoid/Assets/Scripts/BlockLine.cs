using System.Collections.Generic;
using UnityEngine;

// A line of the menu's block lettering built while the game is running, for the
// words nobody could have authored: a champion's name and their score on the
// hall of fame's plaque, and the score a round was just lost with on the game
// over board.
//
// **Every symbol is its own hittable block**, exactly like a letter of the
// title, so a line can be knocked apart while it is being read. The meshes
// belong to whoever built them rather than to the scene — a new word is a new
// set of them — so they are destroyed with their blocks (Clear).
public static class BlockLine
{
    // How deep the blocks stand and how far the masonry is scaled across them:
    // the same stone the menu's authored lettering is cut from, so a line built
    // here and the title never read as two different fonts.
    public const float Depth = 0.35f;
    const float UvScale = 2f;

    // Builds `word` under `parent` in `material`, and adds every block it made
    // to `symbols` — the caller's own list, since Destroy is deferred to the end
    // of the frame and reading the parent's children back would still be last
    // word's. What `parent` is depends on what has to move: the plaque hangs a
    // champion off a group of their own, so the champion arriving and the one
    // they replace can be in two places at once, while the game over board's
    // score hangs off its line directly, the whole board being what travels.
    //
    // Each symbol's UV origin is its place in the word, which keeps the masonry
    // running across the blocks as it does across the title's letters.
    public static void Build(Transform parent, string name, string word, float cell,
        Material material, List<GameObject> symbols)
    {
        if (parent == null || string.IsNullOrEmpty(word)) return;

        for (int i = 0; i < word.Length; i++)
        {
            // A space has nothing to hit and no geometry to show.
            if (BlockText.Blank(word[i])) continue;

            float x = BlockText.GlyphCentreX(word, i, cell);
            var symbol = new GameObject($"{name}{i}-{word[i]}");
            symbol.transform.SetParent(parent, false);
            symbol.transform.localPosition = new Vector3(x, 0f, 0f);
            symbol.AddComponent<MeshFilter>().sharedMesh = BlockText.BuildMesh(
                $"{name}{i}", BlockText.GlyphCells(word[i]), cell, Depth, UvScale, new Vector2(x, 0f));
            symbol.AddComponent<MeshRenderer>().sharedMaterial = material;
            // The glyph's whole 5 x 7 box, holes included, for the same reason
            // the title's letters use one: a collider tracing the strokes would
            // let the ball rattle around inside an O.
            symbol.AddComponent<BoxCollider2D>().size =
                new Vector2(BlockText.GlyphWidth * cell, BlockText.GlyphHeight * cell);
            symbol.AddComponent<MenuTitleBlock>();
            symbols.Add(symbol);
        }
    }

    // A line taken down: every block and the mesh it was drawn with, since the
    // meshes are built here rather than authored and nothing else will ever come
    // back for them.
    //
    // A cleared block is switched off and taken out of whatever it was hanging
    // under *before* it is destroyed, because Destroy only takes effect at the
    // end of the frame: until then the block is still standing there to be
    // found, and `MainMenuPanel.RestoreBoard` mending the board it was on would
    // switch a doomed symbol of the last word back on over the new one.
    public static void Clear(List<GameObject> symbols)
    {
        foreach (var symbol in symbols)
        {
            if (symbol == null) continue;
            symbol.SetActive(false);
            symbol.transform.SetParent(null, false);
            var filter = symbol.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) Object.Destroy(filter.sharedMesh);
            Object.Destroy(symbol);
        }
        symbols.Clear();
    }
}
