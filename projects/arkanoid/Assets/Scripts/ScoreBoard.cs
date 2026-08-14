using UnityEngine;
using UnityEngine.UI;

// Screen-space HUD showing the current score and the remaining lives. The high
// score is not part of it — it only appears on the menu screen. GameManager
// pushes the values in and switches the HUD on for a round; this component only
// renders. The readouts sit under a `hud` child so hiding them leaves the
// panels sharing this canvas (records, main menu) alone.
public class ScoreBoard : MonoBehaviour
{
    [SerializeField] GameObject hud;
    [SerializeField] Text scoreValue;
    [SerializeField] Text livesValue;

    public void SetScore(int score) => scoreValue.text = score.ToString();
    public void SetLives(int lives) => livesValue.text = lives.ToString();

    public void SetVisible(bool visible)
    {
        if (hud != null) hud.SetActive(visible);
    }
}
