using UnityEngine;
using UnityEngine.Events;
using TMPro;

public class CountdownOnTouch : MonoBehaviour
{
    // The tag used to identify the player.
    public string playerTag = "Player";

    // The TextMeshPro text in the scene that shows the countdown.
    public TMP_Text timerText;

    // How many seconds the countdown starts from.
    public float startTime = 10f;

    // Runs when the countdown reaches 0.
    // Hook your ball script's method up to this in the Inspector.
    public UnityEvent onCountdownFinished;

    // The object's Collider.
    private Collider objectCollider;

    // Countdown state.
    private float timeRemaining;
    private bool countdownRunning;
    private bool countdownFinished;

    // Player tracking.
    private bool playerWasInside;
    private bool playerLeftSinceFinish;

    void Awake()
    {
        // Get the Collider on this object.
        objectCollider = GetComponent<Collider>();

        // Warn us if the object is missing required components.
        if (objectCollider == null)
            Debug.LogWarning(name + " needs a Collider.", this);

        if (timerText == null)
            Debug.LogWarning(name + " needs a TextMeshPro text assigned to Timer Text.", this);

        // Show the starting value before anything happens.
        timeRemaining = startTime;
        UpdateDisplay();
    }

    void Update()
    {
        if (objectCollider == null)
            return;

        bool playerInside = IsPlayerInside();

        // "Just entered" means inside now, but not inside last frame.
        bool playerJustEntered = playerInside && !playerWasInside;
        playerWasInside = playerInside;

        // After the countdown has finished, remember that the player left.
        if (countdownFinished && !playerInside)
            playerLeftSinceFinish = true;

        if (playerJustEntered)
        {
            if (!countdownRunning && !countdownFinished)
            {
                // First entry: start counting down.
                StartCountdown();
            }
            else if (countdownFinished && playerLeftSinceFinish)
            {
                // Player left and came back after it hit 0: reset and run again.
                ResetTimer();
                StartCountdown();
            }
        }

        // Once started, the countdown keeps going even if the player leaves.
        if (countdownRunning)
            RunCountdown();
    }

    // The same detection logic as the original script, moved into its own method.
    bool IsPlayerInside()
    {
        // Find all GameObjects tagged as Player.
        GameObject[] players =
            GameObject.FindGameObjectsWithTag(playerTag);

        // Check whether any player's position is inside this object.
        foreach (GameObject player in players)
        {
            if (player == null)
                continue;

            Vector3 closestPoint =
                objectCollider.ClosestPoint(player.transform.position);

            // If the closest point is the player's position,
            // that position is inside the Collider.
            if (Vector3.Distance(
                closestPoint, player.transform.position) < 0.001f)
            {
                return true;
            }
        }

        return false;
    }

    void StartCountdown()
    {
        countdownRunning = true;
        countdownFinished = false;
    }

    void RunCountdown()
    {
        timeRemaining -= Time.deltaTime;

        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            countdownRunning = false;
            countdownFinished = true;

            // If the player is already outside, they have "left" already.
            playerLeftSinceFinish = !playerWasInside;

            UpdateDisplay();

            // Tell the other script (the ball) to start.
            onCountdownFinished.Invoke();
            return;
        }

        UpdateDisplay();
    }

    // Public so other scripts can call it too.
    public void ResetTimer()
    {
        timeRemaining = startTime;
        countdownRunning = false;
        countdownFinished = false;
        playerLeftSinceFinish = false;
        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        if (timerText == null)
            return;

        // Round up so it shows 10 at the start and 0 only when time is up.
        timerText.text = Mathf.CeilToInt(timeRemaining).ToString();
    }
}