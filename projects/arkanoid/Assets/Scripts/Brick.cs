using UnityEngine;

public class Brick : MonoBehaviour
{
    public int Points { get; set; } = 100;
    public int Hardness { get; set; } = 1;

    [SerializeField] SpriteRenderer crackRenderer;
    [SerializeField] Sprite lightCrackSprite;
    [SerializeField] Sprite heavyCrackSprite;

    static MaterialPropertyBlock colorBlock;

    int damage;

    // Tints the brick body per instance without duplicating the shared
    // URP Lit material ("_BaseColor" is its tint property).
    public void SetColor(Color color)
    {
        colorBlock ??= new MaterialPropertyBlock();
        colorBlock.SetColor("_BaseColor", color);
        GetComponent<MeshRenderer>().SetPropertyBlock(colorBlock);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        var ball = collision.collider.GetComponent<Ball>();
        if (ball == null) return;

        TakeDamage(ball.Damage);
    }

    void TakeDamage(int amount)
    {
        damage += amount;
        if (damage >= Hardness)
        {
            if (GameManager.Instance != null) GameManager.Instance.OnBrickDestroyed(this);
            Destroy(gameObject);
            return;
        }

        if (crackRenderer == null) return;
        float fraction = (float)damage / Hardness;
        crackRenderer.sprite = fraction <= 0.5f ? lightCrackSprite : heavyCrackSprite;
    }
}
