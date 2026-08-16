using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A piece of a menu screen while it is moving: the transforms it is made of and
// where each of them rests, so the motion can be written as an offset from the
// layout rather than as a set of positions to remember and put back. A board is
// one transform; a champion's plaque is the two lines' worth of blocks and the
// arrow standing beside them.
//
// It also carries what the piece is made of as far as a change is concerned:
// the colliders that say whether it can be hit, and the renderers that say
// where it stands.
public class ScreenPiece
{
    readonly Transform[] parts;
    readonly Vector3[] homes;
    readonly Collider2D[] colliders;
    readonly Renderer[] renderers;

    public ScreenPiece(params Transform[] parts) : this((IList<Transform>)parts) { }

    public ScreenPiece(IList<Transform> parts)
    {
        this.parts = new Transform[parts.Count];
        homes = new Vector3[parts.Count];
        var foundColliders = new List<Collider2D>();
        var foundRenderers = new List<Renderer>();
        for (int i = 0; i < parts.Count; i++)
        {
            this.parts[i] = parts[i];
            if (parts[i] == null) continue;
            homes[i] = parts[i].localPosition;
            // Inactive children included: a shattered letter or the arrow that
            // was hit to start the change is exactly the object that is
            // switched off, and it is still part of the piece.
            foundColliders.AddRange(parts[i].GetComponentsInChildren<Collider2D>(true));
            foundRenderers.AddRange(parts[i].GetComponentsInChildren<Renderer>(true));
        }
        colliders = foundColliders.ToArray();
        renderers = foundRenderers.ToArray();
    }

    // Across the frame by `x` and behind the playing plane by `depth`, both
    // measured from where the piece was authored.
    public void MoveTo(float x, float depth)
    {
        for (int i = 0; i < parts.Length; i++)
            if (parts[i] != null)
                parts[i].localPosition = homes[i] + new Vector3(x, 0f, depth);
    }

    // A screen under the plane is not in the game: the ball passes over it
    // while it travels, and it only becomes something to hit once it is
    // standing in the plane. This is also what keeps a screen from being broken
    // by the ball it rises into — there is no collision to break it.
    public void SetSolid(bool solid)
    {
        foreach (var collider in colliders)
            if (collider != null) collider.enabled = solid;
    }

    public bool Covers(Vector3 point, float radius) =>
        !float.IsPositiveInfinity(FaceUnder(point, radius));

    // The nearest face this piece has under the given point — nearest meaning
    // closest to the camera, which is the one a ball standing there would ride
    // on. Positive infinity if the piece has nothing there at all.
    //
    // Measured off what is drawn rather than off the colliders, because the
    // colliders are switched off for the whole of the arrival and a switched-off
    // collider has no bounds to measure.
    public float FaceUnder(Vector3 point, float radius)
    {
        float face = float.PositiveInfinity;
        foreach (var renderer in renderers)
        {
            // A block the ball has already knocked out is not there to ride on,
            // and an empty renderer — a plaque line is one, an anchor for the
            // blocks rather than a mesh of its own — has no bounds worth
            // reading.
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            var bounds = renderer.bounds;
            if (bounds.size.x <= 0f || bounds.size.y <= 0f) continue;
            if (point.x < bounds.min.x - radius || point.x > bounds.max.x + radius) continue;
            if (point.y < bounds.min.y - radius || point.y > bounds.max.y + radius) continue;
            if (bounds.min.z < face) face = bounds.min.z;
        }
        return face;
    }
}

// How the menu changes screens: in two dimensions rather than one.
//
// A change used to be a single slide across the frame, one screen out and the
// next in, both of them in the plane the ball plays on. It happens in depth as
// well now. The screen being left flies out across the frame, in the plane and
// solid the whole way — the rally carries on through a change, and what the
// ball breaks on a departing board stays broken. The screen arriving comes in
// *under* the plane, in the background, where it is only scenery: it cannot be
// hit, and the ball passes over it while it travels. Only when it is in place
// does it rise into the plane and become part of the game again.
//
// The one thing that can be standing where it rises is the ball. A screen
// coming up under a ball is not broken by it and does not shoulder it aside —
// it lifts it: the ball rides out of the plane on the face rising towards the
// camera, and drops back once the screen has passed out from under it. Which is
// why the colliders are the last thing to come back: a screen is solid again
// when the ball it lifted is back in the plane and clear of it.
public static class ScreenChange
{
    // How far behind the playing plane an arriving screen travels. The menu's
    // backdrop stands far enough back to leave room for it (ArkanoidSetup's
    // MenuBackdropZ), and at this depth a screen reads as one standing behind
    // the plane rather than as one that has merely been drawn smaller.
    public const float SinkDepth = 1f;

    const float FlyOutDuration = 0.35f;
    const float FlyInDuration = 0.45f;
    // Shorter than either travel: the rise is the arrival, and a slow one reads
    // as the screen struggling into its place rather than taking it.
    const float RaiseDuration = 0.3f;

    // How long an arrived screen will wait for a ball riding on it to get clear
    // before it goes solid regardless. A ball in play is always travelling and
    // will never reach this; a ball waiting to be served on the paddle is not,
    // and would otherwise hold the screen open for ever.
    const float MaxRide = 2f;

    // The screen being left, out across the frame in the playing plane. Nothing
    // is switched off on the way: the ball can still hit it, which is the whole
    // of the rally carrying on across a change.
    public static IEnumerator FlyOut(ScreenPiece piece, float distance)
    {
        for (float t = 0f; t < FlyOutDuration; t += Time.deltaTime)
        {
            piece.MoveTo(Mathf.SmoothStep(0f, distance, t / FlyOutDuration), 0f);
            yield return null;
        }
        piece.MoveTo(distance, 0f);
    }

    // Where a screen waits to come in from: under the plane, off the frame, and
    // not there to be hit. FlyIn does this itself, but a screen that is built
    // rather than standing ready has to be put out of the way the moment it
    // exists — a champion's plaque is built at the place the plaque rests, and
    // the champion it replaces has not left it yet.
    public static void Stage(ScreenPiece piece, float from)
    {
        piece.SetSolid(false);
        piece.MoveTo(from, SinkDepth);
    }

    // The screen arriving: in from `from` under the plane, then up into it.
    public static IEnumerator FlyIn(ScreenPiece piece, float from, Ball ball)
    {
        Stage(piece, from);
        for (float t = 0f; t < FlyInDuration; t += Time.deltaTime)
        {
            piece.MoveTo(Mathf.SmoothStep(from, 0f, t / FlyInDuration), SinkDepth);
            yield return null;
        }

        for (float t = 0f; t < RaiseDuration; t += Time.deltaTime)
        {
            piece.MoveTo(0f, Mathf.SmoothStep(SinkDepth, 0f, t / RaiseDuration));
            Carry(piece, ball);
            yield return null;
        }
        piece.MoveTo(0f, 0f);

        // In the plane, and perhaps under the ball. It is solid again only once
        // the ball it lifted is back in the plane and off it — going solid
        // under a ball it is holding up would leave the two of them in the same
        // place the moment the ball came down.
        for (float ridden = 0f; ridden < MaxRide; ridden += Time.deltaTime)
        {
            Carry(piece, ball);
            if (ball == null || (ball.OnPlane && !piece.Covers(ball.transform.position, ball.Radius)))
                break;
            yield return null;
        }
        piece.SetSolid(true);
    }

    // A ball standing over the screen rides on whatever face is coming up under
    // it, so it is carried out of the plane rather than run through. The ball
    // is told where the face is rather than how far to move: how far in front
    // of it the ball has to sit is the ball's own size, which it knows and this
    // does not.
    static void Carry(ScreenPiece piece, Ball ball)
    {
        if (ball == null) return;
        float face = piece.FaceUnder(ball.transform.position, ball.Radius);
        if (!float.IsPositiveInfinity(face)) ball.PushInFrontOf(face);
    }
}
