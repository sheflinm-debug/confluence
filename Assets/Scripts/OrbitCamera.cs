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

    /// When true the camera co-rotates with the planet so one face stays in view.
    /// Toggle with L key. Set planetRotationDegPerSec from SimulationBootstrap.
    public bool PlanetLockEnabled { get; private set; }
    /// Degrees per second the life-planet rotates on Y. Set once after world gen.
    public float planetRotationDegPerSec;

    private float _yaw;
    private float _pitch = 20f;
    private bool _lKeyWasPressed;

    void Update()
    {
        if (target == null) return;

        var mouse = Mouse.current;
        var keyboard = Keyboard.current;

        // Toggle planet-lock on L key press (falling edge to avoid repeat-fires).
        if (keyboard != null)
        {
            bool lDown = keyboard.lKey.isPressed;
            if (lDown && !_lKeyWasPressed)
                PlanetLockEnabled = !PlanetLockEnabled;
            _lKeyWasPressed = lDown;
        }

        // Co-rotate with the planet: keep _yaw in sync with the planet's Y rotation
        // so the same face stays centered regardless of manual input.
        if (PlanetLockEnabled && planetRotationDegPerSec != 0f)
            _yaw += planetRotationDegPerSec * Time.deltaTime;

        if (mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            _yaw += delta.x * mouseRotateSpeed;
            _pitch -= delta.y * mouseRotateSpeed;
        }

        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            // Suppress zoom when the cursor is over a UI panel (HUD, pause menu, etc.)
            bool uiBlocking = GameHUD.IsScrollBlockedAtScreenPos(mouse.position.ReadValue())
                           || PauseMenuManager.IsOpen;
            if (scroll != 0f && !uiBlocking)
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

    void OnGUI()
    {
        if (GameHUD.SuppressRawOverlays) return;
        string label = PlanetLockEnabled ? "[L] Planet-lock: ON" : "[L] Planet-lock: OFF";
        Color c = PlanetLockEnabled ? new Color(0.4f, 1f, 0.5f) : new Color(0.7f, 0.7f, 0.7f);
        GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        style.normal.textColor = c;
        GUI.Label(new Rect(10f, Screen.height - 24f, 220f, 20f), label, style);
    }

    /// Immediately places the camera at its orbit position, bypassing smoothing.
    /// Call after assigning target/distance so the first rendered frame is correct.
    public void SnapToTarget()
    {
        Reposition();
    }

    /// Swing the camera to face the given world-space direction from the planet center
    /// and zoom in to zoomDistance. Used to auto-focus on the founding organism at spawn.
    public void FocusOnDirection(Vector3 worldDir, float zoomDistance = 15f)
    {
        Vector3 d = worldDir.normalized;
        _pitch = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
        _yaw   = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        distance = zoomDistance;
        Reposition();
    }

    public void EnablePlanetLock()  { PlanetLockEnabled = true;  }
    public void DisablePlanetLock() { PlanetLockEnabled = false; }

    private void Reposition()
    {
        if (target == null) return;
        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 pos = target.position + rot * new Vector3(0f, 0f, -distance);
        transform.position = pos;
        transform.LookAt(target.position);
    }
}
