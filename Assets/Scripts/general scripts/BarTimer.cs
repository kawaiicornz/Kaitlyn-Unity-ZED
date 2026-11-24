using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Video;
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
    public VideoPlayer hourglassVideo; // optional: if set, the video is the countdown (the bar is not used)
    public Renderer hourglassRenderer; // optional: the hourglass plane's renderer (found automatically on the video's object if empty)

    bool waitingForHourglass;          // plane is hidden until the video shows its first frame
    float hourglassWait;
    Renderer hourglassRen;

    [Header("Timer")]
    public float duration = 10f;

    [Header("Result messages (objects on your UI Canvas, e.g. TextMeshPro)")]
    public GameObject hitMessage;     // shown when the ball hits the platform (scene object OR prefab/model asset)
    [FormerlySerializedAs("missMessage")]
    public GameObject tooLowMessage;  // miss on the larger-x side (short of the platform): "Ball released too low"
    public GameObject tooHighMessage; // miss on the smaller-x side (past the platform): "Ball released too high"
    public Transform messageLocation; // optional: where to place a message created from a prefab/model asset
    public float messageSeconds = 5f; // how long it stays up before the game resets

    [Header("Intro (before the game starts)")]
    public bool useIntro = true;               // needs Reset Zone below; the player steps into it to start the game
    public GameObject[] hiddenUntilStart;      // ramp, ball, platform: invisible until the game starts
    public Transform arrow;                    // arrow plane: sweeps along x until the game starts, then hides
    public float arrowDistance = 0.3f;         // how far it moves in -x from its starting position
    public float arrowCycleSeconds = 2f;       // time for one trip out and back

    bool inIntro;
    float introTimer;
    Vector3 arrowStartPos;

    [Header("Reset zone (replaces the automatic reset after the message)")]
    public Transform resetZone;            // the flat cube marking the square on the floor (leave empty to use the timed reset)
    public Transform player;               // the temporary sphere / tracked player (leave empty to use the ball's Target)
    public float zoneHoldSeconds = 1f;     // how long the player must stay in the zone before the round resets

    bool zoneArmed;                        // true once the player has been outside the zone since the message appeared
    float zoneTimer;

    [Header("Platform respawn (after a hit)")]
    public Transform platformRoot;         // optional: the object to move. Leave empty to move the ball's Platform collider object
    public float platformMinX = -2.42f;    // random range for the platform's center x
    public float platformMaxX = 1.8f;

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
        if (hourglassVideo != null) hourglassVideo.loopPointReached += OnVideoFinished;
    }

    void OnDisable()
    {
        if (ball != null)
        {
            ball.PlatformHit -= OnPlatformHit;
            ball.PlatformMissed -= OnPlatformMissed;
        }
        if (hourglassVideo != null) hourglassVideo.loopPointReached -= OnVideoFinished;
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

        HideHourglassUntilReady();

        if (useIntro && resetZone != null) EnterIntro();
        else ResetTimer();
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
        // Safety net: never keep the hourglass hidden for more than 2 seconds.
        if (waitingForHourglass)
        {
            hourglassWait += Time.unscaledDeltaTime;
            if (hourglassWait > 2f) ShowHourglass();
        }

        if (inIntro)
        {
            UpdateIntro();
            return;
        }

        if (allowSpaceReset && ResetPressed())
        {
            ResetTimer();
        }

        // A result is on screen: count down, then start a new round.
        if (resultShowing)
        {
            if (resetZone != null)
            {
                UpdateZoneReset();
            }
            else
            {
                resultTimer -= Time.deltaTime;
                if (resultTimer <= 0f) ResetTimer();
            }
            return;
        }

        if (!finished && hourglassVideo == null)
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

    // The player must step into the zone to start the next round. Standing in it when the result
    // appears doesn't count: they have to leave it first, then walk back in.
    void UpdateZoneReset()
    {
        if (ZoneHoldCompleted()) ResetTimer();
    }

    // True once the player has been outside the zone, then stepped in and stayed for Zone Hold Seconds.
    bool ZoneHoldCompleted()
    {
        Transform p = player != null ? player : (ball != null ? ball.target : null);
        if (p == null) return false;

        if (!IsInsideZone(p.position))
        {
            zoneArmed = true;
            zoneTimer = 0f;
            return false;
        }

        if (!zoneArmed) return false;

        zoneTimer += Time.deltaTime;
        return zoneTimer >= zoneHoldSeconds;
    }

    // ---------------------------------------------------------------- intro (before the game starts)

    void EnterIntro()
    {
        inIntro = true;
        introTimer = 0f;
        zoneArmed = false;
        zoneTimer = 0f;
        elapsed = 0f;
        finished = false;
        resultShowing = false;

        HideMessages();
        if (ball != null) ball.ResetBall();
        SetVisible(hiddenUntilStart, false);
        ApplyBar();

        if (arrow != null)
        {
            arrowStartPos = arrow.position;
            arrow.gameObject.SetActive(true);
        }

        // Hourglass: wait on its first frame until the game starts.
        if (hourglassVideo != null)
        {
            hourglassVideo.isLooping = false;
            if (hourglassVideo.isPrepared)
            {
                ShowFirstFrame(hourglassVideo);
            }
            else
            {
                hourglassVideo.prepareCompleted -= OnIntroVideoPrepared;
                hourglassVideo.prepareCompleted += OnIntroVideoPrepared;
                hourglassVideo.Prepare();
            }
        }
    }

    void UpdateIntro()
    {
        // Arrow sweeps from its start position to start - distance and back, smoothly.
        if (arrow != null && arrowCycleSeconds > 0f)
        {
            introTimer += Time.deltaTime;
            float k = 0.5f - 0.5f * Mathf.Cos(introTimer * 2f * Mathf.PI / arrowCycleSeconds); // 0..1
            Vector3 p = arrowStartPos;
            p.x -= arrowDistance * k;
            arrow.position = p;
        }

        if (allowSpaceReset && ResetPressed())
        {
            BeginGame();
            return;
        }

        if (ZoneHoldCompleted()) BeginGame();
    }

    void BeginGame()
    {
        inIntro = false;
        SetVisible(hiddenUntilStart, true);

        if (arrow != null)
        {
            arrow.position = arrowStartPos;
            arrow.gameObject.SetActive(false);
        }

        ResetTimer();   // starts the first round: ball follows the target, hourglass plays
    }

    // Hides or shows objects by switching their renderers, so their scripts keep running.
    void SetVisible(GameObject[] objects, bool visible)
    {
        if (objects == null) return;
        foreach (GameObject o in objects)
        {
            if (o == null) continue;
            foreach (Renderer r in o.GetComponentsInChildren<Renderer>(true))
                r.enabled = visible;
        }
    }

    void OnIntroVideoPrepared(VideoPlayer vp)
    {
        vp.prepareCompleted -= OnIntroVideoPrepared;
        if (inIntro) ShowFirstFrame(vp);
    }

    // The plane shows a plain white surface until the video has a picture, so keep it invisible
    // until the first frame is ready, then switch it on.
    void HideHourglassUntilReady()
    {
        if (hourglassVideo == null) return;

        hourglassRen = hourglassRenderer != null ? hourglassRenderer : hourglassVideo.GetComponent<Renderer>();
        if (hourglassRen == null) return;

        hourglassRen.enabled = false;
        waitingForHourglass = true;
        hourglassWait = 0f;

        hourglassVideo.sendFrameReadyEvents = true;
        hourglassVideo.frameReady -= OnHourglassFrameReady;
        hourglassVideo.frameReady += OnHourglassFrameReady;
    }

    void OnHourglassFrameReady(VideoPlayer vp, long frameIdx)
    {
        ShowHourglass();
    }

    void ShowHourglass()
    {
        waitingForHourglass = false;
        if (hourglassRen != null) hourglassRen.enabled = true;
        if (hourglassVideo != null)
        {
            hourglassVideo.frameReady -= OnHourglassFrameReady;
            hourglassVideo.sendFrameReadyEvents = false;
        }
    }

    // Plays and immediately pauses so the first frame is displayed.
    void ShowFirstFrame(VideoPlayer vp)
    {
        vp.time = 0;
        vp.Play();
        vp.Pause();
    }

    // Checks x and z only (the zone is a square on the floor; height is ignored).
    // Uses the cube's position and scale, so it assumes the cube isn't rotated.
    bool IsInsideZone(Vector3 worldPos)
    {
        Vector3 c = resetZone.position;
        Vector3 half = resetZone.lossyScale * 0.5f;
        return Mathf.Abs(worldPos.x - c.x) <= half.x &&
               Mathf.Abs(worldPos.z - c.z) <= half.z;
    }

    void OnVideoPrepared(VideoPlayer vp)
    {
        vp.prepareCompleted -= OnVideoPrepared;
        if (!finished && !resultShowing) vp.Play();
    }

    // The video reached its end: the countdown is over, launch the ball.
    void OnVideoFinished(VideoPlayer vp)
    {
        if (inIntro || finished || resultShowing) return;
        finished = true;
        if (ball != null) ball.Launch();
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
        zoneArmed = false;
        zoneTimer = 0f;
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

        // Start the hourglass video from the beginning for the new round.
        if (hourglassVideo != null)
        {
            hourglassVideo.isLooping = false;

            // Don't call Stop(): it clears the picture and unloads the video, leaving the plane
            // blank until the video is prepared again. Rewind instead, so a frame stays visible.
            if (hourglassVideo.isPrepared)
            {
                hourglassVideo.time = 0;
                hourglassVideo.Play();
            }
            else
            {
                // First round: load the video first, then play it once it is ready.
                hourglassVideo.prepareCompleted -= OnVideoPrepared;
                hourglassVideo.prepareCompleted += OnVideoPrepared;
                hourglassVideo.Prepare();
            }
        }
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
        if (hourglassVideo != null) return;   // the video replaces the bar
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