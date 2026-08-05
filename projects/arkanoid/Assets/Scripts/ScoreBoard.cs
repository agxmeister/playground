using UnityEngine;
using UnityEngine.UI;

// Screen-space HUD showing the current score, the persisted high score and the
// remaining lives. GameManager pushes values in; this component only renders.
public class ScoreBoard : MonoBehaviour
{
    [SerializeField] Text scoreValue;
    [SerializeField] Text highScoreValue;
    [SerializeField] Text livesValue;

    public void SetScore(int score) => scoreValue.text = score.ToString();
    public void SetHighScore(int highScore) => highScoreValue.text = highScore.ToString();
    public void SetLives(int lives) => livesValue.text = lives.ToString();
}
