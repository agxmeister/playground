using UnityEngine;
using UnityEngine.UI;

// Title screen overlay: game name, current high score and key prompts.
// Display only — GameManager shows/hides it.
public class MainMenuPanel : MonoBehaviour
{
    [SerializeField] Text highScoreText;

    public void Show(int highScore)
    {
        gameObject.SetActive(true);
        highScoreText.text = highScore > 0 ? $"HIGH SCORE  {highScore}" : "";
    }

    public void Hide() => gameObject.SetActive(false);
}
