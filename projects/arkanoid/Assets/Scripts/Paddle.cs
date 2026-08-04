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

        float x = Mathf.Clamp(transform.position.x + direction * speed * Time.deltaTime, -xLimit, xLimit);
        transform.position = new Vector3(x, transform.position.y, 0f);
    }
}
