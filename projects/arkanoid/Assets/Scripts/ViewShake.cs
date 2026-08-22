using UnityEngine;

// A knock to the whole picture: the camera is thrown off its rest position for a
// moment and settles back. Everything in both rooms is authored in world space
// and the view is the only thing that moves between them, so shaking the view is
// the one way to shake the scene without touching a single thing standing in it.
//
// It is runtime-only state and is never authored: the first knock adds the
// component to whatever `Camera.main` is, so nothing in the scene or in
// `ArkanoidSetup` has to know the mechanic exists.
//
// The offset is undone before each frame's is worked out, so the rest position
// is read back out of the transform rather than remembered. That is what lets
// `GameManager` go on writing the camera's x directly — the travel from the menu
// to the playfield is exactly that — without the two fighting over the same
// number: a shake is a shove away from wherever the view has been put, and it
// hands the view back untouched when it is spent.
public class ViewShake : MonoBehaviour
{
    // How far a full-force knock throws the view, in world units on the
    // playfield's plane. The frame is 12 units tall, so this is a shade over 2%
    // of the picture at its widest — enough to be felt as an impact, small
    // enough that the ball never leaves the eye it is being followed with.
    const float MaxThrow = 0.28f;

    // How long a knock takes to die away. Long enough to read as a room ringing
    // rather than a single frame's glitch, short enough to be over before the
    // paddle can be driven back across the field.
    const float Duration = 0.32f;

    // The two rates the offset wobbles at, in radians a second — deliberately
    // not multiples of each other, so the x and y wobbles never line up into a
    // diagonal rocking and the view traces an untidy figure instead.
    const float RateX = 47f;
    const float RateY = 31f;

    // A knock, `force` being 0 for the gentlest worth showing and 1 for the
    // hardest. Anything at or below nothing is ignored, and a knock landing on
    // a view that is still ringing takes it over rather than adding to it: two
    // shakes summed would throw the picture twice as far as the hardest single
    // hit is allowed to.
    public static void Shake(float force)
    {
        if (force <= 0f) return;
        var view = Camera.main;
        if (view == null) return;
        var shake = view.GetComponent<ViewShake>();
        if (shake == null) shake = view.gameObject.AddComponent<ViewShake>();
        shake.Knock(Mathf.Clamp01(force));
    }

    // What was last added to the camera's position, so it can be taken back off
    // before the next one is worked out.
    Vector3 applied;
    float strength;
    float left;
    float phaseX;
    float phaseY;

    void Knock(float force)
    {
        // Switched back on: a spent shake switches itself off so a still view
        // costs nothing a frame.
        enabled = true;
        strength = Mathf.Max(strength, force);
        left = Duration;
        // A fresh phase per knock, or every bump would throw the view the same
        // way and the shake would read as a canned animation.
        phaseX = Random.value * Mathf.PI * 2f;
        phaseY = Random.value * Mathf.PI * 2f;
    }

    // Late, so it lands on top of whatever moved the view this frame rather
    // than being overwritten by it.
    void LateUpdate()
    {
        // Back to rest first: the view may have been moved by something else
        // since, and only our own shove is ours to remove.
        if (applied != Vector3.zero)
        {
            transform.position -= applied;
            applied = Vector3.zero;
        }

        if (left <= 0f)
        {
            // Spent. The view has just been handed back exactly where it was
            // found — anything else would leave the camera a little off centre
            // for good — and nothing is owed until the next knock, so the
            // component switches itself off rather than idling a frame at a
            // time. Knock switches it back on.
            strength = 0f;
            enabled = false;
            return;
        }

        left -= Time.deltaTime;
        float t = Mathf.Max(left, 0f) / Duration;
        // Squared, so the ring is loudest at the moment of the hit and trails
        // off rather than stopping.
        float reach = MaxThrow * strength * t * t;
        float age = Duration - left;
        // Less of it up and down than across, because the knock being shown is
        // one delivered sideways: the paddle hits the frame's edge travelling
        // along it, and a picture that jumped as far vertically would read as
        // something landing on the floor instead.
        applied = new Vector3(
            Mathf.Sin(age * RateX + phaseX) * reach,
            Mathf.Sin(age * RateY + phaseY) * reach * 0.6f,
            0f);
        transform.position += applied;
    }
}
