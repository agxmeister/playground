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
        if (keyboard == null) return;

        float direction = 0f;
        if (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed) direction -= 1f;
        if (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed) direction += 1f;

        // Only X moves. The Z is kept rather than zeroed because the menu's
        // paddle lives on the menu screen's plane, well in front of the
        // playfield's.
        float x = Mathf.Clamp(transform.position.x + direction * speed * Time.deltaTime,
            homeX - xLimit, homeX + xLimit);
        transform.position = new Vector3(x, transform.position.y, transform.position.z);
    }
}
