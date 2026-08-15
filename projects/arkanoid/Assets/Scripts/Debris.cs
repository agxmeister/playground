using UnityEngine;

// Cosmetic rubble spawned when a brick shatters. Fragments are plain meshes
// moved by hand in Update — no physics components, so they can never disturb
// the 2D gameplay colliders.
public class Debris : MonoBehaviour
{
    const float Gravity = 18f;
    const float KillY = -8f;
    // Below this fraction of remaining life the fragment shrinks away, since
    // the opaque Lit material can't alpha-fade.
    const float ShrinkFraction = 0.35f;

    static Mesh cubeMesh;
    static MaterialPropertyBlock colorBlock;

    Vector3 velocity;
    Vector3 spinAxis;
    float spinSpeed;
    float life;
    float age;
    Vector3 baseScale;

    // `amount` scales the fragment count for casters bigger than a brick — a
    // menu slab four bricks wide would otherwise break into the same handful of
    // chunks and read as too sparse for its size.
    public static void Spawn(Vector3 origin, Vector3 brickSize, Color color, Material material, float amount = 1f)
    {
        colorBlock ??= new MaterialPropertyBlock();
        int count = Mathf.Max(1, Mathf.RoundToInt(Random.Range(6, 10) * amount));
        for (int i = 0; i < count; i++)
        {
            var fragment = new GameObject("Debris");
            fragment.AddComponent<MeshFilter>().sharedMesh = CubeMesh;
            var renderer = fragment.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            // Per-fragment brightness variation makes the pile read as broken
            // chunks rather than uniform confetti.
            float shade = Random.Range(0.7f, 1.05f);
            colorBlock.SetColor("_BaseColor", new Color(color.r * shade, color.g * shade, color.b * shade, color.a));
            renderer.SetPropertyBlock(colorBlock);

            var offset = new Vector3(
                Random.Range(-0.4f, 0.4f) * brickSize.x,
                Random.Range(-0.4f, 0.4f) * brickSize.y,
                0f);
            fragment.transform.position = origin + offset;
            fragment.transform.rotation = Random.rotation;

            float chunk = Mathf.Min(brickSize.x, brickSize.y);
            fragment.transform.localScale = new Vector3(
                Random.Range(0.15f, 0.35f),
                Random.Range(0.15f, 0.35f),
                Random.Range(0.15f, 0.35f)) * chunk;

            var debris = fragment.AddComponent<Debris>();
            debris.velocity = new Vector3(
                offset.x * Random.Range(2f, 5f),
                Random.Range(1f, 4f),
                Random.Range(-0.5f, 0.5f));
            debris.spinAxis = Random.onUnitSphere;
            debris.spinSpeed = Random.Range(90f, 480f);
            debris.life = Random.Range(1.2f, 2f);
            debris.baseScale = fragment.transform.localScale;
        }
    }

    // Sweeps away every fragment still in the air. Fragments are unparented
    // root objects that outlive whatever spawned them, so a screen change has
    // to clear them by hand or the last screen's rubble rains over the next
    // one. Called at transitions only, which is why the scene scan is fine.
    public static void ClearAll()
    {
        foreach (var fragment in FindObjectsByType<Debris>(FindObjectsSortMode.None))
            Destroy(fragment.gameObject);
    }

    // The stock cube mesh isn't loadable by name at runtime, so it is lifted
    // off a throwaway primitive once and shared by every fragment.
    static Mesh CubeMesh
    {
        get
        {
            if (cubeMesh == null)
            {
                var template = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cubeMesh = template.GetComponent<MeshFilter>().sharedMesh;
                Destroy(template);
            }
            return cubeMesh;
        }
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age >= life || transform.position.y < KillY)
        {
            Destroy(gameObject);
            return;
        }

        velocity.y -= Gravity * Time.deltaTime;
        transform.position += velocity * Time.deltaTime;
        transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.World);

        float remaining = (life - age) / life;
        if (remaining < ShrinkFraction) transform.localScale = baseScale * (remaining / ShrinkFraction);
    }
}
