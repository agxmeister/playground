using UnityEngine;

// One block of the menu's lettering: a letter of the ARKANOID title, or a
// symbol of a champion's name or score on the hall of fame. A hit knocks the
// whole block out in a shower of the same debris a brick throws, so the words
// can be taken apart a symbol at a time while the player is deciding what to
// pick.
//
// The block is only switched off, never destroyed: MainMenuPanel puts the title
// back together every time the menu opens and the plaque's symbols back after
// every choice, so breaking them is a toy rather than a permanent edit.
[RequireComponent(typeof(Collider2D))]
public class MenuTitleBlock : MonoBehaviour
{
    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider.GetComponent<Ball>() == null) return;

        var renderer = GetComponent<MeshRenderer>();
        var material = renderer.sharedMaterial;
        Debris.Spawn(renderer.bounds.center, renderer.bounds.size,
            material.GetColor("_BaseColor"), material);
        gameObject.SetActive(false);
    }
}
