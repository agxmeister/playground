using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Ball : MonoBehaviour
{
    [SerializeField] float speed = 8f;
    [SerializeField] int damage = 1;

    public int Damage => damage;

    // The shallowest the ball is ever allowed to travel, off the horizontal.
    // It used to be a hair under 9°, which stopped the ball going *exactly*
    // sideways but not much else: the field is nearly twice as wide as it is
    // tall, so a rally that flat crossed the frame in three seconds while
    // climbing barely a quarter of it, and half a dozen crossings of nothing
    // but left-right read as a ball that was stuck rather than one that was
    // slowly getting somewhere. At 20° a crossing gains most of the frame's
    // height, so the flattest legal rally still visibly goes somewhere. The
    // paddle's own bounce never comes off flatter than 45°, so this only ever
    // catches a ball that the walls and bricks have flattened.
    const float MinAngle = 20f;

    // A bounce off a vertical face — a side border, or the side of a brick —
    // leaves the ball's vertical component exactly as it was, so a run of them
    // with nothing else struck in between is a rally that is not going to end
    // itself. This many in a row and the ball is aimed out of it. It is a
    // backstop rather than the main defence: at MinAngle the ball crosses the
    // field at most twice before meeting the top or the paddle, so reaching
    // this count means something is flattening the ball that we did not
    // foresee.
    const int StallBounces = 3;

    // How far off the horizontal a stalled rally is aimed to break it. Steep
    // enough to read as the ball getting out rather than as one more bounce.
    const float StallEscapeAngle = 35f;

    // How square-on a contact has to be to count as a vertical face.
    const float VerticalFaceDot = 0.9f;

    // How much of a vertical component a contact normal needs before it is
    // taken as saying which way is out of the field's floor or ceiling. A side
    // wall's normal is horizontal to within rounding, and that rounding must
    // not be read as a direction.
    const float EscapeDot = 0.1f;

    // The first launch leaves at this angle off vertical, to the right — a
    // straight-up serve is a dull one, and a random tilt gives the player
    // nothing to read off the screen before they press SPACE. The ball waits
    // at the point on the paddle that angle belongs to, so where it sits is
    // the promise of where it will go.
    const float LaunchAngle = 15f;

    // How much daylight the waiting ball keeps between itself and the paddle it
    // sits on. The rest of the height is measured off the two of them, because
    // the menu's paddle and ball are a scaled-down copy of the round's and a
    // fixed height would leave the smaller pair visibly floating apart.
    const float RestClearance = 0.1f;

    Rigidbody2D body;
    Transform followTarget;
    Vector3 followOffset;

    // Which way a ball that has gone exactly flat should be sent. Taken from
    // the last surface it touched — pointing away from it — rather than being
    // fixed: a ball resting along the top border that is always nudged upwards
    // is pushed straight back into the border it is lying on, every step, and
    // skims along it for ever. That is the endless horizontal rally.
    float escapeY = 1f;

    // Consecutive bounces off vertical faces, counted for StallBounces.
    int flatBounces;

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
        // A fresh serve owes nothing to the rally that ended.
        flatBounces = 0;
        escapeY = 1f;
        followOffset = RestOffset(paddle);
        transform.position = paddle.position + followOffset;
    }

    // Right of the paddle's middle by the fraction of its half-width that the
    // bounce would turn into LaunchAngle — the spot the launch angle comes off.
    // The paddle is measured rather than assumed, so the menu's paddle and the
    // playfield's both serve from their own middle, and so is the height, so
    // both balls rest the same daylight above the paddle they sit on however
    // large either of them is.
    Vector3 RestOffset(Transform paddle)
    {
        // Across, the paddle is measured by its *collider*, because that is what
        // the bounce measures too (see OnCollisionEnter2D) and the two have to
        // agree for the resting spot to be the promise of the launch angle.
        var collider = paddle.GetComponent<Collider2D>();
        float halfWidth = collider != null ? collider.bounds.extents.x : 0f;
        if (halfWidth <= 0f) halfWidth = 1f;

        // Upward it is measured by what is *drawn* instead: a Collider2D's
        // bounds leave out its edgeRadius, and the paddle carries most of its
        // height in exactly that — a box shrunk by the corner radius on every
        // side with edgeRadius filling it back out — so a collider-measured
        // height would sit the ball a corner radius inside the paddle it is
        // supposed to be resting on.
        return new Vector3(LaunchTangent * halfWidth,
            HalfHeightOf(paddle) + HalfHeightOf(transform) + RestClearance, 0f);
    }

    static float HalfHeightOf(Transform target)
    {
        var renderer = target.GetComponent<Renderer>();
        return renderer != null ? renderer.bounds.extents.y : 0f;
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

        if (Mathf.Abs(velocity.y) < speed * Mathf.Sin(MinAngle * Mathf.Deg2Rad))
            velocity = Steepen(velocity, MinAngle);

        body.linearVelocity = velocity.normalized * speed;
    }

    // The same heading, re-aimed to exactly `angle` off the horizontal: the
    // ball carries on the way it was already going across the field, only less
    // flatly. Which way it goes up or down it is `escapeY` rather than the sign
    // of what is left of its own vertical component — a ball flat enough to
    // need this has just come off a surface, and the reflection can leave that
    // sign as a hair either side of zero, which is exactly the case that pins a
    // ball to the border it is lying against. Away from the last surface is
    // always right, and while the ball is travelling properly the two agree
    // anyway: nothing but a bounce can flatten it, and every bounce off a
    // horizontal face sets escapeY.
    Vector2 Steepen(Vector2 velocity, float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        float across = velocity.x < 0f ? -1f : 1f;
        return new Vector2(across * Mathf.Cos(radians), escapeY * Mathf.Sin(radians)) * speed;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsAttached || collision.contactCount == 0) return;

        // The normal points out of whatever was hit and into the ball, so it is
        // both what says which kind of face this was and which way is away
        // from it.
        var normal = collision.GetContact(0).normal;
        if (Mathf.Abs(normal.y) > EscapeDot) escapeY = Mathf.Sign(normal.y);

        // A vertical face returns the ball with its vertical component
        // untouched, so it makes no progress up or down the field. Enough of
        // them in a row is a rally going nowhere; anything else the ball
        // strikes moves it along, and starts the count again.
        if (Mathf.Abs(normal.x) > VerticalFaceDot)
        {
            if (++flatBounces < StallBounces) return;
            flatBounces = 0;
            body.linearVelocity = Steepen(body.linearVelocity, StallEscapeAngle);
            return;
        }

        flatBounces = 0;

        var paddle = collision.collider.GetComponent<Paddle>();
        if (paddle == null) return;

        // Only hits on the paddle's flat top get the arcade angle override.
        // On the rounded corners the contact normal tilts away from straight
        // up, and the engine's reflection off the curve's normal stands.
        if (normal.y < 0.995f) return;

        float offset = (transform.position.x - collision.transform.position.x)
            / collision.collider.bounds.extents.x;
        body.linearVelocity = new Vector2(offset, 1f).normalized * speed;
    }
}
