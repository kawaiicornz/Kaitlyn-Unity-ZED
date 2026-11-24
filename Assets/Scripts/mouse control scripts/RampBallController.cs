using UnityEngine;

/// <summary>
/// Ball behaviour in two modes:
///  1) Following: the ball sits on the ramp at a position set by the target's Z (Edit + Play mode).
///  2) Launched (Play mode only): the ball rolls down the ramp from where it is, accelerating under
///     gravity from rest, crosses a flat ledge at constant speed, leaves the ledge edge horizontally
///     as a projectile, then lands on the platform (rolls with friction) or on the floor (the
///     platform's bottom level).
/// BarTimer calls Launch() when the countdown ends and ResetBall() when it restarts.
/// </summary>
[ExecuteAlways]
public class RampBallController : MonoBehaviour
{
    [Header("Target (drag the temporary object here)")]
    public Transform target;

    [Header("Target Z range")]
    public float zAtTopOfRamp = -4f;       // target z when ball is at the top
    public float zAtBottomOfRamp = -1.1f;  // target z when ball is at the bottom

    [Header("Ramp endpoints (world space, x/y only)")]
    public Vector2 rampTop = new Vector2(2.694f, 1.639f);
    public Vector2 rampBottom = new Vector2(2.1911f, 1.1356f);

    [Header("Ledge")]
    public float ledgeEdgeX = 2.1201f;     // ball leaves horizontally from here (y = rampBottom.y)

    [Header("Platform / target bar (drag the platform here)")]
    public Collider platform;              // its bounds are read at launch, so it can move each round

    [Header("Physics (tunable)")]
    public float gravity = 9.81f;          // used for the ramp and the projectile
    public float friction = 10f;           // deceleration (units/s^2) when rolling on platform or floor
    [Range(0f, 1f)]
    public float edgeBounce = 0.3f;        // how much speed is kept when the ball rolls back from a platform edge (0 = stops dead)

    [Header("Hit feedback")]
    public Color hitColor = Color.green;   // ball turns this color when it lands on the platform
    public Color missColor = Color.red;    // ball turns this color when it lands on the floor without hitting the platform

    [Header("Flight path line")]
    public Color pathColor = Color.white;  // color of the line tracing the ball's flight
    public float pathWidth = 0.02f;        // line thickness in world units
    public float dashLength = 0.05f;       // length of each dash in world units
    public float gapLength = 0.05f;        // length of the gap between dashes
    public Material pathMaterial;          // optional: leave empty for a plain unlit dashed line

    [Header("Rolling")]
    public float radius = 0.1f;            // cylinder X scale 0.2 -> radius 0.1
    public bool invertRoll = false;        // tick if the ball spins the wrong way
    public Vector3 baseEulerRotation = new Vector3(90f, 0f, 0f);

    /// <summary>Raised the moment the ball lands on top of the platform.</summary>
    public event System.Action PlatformHit;
    /// <summary>
    /// Raised the moment the ball lands on the floor without having hit the platform.
    /// The bool is true when the ball ended up on the larger-x side (short of the platform:
    /// released too low) and false when it ended up on the smaller-x side (past it: too high).
    /// </summary>
    public event System.Action<bool> PlatformMissed;

    enum State { Following, Ramp, Ledge, Airborne, PlatformRoll, FloorRoll, Done }
    State state = State.Following;

    Vector2 pos;          // ball center (x, y)
    Vector2 vel;          // used while airborne / rolling on flat surfaces
    float s;              // distance travelled along the ramp from the top
    float rampSpeed;      // speed along the ramp / ledge
    float rollRad;        // accumulated roll angle (positive when moving toward -x)

    Renderer ballRenderer;
    Color originalColor;
    bool colorCaptured;
    bool hitPlatform;     // true once the ball has landed on top of the platform this round
    LineRenderer path;    // white line drawn along the ball's flight (created in Play mode)

    // Platform snapshot taken at launch
    float platMinX, platMaxX, platTop, floorY;

    // ---------------------------------------------------------------- public API

    /// <summary>Releases the ball from wherever it currently is on the ramp.</summary>
    public void Launch()
    {
        if (state != State.Following) return;

        if (platform == null)
        {
            Debug.LogWarning("RampBallController: assign the Platform collider before launching.");
            state = State.Done;
            return;
        }

        Bounds b = platform.bounds;
        platMinX = b.min.x;
        platMaxX = b.max.x;
        platTop = b.max.y;
        floorY = b.min.y;

        pos = new Vector2(transform.position.x, transform.position.y);
        float rampLength = Vector2.Distance(rampTop, rampBottom);
        s = Mathf.Clamp(Vector2.Distance(rampTop, pos), 0f, rampLength);
        rampSpeed = 0f;
        vel = Vector2.zero;
        hitPlatform = false;
        state = State.Ramp;
    }

    /// <summary>Puts the ball back under the control of the target.</summary>
    public void ResetBall()
    {
        state = State.Following;
        ClearPath();
        CaptureColor();
        if (ballRenderer != null) ballRenderer.material.color = originalColor;
    }

    // ---------------------------------------------------------------- flight path line

    void EnsurePath()
    {
        if (path != null) return;

        GameObject go = new GameObject("BallPath");
        path = go.AddComponent<LineRenderer>();
        path.useWorldSpace = true;
        path.alignment = LineAlignment.View;
        path.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        path.receiveShadows = false;
        path.numCapVertices = 4;
        path.numCornerVertices = 4;
        path.widthMultiplier = pathWidth;
        path.textureMode = LineTextureMode.Tile;

        if (pathMaterial != null)
        {
            path.material = pathMaterial;
        }
        else
        {
            // Dashes come from a tiny generated texture: opaque for the dash, transparent for the gap.
            const int texWidth = 64;
            float dash = Mathf.Max(0.001f, dashLength);
            float gap = Mathf.Max(0f, gapLength);
            int dashPixels = Mathf.Clamp(Mathf.RoundToInt(texWidth * dash / (dash + gap)), 1, texWidth);

            Texture2D tex = new Texture2D(texWidth, 1, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Point;
            for (int i = 0; i < texWidth; i++)
                tex.SetPixel(i, 0, i < dashPixels ? Color.white : new Color(1f, 1f, 1f, 0f));
            tex.Apply();

            Material mat = new Material(Shader.Find("Sprites/Default"));
            mat.mainTexture = tex;
            mat.mainTextureScale = new Vector2(1f / (dash + gap), 1f);   // one dash + gap per this many world units
            path.material = mat;
        }
        path.startColor = pathColor;
        path.endColor = pathColor;
        path.positionCount = 0;
    }

    // Begins a fresh line at the point where the ball leaves the ledge.
    void StartPath()
    {
        if (!Application.isPlaying) return;
        EnsurePath();
        path.positionCount = 0;
        AddPathPoint();
    }

    void AddPathPoint()
    {
        if (path == null) return;
        int n = path.positionCount;
        path.positionCount = n + 1;
        path.SetPosition(n, new Vector3(pos.x, pos.y, transform.position.z));
    }

    void ClearPath()
    {
        if (path != null) path.positionCount = 0;
    }

    void OnDestroy()
    {
        if (path != null) Destroy(path.gameObject);
    }

    void RegisterMiss()
    {
        CaptureColor();
        if (ballRenderer != null) ballRenderer.material.color = missColor;
        bool releasedTooLow = pos.x >= (platMinX + platMaxX) * 0.5f;
        PlatformMissed?.Invoke(releasedTooLow);
    }

    // Remembers the ball's starting color the first time it is needed (Play mode only).
    void CaptureColor()
    {
        if (!Application.isPlaying || colorCaptured) return;
        ballRenderer = GetComponent<Renderer>();
        if (ballRenderer != null)
        {
            originalColor = ballRenderer.material.color;
            colorCaptured = true;
        }
    }

    // ---------------------------------------------------------------- update

    void Update()
    {
        if (state == State.Following)
        {
            if (target == null) return;
            Follow();
        }
        else if (Application.isPlaying)
        {
            Simulate(Time.deltaTime);
        }
        else
        {
            state = State.Following;
            return;
        }

        float angleDeg = rollRad * Mathf.Rad2Deg;
        if (invertRoll) angleDeg = -angleDeg;
        transform.rotation = Quaternion.Euler(0f, 0f, angleDeg) * Quaternion.Euler(baseEulerRotation);
    }

    void Follow()
    {
        // 0 = top of ramp, 1 = bottom. InverseLerp clamps at the ends.
        float t = Mathf.InverseLerp(zAtTopOfRamp, zAtBottomOfRamp, target.position.z);
        Vector2 xy = Vector2.Lerp(rampTop, rampBottom, t);
        Vector3 p = transform.position;
        p.x = xy.x;
        p.y = xy.y;
        transform.position = p;

        rollRad = t * Vector2.Distance(rampTop, rampBottom) / radius;
    }

    void Simulate(float dt)
    {
        switch (state)
        {
            case State.Ramp: StepRamp(dt); break;
            case State.Ledge: StepLedge(dt); break;
            case State.Airborne: StepAirborne(dt); break;
            case State.PlatformRoll:
            case State.FloorRoll: StepRoll(dt); break;
            case State.Done: break;
        }

        Vector3 p = transform.position;
        p.x = pos.x;
        p.y = pos.y;
        transform.position = p;
    }

    // ---------------------------------------------------------------- phases

    void StepRamp(float dt)
    {
        float rampLength = Vector2.Distance(rampTop, rampBottom);
        Vector2 dir = (rampBottom - rampTop).normalized;
        float accel = gravity * (-dir.y);            // g * sin(ramp angle)

        float remaining = rampLength - s;
        float step = rampSpeed * dt + 0.5f * accel * dt * dt;

        if (step >= remaining)
        {
            // Reaches the bottom this frame: exact speed from v^2 = v0^2 + 2 a d
            rampSpeed = Mathf.Sqrt(rampSpeed * rampSpeed + 2f * accel * remaining);
            rollRad += remaining / radius;
            s = rampLength;
            state = State.Ledge;
        }
        else
        {
            rampSpeed += accel * dt;
            s += step;
            rollRad += step / radius;
        }

        pos = rampTop + dir * s;
    }

    void StepLedge(float dt)
    {
        // Flat ledge, no friction: constant speed toward -x.
        if (rampSpeed <= 0f)
        {
            // Released with no speed (e.g. exactly at the bottom): it never leaves the ledge.
            state = State.Done;
            RegisterMiss();
            return;
        }

        float dist = rampSpeed * dt;
        pos.y = rampBottom.y;
        pos.x -= dist;
        rollRad += dist / radius;

        if (pos.x <= ledgeEdgeX)
        {
            pos.x = ledgeEdgeX;
            vel = new Vector2(-rampSpeed, 0f);
            state = State.Airborne;
            StartPath();
        }
    }

    void StepAirborne(float dt)
    {
        Vector2 prev = pos;
        vel.y -= gravity * dt;
        pos += vel * dt;
        rollRad += (-vel.x / radius) * dt;           // keeps spinning in the air

        // 1) Landing on top of the platform (ball center must be over the platform).
        float prevBottom = prev.y - radius;
        float newBottom = pos.y - radius;
        if (vel.y < 0f && prevBottom >= platTop && newBottom <= platTop)
        {
            float f = Mathf.InverseLerp(prevBottom, newBottom, platTop);
            float xAt = Mathf.Lerp(prev.x, pos.x, f);
            if (xAt >= platMinX && xAt <= platMaxX)
            {
                pos = new Vector2(xAt, platTop + radius);
                vel.y = 0f;
                state = State.PlatformRoll;
                AddPathPoint();
                hitPlatform = true;
                CaptureColor();
                if (ballRenderer != null) ballRenderer.material.color = hitColor;
                PlatformHit?.Invoke();
                return;
            }
        }

        // 2) Hitting the platform's corners or sides (ball slides off / down).
        PushOutOfPlatform();

        // 3) Reaching the floor (platform bottom level).
        if (pos.y - radius <= floorY)
        {
            pos.y = floorY + radius;
            vel.y = 0f;
            state = State.FloorRoll;

            // Landed on the floor without ever landing on the platform: a miss.
            if (!hitPlatform) RegisterMiss();
        }

        AddPathPoint();
    }

    void PushOutOfPlatform()
    {
        if (pos.y > platTop)
        {
            PushFromCorner(new Vector2(platMaxX, platTop));
            PushFromCorner(new Vector2(platMinX, platTop));
        }
        else
        {
            if (pos.x > platMaxX && pos.x - radius < platMaxX)
            {
                pos.x = platMaxX + radius;
                if (vel.x < 0f) vel.x = 0f;
            }
            else if (pos.x < platMinX && pos.x + radius > platMinX)
            {
                pos.x = platMinX - radius;
                if (vel.x > 0f) vel.x = 0f;
            }
        }
    }

    void PushFromCorner(Vector2 corner)
    {
        Vector2 d = pos - corner;
        float m = d.magnitude;
        if (m < radius && m > 1e-5f)
        {
            Vector2 n = d / m;
            pos = corner + n * radius;
            float vn = Vector2.Dot(vel, n);
            if (vn < 0f) vel -= vn * n;              // remove the part moving into the corner
        }
    }

    void StepRoll(float dt)
    {
        float dir = Mathf.Sign(vel.x);
        float oldSpeed = Mathf.Abs(vel.x);
        float newSpeed = Mathf.Max(0f, oldSpeed - friction * dt);
        float dist = (oldSpeed + newSpeed) * 0.5f * dt;

        pos.x += dir * dist;
        vel.x = dir * newSpeed;
        rollRad += (-dir) * dist / radius;

        if (state == State.PlatformRoll)
        {
            // Stay on the platform: at either edge the ball rolls back with reduced speed.
            if (pos.x < platMinX)
            {
                pos.x = platMinX;
                vel.x = -vel.x * edgeBounce;
            }
            else if (pos.x > platMaxX)
            {
                pos.x = platMaxX;
                vel.x = -vel.x * edgeBounce;
            }
        }
        else
        {
            // Rolling on the floor into the platform's side: stop against it.
            if (pos.x > platMaxX && pos.x - radius < platMaxX)
            {
                pos.x = platMaxX + radius;
                vel.x = 0f;
                state = State.Done;
                return;
            }
        }

        if (Mathf.Abs(vel.x) <= 0.001f) state = State.Done;
    }
}