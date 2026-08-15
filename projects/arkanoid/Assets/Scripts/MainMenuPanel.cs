using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public enum MainMenuOption { StartGame, HallOfFame, NextChampion, BackToMenu }

// The title screen — a small playable scene rather than a UI panel. The
// component sits on the MenuScreen root itself (see "The main menu screen" in
// CLAUDE.md), so showing and hiding the menu is just switching that subtree on
// and off, and everything in it is real geometry the ball can hit: the
// ARKANOID letters and the option arrows.
//
// Picking an option means aiming: the ball waits on the paddle, the player
// slides the paddle under the arrow they want and launches with SPACE, just as
// they would launch a round. The two arrows sit either side of the word, so a
// launch straight up picks nothing.
//
// The menu is two screens side by side inside MenuSlider — the title board and
// the hall of fame a screen's width to its left — and choosing the hall of
// fame slides the board off to the right rather than cutting to another view.
// The paddle and ball are outside the slider, so they stay put while the world
// behind them moves.
//
// There is deliberately no wall around the menu — the field is unlimited, and
// a ball that leaves the camera's frame materialises back on the paddle
// instead of being lost.
public class MainMenuPanel : MonoBehaviour
{
    [SerializeField] GameObject playGroup;
    [SerializeField] Transform slider;
    [SerializeField] Transform title;
    [SerializeField] HallOfFame hall;
    [SerializeField] Paddle paddle;
    [SerializeField] Ball ball;

    // Only ever raised for StartGame: the hall of fame and the way back from it
    // are the menu's own business now that it is a screen rather than a panel.
    public event System.Action<MainMenuOption> OptionChosen;

    // How far apart the two screens sit inside the slider — comfortably more
    // than the camera's frame is wide, so neither shows a corner of the other.
    // The hall of fame sits this far to the *left* of the title board, because
    // every arrow on the menu scrolls the way it points: the left-pointing HALL
    // OF FAME arrow travels left to reach it, and the right-pointing one on the
    // hall travels back right to the board.
    public const float ScreenSpacing = 20f;

    // GameManager draws the shared "press SPACE to launch" prompt for this.
    // While a choice is being carried out the menu is mid-change, so no prompt
    // even though the ball is sitting back on the paddle.
    public bool BallWaiting => isActiveAndEnabled && !busy && ball != null && ball.IsAttached;

    // How far past the frame edge the ball goes before it counts as out, so it
    // leaves properly rather than blinking away on the boundary.
    const float FrameMargin = 0.06f;

    // How long the picked option's rubble gets to fall before the screen
    // changes. Debris lives 1.2–2 s, so this is most of the way through the
    // fall without leaving the player waiting on an empty menu.
    const float ShatterPause = 0.9f;

    // The slide between the two screens. Long enough to read as travel, short
    // enough not to be waited on.
    const float SlideDuration = 0.6f;

    // The gap an option arrow keeps from the frame edge once it has been pulled
    // in to fit — the same gap the 16:9 layout leaves it, so a narrower screen
    // looks like the same design rather than a squeezed one.
    const float OptionEdgeMargin = 0.55f;

    // Where each option arrow was authored, which is the layout for a 16:9
    // frame — the widest the arrows are ever placed.
    readonly System.Collections.Generic.Dictionary<Transform, float> authoredOptionX =
        new System.Collections.Generic.Dictionary<Transform, float>();
    // The frame the arrows were last fitted to, so a window resized while the
    // menu is up doesn't leave them where the old one wanted them.
    Vector2Int fittedTo;

    int shownFrame;
    // True from the hit that picks an option until the screen has finished
    // changing. Nothing else in the menu answers while it is set.
    bool busy;
    bool showingHall;
    Vector3 paddleRest;

    void Awake()
    {
        // Where the paddle was authored: under the middle of the screen.
        if (paddle != null) paddleRest = paddle.transform.localPosition;
        if (slider != null)
            foreach (var option in slider.GetComponentsInChildren<MenuOption>(true))
                authoredOptionX[option.transform] = option.transform.localPosition.x;
    }

    // The layout runs the full width of a 16:9 frame, which is wider than the
    // screen may actually be. Rather than authoring for the narrowest screen
    // anyone might play on and leaving a 16:9 one half empty, the arrows are
    // pulled in from the edges to whatever frame they find themselves in.
    void FitOptionsToFrame()
    {
        var camera = Camera.main;
        if (camera == null) return;
        fittedTo = new Vector2Int(Screen.width, Screen.height);

        foreach (var pair in authoredOptionX)
        {
            var option = pair.Key;
            if (option == null) continue;
            var renderer = option.GetComponent<Renderer>();
            if (renderer == null) continue;

            float depth = option.position.z - camera.transform.position.z;
            float frameHalfWidth = camera.ViewportToWorldPoint(new Vector3(1f, 0.5f, depth)).x
                - camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, depth)).x;
            float limit = frameHalfWidth - renderer.bounds.extents.x - OptionEdgeMargin;

            float x = Mathf.Sign(pair.Value) * Mathf.Min(Mathf.Abs(pair.Value), limit);
            option.localPosition = new Vector3(x, option.localPosition.y, option.localPosition.z);
        }
    }

    public void Show()
    {
        gameObject.SetActive(true);
        busy = false;
        showingHall = false;
        if (slider != null) slider.localPosition = Vector3.zero;
        if (hall != null) hall.Reload();
        RestoreTitle();
        if (playGroup != null) playGroup.SetActive(true);
        RestoreOptions();
        FitOptionsToFrame();
        if (paddle != null) paddle.transform.localPosition = paddleRest;
        ResetBall();
    }

    // The menu's rubble is made of unparented objects, so it would go on
    // falling over the round that replaces the screen if it weren't swept up.
    public void Hide()
    {
        gameObject.SetActive(false);
        Debris.ClearAll();
    }

    // Called by MenuOption when the ball reaches it; the return value says
    // whether this is the hit that counted, and so whether the arrow should
    // shatter. The first hit wins — the ball stays live through the pause and
    // the frames a screen change then takes, and it must not be able to pick a
    // second option on the way.
    public bool OnOptionHit(MainMenuOption option)
    {
        if (busy) return false;
        // An arrow belonging to the screen that isn't up can't be picked. Both
        // screens live in the scene at once, and only the slide separates them.
        if (showingHall != (option == MainMenuOption.NextChampion || option == MainMenuOption.BackToMenu))
            return false;
        busy = true;
        StartCoroutine(CarryOut(option));
        return true;
    }

    // The pause is the point: the arrow breaks apart and its pieces fall before
    // the screen it leads to comes up, so the hit reads as a hit rather than as
    // an instant cut.
    IEnumerator CarryOut(MainMenuOption option)
    {
        yield return new WaitForSeconds(ShatterPause);

        // Starting a game hands the screen over to GameManager, and this menu
        // is switched off wholesale — nothing to put back.
        if (option == MainMenuOption.StartGame)
        {
            OptionChosen?.Invoke(option);
            yield break;
        }

        switch (option)
        {
            case MainMenuOption.HallOfFame:
                yield return SlideTo(true);
                break;
            case MainMenuOption.BackToMenu:
                yield return SlideTo(false);
                break;
            case MainMenuOption.NextChampion:
                // The champion doesn't change on the spot either: the plaque
                // scrolls left, the same travel the arrow that was hit points
                // along, and the champion being left behind slides out of the
                // frame rather than blinking out of it.
                if (hall != null) yield return hall.Advance();
                break;
        }

        // The arrow that was just hit is put back, exactly like the title
        // letters, and the ball is served again — the screen has moved out from
        // under it, and a fresh serve is how every other choice starts. The
        // plaque's symbols go back with it: they are hittable too, and a
        // champion arrived at with half their name missing can't be read.
        RestoreOptions();
        if (hall != null) hall.RestoreSymbols();
        ResetBall();
        busy = false;
    }

    IEnumerator SlideTo(bool toHall)
    {
        showingHall = toHall;
        if (slider == null) yield break;
        float from = slider.localPosition.x;
        // The hall is to the left, so reaching it carries the slider right —
        // which is the view travelling left, the way its arrow points.
        float to = toHall ? ScreenSpacing : 0f;
        for (float t = 0f; t < SlideDuration; t += Time.deltaTime)
        {
            slider.localPosition = new Vector3(
                Mathf.SmoothStep(from, to, t / SlideDuration), 0f, 0f);
            yield return null;
        }
        slider.localPosition = new Vector3(to, 0f, 0f);
    }

    void OnEnable()
    {
        // The key that brought us here (SPACE on an end screen) is still down
        // this frame — don't let it launch the ball as well.
        shownFrame = Time.frameCount;
    }

    void Update()
    {
        // While a choice is being carried out the menu only has to look right —
        // the ball is nobody's input any more.
        if (busy || Time.frameCount == shownFrame || ball == null || paddle == null) return;
        // The exit confirmation freezes time, but SPACE would still be read
        // here and launch the ball behind the prompt.
        if (GameManager.Instance != null && GameManager.Instance.Paused) return;

        if (fittedTo.x != Screen.width || fittedTo.y != Screen.height) FitOptionsToFrame();

        if (ball.IsAttached)
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) ball.Launch();
            return;
        }

        if (OutOfFrame(ball.transform.position)) ResetBall();
    }

    void ResetBall()
    {
        if (ball != null && paddle != null) ball.AttachTo(paddle.transform);
    }

    // "Out of the field" is literally out of the camera's view, which holds on
    // any aspect ratio — a hard-coded rectangle would strand the ball offscreen
    // on a wide monitor and swallow it early on a narrow one.
    static bool OutOfFrame(Vector3 position)
    {
        var camera = Camera.main;
        if (camera == null) return false;
        var viewport = camera.WorldToViewportPoint(position);
        return viewport.z < 0f
            || viewport.x < -FrameMargin || viewport.x > 1f + FrameMargin
            || viewport.y < -FrameMargin || viewport.y > 1f + FrameMargin;
    }

    void RestoreTitle()
    {
        if (title == null) return;
        foreach (Transform letter in title) letter.gameObject.SetActive(true);
    }

    // The arrow the player picked shattered itself off; put every option on
    // both screens back. Searched with inactive objects included, since a
    // shattered arrow is exactly the object that is switched off.
    void RestoreOptions()
    {
        if (slider == null) return;
        foreach (var option in slider.GetComponentsInChildren<MenuOption>(true))
            option.gameObject.SetActive(true);
        // ...except the one the hall of fame itself hides when there is no
        // second champion to advance to. Its own state is left alone: the
        // champion just advanced to must not be reset back to the first.
        if (hall != null) hall.RefreshOptions();
    }
}
