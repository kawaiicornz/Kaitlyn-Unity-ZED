using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Put this on your person. Walk with the keyboard during Play mode:
///   W / Up arrow     walk forward
///   S / Down arrow   walk backward
///   A, D / arrows    turn left and right
///   Hold Shift       walk faster
/// Uses a CharacterController (added for you), so the person bumps into things
/// that have colliders and stays on the floor.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class SimplePlayerMove : MonoBehaviour
{
    [Tooltip("Meters per second. 1.4 is a normal walking pace.")]
    public float walkSpeed = 1.4f;
    [Tooltip("Meters per second while holding Shift.")]
    public float runSpeed = 3f;
    [Tooltip("Degrees per second.")]
    public float turnSpeed = 120f;
    public float gravity = -9.81f;

    CharacterController cc;
    float fallSpeed;

    // Runs when you first add this component: sizes the capsule for an adult
    // whose origin point is at their feet.
    void Reset()
    {
        CharacterController c = GetComponent<CharacterController>();
        if (!c) return;
        c.height = 1.75f;
        c.radius = 0.25f;
        c.center = new Vector3(0f, 0.875f, 0f);
        c.stepOffset = 0.3f;
    }

    void Awake()
    {
        cc = GetComponent<CharacterController>();
    }

    void Update()
    {
        ReadKeys(out float forward, out float turn, out bool run);

        transform.Rotate(0f, turn * turnSpeed * Time.deltaTime, 0f);

        if (cc.isGrounded && fallSpeed < 0f) fallSpeed = -1f; // keep pressed onto the floor
        fallSpeed += gravity * Time.deltaTime;

        Vector3 move = transform.forward * (forward * (run ? runSpeed : walkSpeed));
        move.y = fallSpeed;
        cc.Move(move * Time.deltaTime);
    }

    void ReadKeys(out float forward, out float turn, out bool run)
    {
        forward = 0f;
        turn = 0f;
        run = false;

#if ENABLE_INPUT_SYSTEM
        // New Input System (the default in Unity 6)
        Keyboard k = Keyboard.current;
        if (k == null) return;
        if (k.wKey.isPressed || k.upArrowKey.isPressed) forward += 1f;
        if (k.sKey.isPressed || k.downArrowKey.isPressed) forward -= 1f;
        if (k.dKey.isPressed || k.rightArrowKey.isPressed) turn += 1f;
        if (k.aKey.isPressed || k.leftArrowKey.isPressed) turn -= 1f;
        run = k.leftShiftKey.isPressed || k.rightShiftKey.isPressed;
#else
        // Old Input Manager
        forward = Input.GetAxisRaw("Vertical");
        turn = Input.GetAxisRaw("Horizontal");
        run = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
    }
}
