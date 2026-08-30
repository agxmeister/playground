using UnityEngine;

// A ring of lights standing round the edge of the frame, and the flare that runs
// through them when something is struck.
//
// The scene has one key light and it is directional, which means it pays every
// block in the room exactly the same amount: a directional light has no
// position, so nothing about the picture says where in the frame a block stands.
// The two `FillLeft`/`FillRight` fills in `ArkanoidSetup` are directional too,
// and that is precisely why they came back as flat pale panels beside every
// block and were dialled to zero. A point light is the thing they could not be —
// it falls off with distance, so a block out at the left edge is lit from the
// left, one in the middle catches a little from both sides, and the same block
// moved across the field is shaded differently for having moved. That is the
// whole of the resting effect, and it is the reason this is a rim of *points*
// rather than another attempt at the fills.
//
// It is also **reactive**: an impact anywhere in the room flares the lights that
// can see it (`Flash`), so the edge of the screen lights up on the side the ball
// just hit. The two halves are one component on purpose — the flare has to be
// read against the resting level it rides on, and a lamp that is already lit is
// the cheapest possible flash.
//
// Like `ViewShake`, it is **runtime-only and never authored**. `Playfield` stands
// it up and re-fits it whenever the frame changes, for the same reason the
// borders and the fog banks are fitted rather than placed: a perimeter is only
// known once there is a window, and it changes when the window does. Nothing
// reaches the scene file, so `ArkanoidSetup`'s light stages — which find the key
// light by name precisely because two authored fills once broke them — never
// meet these at all.
public class RimLights : MonoBehaviour
{
    // Eight lamps: one at each corner of the frame and one at the middle of each
    // side. Evenly spacing N lamps round the perimeter was the obvious
    // alternative and it is worse: the corners are where two edges of the
    // picture meet, and a lamp that drifts off a corner as the window is resized
    // takes the shape of the room's lighting with it. Corners and midpoints hold
    // the same shape at every aspect ratio.
    //
    // Eight is also what the per-object budget allows. URP's
    // `m_AdditionalLightsPerObjectLimit` is 4 (`Settings/PC_RPAsset.asset`), and
    // a lamp beyond the fourth nearest a given brick is simply dropped for that
    // brick — which shows up as blocks popping between lighting solutions as
    // they and the lamps move. `Range` below is what keeps the count that ever
    // reaches one brick well inside four; the two numbers have to be read
    // together.
    const int Count = 8;

    // How far each lamp's light carries, as a multiple of the frame's half
    // height. The frame is 12 units tall whatever the window's shape (the
    // camera's field of view is vertical), so this is a little over 5 units —
    // deliberately less than the 6 from a side's midpoint to its own corner.
    //
    // That is what makes this a *rim* rather than a second key light, and it was
    // measured rather than guessed: at 1.35 (a reach of 8.5) the eight lamps
    // between them covered the whole fog wall, and what came back was not an
    // edge that glowed but a picture that was uniformly paler — the murk lit
    // flat, its depth gone. Pulled in to here the middle of the field is out of
    // every lamp's reach and only the edges answer.
    //
    // It is also what keeps the per-object budget honest: no brick is ever
    // within reach of more than two or three lamps, well inside the four URP
    // will render.
    const float Range = 0.8f;

    // How far in front of the playing plane the ring stands, towards the camera.
    // At the plane's own depth a lamp is exactly edge-on to every block and
    // reaches only their ends and flanks — which is the face the fills were
    // built for, but it leaves the fronts, the whole of what the camera sees,
    // untouched by any of this. A single unit forward is enough to put a
    // grazing wash across the front faces without turning the ring into a second
    // key light: the blocks stay lit from the front by the one light that is
    // allowed to say so.
    const float Standoff = 1f;

    // What a lamp burns at with nothing happening. A point light's intensity is
    // spent on the inverse square of the distance, so this number does not read
    // like a directional light's: 1.6 — the sort of value the key light wears —
    // measured as nothing at all at the five units a lamp has to carry, and 30
    // washed the whole picture pale. Eight is the middle that was looked at: the
    // top edge of the fog warms behind the brick rows, the corners pool, and the
    // middle of the field is left to the key light.
    //
    // The resting ring is meant to be read as shading and not as eight lamps.
    // If they can be counted in a still frame this is too high.
    const float RestIntensity = 8f;

    // The resting level actually in force, as opposed to the one the game is
    // written to. They are the same number until the test bench moves it (see
    // "The lighting can be tried on the bench" in CLAUDE.md), which is the whole
    // reason it is a dial and not a constant read straight: a value that has to
    // be compared against another value cannot be a `const`.
    //
    // The bench puts it back on the way out. A number left standing here would
    // follow the player into the next round, and a bench that changes a round is
    // the one thing the bench must never be.
    public static float RestLevel { get; private set; } = RestIntensity;

    // What the game is written to, so the bench can say how far it has been
    // moved and can put it back without knowing the number itself.
    public static float AuthoredRestLevel => RestIntensity;

    // Applied on the spot rather than left for `Update` to notice, because a
    // still ring switches itself off: a lamp with nothing flaring is not being
    // written to on any frame, so a dial that only took effect in `Update` would
    // appear to do nothing at all until the next impact.
    public static void SetRestLevel(float level)
    {
        RestLevel = Mathf.Max(0f, level);
        if (current == null || current.lamps == null) return;
        for (int i = 0; i < current.lamps.Length; i++)
            if (current.left[i] <= 0f) current.lamps[i].intensity = RestLevel;
    }

    // What a full-force impact adds on top of that, at the lamp nearest it —
    // seven or eight times the resting level, because a flare has to be seen
    // against a room that is already lit and it is over in a fifth of a second.
    // The eye is given a brief step of about eight times the light on that edge,
    // which is what makes it read as a flash rather than as a lamp being turned
    // up.
    const float FlashIntensity = 60f;

    // How long a flare takes to die away. A shade under `ViewShake.Duration`
    // (0.32) on purpose: a knock rings the room after the light of the impact
    // has gone, the same way a bang outlasts a spark.
    const float FlashDuration = 0.22f;

    // Warm, and only just. The blocks' ambient is deliberately neutral (see
    // `ArkanoidSetup.AmbientColor`) because a *blue* cast beside a warm block
    // face read as a foreign material rather than as shading; the same argument
    // says a strong tint of any colour here would do the same thing. This is
    // white with the blue taken down a little, so the pools read as light rather
    // than as a colour that has been applied to something.
    static readonly Color LampColor = new Color(1f, 0.97f, 0.90f);

    // The one ring standing, so an impact anywhere can find it without every
    // caller holding a reference. The menu has no ring of its own — `Playfield`
    // owns this one and the menu is a different room — so on the title screen
    // this is either null or pointing at a room that is switched off, and
    // `Flash` refuses both.
    static RimLights current;

    Light[] lamps;
    float[] flash;
    float[] left;
    float range;

    // Stood up and aimed by `Playfield`, every time the frame is fitted. `centre`
    // and `extents` are the frame on the room's own plane, `z` its depth.
    public void FitTo(Vector2 centre, Vector2 extents, float z)
    {
        current = this;
        if (lamps == null) Build();

        range = Range * extents.y;
        float lampZ = z - Standoff;

        // Corners first, then the midpoints of the four sides. The order is only
        // ever read here, so it is written where it can be seen rather than
        // being computed from an index nobody can picture.
        var places = new[]
        {
            new Vector2(-extents.x, -extents.y), new Vector2(extents.x, -extents.y),
            new Vector2(-extents.x, extents.y), new Vector2(extents.x, extents.y),
            new Vector2(0f, -extents.y), new Vector2(0f, extents.y),
            new Vector2(-extents.x, 0f), new Vector2(extents.x, 0f),
        };

        for (int i = 0; i < lamps.Length; i++)
        {
            lamps[i].transform.position = new Vector3(centre.x + places[i].x, centre.y + places[i].y, lampZ);
            lamps[i].range = range;
        }
    }

    // An impact at `point`, `force` being 0 for the gentlest worth showing and 1
    // for the hardest. Every lamp that could see the point takes a share of it,
    // fading to nothing at the edge of its own reach, so the flare is loudest on
    // the side of the frame the hit landed on and the far edge does not answer a
    // hit it is nowhere near. That is the point of the feature: the light says
    // *where*, the way `ViewShake` says *how hard*.
    public static void Flash(Vector3 point, float force)
    {
        // `activeInHierarchy` rather than a null check alone, because the ring
        // outlives the room being switched off: `GameManager` hides the whole
        // playfield for the menu, and the menu's own ball goes on striking the
        // menu's borders the entire time it is up. Those hits must not be banked
        // against lamps nobody can see, or the first frame of the next round
        // would open on a flare left over from the title screen.
        if (current == null || !current.gameObject.activeInHierarchy || force <= 0f) return;
        current.Flare(point, Mathf.Clamp01(force));
    }

    void Flare(Vector3 point, float force)
    {
        enabled = true;
        for (int i = 0; i < lamps.Length; i++)
        {
            // Measured on the plane rather than in space: every lamp stands the
            // same `Standoff` in front of it, so the depth is a constant that
            // would only flatten the difference between near lamps and far ones.
            var lamp = lamps[i].transform.position;
            float distance = Vector2.Distance(new Vector2(point.x, point.y), new Vector2(lamp.x, lamp.y));
            float share = 1f - Mathf.Clamp01(distance / range);
            if (share <= 0f) continue;

            // Taken over rather than added to, like a knock landing on a view
            // that is still ringing: two hits in quick succession on the same
            // side would otherwise stack into a lamp brighter than the hardest
            // single hit is allowed to make it.
            flash[i] = Mathf.Max(flash[i], force * share);
            left[i] = FlashDuration;
        }
    }

    void Build()
    {
        lamps = new Light[Count];
        flash = new float[Count];
        left = new float[Count];

        for (int i = 0; i < Count; i++)
        {
            var go = new GameObject($"RimLight{i}");
            go.transform.SetParent(transform, false);
            var lamp = go.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = LampColor;
            lamp.intensity = RestLevel;
            // Off, and this is the half of it that matters most. Every shadow in
            // this game lands on the backdrop behind its caster, and that one
            // shadow is the depth cue the whole look rests on. Eight lamps that
            // cast would put eight shadows of every block on the fog wall and
            // the room would stop reading as a room.
            lamp.shadows = LightShadows.None;
            lamps[i] = lamp;
        }
    }

    void Update()
    {
        bool anyLit = false;
        for (int i = 0; i < lamps.Length; i++)
        {
            if (left[i] <= 0f)
            {
                lamps[i].intensity = RestLevel;
                continue;
            }

            left[i] -= Time.deltaTime;
            // Squared, so the flare is brightest at the moment of the hit and
            // trails off rather than stopping — the same falloff `ViewShake`
            // rings on, because they are showing the same instant.
            float t = Mathf.Max(left[i], 0f) / FlashDuration;
            lamps[i].intensity = RestLevel + FlashIntensity * flash[i] * t * t;
            anyLit = true;
        }

        // A ring with nothing flaring is eight lamps holding still, and holding
        // still costs nothing worth a frame of work. `Flare` switches it back
        // on. The lamps themselves keep burning either way — this is only the
        // component that animates them.
        if (!anyLit) enabled = false;
    }
}
