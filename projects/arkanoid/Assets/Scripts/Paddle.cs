using UnityEngine;
using UnityEngine.InputSystem;

public class Paddle : MonoBehaviour
{
    [SerializeField] float speed = 10f;
    [SerializeField] float xLimit = 6.5f;

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        float direction = 0f;
        if (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed) direction -= 1f;
        if (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed) direction += 1f;

        // Only X moves. The Z is kept rather than zeroed because the menu's
        // paddle lives on the menu screen's plane, well in front of the
        // playfield's.
        float x = Mathf.Clamp(transform.position.x + direction * speed * Time.deltaTime, -xLimit, xLimit);
        transform.position = new Vector3(x, transform.position.y, transform.position.z);
    }
}
