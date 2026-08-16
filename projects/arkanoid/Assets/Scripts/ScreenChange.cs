using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// A piece of a menu screen while it is moving: the transforms it is made of and
// where each of them rests, so the motion can be written as an offset from the
// layout rather than as a set of positions to remember and put back. A board is
// one transform; a champion's plaque is the two lines' worth of blocks and the
// arrow standing beside them.
//
// It also carries what the piece is made of as far as a change is concerned:
// the colliders that say whether it can be hit, the renderers that say where it
// stands, and the colours those renderers wear when nothing is in front of them
// — which is what the fog is mixed into.
public class ScreenPiece
{
    // How deep in the fog a screen has to be before it stops throwing a shadow.
    // A shadow does not fade with its caster: a screen still mostly in the fog
    // is a dim shape, and a crisp shadow beside it would draw the eye to the one
    // thing that is meant to be barely there. Half way out is where it starts
    // casting, by which point it is solid enough to own it.
    const float ShadowFog = 0.5f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
    static MaterialPropertyBlock block;

    readonly Transform[] parts;
    readonly Vector3[] homes;
    readonly Collider2D[] colliders;
    readonly Renderer[] renderers;
    readonly Color[] colours;
    readonly float[] gloss;

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
        colours = new Color[renderers.Length];
        gloss = new float[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            var material = renderers[i] != null ? renderers[i].sharedMaterial : null;
            colours[i] = material != null && material.HasProperty(BaseColorId)
                ? material.GetColor(BaseColorId)
                : Color.white;
            gloss[i] = material != null && material.HasProperty(SmoothnessId)
                ? material.GetFloat(SmoothnessId)
                : 0f;
        }
    }

    // Across the frame by `x` and behind the playing plane by `depth`, both
    // measured from where the piece was authored.
    public void MoveTo(float x, float depth)
    {
        for (int i = 0; i < parts.Length; i++)
            if (parts[i] != null)
                parts[i].localPosition = homes[i] + new Vector3(x, 0f, depth);
    }

    // How much of the fog is in front of this piece, 0 for none and 1 for all of
    // it. The fog is not a volume the renderer knows about — it is the colour
    // the backdrop is, mixed into whatever the piece wears, per instance so that
    // one screen can be in it while the other is not. At 1 the piece is the fog's
    // own colour, which is what lets it come out of the murk rather than appear
    // in front of it.
    public void SetFog(float amount)
    {
        amount = Mathf.Clamp01(amount);
        block ??= new MaterialPropertyBlock();
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null) continue;
            // Out of the fog is the material's own colour and nothing on top of
            // it, rather than the material's colour written into a block: a
            // screen at rest is a screen this was never applied to.
            if (amount <= 0f)
            {
                renderer.SetPropertyBlock(null);
            }
            else
            {
                renderer.GetPropertyBlock(block);
                block.SetColor(BaseColorId, Color.Lerp(colours[i], ScreenChange.FogColor, amount));
                // The sheen goes with the colour, or the fog is given away by
                // the one thing it cannot cover: a lit face still catching a
                // highlight. The fog's far wall is matte, so a screen fully in
                // the fog has to be matte too, and a screen coming out of it
                // takes its gloss back as it takes its colour back.
                block.SetFloat(SmoothnessId, Mathf.Lerp(gloss[i], 0f, amount));
                renderer.SetPropertyBlock(block);
            }
            renderer.shadowCastingMode =
                amount > ShadowFog ? ShadowCastingMode.Off : ShadowCastingMode.On;
        }
    }

    // A screen under the plane is not in the game: the ball passes over it
    // while it rises, and it only becomes something to hit once it is standing
    // in the plane. This is also what keeps a screen from being broken by the
    // ball it rises into — there is no collision to break it.
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
// next in, both of them in the plane the ball plays on. The second half of it
// happens in depth now. The screen being left flies out across the frame, in
// the plane and solid the whole way — the rally carries on through a change,
// and what the ball breaks on a departing board stays broken. The screen
// arriving does not travel across at all: it is already standing where it
// belongs, down in the fog behind the playing plane, and it **rises out of the
// fog** into place.
//
// The fog is a colour here and two drifting banks of haze in the room itself
// (see MenuFog): what a screen wears is the colour, what a player watches is
// the haze. The two are one thing on purpose — the banks hang in the same
// shallow space a screen sinks into, so a screen down in the murk is behind
// them and is veiled by whatever drifts across it.
//
// The fog is what the space between the playing plane and the backdrop is,
// rather than a gap with a wall at the end of it. It is only a hand's breadth
// deep — FogWall, which is where the backdrop stands — because every shadow on
// the menu is thrown onto that backdrop and a shadow's drop is its object's gap
// from it times the tangent of the light's pitch. Keeping the wall close keeps
// the shadows short; what gives an arriving screen somewhere to be is not
// distance but the fog itself, which takes the screen's colour towards its own
// (ScreenPiece.SetFog) the deeper the screen is. A screen waits at FogDepth,
// which is behind the wall — out of sight entirely — and by the time it clears
// the wall's face it is wearing the fog's own colour, so it emerges rather than
// appears, and darkens back into nothing if it ever went the other way.
//
// The one thing that can be standing where it rises is the ball. A screen
// coming up under a ball is not broken by it and does not shoulder it aside —
// it lifts it: the ball rides out of the plane on the face rising towards the
// camera, and drops back once the screen has passed out from under it. Which is
// why the colliders are the last thing to come back: a screen is solid again
// when the ball it lifted is back in the plane and clear of it.
public static class ScreenChange
{
    // Where the fog's far side is: how far behind the playing plane the menu's
    // backdrop stands (ArkanoidSetup's MenuBackdropZ is built from this). A
    // screen this deep is wearing the fog's colour outright. It is deliberately
    // short — this gap is also what every shadow on the menu is thrown across.
    public const float FogWall = 0.55f;

    // How far behind the plane an arriving screen waits. Deeper than the wall by
    // more than the thickest thing a screen carries (an option arrow, half of
    // whose 0.5 depth stands in front of its centre), so a screen waiting there
    // is behind the backdrop and not to be seen at all.
    public const float FogDepth = 0.9f;

    // The colour the fog takes a screen towards, which is the colour of the wall
    // at the back of it (ArkanoidSetup makes the menu backdrop's material this).
    // They are one colour on purpose: a screen fully in the fog and the fog's
    // own far side have nothing to tell them apart.
    public static readonly Color FogColor = new Color(0.05f, 0.07f, 0.12f);

    const float FlyOutDuration = 0.35f;
    // The rise is the whole of an arrival now that nothing travels in across the
    // frame, so it is given the time the travel used to have. Rising out of fog
    // is a fade as much as a movement, and a fade this short still needs to be
    // watchable.
    const float RiseDuration = 0.55f;

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

    // Where a screen waits to rise from: in its own place, down in the fog
    // behind the backdrop, not there to be hit and not there to be seen. Rise
    // does this itself, but a screen has to be put down there the moment it can
    // be looked at — the champion's plaque is built where the plaque rests,
    // while the champion it replaces is still standing there, and the board
    // being travelled to comes into the frame the moment the slider hands the
    // layout over.
    public static void Stage(ScreenPiece piece)
    {
        piece.SetSolid(false);
        piece.MoveTo(0f, FogDepth);
        piece.SetFog(1f);
    }

    // The screen arriving: up out of the fog into the playing plane, in place.
    public static IEnumerator Rise(ScreenPiece piece, Ball ball)
    {
        Stage(piece);
        for (float t = 0f; t < RiseDuration; t += Time.deltaTime)
        {
            float depth = Mathf.SmoothStep(FogDepth, 0f, t / RiseDuration);
            piece.MoveTo(0f, depth);
            // Full fog from the wall back — which is all the way back to where
            // it waits, and all of it out of sight behind the wall. What is
            // watched is the clearing, from the wall's face into the plane.
            piece.SetFog(depth / FogWall);
            Carry(piece, ball);
            yield return null;
        }
        piece.MoveTo(0f, 0f);
        piece.SetFog(0f);

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
    // does not. A face still down in the fog asks for nothing — the ball is
    // already well in front of it — so the lift begins exactly as the screen
    // reaches the ball rather than as the rise starts.
    static void Carry(ScreenPiece piece, Ball ball)
    {
        if (ball == null) return;
        float face = piece.FaceUnder(ball.transform.position, ball.Radius);
        if (!float.IsPositiveInfinity(face)) ball.PushInFrontOf(face);
    }
}
