using UnityEngine;

public class Brick : MonoBehaviour
{
    public int Points { get; set; } = 100;

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider.GetComponent<Ball>() == null) return;

        if (GameManager.Instance != null) GameManager.Instance.OnBrickDestroyed(this);
        Destroy(gameObject);
    }
}
