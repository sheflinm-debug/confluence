using UnityEngine;
using UnityEngine.InputSystem;

/// Minimal orbit camera for viewing the planet during Tier 1 dev. Not final presentation.
/// Uses the new Input System (this project's Active Input Handling) rather than the
/// legacy UnityEngine.Input class, which is a no-op when the new system is exclusive.
public class OrbitCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 60f;
    public float rotateSpeed = 60f; // degrees/sec for keyboard orbit
    public float mouseRotateSpeed = 0.2f; // degrees per pixel of mouse delta
    public float scrollSpeed = 400f;
    public float keyZoomSpeed = 30f;

    private float _yaw;
    private float _pitch = 20f;

    void Update()
    {
        if (target == null) return;

        var mouse = Mouse.current;
        var keyboard = Keyboard.current;

        if (mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            _yaw += delta.x * mouseRotateSpeed;
            _pitch -= delta.y * mouseRotateSpeed;
        }

        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            distance -= scroll * scrollSpeed * Time.deltaTime;
        }

        if (keyboard != null)
        {
            if (keyboard.leftArrowKey.isPressed || keyboard.dKey.isPressed) _yaw -= rotateSpeed * Time.deltaTime;
            if (keyboard.rightArrowKey.isPressed || keyboard.aKey.isPressed) _yaw += rotateSpeed * Time.deltaTime;
            if (keyboard.upArrowKey.isPressed || keyboard.sKey.isPressed) _pitch -= rotateSpeed * Time.deltaTime;
            if (keyboard.downArrowKey.isPressed || keyboard.wKey.isPressed) _pitch += rotateSpeed * Time.deltaTime;
            if (keyboard.eKey.isPressed) distance -= keyZoomSpeed * Time.deltaTime;
            if (keyboard.qKey.isPressed) distance += keyZoomSpeed * Time.deltaTime;
        }

        _pitch = Mathf.Clamp(_pitch, -80f, 80f);
        distance = Mathf.Clamp(distance, 5f, 500f);

        Reposition();
    }

    /// Immediately places the camera at its orbit position, bypassing smoothing.
    /// Call after assigning target/distance so the first rendered frame is correct.
    public void SnapToTarget()
    {
        Reposition();
    }

    private void Reposition()
    {
        if (target == null) return;
        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 pos = target.position + rot * new Vector3(0f, 0f, -distance);
        transform.position = pos;
        transform.LookAt(target.position);
    }
}
