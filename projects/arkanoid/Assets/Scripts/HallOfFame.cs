using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

// The hall of fame screen: one champion at a time, spelled out in the same
// block letters as the title. It sits a screen's width to the left of the
// menu inside MenuSlider, so picking "hall of fame" is a slide rather than a
// cut, and it is driven the same way everything else on the menu is — by
// hitting an arrow with the ball (left for the record before this one, right
// for the way back). Every arrow scrolls the way it points, the champions
// included.
//
// The book is walked down from the top rather than cycled: the plaque opens on
// the standing record and the left arrow goes back through the records it beat,
// one at a time. There is no way back up and no wrap — the walk ends on the
// oldest record kept, where the arrow is simply not there any more, so the
// player is never invited to ride the same ten names round again.
//
// The names are whatever players typed into the name entry, so the two lines
// are built at runtime rather than authored: block-font meshes made here and
// thrown away on the next champion.
//
// Each symbol of both lines is its own hittable block, exactly like a letter of
// the title — the plaque can be knocked apart while the player reads it, and
// moving on to the next champion builds it again. Putting a knocked-out symbol
// back is the menu's business rather than this component's: MainMenuPanel mends
// whichever board is about to travel into the frame, symbols and arrows alike.
public class HallOfFame : MonoBehaviour
{
    // The top of the record book, best first. More than ten would make the
    // walk down to the bottom of it a chore to sit through.
    public const int MaxChampions = 10;

    [SerializeField] MeshFilter nameLine;
    [SerializeField] MeshFilter scoreLine;
    // Hidden once the plaque is on the oldest record kept, and on an empty book
    // — an arrow that leads nowhere is an arrow the player is invited to knock
    // out for nothing. It is read under its old name as well, so a scene saved
    // while it still fetched the next champion in a cycle keeps its reference
    // instead of quietly losing the arrow — there is no stage that would wire
    // one back up.
    [FormerlySerializedAs("nextOption")]
    [SerializeField] GameObject prevOption;

    // Both lines share one cell size, so a long name doesn't read as a
    // different font from its score. The width is what the plaque has between
    // the two arrows; what caps the cell is the gap between the two lines,
    // measured off where they were authored (CellCap) rather than guessed at.
    // A short name is held back by that cap rather than by the width — "AGX"
    // over "300" has width to spare and would otherwise be blown up until the
    // name's feet stood in the top of the score.
    const float MaxWidth = 9f;
    // How much clear air the cap leaves between the two lines, in world units.
    // It used to be one cell — the same gap BlockText leaves between letters
    // along a line — and what fills it now is the name's shadow: the plaque
    // stands clear of the backdrop it throws that shadow onto, and a shadow's
    // drop is that gap times the tangent of the light's pitch. The gap is the
    // fog's depth (ScreenChange.FogWall), which is deliberately short, so this
    // is a fifth of a unit and a little air rather than the whole unit it was
    // when the backdrop stood well back. Anything less and the name's shadow is
    // thrown across the score, which is a plaque you read past rather than read.
    const float LineShadowGap = 0.35f;
    // Only used if a line is missing, in which case there is no spacing to
    // measure; the authored lines sit 1.75 apart, which is where this comes from.
    const float FallbackCell = 0.2f;
    const float LineDepth = 0.35f;
    const float LineUvScale = 2f;

    // The champion doesn't swap on the spot: the one being left behind travels
    // out across the frame, and the next one rises out of the fog behind it —
    // the same change the menu's two screens make between them (see
    // ScreenChange). A screen's width of travel, so the two never share the
    // frame, and only the champion leaving makes it.
    const float ScrollDistance = MainMenuPanel.ScreenSpacing;

    readonly List<RecordEntry> champions = new List<RecordEntry>();
    // Every symbol currently standing on the plaque, kept so the next champion
    // can clear them: Destroy is deferred to the end of the frame, so counting
    // the line's children again would be reading last champion's letters.
    readonly List<GameObject> symbols = new List<GameObject>();
    // What those symbols hang off — one group per line, and the two of them are
    // the plaque as far as a screen change is concerned: a champion is moved by
    // moving these rather than each of their letters.
    readonly List<Transform> groups = new List<Transform>();
    // Where the "previous record" arrow was authored, so an interrupted change
    // can't leave it out of the frame or under the plane.
    Vector3 optionHome;
    int index;

    void Awake()
    {
        if (prevOption != null) optionHome = prevOption.transform.localPosition;
    }

    // Every time the menu opens, so a record set in the meantime is in the book.
    public void Reload()
    {
        champions.Clear();
        var entries = RecordBook.Load();
        entries.Sort((a, b) => b.score.CompareTo(a.score));
        for (int i = 0; i < entries.Count && i < MaxChampions; i++) champions.Add(entries[i]);
        index = 0;
        if (prevOption != null) prevOption.transform.localPosition = optionHome;
        ClearPlaque(symbols, groups);
        Render();
        RefreshOptions();
    }

    // The record before the one on the plaque — the next one down the book —
    // arrived at rather than swapped in. The champion being left behind stays
    // standing and travels out of the frame, so the change reads as movement
    // along a row of champions rather than as one plaque becoming another; the
    // one arriving rises out of the fog behind the playing plane, which is the
    // change every menu screen makes (see ScreenChange). The arrow that was
    // hit travels in with them: name, score and arrow are one plaque, and it
    // shattered to make the choice, so it comes back in with the champion it
    // fetched rather than reappearing where it stood — unless the champion it
    // fetched is the last one, in which case there is nothing left for it to
    // lead to and it stays down, out of the plaque that arrives.
    public IEnumerator Advance(Ball ball)
    {
        // The arrow is hidden once there is nothing older to show, so this is
        // only ever reached with somewhere to travel to.
        if (index + 1 >= champions.Count) yield break;

        var leavingSymbols = new List<GameObject>(symbols);
        var leavingGroups = new List<Transform>(groups);
        var leaving = new ScreenPiece(leavingGroups);
        symbols.Clear();
        groups.Clear();

        index++;
        Render();

        // Whether the arrow is part of the plaque that is arriving at all: it
        // leads to the record before this one, and the last champion has none.
        bool arrowReturns = prevOption != null && index + 1 < champions.Count;

        // The arrow rests where MainMenuPanel's fit to the frame left it, which
        // is only known at runtime; the piece reads that off it rather than
        // being told, and puts it back there at the end of the travel.
        var arrivingParts = new List<Transform>(groups);
        if (arrowReturns) arrivingParts.Add(prevOption.transform);
        var arriving = new ScreenPiece(arrivingParts);
        // Down into the fog at once. Render builds the champion where the plaque
        // rests, and the one they are replacing is still standing there — the
        // arriving champion has to be out of sight in the same frame they exist.
        ScreenChange.Stage(arriving);
        // Which is also where the arrow can come back on: behind the backdrop,
        // with the champion it fetched.
        if (arrowReturns) prevOption.SetActive(true);

        yield return ScreenChange.FlyOut(leaving, ScrollDistance);
        // The champion who has left the frame goes now, out of sight.
        ClearPlaque(leavingSymbols, leavingGroups);
        yield return ScreenChange.Rise(arriving, ball);
    }

    // A shattered arrow is put back by the menu, or — for this one — scrolled
    // back in by Advance; either way the "previous record" arrow is one it may
    // not be right to show at all: an empty book has no records to walk, and
    // the oldest one kept has none before it.
    public void RefreshOptions()
    {
        if (prevOption != null) prevOption.SetActive(index + 1 < champions.Count);
    }

    // The tallest cell the two lines can wear without growing into each other —
    // or into each other's shadows. A glyph is GlyphHeight cells tall and is
    // drawn centred on its line, so the pair fits only while a glyph plus
    // LineShadowGap covers the distance between the lines. Measured off the
    // authored transforms rather than written down, so moving a line in the
    // scene resizes the lettering to match instead of silently overlapping it —
    // which is exactly what a 0.25 cell used to do across the 1.5 the lines
    // once stood apart.
    float CellCap
    {
        get
        {
            if (nameLine == null || scoreLine == null) return FallbackCell;
            float spacing = Mathf.Abs(
                nameLine.transform.localPosition.y - scoreLine.transform.localPosition.y);
            if (spacing <= LineShadowGap) return FallbackCell;
            return (spacing - LineShadowGap) / BlockText.GlyphHeight;
        }
    }

    // The champion at `index`, built where the plaque rests. Where they are
    // while they are arriving is the screen change's business rather than
    // this one's: it moves the groups the symbols hang off.
    void Render()
    {
        // A book with nothing in it says so in one word, on the name's line,
        // with the score's line left bare: EMPTY across the plaque reads as the
        // state of the hall, where a sentence there reads as a champion called
        // NO RECORDS YET — and one word takes the same cell the shortest name
        // would, so the plaque doesn't change size when the first record lands.
        bool empty = champions.Count == 0;
        string name = empty ? "EMPTY" : champions[index].name;
        string score = empty ? "" : champions[index].score.ToString();

        int columns = Mathf.Max(BlockText.WordColumns(name), BlockText.WordColumns(score));
        float cell = Mathf.Min(CellCap, MaxWidth / columns);

        BuildLine(nameLine, "ChampionName", name, cell);
        BuildLine(scoreLine, "ChampionScore", score, cell);
    }

    // Both lines' symbols go at once, since one cell size serves the pair and a
    // new champion re-measures it, and the groups they hung off go with them.
    static void ClearPlaque(List<GameObject> symbols, List<Transform> groups)
    {
        foreach (var symbol in symbols)
        {
            if (symbol == null) continue;
            var filter = symbol.GetComponent<MeshFilter>();
            // The meshes are this component's own, built and dropped as the
            // plaque changes, so each one is destroyed with its symbol rather
            // than left to the scene.
            if (filter != null && filter.sharedMesh != null) Destroy(filter.sharedMesh);
            Destroy(symbol);
        }
        symbols.Clear();
        foreach (var group in groups)
            if (group != null) Destroy(group.gameObject);
        groups.Clear();
    }

    // One hittable block per symbol rather than one mesh for the whole line, so
    // a name and a score can be broken up the way the title can. The line object
    // itself is only the anchor and the material the symbols wear; the UV origin
    // is each symbol's place in the word, which keeps the masonry running across
    // them as it does across the title's letters.
    //
    // The symbols hang off a group of their own rather than off the line, so
    // that a champion arriving and the champion they are replacing can be in
    // two different places — and at two different depths — at the same time.
    void BuildLine(MeshFilter line, string lineName, string word, float cell)
    {
        if (line == null || string.IsNullOrEmpty(word)) return;
        var renderer = line.GetComponent<MeshRenderer>();
        var material = renderer != null ? renderer.sharedMaterial : null;

        var group = new GameObject("Champion");
        group.transform.SetParent(line.transform, false);
        groups.Add(group.transform);

        for (int i = 0; i < word.Length; i++)
        {
            var cells = BlockText.GlyphCells(word[i]);
            // A space has nothing to hit and no geometry to show.
            if (IsBlank(cells)) continue;

            float x = BlockText.GlyphCentreX(word, i, cell);
            var symbol = new GameObject($"{lineName}{i}-{word[i]}");
            symbol.transform.SetParent(group.transform, false);
            symbol.transform.localPosition = new Vector3(x, 0f, 0f);
            symbol.AddComponent<MeshFilter>().sharedMesh = BlockText.BuildMesh(
                $"{lineName}{i}", cells, cell, LineDepth, LineUvScale, new Vector2(x, 0f));
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

    static bool IsBlank(bool[,] cells)
    {
        foreach (bool solid in cells)
            if (solid) return false;
        return true;
    }
}
