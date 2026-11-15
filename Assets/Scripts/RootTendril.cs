using UnityEngine;

/// <summary>
/// The glowing root. Put this on an empty GameObject (a LineRenderer is added for you).
/// Draws a curved ribbon on the floor from a point on the booth's ring to the player's foot,
/// grows out, shrinks back, and pulses once it is holding on.
/// BoothRootController tells it what to do; it doesn't need anything else.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class RootTendril : MonoBehaviour
{
    [Header("Shape")]
    [Range(8, 64)] public int points = 32;
    [Tooltip("How far the root bends sideways, as a fraction of its length.")]
    public float wiggle = 0.12f;
    [Tooltip("Roughly how many bends along the root.")]
    public float bends = 2f;
    [Tooltip("How fast the bends drift over time. 0 = still.")]
    public float wiggleSpeed = 0.3f;
    [Tooltip("Meters above the floor, so the root doesn't flicker into it.")]
    public float floorOffset = 0.01f;

    [Header("Growth (meters per second)")]
    public float growSpeed = 1.5f;
    public float retractSpeed = 2.5f;

    [Header("Glow")]
    public Color glowColor = new Color(0.3f, 1f, 0.6f, 1f);
    [Tooltip("Brightness. Above 1 is what Bloom turns into a glow.")]
    public float glow = 2f;
    [Tooltip("How much the brightness rises and falls while pulsing. Small = faint pulse.")]
    public float pulse = 0.4f;
    public float pulseSpeed = 2.5f;

    LineRenderer line;
    Material mat;
    int colorId;
    Vector3 a, b;   // a = point on the ring, b = foot
    float progress; // 0 = hidden, 1 = touching the foot
    int dir;        // +1 growing, -1 shrinking back, 0 still
    bool pulsing;
    float seed;

    public bool FullyGrown => progress >= 1f;
    public bool FullyRetracted => progress <= 0f;
    public float Length => Vector3.Distance(a, b);

    public void SetEnds(Vector3 anchor, Vector3 foot) { a = anchor; b = foot; }
    public void Grow() { dir = 1; line.enabled = true; }
    public void Retract() { dir = -1; }
    public void SetPulsing(bool on) { pulsing = on; }

    // Runs when you first add this component in the editor: sensible starting settings.
    void Reset()
    {
        LineRenderer lr = GetComponent<LineRenderer>();
        if (!lr) return;
        lr.widthMultiplier = 0.06f;
        lr.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.3f)); // thick at booth, thin at foot
        lr.numCapVertices = 4;
        lr.textureMode = LineTextureMode.Tile; // texture repeats instead of squashing as the root shortens
        lr.alignment = LineAlignment.TransformZ;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.alignment = LineAlignment.TransformZ;
        transform.rotation = Quaternion.Euler(90f, 0f, 0f); // makes the ribbon lie flat on the floor
        line.positionCount = points;
        line.enabled = false;

        mat = line.material; // this root's own copy, so changing its color doesn't affect others
        if (mat) colorId = Shader.PropertyToID(mat.HasProperty("_BaseColor") ? "_BaseColor" : "_Color");
        seed = Random.value * 100f;
    }

    void Update()
    {
        if (!line.enabled) return;

        float speed = dir > 0 ? growSpeed : retractSpeed;
        progress = Mathf.Clamp01(progress + dir * speed * Time.deltaTime / Mathf.Max(Length, 0.01f));
        if (dir < 0 && progress <= 0f)
        {
            line.enabled = false;
            return;
        }

        // Draw only the first `progress` fraction of the path. This is what makes it grow and shrink back.
        line.positionCount = points;
        for (int i = 0; i < points; i++)
            line.SetPosition(i, PointAt(progress * i / (points - 1f)));

        if (mat)
        {
            float intensity = glow + (pulsing ? pulse * Mathf.Sin(Time.time * pulseSpeed) : 0f);
            Color c = glowColor * intensity;
            c.a = glowColor.a;
            mat.SetColor(colorId, c);
        }
    }

    /// <summary>A point on the full root path. t = 0 at the booth, 1 at the foot.
    /// The ends stay pinned and the middle bends.</summary>
    public Vector3 PointAt(float t)
    {
        Vector3 along = b - a;
        Vector3 side = Vector3.Cross(Vector3.up, along).normalized;
        float n = (Mathf.PerlinNoise(seed, t * bends + Time.time * wiggleSpeed) - 0.5f) * 2f;
        Vector3 p = Vector3.Lerp(a, b, t)
                  + side * (n * wiggle * along.magnitude * Mathf.Sin(t * Mathf.PI));
        p.y = a.y + floorOffset;
        return p;
    }

    void OnDestroy()
    {
        if (mat) Destroy(mat);
    }
}
