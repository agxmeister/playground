using UnityEngine;
using UnityEngine.InputSystem;

public enum MainMenuOption { StartGame, HallOfFame }

// The title screen — a small playable scene rather than a UI panel. The
// component sits on the MenuScreen root itself (see "The main menu screen" in
// CLAUDE.md), so showing and hiding the menu is just switching that subtree on
// and off, and everything in it is real geometry the ball can hit: the
// ARKANOID letters, the two option slabs, the paddle.
//
// Picking an option means aiming: the ball waits on the paddle, the player
// slides the paddle under the option they want and launches with SPACE, just
// as they would launch a round. The two slabs leave an alley between them that
// the paddle rests in, so a launch straight up picks nothing.
//
// There is deliberately no wall around the menu — the field is unlimited, and
// a ball that leaves the camera's frame materialises back on the paddle
// instead of being lost.
public class MainMenuPanel : MonoBehaviour
{
    [SerializeField] GameObject playGroup;
    [SerializeField] Transform title;
    [SerializeField] Paddle paddle;
    [SerializeField] Ball ball;

    public event System.Action<MainMenuOption> OptionChosen;

    // GameManager draws the shared "press SPACE to launch" prompt for this.
    public bool BallWaiting => isActiveAndEnabled && ball != null && ball.IsAttached;

    // How far past the frame edge the ball goes before it counts as out, so it
    // leaves properly rather than blinking away on the boundary.
    const float FrameMargin = 0.06f;

    int shownFrame;
    bool chosen;
    Vector3 paddleRest;

    void Awake()
    {
        // Where the paddle was authored: in the alley between the two slabs.
        if (paddle != null) paddleRest = paddle.transform.localPosition;
    }

    public void Show()
    {
        gameObject.SetActive(true);
        chosen = false;
        RestoreTitle();
        if (playGroup != null) playGroup.SetActive(true);
        if (paddle != null) paddle.transform.localPosition = paddleRest;
        ResetBall();
    }

    // The menu's hall of fame view keeps the screen and the title but drops the
    // playable half, so a stray bounce can't pick an option from behind the
    // records panel.
    public void HideOptions()
    {
        if (playGroup != null) playGroup.SetActive(false);
    }

    public void Hide() => gameObject.SetActive(false);

    // Called by MenuOption when the ball reaches it. The first hit wins: the
    // ball is still in play for the frames it takes GameManager to switch away.
    public void OnOptionHit(MainMenuOption option)
    {
        if (chosen) return;
        chosen = true;
        OptionChosen?.Invoke(option);
    }

    void OnEnable()
    {
        // The key that brought us here (SPACE on an end screen) is still down
        // this frame — don't let it launch the ball as well.
        shownFrame = Time.frameCount;
    }

    void Update()
    {
        if (Time.frameCount == shownFrame || ball == null || paddle == null) return;

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
}
