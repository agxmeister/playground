using System.Collections.Generic;
using UnityEngine;

// Which of the two answers to "where does a block's shadow go" the room is
// using. `Directional` is what the game has always done; `Soft` is the one being
// tried against it (see "The lighting can be tried on the bench" in CLAUDE.md).
public enum ShadowMode { Directional, Soft }

// The other kind of shadow: a soft dark patch on the backdrop directly behind
// each object, with no direction in it at all.
//
// The game's own shadow is the key light's, and it is *directional* in both
// senses — it is thrown by a directional light, and it lands somewhere: pitched
// down by `ArkanoidSetup.LightPitch`, so a block's shadow drops below it by
// `gap x tan(pitch)` and reads as a hard offset copy of the block. That is the
// depth cue the whole look was built on, and it is also the thing that reads as
// heavy when a screen is full of blocks.
//
// This is the alternative, and it cannot be had by moving the key light: aim a
// directional light straight down +Z and every shadow hides exactly behind its
// caster, and a point light put in front of the plane throws shadows that fan
// outwards from wherever it stands, which is a *more* directional picture rather
// than a less one. A shadow that sits squarely behind every object, the same way
// for one at the edge of the frame as for one in the middle, is not something a
// light can cast. So it is drawn.
//
// Each patch is one sprite lying flat on the backdrop, black, soft-edged and
// a little larger than the thing it belongs to — the softness kept in world
// units on both axes by drawing it **nine-sliced**, so a wide block gets the
// same width of blur as a tall one rather than the stretched smear a single
// scaled quad would give.
//
// Like `RimLights` and `Ricochet` it is runtime-only, built from nothing, and
// authored nowhere: the sprite is generated on first use and the patches are
// plain `SpriteRenderer`s. Nothing here reaches the scene file.
public class SoftShadows : MonoBehaviour
{
    // How far past its caster's silhouette a patch reaches, in world units, and
    // therefore how soft it is: the blur is the whole of that margin. A third of
    // a unit is a little under a block's height, which is enough to read as a
    // shadow with no edge and not so much that neighbouring blocks' patches
    // merge into one grey band across the row.
    const float Feather = 0.35f;

    // How dark the middle of a patch is. Not 1: a shadow the backdrop cannot be
    // seen through stops being a shadow and starts being a hole, and the whole
    // point of this mode is that the murk goes on showing behind it.
    const float Darkness = 0.62f;

    // How far in front of the backdrop's face the patches lie. Enough to clear
    // it without argument — two coplanar surfaces z-fight, and the backdrop is
    // the one thing in the room guaranteed to be exactly where this wants to be.
    const float Lift = 0.02f;

    // A caster is something standing on the playing plane. The backdrop and the
    // two fog banks stand behind it and would otherwise each be handed a patch
    // of their own — a full-frame black sheet laid over the picture, which is a
    // memorable way to find out this filter was missing.
    const float PlaneTolerance = 0.25f;

    // How often the room is re-read for things that have appeared or gone. Every
    // frame would work and would also mean a scene-wide search every frame; a
    // fifth of a second is far below what the eye catches a brick breaking in,
    // and the patches of things that *move* are followed every frame regardless
    // (see `LateUpdate`) — it is only the membership of the list that waits.
    const float RescanInterval = 0.2f;

    // The generated sprite: a soft-edged white rectangle, tinted black by each
    // patch. `Border` is the blurred margin in pixels and is what makes the
    // nine-slice work — the middle stretches to the caster, the border does not.
    const int Core = 16;
    const int Border = 24;

    static Sprite patchSprite;

    // The one manager standing, so the mode can be flipped without the bench
    // holding a reference to a component in another room. Same shape as
    // `RimLights.current`, and for the same reason.
    static SoftShadows current;

    public static ShadowMode Mode { get; private set; } = ShadowMode.Directional;

    // Flipping the mode is two changes that have to happen together, which is
    // why they are one call: the drawn patches come on, and the key light's own
    // shadows go off. Leaving both on would give every block two shadows — the
    // hard offset one *and* a soft one behind it — which is not either of the
    // two things being compared.
    //
    // `keyLight` is passed in rather than searched for here because the caller
    // already has it, and because finding it is a question with a wrong answer
    // ready to hand: there are three directional-ish lights in this scene and the
    // two fills are not the key. See `TestBench.FindKeyLight`.
    public static void SetMode(ShadowMode mode, Light keyLight, LightShadows castingShadows)
    {
        Mode = mode;
        if (keyLight != null)
            keyLight.shadows = mode == ShadowMode.Directional ? castingShadows : LightShadows.None;
        if (current != null) current.Apply();
    }

    readonly Dictionary<Renderer, SpriteRenderer> patches = new();
    float planeZ;
    float patchZ;
    float sinceRescan;

    // Stood up and given the room's geometry by whoever owns the room, the same
    // way `RimLights` is: `planeZ` is the plane the casters stand on and the
    // backdrop's face is `ScreenChange.FogWall` behind it, which is where the
    // key light's own shadows land too — so the two modes are drawn on the same
    // wall and can be compared without one of them cheating on depth.
    public void FitTo(float planeZ)
    {
        current = this;
        this.planeZ = planeZ;
        patchZ = planeZ + ScreenChange.FogWall - Lift;
        Apply();
    }

    void Apply()
    {
        enabled = Mode == ShadowMode.Soft;
        if (!enabled) Clear();
        else sinceRescan = RescanInterval;
    }

    void OnDisable() => Clear();

    void Clear()
    {
        foreach (var patch in patches.Values)
            if (patch != null) Destroy(patch.gameObject);
        patches.Clear();
    }

    // Late, so a patch is put where its caster ended the frame rather than where
    // it started it: the ball and the paddle are both moved in `Update`, and a
    // shadow a frame behind a ball crossing the field at 8 units a second is a
    // shadow visibly trailing it.
    void LateUpdate()
    {
        sinceRescan += Time.deltaTime;
        if (sinceRescan >= RescanInterval)
        {
            sinceRescan = 0f;
            Rescan();
        }

        foreach (var pair in patches)
        {
            var caster = pair.Key;
            var patch = pair.Value;
            if (caster == null || patch == null) continue;
            // Read off the drawn bounds rather than the transform's scale, for
            // the reason every measured thing in this game is: a block is a
            // shape prefab whose mesh is authored at its own size, so its scale
            // says nothing about how much of the screen it covers.
            var bounds = caster.bounds;
            patch.transform.position = new Vector3(bounds.center.x, bounds.center.y, patchZ);
            patch.size = new Vector2(bounds.size.x + 2f * Feather, bounds.size.y + 2f * Feather);
            patch.enabled = caster.enabled && caster.gameObject.activeInHierarchy;
        }
    }

    void Rescan()
    {
        // Gone-away casters first: a brick that broke leaves a null key behind,
        // and its patch would otherwise sit on the backdrop for the rest of the
        // round marking where a block used to be.
        List<Renderer> lost = null;
        foreach (var pair in patches)
            if (pair.Key == null)
            {
                if (pair.Value != null) Destroy(pair.Value.gameObject);
                (lost ??= new List<Renderer>()).Add(pair.Key);
            }
        if (lost != null) foreach (var key in lost) patches.Remove(key);

        foreach (var caster in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            if (patches.ContainsKey(caster) || !Casts(caster)) continue;
            patches[caster] = NewPatch();
        }
    }

    // Who gets a patch. Two questions, and the first is answered by asking the
    // renderer rather than by keeping a list of types here: everything cosmetic
    // in this game already switches its own shadow casting off — sparks, rubble,
    // exhaust all do it in their own `Spawn` — so `shadowCastingMode` is
    // *already* the game's own answer to "does this thing throw a shadow", and
    // reading it means this mode agrees with the other one by construction
    // rather than by being kept in step by hand.
    bool Casts(Renderer caster)
    {
        if (caster.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off) return false;
        // And it has to be standing on the playing plane. The backdrop and the
        // two fog banks are renderers too, and each is the size of the frame.
        return Mathf.Abs(caster.transform.position.z - planeZ) < PlaneTolerance;
    }

    SpriteRenderer NewPatch()
    {
        var go = new GameObject("SoftShadow");
        go.transform.SetParent(transform, false);
        var patch = go.AddComponent<SpriteRenderer>();
        patch.sprite = PatchSprite;
        // Nine-sliced, which is the whole reason the sprite has a border: the
        // soft margin keeps its width in world units while the middle stretches
        // to whatever the caster is, so a wide block and a tall one are blurred
        // by the same amount instead of the wide one being smeared sideways.
        patch.drawMode = SpriteDrawMode.Sliced;
        patch.color = new Color(0f, 0f, 0f, Darkness);
        // It is a shadow: it must not be lit by the ring, and it must certainly
        // not cast one of its own.
        patch.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        patch.receiveShadows = false;
        return patch;
    }

    // A white rectangle whose alpha falls smoothly away over the border, built
    // once at runtime. Generated rather than imported for the same reason the
    // cube mesh in `Ricochet` is lifted off a throwaway primitive: it is a fact
    // about this effect, not an asset anybody would ever open and edit.
    static Sprite PatchSprite
    {
        get
        {
            if (patchSprite != null) return patchSprite;

            int size = Core + 2 * Border;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // How far into the rectangle this pixel is, on each axis,
                    // as 0 at the very edge and 1 once the border is cleared.
                    float alongX = Mathf.InverseLerp(0f, Border, Mathf.Min(x, size - 1 - x));
                    float alongY = Mathf.InverseLerp(0f, Border, Mathf.Min(y, size - 1 - y));
                    // Multiplied rather than taken as a minimum, which is what
                    // rounds the corners: a shadow with square corners reads as
                    // a panel behind the block instead of as a shadow under it.
                    float alpha = Mathf.SmoothStep(0f, 1f, alongX) * Mathf.SmoothStep(0f, 1f, alongY);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }

            texture.SetPixels32(pixels);
            texture.Apply();

            // Pixels per unit is chosen so the border comes out exactly
            // `Feather` wide in the world — that is the number this whole sprite
            // exists to place, and getting it from the geometry rather than
            // picking one keeps the two in step if either is retuned.
            float pixelsPerUnit = Border / Feather;
            patchSprite = Sprite.Create(
                texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit,
                0, SpriteMeshType.FullRect, new Vector4(Border, Border, Border, Border));
            return patchSprite;
        }
    }
}
