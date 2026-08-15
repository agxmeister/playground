using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Ball : MonoBehaviour
{
    [SerializeField] float speed = 8f;
    [SerializeField] int damage = 1;

    public int Damage => damage;

    // Below this fraction of speed the vertical component gets nudged, so the
    // ball can never settle into an endless horizontal bounce between the walls.
    const float MinVerticalFraction = 0.15f;

    // The first launch leaves at this angle off vertical, to the right — a
    // straight-up serve is a dull one, and a random tilt gives the player
    // nothing to read off the screen before they press SPACE. The ball waits
    // at the point on the paddle that angle belongs to, so where it sits is
    // the promise of where it will go.
    const float LaunchAngle = 15f;

    // How far above the paddle the waiting ball rests.
    const float RestHeight = 0.5f;

    Rigidbody2D body;
    Transform followTarget;
    Vector3 followOffset = new Vector3(0f, RestHeight, 0f);

    // The paddle's arcade bounce maps a hit's distance from the middle, as a
    // fraction of the paddle's half-width, straight onto the tangent of the
    // angle the ball leaves at (see OnCollisionEnter2D). The launch reads the
    // same tangent, which is what ties the two together.
    static float LaunchTangent => Mathf.Tan(LaunchAngle * Mathf.Deg2Rad);

    public bool IsAttached => followTarget != null;

    void Awake()
    {
        body = GetComponent<Rigidbody2D>();
    }

    public void AttachTo(Transform paddle)
    {
        followTarget = paddle;
        body.bodyType = RigidbodyType2D.Kinematic;
        body.linearVelocity = Vector2.zero;
        followOffset = RestOffset(paddle);
        transform.position = paddle.position + followOffset;
    }

    // Right of the paddle's middle by the fraction of its half-width that the
    // bounce would turn into LaunchAngle — the spot the launch angle comes off.
    // The paddle is measured rather than assumed, so the menu's paddle and the
    // playfield's both serve from their own middle.
    static Vector3 RestOffset(Transform paddle)
    {
        var collider = paddle.GetComponent<Collider2D>();
        float halfWidth = collider != null ? collider.bounds.extents.x : 0f;
        if (halfWidth <= 0f) halfWidth = 1f;
        return new Vector3(LaunchTangent * halfWidth, RestHeight, 0f);
    }

    public void Launch()
    {
        if (!IsAttached) return;
        followTarget = null;
        body.bodyType = RigidbodyType2D.Dynamic;
        body.linearVelocity = new Vector2(LaunchTangent, 1f).normalized * speed;
    }

    void Update()
    {
        if (IsAttached) transform.position = followTarget.position + followOffset;
    }

    void FixedUpdate()
    {
        if (IsAttached) return;

        var velocity = body.linearVelocity;
        if (velocity.sqrMagnitude < 0.01f) return;

        float minVertical = speed * MinVerticalFraction;
        if (Mathf.Abs(velocity.y) < minVertical)
            velocity.y = (velocity.y < 0f ? -1f : 1f) * minVertical;

        body.linearVelocity = velocity.normalized * speed;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsAttached) return;

        var paddle = collision.collider.GetComponent<Paddle>();
        if (paddle == null) return;

        // Only hits on the paddle's flat top get the arcade angle override.
        // On the rounded corners the contact normal tilts away from straight
        // up, and the engine's reflection off the curve's normal stands.
        if (collision.contactCount == 0 || collision.GetContact(0).normal.y < 0.995f) return;

        float offset = (transform.position.x - collision.transform.position.x)
            / collision.collider.bounds.extents.x;
        body.linearVelocity = new Vector2(offset, 1f).normalized * speed;
    }
}
