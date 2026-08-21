using System.Collections.Generic;
using UnityEngine;

// The board a lost round comes to rest on: the words GAME OVER in the menu's own
// block letters, over the score the round finished with.
//
// It is a screen of the menu like the title board and the hall of fame — same
// room, same fog, same ball still in play — and it stands *between* those two
// inside MenuSlider, because every arrow scrolls the way it points: the hall of
// fame is one arrow to the left of a lost round and the title board one to the
// right. Nothing on the menu leads back here; a lost round is the only way in,
// which is why it is the one board that is arrived at rather than travelled to.
//
// The words are authored, one hittable block per letter, exactly as the title is
// (ArkanoidSetup builds them). The score is not: it is only known when a round
// ends, so that line is built here at runtime, the way the hall builds a
// champion, and it comes apart under the ball just the same.
//
// The words are cut from the title's masonry and the number is white, which is
// the split the hall's plaque makes between a champion's name and their score: a
// name is lettering, a score is a readout. The line's own renderer is where that
// comes from, so it is authored rather than chosen here.
public class GameOverBoard : MonoBehaviour
{
    // The anchor the score's blocks hang off, and where they take their material
    // from. Empty in the scene — there is no number to author.
    [SerializeField] MeshFilter scoreLine;
    // The cell the words above are drawn at, handed over by whoever authored
    // them. It is a *ceiling* on the score's own cell rather than a size the
    // score is given: a number drawn larger than the words it stands under
    // would be the loudest thing on the screen.
    [SerializeField] float wordCell = 0.18f;

    // How wide the score's line may be: the span the board has between its two
    // arrows, which is the width the hall's plaque works to as well.
    const float MaxWidth = 9f;

    readonly List<GameObject> symbols = new List<GameObject>();

    // A round lost, with the score it ended on. Called as the menu opens on this
    // board, so the line is built where it rests — everything that happens to it
    // afterwards is the board's own travel.
    public void Show(int score)
    {
        BlockLine.Clear(symbols);
        if (scoreLine == null) return;

        var word = score.ToString();
        var renderer = scoreLine.GetComponent<MeshRenderer>();
        BlockLine.Build(scoreLine.transform, "OverScore", word,
            Mathf.Min(wordCell, MaxWidth / BlockText.WordColumns(word)),
            renderer != null ? renderer.sharedMaterial : null, symbols);
    }
}
