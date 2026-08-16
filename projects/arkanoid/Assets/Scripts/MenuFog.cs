using UnityEngine;

// One bank of the fog a menu screen rises out of (see "A screen change happens
// in depth" in CLAUDE.md): a sheet of haze hanging in the shallow space between
// the playing plane and the backdrop, drifting slowly across it.
//
// The fog was a colour before this and nothing else — the backdrop's, mixed
// into a screen by how deep it stood. That is still what a sinking screen wears,
// and it is what makes one disappear into the murk; but a fog that holds
// perfectly still is a wall painted to look like fog. What moves is this: two of
// these banks hang at different depths, at different scales and drifting at
// different rates, and where they pass over each other the murk gathers and
// thins. Nothing here is a simulation — it is one tileable cloud texture whose
// UVs are walked across the sheet, and the fluidity is the interference between
// the two.
//
// Each bank owns its own copy of the shared material, since the scale and the
// offset are what tell the two apart.
public class MenuFog : MonoBehaviour
{
    // How many times the cloud texture repeats across this bank. The two banks
    // wear it at different sizes on purpose: at the same size they would drift
    // as one sheet with a shadow of itself.
    [SerializeField] Vector2 tiling = new Vector2(1.7f, 1.45f);
    // How far the sheet travels a second, in tiles. A hundredth of a tile is
    // a few centimetres of world: slow enough that it is never caught moving,
    // and only ever noticed by having moved.
    [SerializeField] Vector2 drift = new Vector2(0.01f, 0.004f);

    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

    Material haze;
    Vector2 offset;

    void Awake()
    {
        var sheet = GetComponent<Renderer>();
        if (sheet == null) return;
        // A copy of the material rather than the asset, so this bank's scale and
        // travel are its own — and destroyed with the bank, since a copy nobody
        // owns is a leak.
        haze = sheet.material;
        haze.SetTextureScale(BaseMapId, tiling);
    }

    void OnDestroy()
    {
        if (haze != null) Destroy(haze);
    }

    void Update()
    {
        if (haze == null) return;
        offset += drift * Time.deltaTime;
        // The texture repeats, so a whole tile of travel is no travel at all;
        // dropping it keeps the offset from growing until it loses its
        // precision, which after an afternoon on the menu it would.
        offset.x -= Mathf.Floor(offset.x);
        offset.y -= Mathf.Floor(offset.y);
        haze.SetTextureOffset(BaseMapId, offset);
    }
}
