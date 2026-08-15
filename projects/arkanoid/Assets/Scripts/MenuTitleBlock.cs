using UnityEngine;

// One letter of the ARKANOID title. A hit knocks the whole letter out in a
// shower of the same debris a brick throws, so the word can be taken apart a
// letter at a time while the player is deciding what to pick.
//
// The letter is only switched off, never destroyed: MainMenuPanel puts the
// word back together every time the menu opens, so breaking it is a toy rather
// than a permanent edit to the scene.
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
