using UnityEngine;

// How much one block of a given material is allowed to differ from the next one
// beside it. The material asset says what the substance *is* — its metalness,
// its transparency, the shader path it takes — and this says how far a single
// casting of it may drift from its neighbours: which surface grain it was
// moulded with, how light or dark the batch came out, how glossy that left it.
//
// This is the one thing the split between shape and material did not cover. A
// material was a single shared asset and every block wearing it was pixel-wise
// identical, which is right for a warning — nine columns of nine looks, each
// look one hardness — and wrong for a surface: a field of injection-moulded
// polymer that is the same grey square thirty-six times reads as a spreadsheet
// rather than as a wall of plastic. So the *look* varies per block and the
// *rule* does not, and nothing here can change a hardness.
//
// A class rather than a struct because Unity serializes it as a list entry the
// editor setup fills in, and because a material with no variety simply has no
// entry — the block then wears the shared asset untouched.
[System.Serializable]
public class BlockVariety
{
    // Which material these variations belong to. Matched by value, not by index,
    // so an entry may be added for any material without the list having to
    // mirror the enum the way GameManager's material array does.
    public BlockMaterial material;

    // The surface reliefs this material is moulded with, one picked per block.
    // Height is carried by a normal map rather than by the albedo, because grain
    // is relief and not pigment: a block tinted near-black still catches the
    // scene's light across its bumps, where dark mottling painted into the
    // albedo would simply disappear into the tint. The albedo maps are near
    // white with only a whisper of value in them, so the tint below is what
    // colours the block.
    public Texture2D[] grains;
    public Texture2D[] grainNormals;

    // The band the batch colour is drawn from, in sRGB, the way every other
    // colour in this project is authored. Both ends are the same substance:
    // whatever is rolled between them has to still read as this material and
    // not as the one two rungs up the ladder.
    public Color darkest = Color.white;
    public Color lightest = Color.white;

    // Gloss goes with the roll rather than beside it, because in a real moulded
    // plastic the two are not independent — the dark stock is the glossy stock.
    // It is also what keeps a dark Polymer from being read as a Neutronium: a
    // dielectric with a grain and a sheen is plainly not the block that returns
    // nothing at all.
    public float darkSmoothness = 0.15f;
    public float lightSmoothness = 0.15f;

    // How far the batch colour may wander off the band's own hue, as a fraction
    // of each channel rather than as an amount added to it. Small either way: it
    // is there so that two blocks rolled to the same lightness are still not the
    // same block, not so that Polymer can come out green.
    //
    // It has to be a fraction, and that is the whole reason this is written down.
    // Added, the same 0.03 is a whisper on a channel at 0.93 and a *rout* on one
    // at 0.05 — the dark end of the band is only 0.05 wide, so an absolute jitter
    // there is the difference between grey and a strong hue, and the first dark
    // Polymer block off this code came out plainly brown. A fraction is the same
    // shift wherever it lands, which is what "off the band's own hue" was always
    // meant to say. What it costs is that the darkest blocks vary in hue barely
    // at all — and that is right: down there the variety is carried by the grain
    // and the gloss, which is exactly what has to be doing the work anyway (see
    // ArkanoidSetup.PolymerDarkest).
    public float hueJitter = 0.03f;

    // One casting. Everything in here is decided once, at spawn, and never
    // again — a block's own look does not change as it is worn down; that is
    // what the crack overlay is for.
    public BlockLook Roll(float grainTilesPerUnit)
    {
        float t = Random.value;
        var tint = Color.Lerp(darkest, lightest, t);
        tint = new Color(
            Mathf.Clamp01(tint.r * (1f + Random.Range(-hueJitter, hueJitter))),
            Mathf.Clamp01(tint.g * (1f + Random.Range(-hueJitter, hueJitter))),
            Mathf.Clamp01(tint.b * (1f + Random.Range(-hueJitter, hueJitter))),
            tint.a);

        int index = grains != null && grains.Length > 0 ? Random.Range(0, grains.Length) : -1;
        return new BlockLook(
            index >= 0 ? grains[index] : null,
            grainNormals != null && index >= 0 && index < grainNormals.Length ? grainNormals[index] : null,
            tint,
            Mathf.Lerp(darkSmoothness, lightSmoothness, t),
            // A random slide into the tiled grain, so that two neighbours that
            // rolled the same texture are not the same picture twice. Free
            // diversity: the maps tile, so any offset is as valid as none.
            new Vector2(Random.value, Random.value),
            grainTilesPerUnit);
    }
}

// What one block ended up looking like. A struct passed straight to the renderer
// and not kept: the block reads its own material for anything it needs later.
public readonly struct BlockLook
{
    public readonly Texture2D Grain;
    public readonly Texture2D GrainNormal;
    public readonly Color Tint;
    public readonly float Smoothness;
    public readonly Vector2 GrainOffset;
    public readonly float GrainTilesPerUnit;

    public BlockLook(Texture2D grain, Texture2D grainNormal, Color tint, float smoothness,
        Vector2 grainOffset, float grainTilesPerUnit)
    {
        Grain = grain;
        GrainNormal = grainNormal;
        Tint = tint;
        Smoothness = smoothness;
        GrainOffset = grainOffset;
        GrainTilesPerUnit = grainTilesPerUnit;
    }

    public bool HasGrain => Grain != null;
}
