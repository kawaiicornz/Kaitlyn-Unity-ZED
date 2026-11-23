using UnityEngine;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Runs one round of the game:
///  - A 10-second countdown shown as a bar that shrinks along X (larger-x edge fixed) and fades green -> red.
///  - When it ends, the ball is launched.
///  - When the ball lands (on the platform = hit, on the floor = miss) a result message appears
///    for a few seconds, then the whole round resets automatically.
/// Play mode only.
/// </summary>
public class BarTimer : MonoBehaviour
{
    [Header("References")]
    public RampBallController ball;   // drag the ball (with RampBallController) here

    [Header("Timer")]
    public float duration = 10f;

    [Header("Result messages (objects on your UI Canvas, e.g. TextMeshPro)")]
    public GameObject hitMessage;     // shown when the ball hits the platform (scene object OR prefab/model asset)
    [FormerlySerializedAs("missMessage")]
    public GameObject tooLowMessage;  // miss on the larger-x side (short of the platform): "Ball released too low"
    public GameObject tooHighMessage; // miss on the smaller-x side (past the platform): "Ball released too high"
    public Transform messageLocation; // optional: where to place a message created from a prefab/model asset
    public float messageSeconds = 5f; // how long it stays up before the game resets

    [Header("Platform respawn (after a hit)")]
    public Transform platformRoot;         // optional: the object to move. Leave empty to move the ball's Platform collider object
    public float platformMinX = -1.19f;    // random range for the platform's center x
    public float platformMaxX = 1.14f;

    [Header("Testing")]
    public bool allowSpaceReset = false;   // tick to also reset with Space while testing

    [Header("Colors")]
    public Color fullColor = Color.green;   // bar color when the timer is full
    public Color emptyColor = Color.red;    // bar color when the timer is nearly out

    Renderer barRenderer;
    float elapsed;
    bool finished;

    bool resultShowing;
    float resultTimer;
    bool movePlatformOnReset;

    Vector3 startLocalScale;
    Vector3 startLocalPosition;
    Vector3 localRight;               // the bar's local X direction in its parent's space
    Vector3 fixedEdge;                // the larger-x edge, which never moves

    void OnEnable()
    {
        if (ball != null)
        {
            ball.PlatformHit += OnPlatformHit;
            ball.PlatformMissed += OnPlatformMissed;
        }
    }

    void OnDisable()
    {
        if (ball != null)
        {
            ball.PlatformHit -= OnPlatformHit;
            ball.PlatformMissed -= OnPlatformMissed;
        }
    }

    void Start()
    {
        barRenderer = GetComponent<Renderer>();
        startLocalScale = transform.localScale;
        startLocalPosition = transform.localPosition;
        localRight = transform.localRotation * Vector3.right;
        fixedEdge = startLocalPosition + localRight * (startLocalScale.x * 0.5f);

        hitMessage = ResolveMessage(hitMessage);
        tooLowMessage = ResolveMessage(tooLowMessage);
        tooHighMessage = ResolveMessage(tooHighMessage);

        ResetTimer();
    }

    // If a model/prefab ASSET from the Project window was assigned (not an object already in the
    // scene), make a hidden copy in the scene to show and hide. Scene objects are used as they are.
    GameObject ResolveMessage(GameObject message)
    {
        if (message == null) return null;
        if (message.scene.IsValid()) return message;   // already in the scene

        GameObject copy = messageLocation != null
            ? Instantiate(message, messageLocation.position, messageLocation.rotation)
            : Instantiate(message);
        copy.SetActive(false);
        return copy;
    }

    void Update()
    {
        if (allowSpaceReset && ResetPressed())
        {
            ResetTimer();
        }

        // A result is on screen: count down, then start a new round.
        if (resultShowing)
        {
            resultTimer -= Time.deltaTime;
            if (resultTimer <= 0f) ResetTimer();
            return;
        }

        if (!finished)
        {
            elapsed += Time.deltaTime;
            if (elapsed >= duration)
            {
                elapsed = duration;
                finished = true;
                if (ball != null) ball.Launch();
            }
            ApplyBar();
        }
    }

    void OnPlatformHit()
    {
        movePlatformOnReset = true;   // the platform moves when the round resets, not while the ball sits on it
        ShowResult(hitMessage);
    }
    void OnPlatformMissed(bool releasedTooLow)
    {
        ShowResult(releasedTooLow ? tooLowMessage : tooHighMessage);
    }

    void ShowResult(GameObject message)
    {
        HideMessages();
        if (message != null) message.SetActive(true);
        resultShowing = true;
        resultTimer = messageSeconds;
    }

    void HideMessages()
    {
        if (hitMessage != null) hitMessage.SetActive(false);
        if (tooLowMessage != null) tooLowMessage.SetActive(false);
        if (tooHighMessage != null) tooHighMessage.SetActive(false);
    }

    void ResetTimer()
    {
        elapsed = 0f;
        finished = false;
        resultShowing = false;
        HideMessages();

        if (movePlatformOnReset)
        {
            MovePlatformToRandomX();
            movePlatformOnReset = false;
        }

        if (ball != null) ball.ResetBall();
        ApplyBar();
    }

    // Moves the platform so its CENTER x is a random value in [platformMinX, platformMaxX].
    // Only x changes; y and z stay as they are.
    void MovePlatformToRandomX()
    {
        Transform root = platformRoot;
        if (root == null && ball != null && ball.platform != null) root = ball.platform.transform;
        if (root == null) return;

        float currentCenterX = (ball != null && ball.platform != null)
            ? ball.platform.bounds.center.x
            : root.position.x;

        float newCenterX = Random.Range(platformMinX, platformMaxX);
        root.position += new Vector3(newCenterX - currentCenterX, 0f, 0f);
    }

    void ApplyBar()
    {
        float remaining = 1f - Mathf.Clamp01(elapsed / duration);

        Vector3 scale = startLocalScale;
        scale.x = startLocalScale.x * remaining;
        transform.localScale = scale;

        // Keep the larger-x edge fixed by moving the center as the bar shrinks.
        transform.localPosition = fixedEdge - localRight * (scale.x * 0.5f);

        // Color: blend in HSV so green -> yellow -> red passes through bright
        // yellow instead of a muddy brown.
        if (barRenderer != null)
        {
            Color.RGBToHSV(fullColor, out float h1, out float s1, out float v1);
            Color.RGBToHSV(emptyColor, out float h2, out float s2, out float v2);
            float k = 1f - remaining; // 0 = full, 1 = empty
            barRenderer.material.color = Color.HSVToRGB(
                Mathf.Lerp(h1, h2, k),
                Mathf.Lerp(s1, s2, k),
                Mathf.Lerp(v1, v2, k));
        }
    }

    bool ResetPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }
}