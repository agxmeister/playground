using UnityEngine;

// One of the three walls that close the menu's room off at the left, right and
// top edges of the screen. The menu has no walls to author: its border *is* the
// camera's frame, which is only known at runtime and changes with the window,
// so the walls are built and fitted by MainMenuPanel rather than placed in the
// scene — the same reason the option arrows are pulled in to the frame they
// find themselves in.
//
// They carry no renderer. What the player sees of the border is the ricochet
// itself: a burst of sparks at the point of impact, which is why the collision
// is handled here rather than left entirely to the physics engine.
//
// The bottom is deliberately left open — a ball that falls past the paddle
// materialises back on it, exactly as a menu ball always has.
[RequireComponent(typeof(BoxCollider2D))]
public class MenuBorder : MonoBehaviour
{
    public enum Side { Left, Right, Top }

    // How deep each wall runs behind the edge it closes. The menu's ball covers
    // about 0.13 of a unit per fixed step, so this is far too thick to be
    // tunnelled through however the frame is shaped.
    const float Thickness = 2f;

    [SerializeField] Side side;

    // The direction the wall throws the ball back in, which is also the
    // direction its sparks fly. Taken from the side rather than the contact, so
    // it is the same every time whichever corner of the ball touched first.
    Vector2 Outward => side switch
    {
        Side.Left => Vector2.right,
        Side.Right => Vector2.left,
        _ => Vector2.down,
    };

    // Builds the three walls under `room` if they aren't there yet and lays them
    // against the frame described by `centre` and `halfExtents`, with their
    // inner faces exactly on the frame's edges. Called again whenever the window
    // changes size, so a resized frame keeps its walls.
    public static void Fit(Transform room, Vector2 centre, Vector2 halfExtents, float z)
    {
        foreach (Side side in System.Enum.GetValues(typeof(Side)))
        {
            var border = Find(room, side);
            var collider = border.GetComponent<BoxCollider2D>();

            // The walls overlap at the corners — each is grown by its own
            // thickness along the edge it lies on — so a ball reaching a corner
            // meets a wall rather than the gap between two.
            Vector2 offset = side switch
            {
                Side.Left => new Vector2(-halfExtents.x - Thickness * 0.5f, 0f),
                Side.Right => new Vector2(halfExtents.x + Thickness * 0.5f, 0f),
                _ => new Vector2(0f, halfExtents.y + Thickness * 0.5f),
            };
            Vector2 size = side == Side.Top
                ? new Vector2(2f * (halfExtents.x + Thickness), Thickness)
                : new Vector2(Thickness, 2f * (halfExtents.y + Thickness));

            var world = new Vector3(centre.x + offset.x, centre.y + offset.y, z);
            border.transform.position = world;
            collider.size = size;
        }
    }

    static MenuBorder Find(Transform room, Side side)
    {
        string name = $"Border{side}";
        var existing = room.Find(name);
        if (existing != null) return existing.GetComponent<MenuBorder>();

        var go = new GameObject(name);
        go.transform.SetParent(room, false);
        go.AddComponent<BoxCollider2D>();
        var border = go.AddComponent<MenuBorder>();
        border.side = side;
        return border;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        var ball = collision.collider.GetComponent<Ball>();
        if (ball == null || collision.contactCount == 0) return;

        var renderer = ball.GetComponentInChildren<MeshRenderer>();
        var contact = collision.GetContact(0).point;
        Ricochet.Spawn(new Vector3(contact.x, contact.y, ball.transform.position.z),
            Outward, renderer != null ? renderer.sharedMaterial : null);
    }
}
