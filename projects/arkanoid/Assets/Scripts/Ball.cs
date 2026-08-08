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

    Rigidbody2D body;
    Transform followTarget;
    readonly Vector3 followOffset = new Vector3(0f, 0.5f, 0f);

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
        transform.position = paddle.position + followOffset;
    }

    public void Launch()
    {
        if (!IsAttached) return;
        followTarget = null;
        body.bodyType = RigidbodyType2D.Dynamic;
        float tilt = Random.Range(-0.5f, 0.5f);
        body.linearVelocity = new Vector2(tilt, 1f).normalized * speed;
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

        float offset = (transform.position.x - collision.transform.position.x)
            / collision.collider.bounds.extents.x;
        body.linearVelocity = new Vector2(offset, 1f).normalized * speed;
    }
}
