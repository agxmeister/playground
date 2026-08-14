using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public enum MainMenuOption { StartGame, HallOfFame }

// Title screen. The opaque part of the screen is 3D scene content (menuScreen:
// a full-frame backdrop box plus the brick-textured ARKANOID word), so this
// UGUI panel carries no background of its own — only the high score line and
// the option list drawn on top of it.
//
// Unlike the other panels this one is interactive: it owns the selection
// (arrows / W-S / mouse hover) and reports the chosen option to GameManager.
public class MainMenuPanel : MonoBehaviour
{
    [SerializeField] Text highScoreText;
    [SerializeField] GameObject menuScreen;
    [SerializeField] Text[] optionTexts;
    [SerializeField] Image[] optionHighlights;

    public event System.Action<MainMenuOption> OptionChosen;

    static readonly Color SelectedTextColor = new Color(1f, 0.95f, 0.62f);
    static readonly Color NormalTextColor = new Color(0.62f, 0.66f, 0.74f);
    static readonly Color SelectedBarColor = new Color(0.95f, 0.83f, 0.18f, 0.16f);
    static readonly Color NormalBarColor = new Color(1f, 1f, 1f, 0f);

    int selected;
    int shownFrame;
    Vector2 lastMousePosition;

    public void Show(int highScore)
    {
        if (menuScreen != null) menuScreen.SetActive(true);
        gameObject.SetActive(true);
        highScoreText.text = highScore > 0 ? $"HIGH SCORE  {highScore}" : "";
        Select(0);
    }

    // Hides the options but keeps the 3D screen up: the menu's hall of fame
    // view draws the records panel over the same backdrop.
    public void HideOptions() => gameObject.SetActive(false);

    public void Hide()
    {
        if (menuScreen != null) menuScreen.SetActive(false);
        gameObject.SetActive(false);
    }

    void OnEnable()
    {
        // The key that brought us here (SPACE on an end screen) is still down
        // this frame — don't let it activate an option as well.
        shownFrame = Time.frameCount;
        lastMousePosition = MousePosition();
    }

    void Update()
    {
        if (Time.frameCount == shownFrame) return;

        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)
                Select(selected + 1);
            if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame)
                Select(selected - 1);
        }

        bool activated = keyboard != null
            && (keyboard.enterKey.wasPressedThisFrame
                || keyboard.numpadEnterKey.wasPressedThisFrame
                || keyboard.spaceKey.wasPressedThisFrame);

        var mouse = Mouse.current;
        if (mouse != null)
        {
            // Only a moved cursor takes the selection over from the keyboard,
            // otherwise a cursor resting on an option would fight the arrows.
            var position = MousePosition();
            if ((position - lastMousePosition).sqrMagnitude > 1f)
            {
                lastMousePosition = position;
                int hovered = OptionAt(position);
                if (hovered >= 0) Select(hovered);
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                int clicked = OptionAt(position);
                if (clicked >= 0)
                {
                    Select(clicked);
                    activated = true;
                }
            }
        }

        if (activated) OptionChosen?.Invoke((MainMenuOption)selected);
    }

    void Select(int index)
    {
        if (optionTexts == null || optionTexts.Length == 0) return;
        selected = (index % optionTexts.Length + optionTexts.Length) % optionTexts.Length;

        for (int i = 0; i < optionTexts.Length; i++)
        {
            bool active = i == selected;
            if (optionTexts[i] != null)
                optionTexts[i].color = active ? SelectedTextColor : NormalTextColor;
            if (optionHighlights != null && i < optionHighlights.Length && optionHighlights[i] != null)
                optionHighlights[i].color = active ? SelectedBarColor : NormalBarColor;
        }
    }

    // The index of the option row under a screen point, or -1. The canvas is a
    // screen-space overlay, so the hit test needs no camera.
    int OptionAt(Vector2 screenPosition)
    {
        if (optionHighlights == null) return -1;
        for (int i = 0; i < optionHighlights.Length; i++)
        {
            if (optionHighlights[i] == null) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(optionHighlights[i].rectTransform, screenPosition, null))
                return i;
        }
        return -1;
    }

    static Vector2 MousePosition() =>
        Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
}
