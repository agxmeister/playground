using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The hall of fame screen: one champion at a time, spelled out in the same
// block letters as the title. It sits a screen's width to the left of the
// menu inside MenuSlider, so picking "hall of fame" is a slide rather than a
// cut, and it is driven the same way everything else on the menu is — by
// hitting an arrow with the ball (left for the next champion, right for the
// way back). Every arrow scrolls the way it points, the champions included.
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
    // cycle a chore to sit through.
    public const int MaxChampions = 10;

    [SerializeField] MeshFilter nameLine;
    [SerializeField] MeshFilter scoreLine;
    // Hidden when there is nothing to advance to, so the player isn't invited
    // to knock out an arrow that would show them the same champion again.
    [SerializeField] GameObject nextOption;

    // Both lines share one cell size, so a long name doesn't read as a
    // different font from its score. The width is what the plaque has between
    // the two arrows; what caps the cell is the gap between the two lines,
    // measured off where they were authored (CellCap) rather than guessed at.
    // A short name is held back by that cap rather than by the width — "AGX"
    // over "300" has width to spare and would otherwise be blown up until the
    // name's feet stood in the top of the score.
    const float MaxWidth = 9f;
    // How much clear air the cap leaves between the two lines, in cells. One
    // cell is the same gap BlockText leaves between letters along a line, so
    // the plaque reads as evenly spaced in both directions.
    const float LineGapCells = 1f;
    // Only used if a line is missing, in which case there is no spacing to
    // measure; the authored lines sit 1.5 apart, which is where this comes from.
    const float FallbackCell = 0.1875f;
    const float LineDepth = 0.35f;
    const float LineUvScale = 2f;

    // The champion doesn't swap on the spot: the plaque scrolls, the next
    // champion coming in from the left as the current one leaves to the right,
    // the same travel the arrow that was hit points along. A screen's width of
    // it, so the two never share the frame, and the same span the slide between
    // the menu's two screens covers.
    const float ScrollDistance = MainMenuPanel.ScreenSpacing;
    const float ScrollDuration = 0.6f;

    readonly List<RecordEntry> champions = new List<RecordEntry>();
    // Every symbol currently standing on the plaque, kept so the next champion
    // can clear them: Destroy is deferred to the end of the frame, so counting
    // the line's children again would be reading last champion's letters.
    readonly List<GameObject> symbols = new List<GameObject>();
    int index;

    // Every time the menu opens, so a record set in the meantime is in the book.
    public void Reload()
    {
        champions.Clear();
        var entries = RecordBook.Load();
        entries.Sort((a, b) => b.score.CompareTo(a.score));
        for (int i = 0; i < entries.Count && i < MaxChampions; i++) champions.Add(entries[i]);
        index = 0;
        MoveLines(0f);
        ClearSymbols(symbols);
        Render(0f);
    }

    // The next champion, wrapping — the tenth's next is the first again —
    // scrolled in rather than swapped in. The champion being left behind stays
    // standing and travels out of the frame, so the change reads as movement
    // along a row of champions rather than as one plaque becoming another. The
    // arrow that was hit travels in with the champion arriving: name, score and
    // arrow are one plaque, and putting the arrow back where it stood while the
    // rest flowed in from the left was the one part appearing out of thin air.
    public IEnumerator Advance()
    {
        // The arrow is hidden when there is nobody else to show, so this is
        // only ever reached with somewhere to scroll to.
        if (champions.Count < 2) yield break;

        var outgoing = new List<GameObject>(symbols);
        symbols.Clear();
        index = (index + 1) % champions.Count;
        // The arrow that was just hit belongs to the champion arriving as much
        // as the name and the score do, so it comes in with them from the left
        // rather than being switched back on where it stood. Where it stands is
        // read off it now, before it is sent out to the left: that is where the
        // frame it was last fitted to wants it, which is not something to
        // guess at.
        float optionRest = OptionX;
        MoveOption(optionRest - ScrollDistance);
        // Built where it will come from, off to the left of the plaque.
        Render(-ScrollDistance);

        for (float t = 0f; t < ScrollDuration; t += Time.deltaTime)
        {
            float travelled = Mathf.SmoothStep(0f, ScrollDistance, t / ScrollDuration);
            MoveLines(travelled);
            MoveOption(optionRest - ScrollDistance + travelled);
            yield return null;
        }
        MoveLines(ScrollDistance);
        MoveOption(optionRest);

        // The travel is over; what is left is putting the lines back where they
        // were authored with the champion that has arrived standing on them, so
        // the next scroll starts from the same place this one did.
        ClearSymbols(outgoing);
        MoveLines(0f);
        foreach (var symbol in symbols)
            if (symbol != null) symbol.transform.localPosition += new Vector3(ScrollDistance, 0f, 0f);
    }

    // Both lines travel together — they are one plaque, and only their X moves.
    void MoveLines(float x)
    {
        MoveLine(nameLine, x);
        MoveLine(scoreLine, x);
    }

    // Where the "next champion" arrow stands when nothing is scrolling: the
    // place MainMenuPanel's fit to the frame left it, which is only known at
    // runtime and may change with the window.
    float OptionX => nextOption != null ? nextOption.transform.localPosition.x : 0f;

    void MoveOption(float x)
    {
        if (nextOption == null) return;
        var position = nextOption.transform.localPosition;
        nextOption.transform.localPosition = new Vector3(x, position.y, position.z);
    }

    static void MoveLine(MeshFilter line, float x)
    {
        if (line == null) return;
        var position = line.transform.localPosition;
        line.transform.localPosition = new Vector3(x, position.y, position.z);
    }

    // A shattered arrow is put back by the menu, or — for this one — scrolled
    // back in by Advance; either way the "next champion" arrow is one it may
    // not be right to show at all.
    public void RefreshOptions()
    {
        if (nextOption != null) nextOption.SetActive(champions.Count > 1);
    }

    // The tallest cell the two lines can wear without growing into each other.
    // A glyph is GlyphHeight cells tall and is drawn centred on its line, so
    // the pair fits only while a glyph plus the gap covers the distance between
    // the lines. Measured off the authored transforms rather than written down,
    // so moving a line in the scene resizes the lettering to match instead of
    // silently overlapping it — which is exactly what a 0.25 cell used to do
    // across the 1.5 the lines stand apart.
    float CellCap
    {
        get
        {
            if (nameLine == null || scoreLine == null) return FallbackCell;
            float spacing = Mathf.Abs(
                nameLine.transform.localPosition.y - scoreLine.transform.localPosition.y);
            if (spacing <= 0f) return FallbackCell;
            return spacing / (BlockText.GlyphHeight + LineGapCells);
        }
    }

    // xOffset is where along the plaque's travel this champion is built: 0 for
    // one that is simply there, a screen's width to the left for one that is
    // about to be scrolled in.
    void Render(float xOffset)
    {
        bool empty = champions.Count == 0;
        string name = empty ? "NO RECORDS YET" : champions[index].name;
        string score = empty ? "" : champions[index].score.ToString();

        int columns = Mathf.Max(BlockText.WordColumns(name), BlockText.WordColumns(score));
        float cell = Mathf.Min(CellCap, MaxWidth / columns);

        BuildLine(nameLine, "ChampionName", name, cell, xOffset);
        BuildLine(scoreLine, "ChampionScore", score, cell, xOffset);
        RefreshOptions();
    }

    // Both lines' symbols go at once, since one cell size serves the pair and a
    // new champion re-measures it.
    static void ClearSymbols(List<GameObject> symbols)
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
    }

    // One hittable block per symbol rather than one mesh for the whole line, so
    // a name and a score can be broken up the way the title can. The line object
    // itself is only the anchor and the material the symbols wear; the UV origin
    // is each symbol's place in the word, which keeps the masonry running across
    // them as it does across the title's letters.
    void BuildLine(MeshFilter line, string lineName, string word, float cell, float xOffset)
    {
        if (line == null || string.IsNullOrEmpty(word)) return;
        var renderer = line.GetComponent<MeshRenderer>();
        var material = renderer != null ? renderer.sharedMaterial : null;

        for (int i = 0; i < word.Length; i++)
        {
            var cells = BlockText.GlyphCells(word[i]);
            // A space has nothing to hit and no geometry to show.
            if (IsBlank(cells)) continue;

            float x = BlockText.GlyphCentreX(word, i, cell);
            var symbol = new GameObject($"{lineName}{i}-{word[i]}");
            symbol.transform.SetParent(line.transform, false);
            // The UV origin stays the symbol's place in the word whatever the
            // offset is, so the masonry runs across the letters the same way
            // wherever along its travel the plaque was built.
            symbol.transform.localPosition = new Vector3(x + xOffset, 0f, 0f);
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
