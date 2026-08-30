using UnityEngine;
using UnityEngine.InputSystem;

public class Paddle : MonoBehaviour
{
    // Cruising speed: what the paddle does on the arrow keys alone, and the
    // unit everything below is measured in.
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
    // paddle has no twist to give. Nor is it simply the drive: a paddle coasting
    // a boost off with the key already let go of is still travelling, and still
    // has a twist in it.
    public float Drift { get; private set; }

    // The rocket boost. How much faster than cruising the paddle can be driven
    // with SPACE held, and how long the thrust takes to get there. Three and a
    // half times is fast enough that the far side of the field is suddenly
    // reachable, and the spool-up is deliberately most of a second: it is what
    // makes this a boost rather than a second speed setting, since the player
    // commits to the speed well before they have it. The two go together — a
    // higher top speed wants a longer climb, or the extra speed arrives before
    // anyone has decided to want it.
    const float BoostTopSpeed = 3.5f;
    const float BoostTime = 0.9f;

    // How long it takes to fall back to cruising once the thrust is off. Faster
    // than it built, since the paddle is being *stopped* rather than driven,
    // but not instant — coasting down is the other half of what makes the
    // boost feel like something with weight behind it.
    const float CoastTime = 0.3f;

    const float Thrust = (BoostTopSpeed - 1f) / BoostTime;
    const float Coast = (BoostTopSpeed - 1f) / CoastTime;

    // The push. Holding DOWN gathers a charge on the paddle, and letting go of
    // it spends the charge into the ball as speed — all of it if the release
    // lands on the moment of the catch, less the further off it lands (see
    // Ball.PunchWindow, which owns that arithmetic because the ball is the only
    // one of the two that knows when it was caught).
    //
    // The charge is kept as a fraction of a full one, 0 to 1, rather than as
    // the speed it is worth, because that is the number both of the things that
    // read it are written in: the ball turns it into a multiple of its own
    // speed, and the waves under the paddle are drawn from it directly.
    //
    // Four fifths of a second to fill is chosen against the same clock the
    // boost's spool-up is: long enough that the charge is committed to before
    // the ball arrives, short enough that a rally does not have to be given up
    // to gather one. And it fills while the paddle is doing everything else it
    // does — DOWN is a key of its own, so a charge can be gathered on the run,
    // under thrust, and the push and the rocket never argue over a key.
    const float ChargeTime = 0.8f;

    // How much of a full charge is on the paddle right now, 0 to 1. Runtime
    // only: nothing authors it, and there is nothing to charge without a
    // keyboard.
    public float Charge { get; private set; }

    // The last charge let go of and when, standing until a ball takes it or the
    // window it is good for runs out. This is the whole of the handshake: the
    // paddle says what it let go of and when, the ball says when it was caught,
    // and how much of the one became the other is a subtraction.
    public float ReleasedCharge { get; private set; }
    public float ReleasedAt { get; private set; }

    // The gauge under the paddle that shows the charge (see PowerWave). Made the
    // first time there is a charge to show and driven every frame after, since
    // it has a flying-apart to finish once the charge has gone.
    PowerWave gauge;

    // The paddle's travel as a signed multiple of its cruising speed, so 1 is
    // the one speed it has ever had and BoostTopSpeed is a fully spooled-up
    // rocket. Runtime-only: nothing authors it and it is dropped the moment
    // there is no keyboard.
    float drive;

    // Embers owed to the exhaust, carried between frames so the trail's
    // thickness is a rate rather than one piece per frame drawn — the trail
    // must not thin out because the game happened to draw faster.
    float emberDebt;

    // The paddle's own drawn body: what its travel is clamped against and what
    // the exhaust is measured and coloured off, since the menu's paddle is a
    // scaled-down copy of the round's and nothing about either may be assumed.
    Renderer body;

    void Awake()
    {
        homeX = transform.position.x;
        body = GetComponent<Renderer>();
    }

    // Both rooms' fields are the camera's frame now, so how far the paddle may
    // travel is only known once there is a window: the room that owns it hands
    // over half the frame's width and the paddle keeps its own body inside it.
    // Its width is measured rather than assumed, since the menu's paddle is a
    // scaled-down copy of the round's.
    public void FitTo(float roomHalfWidth)
    {
        if (body == null) body = GetComponent<Renderer>();
        float halfWidth = body != null ? body.bounds.extents.x : 0f;
        xLimit = Mathf.Max(0f, roomHalfWidth - halfWidth);
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            Drift = 0f;
            drive = 0f;
            Charge = 0f;
            return;
        }

        float direction = 0f;
        if (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed) direction -= 1f;
        if (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed) direction += 1f;

        float here = transform.position.x;
        float min = homeX - xLimit, max = homeX + xLimit;

        // The thrust is a key held rather than a key struck, so SPACE goes on
        // serving the ball as well: the press launches it and the hold that
        // follows drives the paddle out from under it.
        bool boosting = keyboard.spaceKey.isPressed;

        Wind(keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed);

        // A paddle already jammed against the edge with the arrow still
        // pushing it there has nowhere to spool up to. The thrust is held at
        // nothing rather than built into a crash it would deliver again on
        // every spool-up, which also means leaving the edge costs a fresh
        // spool-up: a rocket does not carry speed it never got to use.
        bool jammed = (direction < 0f && here <= min) || (direction > 0f && here >= max);
        if (jammed)
            drive = 0f;
        else if (boosting && direction != 0f)
        {
            // The thrust builds on top of cruising rather than up to it: a
            // paddle that answers the arrow instantly must not be *slower* for
            // having been asked to hurry, which is what spooling up from a
            // standstill would have made the opening of every boost. So the
            // drive is put at cruising speed the way the arrow
            // points first — from a standstill, or from travelling the other
            // way, which is exactly what the arrow alone would have given —
            // and the rocket takes it from there.
            if (drive * direction < 1f) drive = direction;
            drive = Mathf.MoveTowards(drive, direction * BoostTopSpeed, Thrust * Time.deltaTime);
        }
        else if (Mathf.Abs(drive) > 1f)
            // Nothing to thrust along — the thrust let go of, or held with no
            // arrow to point it — and still faster than cruising: coasting
            // down. The arrows still steer, they just cannot cancel the speed
            // already gathered.
            drive = Mathf.MoveTowards(drive, direction, Coast * Time.deltaTime);
        else
            // Inside cruising speed the paddle answers the keys exactly as it
            // always did, instantly and with nothing carried over.
            drive = direction;

        // Only X moves. The Z is kept rather than zeroed because the menu's
        // paddle lives on the menu screen's plane, well in front of the
        // playfield's.
        float wanted = here + drive * speed * Time.deltaTime;
        float x = Mathf.Clamp(wanted, min, max);

        if (!Mathf.Approximately(x, wanted))
        {
            // Run out of field. A paddle merely cruising into the edge stops
            // and that is all; one arriving under boost hits it hard enough to
            // ring the room, as hard as the speed it was carrying over cruising
            // — which is the whole reason the drive is measured in cruising
            // speeds. Either way the speed is gone: what stopped it is a wall.
            float force = Mathf.InverseLerp(1f, BoostTopSpeed, Mathf.Abs(drive));
            ViewShake.Shake(force);
            // The same crash, seen rather than felt: the paddle hits the edge of
            // the frame, which is where the lamps stand, so the corner it
            // arrived in lights up as hard as it was thrown there.
            RimLights.Flash(transform.position, force);
            drive = 0f;
        }

        // Taken from whether the paddle actually travelled, rather than from
        // the key alone: a paddle against the edge of the field is not
        // travelling however hard it is pushed, and one coasting a boost off
        // with no key held still is.
        Drift = Mathf.Approximately(x, here) ? 0f : Mathf.Sign(x - here);
        transform.position = new Vector3(x, transform.position.y, transform.position.z);

        Exhaust(here, x);
        Waves();
    }

    // The charge, wound up while DOWN is held and let go of the instant it is
    // not. A release is *spent* whether or not there was a ball there to take
    // it: a charge held on a paddle nobody caught anything with is worth
    // nothing, and having to wind a fresh one is what makes a mistimed push a
    // mistake rather than a free retry.
    void Wind(bool charging)
    {
        if (charging)
        {
            // Held past full it simply stays full. The cost of holding is not
            // that the charge decays — it is that every frame of holding is a
            // frame the ball is closer, and the release still has to land on
            // the catch.
            Charge = Mathf.Min(1f, Charge + Time.deltaTime / ChargeTime);
            return;
        }

        if (Charge > 0f)
        {
            ReleasedCharge = Charge;
            ReleasedAt = Time.time;
            Charge = 0f;
            if (gauge != null) gauge.Spend(ReleasedCharge);
        }
        // A release that met no ball inside its window lapses, so a charge let
        // go of a rally ago can never turn up on a catch it had nothing to do
        // with. The ball guards the same window from its own side; this is the
        // half of it that stops a release waiting for ever.
        else if (ReleasedCharge > 0f && Time.time - ReleasedAt > Ball.PunchWindow)
            ReleasedCharge = 0f;
    }

    // Called by the ball once it has worked out how much of a release it caught.
    // The charge is taken off the paddle either way — a push is spent on the
    // ball it was aimed at, however badly it was timed.
    public void SpendRelease()
    {
        ReleasedCharge = 0f;
    }

    // The gauge under the paddle, driven every frame whether or not there is a
    // charge on it: a charge just let go of has a gauge still flying apart, and
    // that is a frame-by-frame thing like everything else here. Unlike the
    // exhaust it does not care whether the paddle is moving — a charge is
    // gathered by a paddle standing still just as well as by one on the run.
    //
    // Everything it is measured by comes off the paddle's own drawn body, never
    // assumed, for the reason the exhaust's does: the menu's paddle is a
    // scaled-down copy of the round's and carries the same component, so it
    // charges with a scaled-down gauge under it.
    void Waves()
    {
        if (body == null) return;
        if (gauge == null)
        {
            // Nothing to make one for yet. Made on the first charge rather than
            // in Awake, so a paddle nobody ever pushes with costs nothing.
            if (Charge <= 0f) return;
            gauge = PowerWave.Attach(body.sharedMaterial);
            if (gauge == null) return;
        }

        var extents = body.bounds.extents;
        gauge.Tick(transform.position, extents.x * 2f, extents.y * 2f, Charge,
            Time.deltaTime);
    }

    // The gauge is an unparented root, so it does not go away with the paddle
    // the way a child would: the menu's paddle is switched off wholesale when
    // the menu leaves, and a gauge left standing would be a charge glowing under
    // a paddle that is not there.
    void OnDisable()
    {
        if (gauge != null) gauge.Hide();
    }

    void OnDestroy()
    {
        if (gauge != null) Destroy(gauge.gameObject);
    }

    // The trail behind the paddle, thrown off whenever it is travelling faster
    // than it is allowed to for free — under thrust or coasting a boost off,
    // since a paddle carrying borrowed speed is a paddle with the exhaust still
    // in it. That is the same 1.0 the crash into the frame is measured against
    // (see ViewShake), so what the player can see is exactly what the game is
    // reading: the trail thickens as the drive climbs, thins as it bleeds away,
    // and is gone the instant the paddle is back to its own speed.
    void Exhaust(float here, float x)
    {
        float over = Mathf.Abs(drive) - 1f;
        if (over <= 0f || Drift == 0f)
        {
            // Nothing borrowed, or nothing moving — a paddle held against the
            // edge of the frame is not running an engine however hard the keys
            // are pushed.
            emberDebt = 0f;
            return;
        }

        if (body == null) return;
        var material = body.sharedMaterial;
        if (material == null) return;

        float strength = Mathf.Clamp01(over / (BoostTopSpeed - 1f));
        // Behind the paddle, which is the way it is *not* going, and the nozzle
        // is the trailing edge of its own drawn body rather than its middle.
        // Both the nozzle and everything the exhaust is sized by are measured
        // off that body, since the menu's paddle is a scaled-down copy of this
        // one and its exhaust has to be scaled with it.
        var back = new Vector3(-Mathf.Sign(drive), 0f, 0f);
        var extents = body.bounds.extents;
        float height = extents.y * 2f;
        var nozzle = transform.position + back * extents.x;
        // Where that nozzle was before this frame's move, so the plume can be
        // laid along the ground it actually covered rather than dropped at a
        // point — the trail is then the same trail however fast the game is
        // drawing.
        var wasNozzle = nozzle + new Vector3(here - x, 0f, 0f);

        // The paddle's height serves as both the bore and the reach: a rocket
        // bolted to the back of a slab is as wide as the slab, and its flame was
        // always counted in the same unit.
        JetTrail.Plume(wasNozzle, nozzle, back, height, height, Mathf.Abs(drive) * speed,
            strength, JetTrail.Rocket, material);

        emberDebt += JetTrail.EmberRate * strength * Time.deltaTime;
        while (emberDebt >= 1f)
        {
            emberDebt -= 1f;
            JetTrail.Ember(nozzle, back, height, strength, JetTrail.Rocket, material);
        }
    }
}
