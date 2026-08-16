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
// The menu's room is closed on three sides by the frame itself: Border walls,
// built here and laid along the left, right and top edges of whatever the
// camera actually sees, so the ball ricochets off the edge of the screen in a
// burst of sparks rather than sailing out of it. The bottom is open, and a
// ball that falls past the paddle materialises back on it. A round's room is
// built the same way now (see Playfield), which is what makes the two screens
// one game rather than a bordered box and a full-width menu.
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
    // A screen change is no longer a reason to hold it back: the rally carries
    // on across the change, so a ball on the paddle mid-change is one that fell
    // past it and is waiting to be served again like any other.
    public bool BallWaiting => isActiveAndEnabled && ball != null && ball.IsAttached;

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
    // pulled in from the edges to whatever frame they find themselves in — and
    // the room's three walls are laid against those same edges.
    void FitToFrame()
    {
        var camera = Camera.main;
        if (camera == null) return;
        fittedTo = new Vector2Int(Screen.width, Screen.height);

        FitBorders(camera);

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

    // The menu's field is closed at the left, right and top by the frame itself
    // — there is nothing to author, since the frame is only known once there is
    // a window — so the walls are built here and laid against its edges on the
    // menu's own plane, the one everything hittable stands on. The paddle takes
    // its travel from the same measurement: the field is the frame, so it may
    // go as far as its own body allows either way.
    //
    // The frame is measured as extents and then centred on *the room* rather
    // than on the camera: the view travels out of this room to the playfield
    // with the menu still up, and walls that followed it would take the room's
    // edges with them.
    void FitBorders(Camera camera)
    {
        if (ball == null) return;
        float planeZ = ball.transform.position.z;
        var extents = Border.FrameExtents(camera, planeZ);
        Border.Fit(transform,
            new Vector2(transform.position.x, camera.transform.position.y),
            extents, planeZ);
        if (paddle != null) paddle.FitTo(extents.x);
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
        FitToFrame();
        if (paddle != null) paddle.transform.localPosition = paddleRest;
        ResetBall();
    }

    // The menu's rubble and its sparks are made of unparented objects, so they
    // would go on falling over the round that replaces the screen if they
    // weren't swept up.
    public void Hide()
    {
        gameObject.SetActive(false);
        Debris.ClearAll();
        Ricochet.ClearAll();
    }

    // Called by MenuOption when the ball reaches it, and answering false is
    // what keeps the arrow standing. The first hit wins — the ball stays live
    // through the pause and the frames a screen change then takes, and it must
    // not be able to pick a second option on the way — and a refused arrow has
    // to survive the hit, since the only mending it would get is on the way in
    // to a board it is already standing on.
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

        // A board is mended on the way *in*, before it sets off, which is the
        // one moment nobody is looking at it: it arrives whole and ready to be
        // read, and the board being left behind keeps whatever the ball did to
        // it until it is the one arriving. Nothing is mended once the movement
        // has started — see below.
        switch (option)
        {
            case MainMenuOption.HallOfFame:
                RestoreBoard(hall != null ? hall.transform : null);
                yield return SlideTo(true);
                break;
            case MainMenuOption.BackToMenu:
                // The word's parent is the board: the two arrows stand beside
                // it there, and they are mended with it.
                RestoreBoard(title != null ? title.parent : null);
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

        // Only the arrow that was hit is put back, because the screen needs it
        // to be picked again. Everything else the ball broke on the way stays
        // broken: the boards keep moving with the ball still in play, so the
        // player watches these hits land, and mending them the instant the
        // movement stops reads as the game quietly undoing them. Damage done
        // before a board sets off is a different matter — that board is mended
        // on the way in, above, out of sight.
        //
        // The ball is left alone too — it has been playing on through the
        // change, and catching it to serve it again would be the one part of
        // the menu that stops for a screen.
        RestoreOption(option);
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
        // A choice being carried out no longer stops any of this: the ball plays
        // on through the change, and it still has to be caught when it falls out
        // of the frame and served again when it does. What a choice does hold
        // off is another choice, which OnOptionHit refuses while busy.
        if (Time.frameCount == shownFrame || ball == null || paddle == null) return;
        // The exit confirmation freezes time, but SPACE would still be read
        // here and launch the ball behind the prompt.
        if (GameManager.Instance != null && GameManager.Instance.Paused) return;

        if (fittedTo.x != Screen.width || fittedTo.y != Screen.height) FitToFrame();

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
    // on a wide monitor and swallow it early on a narrow one. In practice this
    // is the bottom edge, the one the borders leave open; the other three stay
    // tested all the same, as the way home for a ball that a resized frame left
    // on the wrong side of a wall.
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

    // One board made whole again — its lettering and the arrows standing beside
    // it. Called on a board that is about to travel into the frame, never on
    // one already in it: mending is invisible from off-screen, and that is the
    // only way it should ever be seen.
    void RestoreBoard(Transform board)
    {
        if (board == null) return;
        foreach (var block in board.GetComponentsInChildren<MenuTitleBlock>(true))
            block.gameObject.SetActive(true);
        foreach (var arrow in board.GetComponentsInChildren<MenuOption>(true))
            arrow.gameObject.SetActive(true);
        // ...except the "next champion" arrow when there is nobody else to show.
        if (hall != null) hall.RefreshOptions();
    }

    // The arrow that carried out a choice, put back on its own. It shattered to
    // make the choice, and without it the board it belongs to has one fewer way
    // out than it was built with.
    void RestoreOption(MainMenuOption option)
    {
        if (slider == null) return;
        foreach (var arrow in slider.GetComponentsInChildren<MenuOption>(true))
            if (arrow.Option == option) arrow.gameObject.SetActive(true);
        if (hall != null) hall.RefreshOptions();
    }

    // The arrow the player picked shattered itself off; put every option on
    // both screens back. For reopening the menu, which is the one moment the
    // whole thing goes back to how it was built. Searched with inactive objects
    // included, since a shattered arrow is exactly the object that is switched
    // off.
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
