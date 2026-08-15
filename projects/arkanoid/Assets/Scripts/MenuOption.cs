using UnityEngine;

// One of the menu's option slabs. Steering the ball into it is the only way to
// pick the option — there is no keyboard or mouse path — so the collider is
// the whole of the interaction.
//
// A hit shatters the slab the way a brick shatters, and MainMenuPanel holds the
// choice back until the rubble has fallen, so the option visibly breaks apart
// before the screen changes.
[RequireComponent(typeof(Collider2D))]
public class MenuOption : MonoBehaviour
{
    // A normal brick's width — the debris count a slab breaks into is scaled
    // against it so a wide slab throws proportionally more rubble.
    const float BrickWidth = 1.5f;

    [SerializeField] MainMenuOption option;

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider.GetComponent<Ball>() == null) return;

        // The panel refuses every hit after the first, including one on the
        // other slab while the rubble of this one is still falling.
        var panel = GetComponentInParent<MainMenuPanel>();
        if (panel == null || !panel.OnOptionHit(option)) return;

        Shatter();
    }

    // The slab and the label standing proud of it each break into rubble of
    // their own colour, then the whole option switches off. Like the title
    // letters it is never destroyed — MainMenuPanel puts it back when the menu
    // next opens.
    void Shatter()
    {
        foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
        {
            var material = renderer.sharedMaterial;
            var size = renderer.bounds.size;
            Debris.Spawn(renderer.bounds.center, size,
                material.GetColor("_BaseColor"), material, size.x / BrickWidth);
        }
        gameObject.SetActive(false);
    }
}
