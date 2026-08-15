using System.Collections.Generic;
using UnityEngine;

// The hall of fame screen: one champion at a time, spelled out in the same
// block letters as the title. It sits a screen's width to the right of the
// menu inside MenuSlider, so picking "hall of fame" is a slide rather than a
// cut, and it is driven the same way everything else on the menu is — by
// hitting an arrow with the ball (left for the next champion, right for the
// way back).
//
// The names are whatever players typed into the name entry, so the two lines
// are built at runtime rather than authored: block-font meshes made here and
// thrown away on the next champion.
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
    // the two arrows; the cell cap is what keeps a three-letter name from
    // being blown up to twice the height of the title.
    const float MaxCell = 0.2f;
    const float MaxWidth = 7f;
    const float LineDepth = 0.35f;
    const float LineUvScale = 2f;

    readonly List<RecordEntry> champions = new List<RecordEntry>();
    int index;

    // Every time the menu opens, so a record set in the meantime is in the book.
    public void Reload()
    {
        champions.Clear();
        var entries = RecordBook.Load();
        entries.Sort((a, b) => b.score.CompareTo(a.score));
        for (int i = 0; i < entries.Count && i < MaxChampions; i++) champions.Add(entries[i]);
        index = 0;
        Render();
    }

    // Wraps: the tenth champion's next is the first again.
    public void Next()
    {
        if (champions.Count > 0) index = (index + 1) % champions.Count;
        Render();
    }

    // The menu puts every shattered arrow back after a choice, including the
    // "next champion" one it may not be right to show.
    public void RefreshOptions()
    {
        if (nextOption != null) nextOption.SetActive(champions.Count > 1);
    }

    void Render()
    {
        bool empty = champions.Count == 0;
        string name = empty ? "NO RECORDS YET" : champions[index].name;
        string score = empty ? "" : champions[index].score.ToString();

        int columns = Mathf.Max(BlockText.WordColumns(name), BlockText.WordColumns(score));
        float cell = Mathf.Min(MaxCell, MaxWidth / columns);

        SetLine(nameLine, "ChampionName", name, cell);
        SetLine(scoreLine, "ChampionScore", score, cell);
        RefreshOptions();
    }

    // The mesh is this component's own, built and dropped as the plaque
    // changes, so each one is destroyed rather than left to the scene.
    void SetLine(MeshFilter line, string meshName, string word, float cell)
    {
        if (line == null) return;
        if (line.sharedMesh != null) Destroy(line.sharedMesh);
        line.sharedMesh = string.IsNullOrEmpty(word)
            ? null
            : BlockText.BuildWordMesh(meshName, word, cell, LineDepth, LineUvScale);
    }
}
