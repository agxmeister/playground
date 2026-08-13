using UnityEngine;

public class Brick : MonoBehaviour
{
    public int Points { get; set; } = 100;
    public int Hardness { get; set; } = 1;

    [SerializeField] SpriteRenderer crackRenderer;
    [SerializeField] Sprite[] lightCrackSprites;
    [SerializeField] Sprite[] heavyCrackSprites;

    static MaterialPropertyBlock colorBlock;

    int damage;
    int crackVariant = -1;
    Color color = Color.white;

    // Tints the brick body per instance without duplicating the shared
    // URP Lit material ("_BaseColor" is its tint property).
    public void SetColor(Color color)
    {
        this.color = color;
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
            var renderer = GetComponent<MeshRenderer>();
            Debris.Spawn(transform.position, renderer.bounds.size, color, renderer.sharedMaterial);
            if (GameManager.Instance != null) GameManager.Instance.OnBrickDestroyed(this);
            Destroy(gameObject);
            return;
        }

        if (crackRenderer == null || lightCrackSprites == null || lightCrackSprites.Length == 0) return;

        // The variant (and its mirroring) is picked on the first hit and then
        // kept, so escalating damage reads as the same crack spreading.
        if (crackVariant < 0)
        {
            crackVariant = Random.Range(0, lightCrackSprites.Length);
            crackRenderer.flipX = Random.value < 0.5f;
            crackRenderer.flipY = Random.value < 0.5f;
        }
        float fraction = (float)damage / Hardness;
        crackRenderer.sprite = fraction <= 0.5f ? lightCrackSprites[crackVariant] : heavyCrackSprites[crackVariant];
    }
}
