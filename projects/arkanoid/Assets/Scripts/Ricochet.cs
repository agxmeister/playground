using UnityEngine;

// The flash where the menu's ball strikes the edge of the screen. The borders
// themselves are invisible — they are the frame, not objects — so this burst is
// the only thing that says the ball hit something rather than turning around on
// its own.
//
// Like Debris it is cosmetic and runtime-only: plain meshes moved by hand in
// Update, with no physics components, so nothing here can disturb the 2D
// gameplay colliders. It is short and small where debris is slow and heavy — a
// ricochet is an instant, not a collapse.
public class Ricochet : MonoBehaviour
{
    // How wide the sparks fan out either side of the surface normal. A narrow
    // cone reads as a jet; this is wide enough to read as a scatter without any
    // spark travelling along the wall it just came off.
    const float Spread = 55f;

    // Sparks lose most of their speed over their short life, so the burst
    // blooms out of the impact and stops rather than drifting away from it.
    const float Drag = 4.5f;

    const int MinSparks = 8;
    const int MaxSparks = 12;

    // How far into the room the burst is set from the point the ball actually
    // touched. That point is on the very edge of the screen, so a burst centred
    // on it would spend half of itself outside the frame.
    const float Inset = 0.2f;

    static Mesh cubeMesh;
    static MaterialPropertyBlock colorBlock;

    Vector3 velocity;
    Vector3 spinAxis;
    float spinSpeed;
    Vector3 fromScale;
    Vector3 toScale;
    float life;
    float age;

    // `point` is where the ball touched, `normal` points back into the room off
    // the surface it touched, and `material` is the ball's own, so the burst is
    // lit by the same light as everything else on the screen.
    public static void Spawn(Vector3 point, Vector2 normal, Material material)
    {
        if (material == null) return;
        colorBlock ??= new MaterialPropertyBlock();

        var outward = new Vector3(normal.x, normal.y, 0f).normalized;
        if (outward.sqrMagnitude < 0.5f) outward = Vector3.up;
        var along = new Vector3(-outward.y, outward.x, 0f);
        var origin = point + outward * Inset;

        // The streak: a thin slab lying against the surface that stretches out
        // along it and thins to nothing, so the eye is caught by the line of
        // the impact before it reads the individual sparks.
        var flash = NewPiece(material, origin, Quaternion.LookRotation(Vector3.forward, outward));
        flash.fromScale = new Vector3(0.5f, 0.3f, 0.3f);
        flash.toScale = new Vector3(2.2f, 0.03f, 0.03f);
        flash.life = 0.18f;
        flash.transform.localScale = flash.fromScale;

        int count = Random.Range(MinSparks, MaxSparks + 1);
        for (int i = 0; i < count; i++)
        {
            var spark = NewPiece(material, origin, Random.rotation);
            float angle = Random.Range(-Spread, Spread) * Mathf.Deg2Rad;
            var direction = outward * Mathf.Cos(angle) + along * Mathf.Sin(angle);
            spark.velocity = direction * Random.Range(3.5f, 8f);
            spark.velocity.z = Random.Range(-0.6f, 0.6f);
            spark.spinAxis = Random.onUnitSphere;
            spark.spinSpeed = Random.Range(180f, 720f);
            float size = Random.Range(0.09f, 0.18f);
            spark.fromScale = new Vector3(size, size, size);
            spark.toScale = Vector3.zero;
            spark.life = Random.Range(0.18f, 0.34f);
            spark.transform.localScale = spark.fromScale;
        }
    }

    // Ricochet pieces are unparented roots, like debris, so they outlive the
    // screen that spawned them and have to be swept up when it changes.
    public static void ClearAll()
    {
        foreach (var piece in FindObjectsByType<Ricochet>(FindObjectsSortMode.None))
            Destroy(piece.gameObject);
    }

    static Ricochet NewPiece(Material material, Vector3 point, Quaternion rotation)
    {
        var go = new GameObject("Ricochet");
        go.transform.SetPositionAndRotation(point, rotation);
        go.AddComponent<MeshFilter>().sharedMesh = CubeMesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // A spark is hotter than whatever it came off, so the ball's material is
        // tinted up to a pale flame colour rather than used as it is.
        colorBlock.SetColor("_BaseColor",
            Color.Lerp(new Color(1f, 0.86f, 0.55f), Color.white, Random.value));
        renderer.SetPropertyBlock(colorBlock);

        return go.AddComponent<Ricochet>();
    }

    // The stock cube mesh isn't loadable by name at runtime, so it is lifted off
    // a throwaway primitive once and shared by every piece.
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
        if (age >= life)
        {
            Destroy(gameObject);
            return;
        }

        float t = age / life;
        transform.position += velocity * Time.deltaTime;
        velocity *= Mathf.Max(0f, 1f - Drag * Time.deltaTime);
        if (spinSpeed > 0f) transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.World);
        transform.localScale = Vector3.Lerp(fromScale, toScale, t);
    }
}
