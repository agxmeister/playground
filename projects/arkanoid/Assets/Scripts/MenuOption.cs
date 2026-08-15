using UnityEngine;

// One of the menu's option slabs. Steering the ball into it is the only way to
// pick the option — there is no keyboard or mouse path — so the collider is
// the whole of the interaction.
[RequireComponent(typeof(Collider2D))]
public class MenuOption : MonoBehaviour
{
    [SerializeField] MainMenuOption option;

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider.GetComponent<Ball>() == null) return;
        var panel = GetComponentInParent<MainMenuPanel>();
        if (panel != null) panel.OnOptionHit(option);
    }
}
