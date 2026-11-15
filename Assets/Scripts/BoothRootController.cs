using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Put this on the booth.
/// Finds people around the booth by itself, so there is no single "player" to assign.
/// Anyone a body-tracking camera turns into a humanoid character in the scene counts,
/// and so does anything tagged "Player" (handy for a test capsule).
///
/// When someone steps inside the grab ring, a root grows from the booth to their nearest foot.
/// It follows them, pulses, shows its word up close, and lets go at the release ring.
/// The root holds one person at a time: the closest one inside the grab ring.
///
/// Works on its own for testing: leave Tendril and Label empty and it only
/// draws the rings and prints what it sees to the Console.
/// </summary>
[DefaultExecutionOrder(-10)] // run before RootTendril so the root uses this frame's positions
public class BoothRootController : MonoBehaviour
{
    const float FT = 0.3048f; // feet to meters (1 Unity unit = 1 meter)

    [Header("Booth")]
    [Tooltip("Empty object at floor level, at the center of the pole.")]
    public Transform rootOrigin;
    [Tooltip("Meters. Make this a little wider than the radius of the booth top.")]
    public float ringRadius = 0.6f;

    [Header("People (anyone matching either box counts)")]
    [Tooltip("Every humanoid character in the scene counts as a person, such as the avatars " +
             "a body-tracking camera creates for each person it sees. Their feet are used.")]
    public bool countHumanoidCharacters = true;
    [Tooltip("Every object with the Person Tag counts as a person, such as a test capsule. " +
             "If it has a humanoid rig, its feet are used; otherwise its position is.")]
    public bool countTaggedObjects = true;
    [Tooltip("The tag for Count Tagged Objects. It must exist under Tags and Layers.")]
    public string personTag = "Player";
    [Tooltip("Seconds between looks for people who arrived or left. " +
             "The person the root is holding is followed every frame either way.")]
    public float lookEverySeconds = 0.25f;

    [Header("Root (leave empty to only test distances)")]
    public RootTendril tendril;
    public RootLabel label;

    [Header("Distances in feet, measured from the ring")]
    public float grabFt = 5f;
    public float revealFt = 4f;
    public float releaseFt = 0.7f;
    [Tooltip("If the person backs away past this while the root is holding on, it lets go.")]
    public float letGoFt = 5.5f;
    [Tooltip("After the root lets go of someone, how much farther back than Release Ft they must step " +
             "before it grabs them again. Keep it above 0 so the root doesn't flicker on and off. " +
             "Other people can be grabbed right away.")]
    public float reconnectFt = 0.5f;

    [Header("Debug")]
    [Tooltip("Draws a line from the ring to each person (magenta for the one being held) " +
             "and prints how many people it sees to the Console.")]
    public bool showDebug = true;

    struct Person
    {
        public Transform body;
        public Transform leftFoot, rightFoot; // empty when the person has no humanoid rig
    }

    enum State { Idle, Growing, Attached, Releasing }
    State state = State.Idle;

    readonly List<Person> people = new List<Person>();
    readonly HashSet<Animator> rigsAdded = new HashSet<Animator>();
    float nextLook;
    Transform heldFoot;     // chosen once, when the root starts growing, so it doesn't jump between feet
    Transform heldBody;
    Transform releasedBody; // the person just let go of, until they step back
    bool warnedAboutTag;

    void Update()
    {
        if (!rootOrigin) return;

        if (Time.time >= nextLook)
        {
            FindPeople();
            nextLook = Time.time + Mathf.Max(0.05f, lookEverySeconds);
        }

        if (tendril) RunRoot();
        if (showDebug) ShowDebug();
    }

    void RunRoot()
    {
        if (state == State.Idle) TryGrab();

        if (state == State.Growing || state == State.Attached)
        {
            // The person left the camera's view, so their character disappeared.
            if (!Present(heldFoot)) { Release(); return; }

            Measure(heldFoot.position, out Vector3 floorPos, out Vector3 anchor, out float distFt);
            // Both ends pinned every frame, so the root gets shorter on its own as the person walks in.
            tendril.SetEnds(anchor, floorPos);

            if (state == State.Growing)
            {
                if (distFt > letGoFt) Release();
                else if (tendril.FullyGrown)
                {
                    tendril.SetPulsing(true);
                    state = State.Attached;
                }
            }
            else
            {
                if (label) label.SetVisible(distFt <= revealFt);
                if (distFt <= releaseFt || distFt > letGoFt) Release();
            }
        }
        else if (state == State.Releasing && tendril.FullyRetracted)
        {
            state = State.Idle;
        }
    }

    // Grabs the closest person inside the grab ring, if there is one.
    void TryGrab()
    {
        Transform bestFoot = null, bestBody = null;
        float bestFt = grabFt;

        foreach (Person p in people)
        {
            if (!Present(p.body)) continue;
            Transform foot = NearestFoot(p, out float distFt);
            if (!foot) continue;

            // Someone already standing at the booth (for example, the camera only just spotted them)
            // isn't grabbed until they step back out past the release ring.
            if (distFt <= releaseFt) continue;

            // Whoever the root just let go of has to step back a little before it grabs them again.
            if (p.body == releasedBody)
            {
                if (distFt <= releaseFt + Mathf.Max(reconnectFt, 0.1f)) continue;
                releasedBody = null;
            }

            if (distFt <= bestFt)
            {
                bestFt = distFt;
                bestFoot = foot;
                bestBody = p.body;
            }
        }

        if (!bestFoot) return;
        heldFoot = bestFoot;
        heldBody = bestBody;
        tendril.Grow();
        state = State.Growing;
    }

    void Release()
    {
        if (label) label.SetVisible(false);
        tendril.SetPulsing(false);
        tendril.Retract(); // stops following the foot and slides back to the ring
        releasedBody = heldBody;
        heldFoot = null;
        heldBody = null;
        state = State.Releasing;
    }

    void FindPeople()
    {
        people.Clear();
        rigsAdded.Clear();

        if (countHumanoidCharacters)
        {
            foreach (Animator anim in FindObjectsByType<Animator>())
                if (anim.isHuman) AddPerson(anim.transform, anim);
        }

        if (countTaggedObjects && !string.IsNullOrEmpty(personTag))
        {
            GameObject[] tagged;
            try
            {
                tagged = GameObject.FindGameObjectsWithTag(personTag);
            }
            catch (UnityException)
            {
                if (!warnedAboutTag)
                    Debug.LogWarning($"[Booth] There is no tag called \"{personTag}\". Add it under " +
                                     "Edit > Project Settings > Tags and Layers, or change Person Tag.", this);
                warnedAboutTag = true;
                return;
            }

            foreach (GameObject go in tagged)
            {
                Animator anim = go.GetComponentInChildren<Animator>();
                if (anim && anim.isHuman)
                {
                    // A tagged humanoid character: use its rig, and don't count it twice.
                    if (!rigsAdded.Contains(anim)) AddPerson(anim.transform, anim);
                }
                else
                {
                    AddPerson(go.transform, null);
                }
            }
        }
    }

    void AddPerson(Transform body, Animator rig)
    {
        Person p = new Person { body = body };
        if (rig)
        {
            rigsAdded.Add(rig);
            p.leftFoot = rig.GetBoneTransform(HumanBodyBones.LeftFoot);
            p.rightFoot = rig.GetBoneTransform(HumanBodyBones.RightFoot);
        }
        people.Add(p);
    }

    // The person's foot closest to the booth (or their position, if they have no feet),
    // and how far it is from the ring in feet.
    Transform NearestFoot(Person p, out float distFt)
    {
        Transform best = null;
        distFt = float.PositiveInfinity;
        Consider(p.leftFoot, ref best, ref distFt);
        Consider(p.rightFoot, ref best, ref distFt);
        if (!best) Consider(p.body, ref best, ref distFt);
        return best;
    }

    void Consider(Transform t, ref Transform best, ref float bestFt)
    {
        if (!Present(t)) return;
        Measure(t.position, out _, out _, out float d);
        if (d < bestFt)
        {
            best = t;
            bestFt = d;
        }
    }

    // Flattens a position onto the floor and finds the nearest point on the ring.
    void Measure(Vector3 worldPos, out Vector3 floorPos, out Vector3 anchor, out float distFt)
    {
        floorPos = worldPos;
        floorPos.y = rootOrigin.position.y;
        anchor = RingPointToward(floorPos);
        distFt = Vector3.Distance(floorPos, anchor) / FT;
    }

    Vector3 RingPointToward(Vector3 p)
    {
        Vector3 c = rootOrigin.position;
        Vector3 dir = p - c;
        dir.y = 0f;
        return c + dir.normalized * ringRadius;
    }

    static bool Present(Transform t) => t && t.gameObject.activeInHierarchy;

    void ShowDebug()
    {
        int seen = 0;
        float nearestFt = float.PositiveInfinity;

        foreach (Person p in people)
        {
            if (!Present(p.body)) continue;
            Transform foot = NearestFoot(p, out float distFt);
            if (!foot) continue;
            seen++;
            nearestFt = Mathf.Min(nearestFt, distFt);
            Measure(foot.position, out Vector3 floorPos, out Vector3 anchor, out _);
            Debug.DrawLine(anchor, floorPos, p.body == heldBody ? Color.magenta : Color.gray);
        }

        if (Time.frameCount % 15 == 0) // about 4 times a second, so the Console doesn't flood
        {
            string nearest = seen == 0
                ? "nobody found"
                : $"nearest: {nearestFt:F2} ft   zone: {Zone(nearestFt)}";
            Debug.Log($"[Booth] people: {seen}   {nearest}   state: {state}");
        }
    }

    string Zone(float d) =>
        d <= releaseFt ? "RELEASE" :
        d <= revealFt ? "SHOW WORD" :
        d <= grabFt ? "GRAB" :
        "outside";

#if UNITY_EDITOR
    // Select the booth to see these rings in the Scene tab.
    // Cyan = where roots start, yellow = grab, green = word, red = let go,
    // orange = step back past this to make the root grab the same person again.
    void OnDrawGizmosSelected()
    {
        if (!rootOrigin) return;
        Vector3 c = rootOrigin.position;
        UnityEditor.Handles.color = Color.cyan;
        UnityEditor.Handles.DrawWireDisc(c, Vector3.up, ringRadius);
        UnityEditor.Handles.color = Color.yellow;
        UnityEditor.Handles.DrawWireDisc(c, Vector3.up, ringRadius + grabFt * FT);
        UnityEditor.Handles.color = Color.green;
        UnityEditor.Handles.DrawWireDisc(c, Vector3.up, ringRadius + revealFt * FT);
        UnityEditor.Handles.color = Color.red;
        UnityEditor.Handles.DrawWireDisc(c, Vector3.up, ringRadius + releaseFt * FT);
        UnityEditor.Handles.color = new Color(1f, 0.5f, 0f);
        UnityEditor.Handles.DrawWireDisc(c, Vector3.up, ringRadius + (releaseFt + Mathf.Max(reconnectFt, 0.1f)) * FT);
    }
#endif
}
