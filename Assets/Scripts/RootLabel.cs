using UnityEngine;
using TMPro;

/// <summary>
/// The word on the root. Put this on a 3D TextMeshPro object
/// (GameObject > 3D Object > Text - TextMeshPro, NOT the UI version) and type your word into it.
/// It keeps the word on the root and fades it in and out when BoothRootController says so.
/// </summary>
[RequireComponent(typeof(TextMeshPro))]
public class RootLabel : MonoBehaviour
{
    public RootTendril tendril;
    [Tooltip("Where along the root the word sits. 0 = booth, 1 = foot.")]
    [Range(0f, 1f)] public float positionAlongRoot = 0.5f;
    [Tooltip("How fast the word fades in and out (1 = one second).")]
    public float fadeSpeed = 2f;
    [Tooltip("On: the word faces the camera. Off: it lies flat on the floor along the root.")]
    public bool faceCamera = false;
    [Tooltip("Meters. When the root is shorter than this, the word shrinks so it never sticks out past the root.")]
    public float fullSizeLength = 1.2f;

    TextMeshPro text;
    Vector3 baseScale;
    bool visible;

    void Awake()
    {
        text = GetComponent<TextMeshPro>();
        text.alpha = 0f;
        baseScale = transform.localScale;
    }

    public void SetVisible(bool v) { visible = v; }

    void LateUpdate()
    {
        text.alpha = Mathf.MoveTowards(text.alpha, visible ? 1f : 0f, fadeSpeed * Time.deltaTime);
        if (!tendril || text.alpha <= 0f) return;

        float t = positionAlongRoot;
        Vector3 nearBooth = tendril.PointAt(Mathf.Clamp01(t - 0.05f));
        Vector3 nearFoot = tendril.PointAt(Mathf.Clamp01(t + 0.05f));
        Vector3 towardBooth = nearBooth - nearFoot;

        transform.position = tendril.PointAt(t) + Vector3.up * 0.005f;

        if (faceCamera && Camera.main)
        {
            transform.position += Vector3.up * 0.1f;
            transform.rotation = Camera.main.transform.rotation;
        }
        else if (towardBooth.sqrMagnitude > 0.000001f)
        {
            // Lie flat on the floor, reading along the root toward the booth.
            transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.Cross(Vector3.down, towardBooth));
        }

        transform.localScale = baseScale * Mathf.Clamp01(tendril.Length / fullSizeLength);
    }
}
