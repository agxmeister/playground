using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// The order is what the scene stores — a serialized enum is an index — so
// members are renamed in place and never reordered. PreviousRecord went up as
// NextChampion, back when the hall cycled its names instead of walking down
// through them.
public enum MainMenuOption { StartGame, HallOfFame, PreviousRecord, BackToMenu }

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
// The menu is three screens side by side inside MenuSlider — left to right, the
// hall of fame, the board a lost round ends on (GameOverBoard) and the title
// board — and choosing one is travelled to rather than cut to: the board being
// left flies out across the frame and the one arriving rises out of the fog
// behind the playing plane (see ScreenChange). The slider holds the layout; a
// change moves the boards. The paddle and ball are outside the slider, so they
// stay put while the world behind them moves.
//
// **That order in X is the order the arrows walk**, since every arrow scrolls
// the way it points: the game over board's left arrow reaches the hall and its
// right one the menu, which puts it between the two, and leaves the title
// board's HALL OF FAME arrow travelling two screens past it to get there.
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
    [SerializeField] GameOverBoard over;
    [SerializeField] Paddle paddle;
    [SerializeField] Ball ball;

    // Only ever raised for StartGame: the hall of fame and the way back from it
    // are the menu's own business now that it is a screen rather than a panel.
    public event System.Action<MainMenuOption> OptionChosen;

    // How far apart the screens sit inside the slider — comfortably more than
    // the camera's frame is wide, so none of them shows a corner of another.
    // They stand to the *left* of the title board, because every arrow on the
    // menu scrolls the way it points: the left-pointing HALL OF FAME arrow
    // travels left to reach the hall, and the right-pointing one on the hall
    // travels back right to the board.
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
    Vector3 paddleRest;
    // Where each of the slider's boards was authored. A change moves them
    // itself — out across the frame, and in again from under the plane — so
    // this is what says where "in place" is, and it is what a change is
    // measured against rather than any spacing written down here: the boards
    // are not all one screen apart.
    readonly System.Collections.Generic.Dictionary<Transform, Vector3> boardHomes =
        new System.Collections.Generic.Dictionary<Transform, Vector3>();
    // Which board is standing in the frame. Everything a change does is written
    // as "bring that one here" and everything a hit asks is "is this arrow on
    // it", so this is the whole of the menu's state.
    Transform current;

    Transform Board => title != null ? title.parent : null;

    // The boards the slider holds, in the order they stand in X: the hall of
    // fame, the board a lost round ends on, the title board.
    System.Collections.Generic.IEnumerable<Transform> Boards()
    {
        if (hall != null) yield return hall.transform;
        if (over != null) yield return over.transform;
        if (Board != null) yield return Board;
    }

    Vector3 HomeOf(Transform board) =>
        board != null && boardHomes.TryGetValue(board, out var home) ? home : Vector3.zero;

    void Awake()
    {
        // Where the paddle was authored: under the middle of the screen.
        if (paddle != null) paddleRest = paddle.transform.localPosition;
        foreach (var board in Boards()) boardHomes[board] = board.localPosition;
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

    public void Show() => Show(Board, 0);

    // The way in from a lost round: the menu opens on the game over board rather
    // than on the title board, with the score the round ended on read out under
    // the words. Nothing else about it is different — the ball is served, the
    // arrows are aimed at and the boards travel exactly as they do from the
    // title screen, and one arrow to the right is the title screen.
    public void ShowGameOver(int score) =>
        Show(over != null ? over.transform : Board, score);

    // Reopening the menu is the one moment the whole thing goes back to how it
    // was built: every board back where it was authored, mended, and whichever
    // one is being opened on standing in the frame.
    void Show(Transform opening, int score)
    {
        gameObject.SetActive(true);
        busy = false;
        ResetScreens(opening);
        if (hall != null) hall.Reload();
        // The score is a fact about the round that has just ended, so the board
        // is only ever told it on the way in from one.
        if (over != null && current == over.transform) over.Show(score);
        if (playGroup != null) playGroup.SetActive(true);
        // Mending a board in the frame is normally exactly what must not happen
        // (see RestoreBoard); the menu opening is the exception, since there was
        // nobody watching the last thing the ball did to it.
        foreach (var board in Boards()) RestoreBoard(board);
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
        JetTrail.ClearAll();
    }

    // Called by MenuOption when the ball reaches it, and answering false is
    // what keeps the arrow standing. The first hit wins — the ball stays live
    // through the pause and the frames a screen change then takes, and it must
    // not be able to pick a second option on the way — and a refused arrow has
    // to survive the hit, since the only mending it would get is on the way in
    // to a board it is already standing on.
    public bool OnOptionHit(MenuOption arrow)
    {
        if (busy || arrow == null) return false;
        // An arrow belonging to a board that isn't up can't be picked: every
        // board lives in the scene at once and only the slide separates them.
        // Which board an arrow belongs to is *where it stands* rather than which
        // choice it carries, now that HALL OF FAME and MENU each stand on two of
        // them — the same two destinations, reached from either.
        if (BoardOf(arrow.transform) != current) return false;
        busy = true;
        StartCoroutine(CarryOut(arrow));
        return true;
    }

    // The board something on the menu stands on: whichever of the slider's own
    // children it hangs under.
    Transform BoardOf(Transform part)
    {
        for (var at = part; at != null; at = at.parent)
            if (at.parent == slider) return at;
        return null;
    }

    // The pause is the point: the arrow breaks apart and its pieces fall before
    // the screen it leads to comes up, so the hit reads as a hit rather than as
    // an instant cut.
    IEnumerator CarryOut(MenuOption arrow)
    {
        yield return new WaitForSeconds(ShatterPause);

        var option = arrow.Option;

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
        // Both of these are reachable from more than one board now — the hall of
        // fame from the title board and from the game over board, the menu from
        // the hall and from the game over board — and they lead to the same
        // place from either. Where the arrow was hit only decides how far the
        // change has to travel, which ChangeTo works out for itself.
        switch (option)
        {
            case MainMenuOption.HallOfFame:
                RestoreBoard(hall != null ? hall.transform : null);
                yield return ChangeTo(hall != null ? hall.transform : null);
                break;
            case MainMenuOption.BackToMenu:
                // The word's parent is the board: the two arrows stand beside
                // it there, and they are mended with it.
                RestoreBoard(Board);
                yield return ChangeTo(Board);
                break;
            case MainMenuOption.PreviousRecord:
                // The champion doesn't change on the spot either: the plaque
                // makes the same change these boards do, the champion being
                // left behind travelling out of the frame the way the arrow
                // that was hit points and the record before theirs arriving
                // under the plane, rather than one of them blinking into the
                // other.
                if (hall != null) yield return hall.Advance(ball);
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
        RestoreOption(arrow);
        busy = false;
    }

    // One screen out and the next one up — no longer as one movement of the
    // slider carrying both, because the two halves no longer travel in the same
    // plane, or even in the same direction: the screen being left flies out
    // across the frame in the playing plane, and the one arriving never crosses
    // the frame at all — it stands where it belongs, down in the fog, and rises
    // out of it (see ScreenChange). The slider is only the layout now.
    IEnumerator ChangeTo(Transform target)
    {
        if (slider == null || target == null || target == current) yield break;

        var leaving = new ScreenPiece(current);
        var arriving = new ScreenPiece(target);

        // A board to the left is reached by carrying everything right — which is
        // the view travelling left, the way that board's arrow points. How far
        // is the gap between the two boards rather than one screen's width,
        // since they are not all one screen apart: the hall of fame stands two
        // screens from the title board with the game over board between them.
        float distance = HomeOf(current).x - HomeOf(target).x;

        yield return ScreenChange.FlyOut(leaving, distance);

        // The screen that has gone is that whole gap off to one side. Handing
        // that offset over to the slider, which is where the layout keeps it,
        // moves nothing on screen — the slider gains exactly what the screen
        // gives up — and carries the arriving screen into the frame, which is
        // why it goes down into the fog in the same frame: it is in the middle
        // of the picture from here on, and nothing should be seen there until
        // it rises.
        ScreenChange.Stage(arriving);
        slider.localPosition = new Vector3(-HomeOf(target).x, 0f, 0f);
        leaving.MoveTo(0f, 0f);
        current = target;

        yield return ScreenChange.Rise(arriving, ball);
    }

    // Everything a screen change moves, put back where it was authored, and
    // everything it switched off made solid, lit and visible again. A change is
    // a coroutine, and leaving the menu (START, and the round it leads to) stops
    // one wherever it had got to — with a screen off the frame, or down in the
    // fog: not there to be hit, wearing the fog's colour instead of its own and
    // casting no shadow. The menu is only ever reopened from the top, so this is
    // where all of that is undone.
    void ResetScreens(Transform opening)
    {
        if (slider == null) return;
        foreach (var board in Boards()) board.localPosition = HomeOf(board);
        // Which board the menu is opening on is the whole of where the slider
        // has to stand: the layout puts that board at its home, and the slider
        // takes it back off to bring it into the middle of the picture.
        current = opening != null ? opening : Board;
        slider.localPosition = new Vector3(-HomeOf(current).x, 0f, 0f);
        foreach (var collider in slider.GetComponentsInChildren<Collider2D>(true))
            collider.enabled = true;
        // Nothing else on the menu tints per instance, so clearing the block
        // outright is the same thing as taking the fog out of it.
        foreach (var renderer in slider.GetComponentsInChildren<Renderer>(true))
        {
            renderer.SetPropertyBlock(null);
            renderer.shadowCastingMode = ShadowCastingMode.On;
        }
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

    // One board made whole again — its lettering and the arrows standing beside
    // it. Called on a board that is about to travel into the frame, never on
    // one already in it: mending is invisible from off-screen, and that is the
    // only way it should ever be seen. (The exception is the menu opening, when
    // every board is mended at once and none of them has been looked at yet.)
    void RestoreBoard(Transform board)
    {
        if (board == null) return;
        foreach (var block in board.GetComponentsInChildren<MenuTitleBlock>(true))
            block.gameObject.SetActive(true);
        foreach (var arrow in board.GetComponentsInChildren<MenuOption>(true))
            arrow.gameObject.SetActive(true);
        // ...except the "previous record" arrow when the plaque has reached the
        // bottom of the book, or the book is empty.
        if (hall != null) hall.RefreshOptions();
    }

    // The arrow that carried out a choice, put back on its own — that one arrow
    // rather than every arrow carrying the same choice, since the same two
    // choices stand on two boards each and the ones the player did not hit have
    // to keep whatever the ball did to them. It shattered to make the choice,
    // and without it the board it belongs to has one fewer way out than it was
    // built with — unless it is the hall's own arrow and the plaque it led to is
    // the last record, which RefreshOptions puts back down in the same breath.
    void RestoreOption(MenuOption arrow)
    {
        if (arrow != null) arrow.gameObject.SetActive(true);
        if (hall != null) hall.RefreshOptions();
    }
}
