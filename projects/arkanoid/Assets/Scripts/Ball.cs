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

    // Below this fraction of its own speed the ball counts as stopped rather
    // than as travelling slowly. Every step puts it back to exactly `speed`, so
    // there is no honest way to be down here: it is a ball something has pinned.
    const float PinnedSpeed = 0.05f;

    // How square-on a contact has to be to count as a vertical face.
    const float VerticalFaceDot = 0.9f;

    // How far a paddle travelling at full tilt drags the bounce across, in the
    // same units the hit's own offset is measured in — a fraction of the
    // paddle's half-width, which is the tangent of the angle the ball leaves
    // at. So a dead-centre hit off a paddle sliding right leaves at about 19°
    // off vertical where a still paddle would have sent it straight up: the
    // player can aim a ball that arrived in the wrong place by shoving the
    // paddle under it, which is the whole point.
    //
    // It stops short of the ±1 the offset itself spans, so a twist bends a
    // bounce rather than replacing it: where the ball struck is still the
    // louder half of where it goes.
    const float TwistReach = 0.35f;

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

    // What a twisted hit leaves behind. The paddle's drag does two things to
    // the ball, and only one of them is over by the time it has left: the
    // bounce is aimed once (TwistReach), and the ball is *scuffed*, which it
    // carries with it. `spin` holds that scuff as a signed fraction of a full
    // one — the paddle's `Drift` at the moment of the hit — and it is what the
    // roll and the curve are both read off, so the two can never disagree.
    //
    // Where the ball landed on the paddle has no say in it: the scuff is the
    // paddle sliding under the ball, and it slides just as hard under a ball
    // caught on the tip as under one caught in the middle. Only the *aim* is
    // about where it struck.

    // How fast a fully spinning ball turns, in degrees a second, at the instant
    // of the hit. What matters is not this number but what it buys once the
    // decay below has bled it away — `RollSpeed / SpinDecay`, better than six
    // full turns — because a scuff that turns the ball a tenth of a turn and
    // stops is a twitch rather than a roll. Three and a third turns a second at
    // the peak is about as fast as four panels can come round while still
    // reading as panels going round rather than as a flicker. The peak is the ceiling on this
    // rather than the total: two and a half turns a second is about as fast as
    // four panels can come round before they stop reading as panels going
    // round, so a bigger spin is bought by lasting longer and not by turning
    // faster.
    const float RollSpeed = 1200f;

    // How hard a fully spinning ball's flight bends, in degrees a second. What
    // there is to spend is `CurveRate / SpinDecay`, a full 80° — a right angle
    // of bend, so a twisted ball's arc is its own line across the field rather
    // than a lean on it. In the first second after a full-tilt hit — while
    // there is most of a spin still on the ball — that is around 50° of its
    // line, which is an arc nobody has to be told about. `MinAngle` is what
    // stops it running away: the bend goes in ahead of that floor, so a ball
    // curved past 20° off the horizontal is steepened back like any other flat
    // ball. It is rarely all spent in one run anyway — the ball meets something
    // every few tenths of a second and the bounce sets it on a fresh heading
    // with whatever is left of the turn still on it, which is what keeps a long
    // spin interesting instead of circular.
    const float CurveRate = 65f;

    // How quickly a spin bleeds away, as a rate: this fraction of what is left
    // goes every second, so a full spin is half gone in `ln 2 / SpinDecay`,
    // about a second and a half, and most of the way gone in three.
    //
    // It bleeds rather than counting down to nothing, which is the opposite of
    // how this started. A linear countdown was chosen so that a spin would
    // *end* — an exponential tail leaves the ball faintly turning for ever —
    // and then the ball with no turn on it at all turned out to be the thing
    // that looked wrong: a sphere sitting perfectly still in flight reads as a
    // sprite, not a ball. So the tail is the point now, and `BounceNick` below
    // keeps topping it up. Nothing is ever quite done turning.
    const float SpinDecay = 0.5f;

    // What every contact does to the ball's turn, whatever it hit: takes a
    // little off (`BounceScrub`) and puts a little random on (± `BounceNick`).
    // This is not the twist — it is the reason a ball in play is never perfectly
    // still, and it is deliberately far too small to aim with: a nick carries a
    // fifth of a full spin's bend against the paddle's whole one, so the drag
    // is worth five bounces and stays the only thing that moves a ball's line.
    // The turn it puts on is plainly visible even so, because the roll is the
    // generous half of the same number: a nick alone is worth up to
    // `BounceNick × RollSpeed` = 170° a second.
    //
    // Random, because a bounce's real spin depends on where on the ball the
    // face caught it, which 2D physics does not model and a circle collider
    // could not tell us anyway. The scrub is mild on purpose: a bounce that
    // halved the turn would have the paddle's twist spent within a couple of
    // bricks, and the twist is supposed to be the ball's for seconds.
    const float BounceScrub = 0.85f;
    const float BounceNick = 0.14f;

    // The least of a nick that still counts as one, as a fraction of it. A
    // nick drawn evenly from ±BounceNick is sometimes almost nothing, and a
    // bounce that leaves the ball not turning is the thing this is here to
    // stop, so the magnitude is drawn from the top half of the range and only
    // the direction is a coin toss.
    const float NickFloor = 0.5f;

    // The turn the ball leaves the paddle with. Fixed, and to the right, for
    // exactly the reason `LaunchAngle` is: a serve is the one moment the player
    // is owed a promise rather than a surprise, and the ball sitting on the
    // paddle is that promise — it waits right of the middle because that is
    // where the angle it will leave at comes from. Now it leaves *rolling* that
    // way too, which is the same fact told a third time: right of centre, off
    // to the right, turning to the right. A random serve spin gave the ball a
    // small unasked-for curve off the very first shot, in a direction the
    // player had no way to read.
    //
    // A whole nick's worth, so a served ball turns as visibly as a struck one.
    const float LaunchSpin = BounceNick;

    // The least turn a ball in play ever carries. Two things were still leaving
    // the ball all but static, which is the very thing this is here to stop: a
    // nick can land against the spin the ball already had and very nearly
    // cancel it, and a long flight with nothing struck bleeds most of the way
    // to nothing on its own. So the turn is held off zero from underneath and
    // only its direction is ever in doubt. It costs a permanent, tiny curve —
    // `MinSpin × CurveRate`, under 4° a second — which is a ball that never
    // flies quite straight, and that is nearer the truth than one that does.
    const float MinSpin = 0.06f;

    // What is left of a spin when the paddle hits the ball, before the drag is
    // added to it. This is the one contact with a speed of its own, and it is
    // worth an order more than a bounce's nick: a paddle at full tilt puts a
    // whole spin on where a brick puts a tenth of one, which is the difference
    // between a ball that is turning and a ball whose line across the field has
    // been bent.
    //
    // Halved and added to rather than replaced. Replacing it was the first
    // version, and a ball caught on a paddle standing still stopped dead
    // mid-roll, which reads as the mechanic being switched off rather than as a
    // ball being caught. Halved: a still paddle takes half the turn off, a
    // paddle dragging the same way tops it up to full, and a paddle dragging
    // against it takes the spin off and puts the other way on.
    const float ImpactScrub = 0.5f;

    // Which way the bend goes for a given scuff. A ball scuffed to the right
    // rolls clockwise and arcs clockwise — it rolls *into* its own turn, the
    // way a ball running round the inside of a bowl does, so the roll the
    // player sees and the curve they get are the same fact twice. That is the
    // reading rather than the aerodynamic one: true Magnus lift on this spin
    // bends the other way, which would have the ball undo the shove that the
    // player just aimed with. Flipping this sign is the whole of that change,
    // if the honest one is ever wanted.
    const float SpinHandedness = -1f;

    // How much daylight the waiting ball keeps between itself and the paddle it
    // sits on. The rest of the height is measured off the two of them, because
    // the menu's paddle and ball are a scaled-down copy of the round's and a
    // fixed height would leave the smaller pair visibly floating apart.
    const float RestClearance = 0.1f;

    // How quickly the ball comes back to its plane once whatever lifted it out
    // of it is out from under it. Nothing pushes it back down — it drops at its
    // own rate, and it can only start dropping once nothing is holding it up.
    const float PlaneReturn = 2.5f;

    // The push (see "The push is charged and timed" in CLAUDE.md, and
    // Paddle.Charge for the half of it the player winds up). A charge let go of
    // on the paddle at the moment the ball is caught is handed to the ball as
    // speed, and this is where the two halves are reconciled: the paddle knows
    // what it let go of and when, the ball knows when it was caught, and how
    // much of the one became the other is the gap between the two times.
    //
    // How far off the catch a release may land and still be worth anything.
    // Either side of it, and by the same amount: a release a shade *after* the
    // catch counts exactly as much as one the same shade before, because a
    // player timing a key to a bounce has no way of knowing which side of it
    // they landed on, and a window that only opened backwards would punish half
    // of every honest attempt. It is arithmetic rather than prescience — the
    // ball waits out the window before it gives up on a catch, and a release
    // that arrives inside it is applied to a ball that has already left.
    //
    // A third of a second is wide enough to be learnable and narrow enough that
    // landing it is a thing the player did rather than a thing that happened.
    // It is public because the paddle lapses its own release against the same
    // number, and two windows that could drift apart would be a bug waiting.
    public const float PunchWindow = 0.33f;

    // What a full charge, perfectly timed, is worth: three times the speed the
    // ball has ever had. The whole charge and none of the window's forgiveness
    // is the only way to get all of it — anything less arrives somewhere
    // between 1 and 3, which is the point of the mechanic.
    const float PunchTopSpeed = 3f;

    // How quickly the extra speed bleeds off, as a rate: this fraction of
    // what is left goes every second, so a full push is half spent in `ln 2 /
    // PunchDecay`, about a second, and back to the ball's own speed inside
    // seven. It bleeds rather than counting down for the reason the spin does —
    // a speed that ends on a particular frame reads as a switch being thrown —
    // and the loud part of it is over quickly on purpose: a pushed ball is a
    // shot, and a shot that stayed fast for a rally would make the paddle's own
    // speed the thing that felt wrong.
    const float PunchDecay = 0.7f;

    // Below this much borrowed speed there is nothing left worth carrying, and
    // the ball is put back on exactly its own speed. An exponential bleed never
    // quite arrives, and a ball permanently a hair fast would be a lie told in
    // every number downstream of `speed`.
    const float PunchFloor = 0.02f;

    Rigidbody2D body;
    Renderer sphere;
    Transform followTarget;
    Vector3 followOffset;

    // The plane the ball plays in, and how far in front of it — towards the
    // camera — something solid has lifted it. 2D physics ignores Z, so this is
    // the one direction the ball can be moved in without touching the rally:
    // the menu raises a screen into the playing plane under a ball in flight,
    // and rather than breaking under it or shoving it sideways, the screen
    // carries it up on its face for a moment. The ball knows nothing of that —
    // only that something solid is at a certain depth and it belongs in front
    // of it.
    float planeZ;
    float planeOffset;
    // What has been asked for this frame, taken as the largest of them: nothing
    // resists being lifted, so the nearest face wins.
    float pushed;
    // Whether the offset was applied last frame, so a ball that has never been
    // lifted — every ball in a round — is never written to at all.
    bool lifted;

    // Which way a ball that has gone exactly flat should be sent. Taken from
    // the last surface it touched — pointing away from it — rather than being
    // fixed: a ball resting along the top border that is always nudged upwards
    // is pushed straight back into the border it is lying on, every step, and
    // skims along it for ever. That is the endless horizontal rally.
    float escapeY = 1f;

    // Consecutive bounces off vertical faces, counted for StallBounces.
    int flatBounces;

    // What the paddle's last drag scuffed into the ball, ±1 down to nothing.
    float spin;

    // The borrowed speed a push put on the ball, as a fraction of its own on
    // top of it: 0 is the ball the player already knows and PunchTopSpeed − 1 is
    // a perfectly timed full charge. Everything about the ball's speed is read
    // through `Speed` rather than `speed` because of it.
    float punch;

    // The paddle the ball was last caught on and when, kept only for as long as
    // a release could still turn up for it. This is the ball's half of the
    // handshake: it is set by the catch and spent by whichever comes first, a
    // release or the window running out.
    Paddle puncher;
    float caughtAt;

    // The paddle's arcade bounce maps a hit's distance from the middle, as a
    // fraction of the paddle's half-width, straight onto the tangent of the
    // angle the ball leaves at (see OnCollisionEnter2D). The launch reads the
    // same tangent, which is what ties the two together.
    static float LaunchTangent => Mathf.Tan(LaunchAngle * Mathf.Deg2Rad);

    public bool IsAttached => followTarget != null;

    // How fast the ball is actually travelling: its own speed plus whatever a
    // push lent it. Every step renormalizes to this rather than to `speed`,
    // which is what makes the push a single number the whole of the rally is
    // read through — the angle floor, the pinned-ball test and the paddle's
    // arcade bounce all get the boosted speed without knowing there is one.
    float Speed => speed * (1f + punch);

    // How big the ball is drawn, which is the same all round: what tells
    // whatever is rising under it whether it is standing over it, and how far
    // in front of a face it has to sit to be resting on it rather than in it.
    public float Radius => sphere != null ? sphere.bounds.extents.x : 0f;

    // Whether the ball is in the plane it plays in, rather than out in front of
    // it on top of something that lifted it.
    public bool OnPlane => planeOffset <= 0f;

    void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        sphere = GetComponent<Renderer>();
        planeZ = transform.position.z;
    }

    // Something solid stands with its near face at this depth, under the ball.
    // The ball rides in front of it: its own radius clear, so it rests on the
    // face rather than in it. Asked for every frame it holds, since a lift
    // lasts exactly as long as something is under the ball.
    public void PushInFrontOf(float faceZ)
    {
        float offset = planeZ - (faceZ - Radius);
        if (offset > pushed) pushed = offset;
    }

    public void AttachTo(Transform paddle)
    {
        followTarget = paddle;
        body.bodyType = RigidbodyType2D.Kinematic;
        body.linearVelocity = Vector2.zero;
        // A fresh serve owes nothing to the rally that ended — including any
        // depth it was left at when a menu screen changed under it. The plane
        // is the paddle's: a ball is only ever served off one.
        flatBounces = 0;
        escapeY = 1f;
        // Including whatever it was still turning at: a kinematic body keeps the
        // angular velocity it was left with, and FixedUpdate is not looking at
        // an attached ball to take it away again, so a ball put back on the
        // paddle mid-spin would sit there spinning.
        spin = 0f;
        body.angularVelocity = 0f;
        // Including any speed a push lent the ball that was lost, and any
        // release still waiting to be paid to it: a serve is the ball's own
        // speed, always.
        punch = 0f;
        puncher = null;
        planeZ = paddle.position.z;
        planeOffset = 0f;
        pushed = 0f;
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
        punch = 0f;
        body.linearVelocity = new Vector2(LaunchTangent, 1f).normalized * speed;
        // Rolled off to the right, the way it is aimed. A ball is rolled off a
        // paddle rather than let go of in mid-air, so it has no business
        // leaving perfectly still — and it was the last ball in the game that
        // did.
        spin = LaunchSpin;
    }

    void Update()
    {
        if (IsAttached)
        {
            transform.position = followTarget.position + followOffset;
            return;
        }

        TakePush();
    }

    // The push, collected rather than delivered: the ball asks the paddle it
    // was caught on whether the charge has been let go of yet, every frame,
    // until either it has or the window is past. That is the way round it has to
    // be, because the release can land *after* the catch and nothing at the
    // moment of the catch can know whether it is about to.
    //
    // Which means a well-timed push arrives a frame or two into the ball's
    // flight rather than at the bounce itself. Nothing downstream can tell:
    // FixedUpdate renormalizes the ball to `Speed` every step, so raising it
    // mid-flight is exactly the same act as raising it at the contact.
    void TakePush()
    {
        if (puncher == null) return;

        // The catch stays live for the whole window rather than being spent on
        // the first release that turns up, because a release can land either
        // side of it and the *nearer* one is the one the player meant. A charge
        // let go of just too early is spent — it bursts, and the paddle starts
        // winding a fresh one — and the player who then lets that one go on the
        // bounce has timed the second one well. Taking the first and closing the
        // books would hand the ball the worse of the two, measured: a release
        // 0.30s early is worth 0.09 of a charge where the re-wound one on the
        // bounce is worth 0.38 of one. So every release inside the window is
        // read, and the best of them stands (see the clamp below).
        if (Time.time - caughtAt > PunchWindow)
        {
            puncher = null;
            return;
        }

        float charge = puncher.ReleasedCharge;
        if (charge <= 0f) return;

        // How far the release landed from the catch, either side of it, as a
        // fraction of the window. A dead-on release hands the charge over
        // whole; one at the very edge of the window hands over nothing, and the
        // charge is spent all the same.
        float miss = Mathf.Abs(puncher.ReleasedAt - caughtAt);
        puncher.SpendRelease();

        float taken = charge * Mathf.Clamp01(1f - miss / PunchWindow);
        // The greater of the two rather than the sum, which does two jobs. Two
        // pushes in a rally must not compound into a ball nothing can be done
        // about — PunchTopSpeed is meant to be the fastest the ball is ever
        // seen, so a fresh push on a ball still carrying one is a top-up and
        // not a stack. And within a single catch it is what lets the better of
        // two releases stand: the first is applied at once, so the surge lands
        // on the bounce rather than a beat after it, and a nearer release
        // arriving later in the window can only raise it.
        punch = Mathf.Max(punch, taken * (PunchTopSpeed - 1f));
    }

    // After everything that moves the ball across the field — the rally is 2D
    // and owns X and Y; this owns Z, and the two never meet. Written only for a
    // ball something has actually lifted, so a round's ball, which nothing ever
    // does, keeps the depth it was spawned at untouched.
    void LateUpdate()
    {
        planeOffset = pushed > planeOffset
            ? pushed
            : Mathf.Max(pushed, planeOffset - PlaneReturn * Time.deltaTime);
        pushed = 0f;
        if (planeOffset <= 0f && !lifted) return;
        lifted = planeOffset > 0f;
        var position = transform.position;
        transform.position = new Vector3(position.x, position.y, planeZ - planeOffset);
    }

    void FixedUpdate()
    {
        if (IsAttached) return;

        var velocity = body.linearVelocity;

        // A ball that has been stopped dead has to be sent off again, because
        // nothing else will ever move it: the heading it would be given back is
        // its own, and it hasn't got one. Two faces meeting almost head on can
        // do it — the notch where an option arrow's tail meets its body is
        // where we found it — and the way out is the way off any surface, away
        // from the last one touched. This used to be a bail-out, which is what
        // made a pinned ball a permanent one.
        // The borrowed speed bleeds away first, so the step the push is spent
        // in is already flying at what is left of it rather than at what it
        // was — the same order the spin is spent in below.
        if (punch > 0f)
        {
            punch *= Mathf.Exp(-PunchDecay * Time.fixedDeltaTime);
            if (punch < PunchFloor) punch = 0f;
        }

        if (velocity.sqrMagnitude < Speed * PinnedSpeed * (Speed * PinnedSpeed))
        {
            body.linearVelocity = Steepen(velocity, StallEscapeAngle);
            return;
        }

        // The scuff spends itself: it bends the heading a little every step and
        // turns the ball while it lasts. Both are written every step rather
        // than only while there is spin left, so the one contact that could
        // set the ball turning behind our back — friction against a face,
        // which a circle collider is otherwise indifferent to — is overwritten
        // before it is ever seen. The bend goes in ahead of MinAngle below, so
        // the floor on the ball's angle stays the last word: a curve that has
        // flattened the ball too far is steepened back like any other.
        velocity = Rotate(velocity, SpinHandedness * spin * CurveRate * Time.fixedDeltaTime);
        spin *= Mathf.Exp(-SpinDecay * Time.fixedDeltaTime);
        if (Mathf.Abs(spin) < MinSpin) spin = spin < 0f ? -MinSpin : MinSpin;
        body.angularVelocity = SpinHandedness * spin * RollSpeed;

        if (Mathf.Abs(velocity.y) < Speed * Mathf.Sin(MinAngle * Mathf.Deg2Rad))
            velocity = Steepen(velocity, MinAngle);

        body.linearVelocity = velocity.normalized * Speed;
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
    // A bounce's worth of turn: which way is a coin toss, how much comes from
    // the top of the range so that it is always worth something (see
    // BounceNick). Only contacts draw one — the serve takes the fixed
    // `LaunchSpin` instead, because a serve is aimed and a bounce is not.
    static float Nick()
    {
        float nick = Random.Range(NickFloor, 1f) * BounceNick;
        return Random.value < 0.5f ? -nick : nick;
    }

    // The same speed, turned by `degrees`. Counter-clockwise, as every angle in
    // 2D physics is, so the sign the roll is given is the sign the bend gets.
    static Vector2 Rotate(Vector2 velocity, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
        return new Vector2(velocity.x * cos - velocity.y * sin,
            velocity.x * sin + velocity.y * cos);
    }

    Vector2 Steepen(Vector2 velocity, float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        float across = velocity.x < 0f ? -1f : 1f;
        return new Vector2(across * Mathf.Cos(radians), escapeY * Mathf.Sin(radians)) * Speed;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsAttached || collision.contactCount == 0) return;

        // The normal points out of whatever was hit and into the ball, so it is
        // both what says which kind of face this was and which way is away
        // from it.
        var normal = collision.GetContact(0).normal;
        if (Mathf.Abs(normal.y) > EscapeDot) escapeY = Mathf.Sign(normal.y);

        // Whatever was hit and wherever it was hit, the contact leaves the ball
        // turning a little differently than it arrived. Everything below can
        // return early — a vertical face does, a brick does — so this is taken
        // first, for every contact there is.
        spin = Mathf.Clamp(spin * BounceScrub + Nick(), -1f, 1f);

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

        // A catch is a catch: the moment is recorded before the corner hits
        // below are handed back to the engine, because a push is about the
        // paddle meeting the ball and not about where on the paddle it met it.
        // Any release still owed to an earlier catch is dropped — the ball can
        // only be pushed off the bounce it is on.
        puncher = paddle;
        caughtAt = Time.time;

        // Only hits on the paddle's flat top get the arcade angle override.
        // On the rounded corners the contact normal tilts away from straight
        // up, and the engine's reflection off the curve's normal stands.
        if (normal.y < 0.995f) return;

        // Where on the paddle the ball struck, and which way the paddle was
        // going as it did. The two are added, then clamped back into the range
        // the offset alone spans: everything downstream is written against a
        // paddle bounce that never comes off flatter than 45° — MinAngle leans
        // on it by name — and a twist is meant to move a bounce inside that
        // envelope, not out through the side of it. Which also gives the clamp
        // its feel: a ball caught out at the very end of the paddle cannot be
        // twisted any wider, only hauled back towards the middle.
        float offset = (transform.position.x - collision.transform.position.x)
            / collision.collider.bounds.extents.x;
        offset = Mathf.Clamp(offset + paddle.Drift * TwistReach, -1f, 1f);
        body.linearVelocity = new Vector2(offset, 1f).normalized * Speed;

        // The other half of the twist, and the half the player can see: the
        // drag is scuffed into the ball rather than only into its heading, and
        // it is spent over the seconds that follow as roll and as bend. It is
        // added to what the ball arrived with rather than put in its place, so
        // a rally can build a spin up over several hits and a catch is never
        // the moment the turning stops.
        spin = Mathf.Clamp(spin * ImpactScrub + paddle.Drift, -1f, 1f);
    }
}
