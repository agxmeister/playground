using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Ball : MonoBehaviour
{
    [SerializeField] float speed = 8f;
    [SerializeField] int damage = 1;

    // What one hit is worth, which is what the ball is *carrying* rather than a
    // flat number: the authored damage times how many of its own speeds it is
    // travelling at. A ball at its own speed does the 1 it always did; a ball
    // at two and a half times it does two and a half, and takes a slab of
    // hardness 2 out in a single hit. So a push is not only a faster ball but a
    // heavier one, which is what makes a charge worth aiming at a wall rather
    // than only at a gap.
    public float Damage => damage * SpeedMultiple;

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
    // How long before the catch a release may land and still be worth anything.
    // Before it only: the window opens backwards from the bounce and stops
    // there. A charge let go of while the ball is still coming is a shot aimed
    // early, and how early is what decides how much of it lands; a charge let go
    // of once the ball has gone is a charge spent on nothing, however narrowly
    // it missed.
    //
    // It opened both ways for a while, by the same amount either side, on the
    // reasoning that a player timing a key to a bounce cannot know which side of
    // it they landed on — so punishing the late half would punish half of every
    // honest attempt. What that bought instead was a push applied to a ball that
    // had already left, which is the one thing this mechanic cannot look like.
    // Aiming ahead of the ball is the skill now.
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

    // How fast the ball has to be going, as a multiple of its own speed, before
    // it starts burning (see the wake below). Half again over its own speed is
    // the point at which a rally reads as *fast* rather than as the ball being
    // hit slightly harder: a push of a fifth is a fifth, and a trail on one
    // would be a trail behind almost every catch. It is also comfortably clear
    // of `PunchFloor`, so the last flicker of a bleeding push cannot flash a
    // frame of flame on its way out.
    const float WakeSpeed = 1.5f;

    // The two sizes the wake is measured in, both as fractions of the ball's own
    // diameter. `WakeBore` is the nozzle: how wide the flame is across, and how
    // big the sparks in it are. `WakeReach` is the unit its ribbon's length is
    // counted in.
    //
    // They are two numbers because the wake wants to be short *and* wide, and
    // for a while it was measured in one, which only offered both or neither. At
    // the ball's full diameter for both it read as a beam the ball was riding on
    // the end of, and a third of it for both — the fix for that — gave the right
    // length on a flame too thin to be one. So: a good deal narrower than the
    // ball, because a flame as wide as its nozzle is not a flame, but plainly a
    // jet rather than a thread.
    const float WakeBore = 0.6f;
    const float WakeReach = 1f / 3f;

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

    // The heading the ball took into the physics step, kept because a punch
    // through a block has to *undo* a bounce the engine has already applied
    // (see Punch) and the way in is the one thing that is gone by then.
    // FixedUpdate runs before the step it belongs to, so what it leaves here is
    // exactly what the ball was doing when it met whatever it met.
    Vector2 wayIn;

    // What the paddle's last drag scuffed into the ball, ±1 down to nothing.
    float spin;

    // The borrowed speed a push put on the ball, as a fraction of its own on
    // top of it: 0 is the ball the player already knows and PunchTopSpeed − 1 is
    // a perfectly timed full charge. Everything about the ball's speed is read
    // through `Speed` rather than `speed` because of it.
    float punch;

    // Where the wake was laid from last frame — the back of the ball as it was —
    // so each frame's plume is the ground actually covered rather than a puff
    // dropped at a point. `burning` is what says there is a previous frame worth
    // sweeping from: without it the plume that opened a wake would be laid from
    // wherever the ball was the last time it happened to be fast, which on a
    // fresh push is halfway across the field.
    Vector3 wakeFrom;
    bool burning;
    float emberDebt;

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

    // The same thing said as a multiple, which is the unit both halves of a
    // break are written in: a hit is worth this many of the ball's own damage,
    // and breaking a block costs this many back (see Punch). 1 is the ball the
    // player already knows and nothing can take it below that.
    public float SpeedMultiple => 1f + punch;

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
        // And the wake it was carrying: a served ball is at its own speed, so
        // there is nothing to burn, and a stale sweep-from would lay the first
        // plume of the next push from wherever the last one ended.
        burning = false;
        emberDebt = 0f;
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
        Wake();
    }

    // The trail behind a pushed ball, thrown off whenever it is travelling more
    // than `WakeSpeed` times the one speed it has ever had. This is the ball's
    // half of what the exhaust does for the paddle: a push is otherwise a fact
    // about the rally that only the rally can feel — a ball at three times its
    // speed looks exactly like the ball, only sooner somewhere else — and since
    // the push bleeds away over the seconds after the catch (`PunchDecay`), the
    // trail is also the only reading of how much of the shot is left.
    //
    // It burns blue where the paddle's rocket burns orange, which is the whole
    // of what tells the two trails apart, and it is the same blue the charge was
    // wound up in under the paddle (`PowerWave`): the gauge is the push going
    // in and this is the push coming out.
    void Wake()
    {
        // Read off `Speed` rather than off `punch` directly, so the threshold is
        // written in the same units the player would describe it in — "half
        // again as fast as normal" — and stays true if the ball's own speed is
        // ever retuned.
        float over = Speed / speed - WakeSpeed;
        if (over <= 0f)
        {
            burning = false;
            emberDebt = 0f;
            return;
        }

        if (sphere == null) return;
        var material = sphere.sharedMaterial;
        if (material == null) return;

        var heading = body.linearVelocity;
        if (heading.sqrMagnitude <= 0f) return;

        // Behind the ball is the way it is *not* going, which unlike the
        // paddle's exhaust is not an axis: the ball flies at whatever angle it
        // was last given, and curves while it does (`CurveRate`), so the wake
        // has to be laid along the heading of the moment. It bends with the
        // flight for free, since each frame's plume keeps the direction it was
        // born with.
        var back = -new Vector3(heading.x, heading.y, 0f).normalized;
        // Everything the wake is sized by comes off the ball's own drawn body,
        // as the paddle's comes off its, so the menu's scaled-down ball trails a
        // scaled-down wake. Where the paddle hands its height in for both of
        // them, the ball's two are its diameter taken in by two different
        // fractions — a jet wider than it is measured long.
        float diameter = Radius * 2f;
        float bore = diameter * WakeBore;
        float reach = diameter * WakeReach;
        var nozzle = transform.position + back * Radius;

        // The frame a wake opens on has no ground behind it to sweep, so it
        // starts as a plume of the minimum length at the nozzle and the sweeping
        // begins next frame.
        if (!burning) wakeFrom = nozzle;
        burning = true;

        // 0 for a ball just over the threshold and 1 for one at the very top of
        // what a perfectly timed full charge can buy it, which is what makes the
        // trail a reading of the push rather than a light that is merely on: it
        // lengthens, thickens and brightens with the speed, and thins back down
        // as the push bleeds away under it.
        float strength = Mathf.Clamp01(over / (PunchTopSpeed - WakeSpeed));

        JetTrail.Plume(wakeFrom, nozzle, back, bore, reach, Speed, strength,
            JetTrail.Plasma, material);
        wakeFrom = nozzle;

        // Embers on a cadence rather than one a frame, for the same reason the
        // plume is a swept path: how many sparks a fast ball throws is a fact
        // about the ball and not about how often the game is drawing it.
        emberDebt += JetTrail.EmberRate * strength * Time.deltaTime;
        while (emberDebt >= 1f)
        {
            emberDebt -= 1f;
            JetTrail.Ember(nozzle, back, bore, strength, JetTrail.Plasma, material);
        }
    }

    // The push, worked out from a release the paddle was already holding: the
    // window opens *before* the catch and nowhere else. A charge let go of a
    // moment before the ball arrives is a shot aimed early and lands whole; one
    // let go of after the ball has gone is a charge spent on nothing, because by
    // then there is nothing left to push. It used to open both ways, by the same
    // amount either side, on the reasoning that a player timing a key to a
    // bounce cannot know which side of it they landed on — and that made a late
    // release worth as much as an early one at the same distance, which is a
    // push arriving after its own bounce.
    //
    // It is still *collected* rather than read once at the contact, but only for
    // as long as the two clocks could disagree. The catch is timed in
    // FixedUpdate and the release in the paddle's own Update, in an order
    // nothing here fixes, so a release the player made on the bounce can be
    // stamped a frame the wrong side of it. `grace` below is exactly that slop
    // and nothing more: a dead-on release must not be the one timing the game
    // refuses.
    //
    // Which means a well-timed push can arrive a frame into the ball's flight
    // rather than at the bounce itself. Nothing downstream can tell:
    // FixedUpdate renormalizes the ball to `Speed` every step, so raising it
    // mid-flight is exactly the same act as raising it at the contact.
    void TakePush()
    {
        if (puncher == null) return;

        // How far apart the catch's clock and the release's clock can be while
        // still describing the same instant: one physics step and one frame.
        // Written as those two rather than as a number, because that is what it
        // is — the ordering slop, not a window the player can play inside.
        float grace = Time.fixedDeltaTime + Time.deltaTime;

        float charge = puncher.ReleasedCharge;
        if (charge <= 0f)
        {
            // Nothing let go of at the moment of the catch, and only the slop is
            // waited out: a charge released after that is released after the
            // ball, and this catch is done with.
            if (Time.time - caughtAt > grace) puncher = null;
            return;
        }

        // How long before the catch the release landed, as a fraction of the
        // window. A dead-on release hands the charge over whole; one at the very
        // edge of the window hands over nothing.
        float early = caughtAt - puncher.ReleasedAt;
        if (early < -grace)
        {
            // Let go of after the ball had already left. Nothing is taken and
            // nothing is spent here — the paddle lapses its own release, and
            // this charge may yet be the one the player means for the next
            // catch. The charge is gone from the gauge either way, which is the
            // point of a mistimed push: it bursts, and there is nothing to show
            // for it.
            puncher = null;
            return;
        }

        puncher.SpendRelease();
        // One release to a catch. There is no waiting for a nearer one now, the
        // way there was when the window opened both ways: every release still to
        // come is later than this one, and later is worth nothing.
        puncher = null;

        float taken = charge * Mathf.Clamp01(1f - Mathf.Max(early, 0f) / PunchWindow);
        // The greater rather than the sum: two pushes in a rally must not
        // compound into a ball nothing can be done about. PunchTopSpeed is meant
        // to be the fastest the ball is ever seen, so a fresh push on a ball
        // still carrying one is a top-up and not a stack.
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
            wayIn = body.linearVelocity;
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
        // Written last, on every path out of here, because this is the heading
        // the step that follows will be taken with — and the only record of it
        // once a contact in that step has turned the ball around.
        wayIn = body.linearVelocity;
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

    // The hit on a block, and whether the ball went *through* it. A push is
    // weight as well as speed (see Damage), so a ball carrying one can break a
    // block outright — and a block broken outright is no longer a surface to
    // come off.
    //
    // Breaking one costs the ball the block's hardness out of its speed, in the
    // multiples both are counted in: a ball at 2.5 through a slab of hardness 2
    // comes out the far side at 1 — its own speed, floored there, since nothing
    // may leave the ball slower than the ball. Whatever is left over decides
    // what happens next, and that is the whole rule:
    //
    // - Still over its own speed, and the ball carries straight on, through the
    //   hole it just made and into whatever is behind it. Which is what makes a
    //   full charge into a wall worth aiming: one shot can take a row.
    // - Back down to its own speed, and it comes off the block it broke like
    //   any other bounce. The push is spent, and the last thing it buys is the
    //   block.
    //
    // Carrying on means undoing a bounce, because the engine resolved the
    // contact before any of this ran: the ball is put back on `wayIn`, the
    // heading it met the block with. Its *speed* is not restored with it —
    // FixedUpdate renormalizes to `Speed` every step and `Speed` has just come
    // down — so what carries on is the line and not the pace.
    //
    // It takes the whole contact rather than the collider it is on because the
    // block wants to know *where* it was hit as well as how hard: a material
    // that chips puts the flake at that point (see Brick.Chip). Contact 0 is
    // the same one the bounce below is worked out from.
    bool Punch(Collision2D collision)
    {
        var brick = collision.collider.GetComponent<Brick>();
        if (brick == null) return false;

        // Read before the hit: a broken block is on its way out, and asking a
        // corpse how hard it was is asking for trouble.
        float hardness = brick.Hardness;
        if (!brick.TakeDamage(Damage, collision.GetContact(0).point)) return false;

        punch = Mathf.Max(0f, punch - hardness);
        if (punch < PunchFloor) punch = 0f;
        if (punch <= 0f) return false;

        // A block gone through is progress by any reading, so the stalled-rally
        // count starts again — it is there for a ball being batted between two
        // faces, and this ball is going somewhere.
        flatBounces = 0;
        body.linearVelocity = wayIn.normalized * Speed;
        return true;
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

        // A block is hit before any of the bounce is worked out, because
        // whether there is a bounce at all is one of the things the hit
        // decides.
        if (Punch(collision)) return;

        // And it breaks the wake, for the same reason and in the same place:
        // everything below can return early, and every contact there is turns
        // the ball. A bounce is a corner in the flight, and a flame does not go
        // round one — what the eye should see is the ribbon that was there
        // dissolving along the heading it was laid on while a new one rises out
        // of the ball on the new heading. Which is exactly what dropping
        // `burning` buys: the plume that would otherwise be swept from the
        // nozzle's place *before* the bounce to its place after it — one piece
        // lying across the corner, the join that made the whole trail look
        // hinged — is never laid, and the next frame starts a fresh sweep at
        // the ball. The pieces already in the air are not touched: they are
        // unparented and carry their own heading, so the old ribbon goes on
        // thinning away where it was put, which is the dissolving half of it.
        burning = false;

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
