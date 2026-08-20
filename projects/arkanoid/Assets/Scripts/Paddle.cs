using UnityEngine;
using UnityEngine.InputSystem;

public class Paddle : MonoBehaviour
{
    [SerializeField] float speed = 10f;
    // Overwritten by FitTo as soon as the room this paddle belongs to has
    // measured its frame; the authored value only stands until then.
    [SerializeField] float xLimit = 6.5f;

    // The limits are kept either side of where the paddle was authored rather
    // than either side of the world's middle, because the menu is a room of its
    // own a screen's width to the left of the playfield: a menu paddle clamped
    // about x 0 would be dragged out of its own room on the first frame.
    float homeX;

    // Which way the paddle was travelling over the last frame, as −1, 0 or 1,
    // which is what the ball reads off it to twist a hit (see
    // Ball.OnCollisionEnter2D). It is not simply the key that is held: the two
    // part company exactly where it matters, because a paddle jammed against
    // the edge of the frame with the key still down has stopped, and a stopped
    // paddle has no twist to give. The paddle has one speed and nothing else
    // moves it, so there is nothing in between to report.
    public float Drift { get; private set; }

    void Awake()
    {
        homeX = transform.position.x;
    }

    // Both rooms' fields are the camera's frame now, so how far the paddle may
    // travel is only known once there is a window: the room that owns it hands
    // over half the frame's width and the paddle keeps its own body inside it.
    // Its width is measured rather than assumed, since the menu's paddle is a
    // scaled-down copy of the round's.
    public void FitTo(float roomHalfWidth)
    {
        var renderer = GetComponent<Renderer>();
        float halfWidth = renderer != null ? renderer.bounds.extents.x : 0f;
        xLimit = Mathf.Max(0f, roomHalfWidth - halfWidth);
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            Drift = 0f;
            return;
        }

        float direction = 0f;
        if (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed) direction -= 1f;
        if (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed) direction += 1f;

        // Only X moves. The Z is kept rather than zeroed because the menu's
        // paddle lives on the menu screen's plane, well in front of the
        // playfield's.
        float wanted = transform.position.x + direction * speed * Time.deltaTime;
        float x = Mathf.Clamp(wanted, homeX - xLimit, homeX + xLimit);
        // Taken from whether the clamp let the move through, rather than from
        // the key alone: a paddle already against the edge of the field is not
        // travelling however hard it is pushed.
        Drift = Mathf.Approximately(x, wanted) ? direction : 0f;
        transform.position = new Vector3(x, transform.position.y, transform.position.z);
    }
}
