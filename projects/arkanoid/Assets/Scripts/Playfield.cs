using UnityEngine;

// The round's room. Like the menu's, its field is the frame itself: there are no
// walls standing in the picture any more, only three invisible `Border`s laid
// along the left, right and top edges of what the camera actually sees, so the
// ball ricochets off the edge of the screen in a burst of sparks. The game is
// the whole width of the window rather than a bordered box in the middle of it,
// which is what the menu screens have always been.
//
// Nothing here can be authored, because a frame is only known once there is a
// window and it changes when the window does: the borders, the backdrop's size
// and how far the paddle may travel are all fitted at runtime, and re-fitted
// whenever the window is resized. GameManager reads the fitted extents back to
// lay out the brick grid, so the level fills the frame it is dealt too.
//
// The component sits on the room's root, which GameManager switches off for the
// whole menu — 2D physics ignores Z, so borders left standing would fence in the
// menu's ball even though the rooms are apart in X. Being switched off is also
// why fitting happens in OnEnable: coming back for a round is what re-fits it.
public class Playfield : MonoBehaviour
{
    // The backdrop is scenery rather than part of the room, so it is referenced
    // rather than parented: it stays lit and standing while the room's colliders
    // are switched off for the menu.
    [SerializeField] Transform backdrop;
    // The banks of haze drifting between the plane and that backdrop — the same
    // weather the menu's room has, because a round is a continuation of the menu
    // screens rather than a different place. Scenery like the backdrop, and
    // sized like it: each covers the frame at its own depth, so its edges are
    // never seen whatever shape the window takes.
    [SerializeField] Transform[] fogBanks;
    [SerializeField] Paddle paddle;

    // How far past the frame's edges the backdrop is drawn, so its own edge is
    // never seen creeping into a corner as the window is resized.
    const float BackdropOverhang = 0.6f;

    // Half the width and height of the frame on the room's own plane. The brick
    // grid is laid out against these, so it fills the window it is dealt.
    public float HalfWidth { get; private set; }
    public float HalfHeight { get; private set; }

    // The frame these were last fitted to, so a window resized mid-round doesn't
    // leave the borders where the old one wanted them.
    Vector2Int fittedTo;

    void OnEnable() => FitToFrame();

    void Update()
    {
        if (fittedTo.x != Screen.width || fittedTo.y != Screen.height) FitToFrame();
    }

    void FitToFrame()
    {
        var camera = Camera.main;
        if (camera == null) return;
        fittedTo = new Vector2Int(Screen.width, Screen.height);

        float planeZ = transform.position.z;
        var extents = Border.FrameExtents(camera, planeZ);
        HalfWidth = extents.x;
        HalfHeight = extents.y;

        // Centred on the room rather than on the camera: the view starts a round
        // by travelling here from the menu, and walls that followed it would
        // take the room's edges with them on the way.
        Border.Fit(transform, new Vector2(transform.position.x, camera.transform.position.y),
            extents, planeZ);

        // The backdrop stands further back than the room does, so it has its own
        // frame to cover — a plane sized for the gameplay plane would leave the
        // camera's clear colour showing along the edges, which is exactly the
        // border this screen is meant to have lost.
        if (backdrop != null)
        {
            var behind = Border.FrameExtents(camera, backdrop.position.z);
            backdrop.localScale = new Vector3(
                2f * behind.x + BackdropOverhang,
                2f * behind.y + BackdropOverhang,
                backdrop.localScale.z);
        }

        // The haze hangs at its own depths too, and a bank whose edge crept
        // into a corner would give the weather away as a sheet.
        if (fogBanks != null)
        {
            foreach (var bank in fogBanks)
            {
                if (bank == null) continue;
                var inFog = Border.FrameExtents(camera, bank.position.z);
                bank.localScale = new Vector3(
                    2f * inFog.x + BackdropOverhang,
                    2f * inFog.y + BackdropOverhang, 1f);
            }
        }

        if (paddle != null) paddle.FitTo(extents.x);
    }
}
