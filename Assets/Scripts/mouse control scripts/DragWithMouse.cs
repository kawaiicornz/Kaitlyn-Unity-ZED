using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

// Click and drag this object with the mouse (for testing).
// Default: slides on the ground (left/right + forward/back).
// Hold Shift: slides on a plane facing the camera (left/right + up/down).
public class DragWithMouse : MonoBehaviour
{
    // The camera used to turn mouse clicks into 3D positions.
    // Leave empty to use the camera tagged "MainCamera".
    public Camera cam;

    private bool isDragging;
    private bool wasUsingCameraPlane;
    private Vector3 grabOffset;

    void Awake()
    {
        if (cam == null)
            cam = Camera.main;

        if (cam == null)
            Debug.LogWarning(name + " needs a Camera (tag one as MainCamera or assign Cam).", this);

        if (GetComponent<Collider>() == null)
            Debug.LogWarning(name + " needs a Collider so it can be clicked.", this);
    }

    void Update()
    {
        if (cam == null)
            return;

        // Start dragging when the mouse is pressed on this object.
        if (PressedThisFrame())
        {
            Ray clickRay = cam.ScreenPointToRay(MousePosition());

            if (Physics.Raycast(clickRay, out RaycastHit hit) &&
                hit.collider.transform.IsChildOf(transform))
            {
                isDragging = true;
                wasUsingCameraPlane = ShiftHeld();
                UpdateGrabOffset();
            }
        }

        // Stop dragging when the mouse is released.
        if (!IsHeld())
            isDragging = false;

        if (!isDragging)
            return;

        // If Shift was pressed or released mid-drag, switch planes smoothly.
        bool useCameraPlane = ShiftHeld();
        if (useCameraPlane != wasUsingCameraPlane)
        {
            wasUsingCameraPlane = useCameraPlane;
            UpdateGrabOffset();
        }

        if (TryGetMouseOnPlane(out Vector3 pointOnPlane))
            transform.position = pointOnPlane + grabOffset;
    }

    // Remember where on the object we grabbed it, so it doesn't jump.
    void UpdateGrabOffset()
    {
        if (TryGetMouseOnPlane(out Vector3 pointOnPlane))
            grabOffset = transform.position - pointOnPlane;
        else
            grabOffset = Vector3.zero;
    }

    // Finds where the mouse ray hits the current drag plane.
    bool TryGetMouseOnPlane(out Vector3 point)
    {
        Plane dragPlane;

        if (wasUsingCameraPlane)
            dragPlane = new Plane(-cam.transform.forward, transform.position); // faces the camera
        else
            dragPlane = new Plane(Vector3.up, transform.position);             // flat ground

        Ray ray = cam.ScreenPointToRay(MousePosition());

        if (dragPlane.Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }

        point = Vector3.zero;
        return false;
    }

    // ---- Input helpers: work with the old or new Unity input system ----
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    Vector2 MousePosition() =>
        Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

    bool PressedThisFrame() =>
        Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

    bool IsHeld() =>
        Mouse.current != null && Mouse.current.leftButton.isPressed;

    bool ShiftHeld() =>
        Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
#else
    Vector2 MousePosition() => Input.mousePosition;

    bool PressedThisFrame() => Input.GetMouseButtonDown(0);

    bool IsHeld() => Input.GetMouseButton(0);

    bool ShiftHeld() =>
        Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
}