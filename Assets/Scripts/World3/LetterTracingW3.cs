using Firebase.Auth;
using Firebase.Database;
using Mankibo;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.InputSystem;   // << سطر جديد مهم

public class LetterTracingW3 : MonoBehaviour // Main class that manages the letter tracing experience
{
    [Header("Next Step (Speech Recognition)")]
    public GameObject srCanvas; // كانفس التعرف على النطق (SR) الذي يُفتح بعد الفوز

    [Header("Letter Info")]
    public string currentLetter = "ض"; // Arabic letter to trace

    [Header("Audio & UI")]
    public AudioSource letterAudio; // Audio for the letter pronunciation
    public AudioSource writing; // Audio while tracing
    public AudioSource errorAudio; // Error sound when tracing fails
    public GameObject tracingPanel; // Main panel containing tracing UI
    public GameObject winningPopUp; // Popup shown when tracing is completed successfully
    public GameObject LetterTracingCanvas; // Canvas containing the tracing components
    public AudioSource winningAudio; // Sound played after winning
    public Button closeButton; // Close button for popup
    public GameObject PopUpLetterTracing; // Root popup GameObject for tracing

    [Header("Tracing Display Objects")]
    public GameObject TracingPointsGroup; // Group containing all traceable points
    public GameObject LetterImageObj; // The image of the letter being traced

    [Header("Tracing Segments")]
    public List<SegmentPoints> segments = new List<SegmentPoints>(); // List of letter segments (each segment contains multiple points)
    public List<LineRenderer> segmentLineRenderers = new List<LineRenderer>(); // LineRenderer for each segment

    [Header("Zones")]
    public List<RectTransform> lineZones; // Allowed UI zones where touch input is valid

    [Header("Tracing Settings")]
    [Range(50f, 500f)]
    public float traceRadius = 250f; // Allowed distance to consider touch inside a point
    public float pointSpacing = 0.05f; // Distance between points in the trail
    public float outOfBoundsLimit = 0.5f; // Time allowed out of bounds before error triggers

    private int currentAttempt = 1; // The current attempt number
    private int attemptErrorCount = 0; // Count of errors in the current attempt
    private bool attemptInitialized = false; // Has attempt setup been completed

    private List<List<bool>> segmentTraced; // Tracks which points have been traced in each segment
    private List<List<Vector3>> segmentTrails = new List<List<Vector3>>(); // Drawn trail points for each segment
    private int currentSegment = 0; // Index of current active segment
    private int currentPointInSegment = 0; // Index of the next point to trace in current segment
    private bool canTrace = false; // Can the user currently trace
    private bool isDrawing = false; // Is the user drawing right now
    private bool startedFromFirstPoint = false; // Did user begin from the first point
    private bool waitingForNextSegment = false; // Waiting for user to start the next segment
    private float outOfBoundsTimer = 0f; // Timer for staying outside allowed zones

    private World3 playerScript; // Reference to main game player
    private string parentId; // Firebase parent user ID

    [System.Serializable]
    public class SegmentPoints
    {
        public List<RectTransform> points; // UI points that form a segment
    }

    private void OnEnable()
    {
        playerScript = FindObjectOfType<World3>(); // Find and assign World3 player script

        if (FirebaseAuth.DefaultInstance.CurrentUser != null)
            parentId = FirebaseAuth.DefaultInstance.CurrentUser.UserId; // Use actual user ID
        else
            parentId = "debug_parent"; // Use default if not logged in

        attemptInitialized = false;

        if (TracingPointsGroup) TracingPointsGroup.SetActive(false); // Hide points
        if (LetterImageObj) LetterImageObj.SetActive(false); // Hide letter
    }

    public void StartNewAttempt()
    {
        StartCoroutine(LoadLastAttemptNumberAndStart()); // Begin attempt
    }

    IEnumerator LoadLastAttemptNumberAndStart()
    {
        string selectedChildKey = CoinsManager.instance.SelectedChildKey;

        // NEW: ضمن تهيئة عقدة الحرف (badge=false) مرة واحدة مثل W1/W2
        yield return StartCoroutine(EnsureLetterNodeInitialized(parentId, selectedChildKey, currentLetter));


        string attemptsPath = $"parents/{parentId}/children/{selectedChildKey}/letters/{currentLetter}/activities/tracing/attempts";
        var task = FirebaseDatabase.DefaultInstance.RootReference.Child(attemptsPath).GetValueAsync();
        yield return new WaitUntil(() => task.IsCompleted);

        int maxAttempt = 0;
        if (task.Exception == null && task.Result.Exists)
        {
            foreach (var att in task.Result.Children)
                if (int.TryParse(att.Key, out int attNum) && attNum > maxAttempt) maxAttempt = attNum;
        }

        currentAttempt = maxAttempt + 1;
        attemptErrorCount = 0;
        attemptInitialized = true;
        InitializeTracing();
    }


    public void SetCanTraceAfterAudio(AudioSource animalAudio)
    {
        canTrace = false;
        StartCoroutine(WaitForAnimalAudio(animalAudio));
    }

    private IEnumerator WaitForAnimalAudio(AudioSource animalAudio)
    {
        yield return new WaitUntil(() => !animalAudio.isPlaying);
        canTrace = true;

        if (TracingPointsGroup) TracingPointsGroup.SetActive(true);
        if (LetterImageObj) LetterImageObj.SetActive(true);
    }

    private void Update()
    {
        if (!canTrace || !attemptInitialized) return;
        if (currentSegment >= segments.Count) return;

        // ===== إدخال موحّد باستخدام New Input System =====
        Vector2 screenPos = Vector2.zero;
        bool down = false, up = false, held = false;

        // أولاً: اللمس (للجوال)
        if (Touchscreen.current != null)
        {
            var touch = Touchscreen.current.primaryTouch;

            // موقع اللمسة على الشاشة بالبكسل
            screenPos = touch.position.ReadValue();

            down = touch.press.wasPressedThisFrame;
            up = touch.press.wasReleasedThisFrame;

            // يتحرّك أو ثابت والزر مضغوط
            held = touch.press.isPressed &&
                   (touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Moved ||
                    touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Stationary);
        }
        // ثانياً: الماوس (للـ Editor على اللابتوب)
        else if (Mouse.current != null)
        {
            screenPos = Mouse.current.position.ReadValue();
            down = Mouse.current.leftButton.wasPressedThisFrame;
            up = Mouse.current.leftButton.wasReleasedThisFrame;
            held = Mouse.current.leftButton.isPressed;
        }
        // لو لا لمس ولا ماوس → لا يوجد إدخال
        else
        {
            return;
        }
        // ===================================================

        if (waitingForNextSegment)
        {
            if (down)
            {
                if (IsTouchWithinPoint(screenPos, segments[currentSegment].points[0], traceRadius * 0.8f))
                {
                    isDrawing = true;
                    startedFromFirstPoint = true;
                    waitingForNextSegment = false;
                    currentPointInSegment = 0;
                }
                else TriggerError();
            }
            return;
        }

        if (down)
        {
            var segment = segments[currentSegment].points;
            var traced = segmentTraced[currentSegment];
            int i = currentPointInSegment;

            if (!startedFromFirstPoint && i == 0 &&
                IsTouchWithinPoint(screenPos, segment[0], traceRadius * 0.8f))
            {
                isDrawing = true;
                startedFromFirstPoint = true;
                AddFingerPoint(screenPos);
            }
            else if (!startedFromFirstPoint && i > 0 && traced[i - 1] &&
                     IsTouchWithinPoint(screenPos, segment[i - 1], traceRadius * 0.8f))
            {
                isDrawing = true;
                startedFromFirstPoint = true;
            }
            else if (startedFromFirstPoint)
            {
                isDrawing = true;
            }
            else
            {
                TriggerError();
            }
        }

        if (up) isDrawing = false;

        if (held && startedFromFirstPoint)
            HandleSegmentTouchStrict(screenPos, down);

        if (startedFromFirstPoint && AllSegmentPointsTraced(currentSegment) && !waitingForNextSegment)
        {
            isDrawing = false;
            startedFromFirstPoint = false;
            currentSegment++;
            currentPointInSegment = 0;

            if (currentSegment < segments.Count)
                waitingForNextSegment = true;
            else
                FinishLetterTracing();
        }
    }

    private void AddFingerPoint(Vector2 screenPos) // Adds the current touch position to the trail of the current segment
    {
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 10f)); // Converts screen position to world position
        worldPos.z = 0; // Set Z to 0 to keep drawing on 2D plane
        segmentTrails[currentSegment].Add(worldPos); // Add the point to the current segment's trail list
        segmentLineRenderers[currentSegment].positionCount = segmentTrails[currentSegment].Count; // Update how many positions to draw
        segmentLineRenderers[currentSegment].SetPositions(segmentTrails[currentSegment].ToArray()); // Apply the trail to the LineRenderer
    }

    private void HandleSegmentTouchStrict(Vector2 screenPos, bool isDown) // Processes touch movement and validates if it’s within correct bounds
    {
        var segment = segments[currentSegment].points; // Get the current segment's list of points
        var traced = segmentTraced[currentSegment]; // Get the traced booleans for this segment
        int i = currentPointInSegment; // Get the current point index to trace

        Vector3 drawPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 10f)); // Convert touch to world space
        drawPos.z = 0; // Flatten to 2D
        if (segmentTrails[currentSegment].Count == 0 || Vector3.Distance(segmentTrails[currentSegment][^1], drawPos) > pointSpacing) // If trail is empty or spaced enough
        {
            segmentTrails[currentSegment].Add(drawPos); // Add to trail
            segmentLineRenderers[currentSegment].positionCount = segmentTrails[currentSegment].Count; // Update how many positions to draw
            segmentLineRenderers[currentSegment].SetPositions(segmentTrails[currentSegment].ToArray()); // Apply the trail to the LineRenderer
        }

        if (!writing.isPlaying) // If tracing sound isn't already playing
            writing.Play(); // Play the writing sound

        if (i < segment.Count && !traced[i] && IsTouchWithinPoint(screenPos, segment[i], traceRadius)) // If touch is on the current point
        {
            traced[i] = true; // Mark as traced
            var img = segment[i].GetComponent<Image>(); // Get the UI image
            if (img != null)
                img.color = Color.green; // Change color to green to show progress

            currentPointInSegment++; // Move to the next point
        }
        else if (isDown && !IsTouchInsideAnyZone(screenPos)) // If touching down outside any valid zone
        {
            TriggerError(); // Trigger error handling
        }

        if (!IsTouchInsideAnyZone(screenPos)) // If outside zone while dragging
        {
            outOfBoundsTimer += Time.deltaTime; // Add time spent out of bounds
            if (outOfBoundsTimer > outOfBoundsLimit) // If exceeded limit
            {
                TriggerError(); // Trigger error
                outOfBoundsTimer = 0f; // Reset timer
            }
        }
        else
        {
            outOfBoundsTimer = 0f; // Reset if inside zone
        }
    }

    private bool IsTouchWithinPoint(Vector2 touch, RectTransform point, float radius) // Checks if touch is close enough to a point
    {
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(Camera.main, point.position); // Convert point to screen coordinates
        return Vector2.Distance(screenPoint, touch) <= radius; // Compare distance to radius
    }

    private bool IsTouchInsideAnyZone(Vector2 screenPos) // Checks if touch is inside any allowed zone
    {
        foreach (var zone in lineZones) // Loop through all allowed zones
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(zone, screenPos, Camera.main)) // If touch is inside zone
                return true;
        }
        return false; // Not inside any zone
    }

    private void TriggerError() // Handles trace failure
    {
        attemptErrorCount++; // Increment error count

        foreach (var seg in segments) // Loop through segments
            foreach (RectTransform pt in seg.points) // Loop through points
                pt.GetComponent<Image>().color = Color.red; // Set point color to red

        foreach (var lr in segmentLineRenderers) // Clear all rendered lines
            lr.positionCount = 0;
        foreach (var trail in segmentTrails) // Clear trail data
            trail.Clear();

        if (errorAudio != null)
            errorAudio.Play(); // Play error sound

        SaveAttemptError(); // Save the error count
        StartCoroutine(ResetAndReinitAfterError()); // Restart attempt after short wait
    }

    void SaveAttemptError()
    {
        string selectedChildKey = CoinsManager.instance.SelectedChildKey;
        if (string.IsNullOrEmpty(selectedChildKey)) { Debug.LogError("Selected child key is not set!"); return; }

        string basePath = $"parents/{parentId}/children/{selectedChildKey}/letters/{currentLetter}/activities/tracing/attempts/{currentAttempt}";
        var root = FirebaseDatabase.DefaultInstance.RootReference;

        var updates = new Dictionary<string, object>
        {
            [$"{basePath}/errors"] = attemptErrorCount,
            [$"{basePath}/finished"] = false,
            [$"{basePath}/ts"] = ServerValue.Timestamp
        };

        root.UpdateChildrenAsync(updates);
    }

    private IEnumerator ResetAndReinitAfterError() // Waits, then reinitializes tracing
    {
        float wait = (errorAudio != null && errorAudio.clip != null) ? errorAudio.clip.length : 0.3f; // Delay based on audio length
        yield return new WaitForSeconds(wait); // Wait
        InitializeTracing(); // Reinitialize
    }

    private void InitializeTracing() // Reset all tracing state for a fresh start
    {
        currentSegment = 0;
        currentPointInSegment = 0;
        segmentTraced = new List<List<bool>>();
        segmentTrails = new List<List<Vector3>>();

        foreach (var seg in segments)
        {
            segmentTraced.Add(new List<bool>(new bool[seg.points.Count])); // Initialize traced flags
            segmentTrails.Add(new List<Vector3>()); // Initialize trail list
        }

        waitingForNextSegment = false;
        isDrawing = false;
        startedFromFirstPoint = false;

        foreach (var lr in segmentLineRenderers)
            lr.positionCount = 0;

        writing.Stop(); // Stop any tracing sound

        foreach (var seg in segments)
            foreach (var pt in seg.points)
                pt.GetComponent<Image>().color = Color.white; // Reset color
    }

    private bool AllSegmentPointsTraced(int segIndex) // Check if all points in segment are traced
    {
        foreach (bool traced in segmentTraced[segIndex])
            if (!traced) return false;
        return true;
    }

    private void FinishLetterTracing() // Handles when all segments are completed
    {
        canTrace = false; // Stop tracing
        writing.Stop(); // Stop writing sound
        CoinsManager.instance.AddCoinsToSelectedChild(5); // Award coins
        SaveTracingSuccess();
        SaveTracingResult(); // Save locally
        StartCoroutine(ShowWinningPopUpWithDelay(1f)); // Show popup
    }


    void SaveTracingSuccess()
    {
        string selectedChildKey = CoinsManager.instance.SelectedChildKey;
        if (string.IsNullOrEmpty(selectedChildKey)) { Debug.LogError("Selected child key is not set!"); return; }

        string basePath = $"parents/{parentId}/children/{selectedChildKey}/letters/{currentLetter}/activities/tracing/attempts/{currentAttempt}";
        var root = FirebaseDatabase.DefaultInstance.RootReference;

        var updates = new Dictionary<string, object>
        {
            [$"{basePath}/errors"] = attemptErrorCount,
            [$"{basePath}/successes"] = 1,
            [$"{basePath}/finished"] = true,
            [$"{basePath}/ts"] = ServerValue.Timestamp
        };

        root.UpdateChildrenAsync(updates);
    }

    private IEnumerator ShowWinningPopUpWithDelay(float delay) // Delay before showing popup
    {
        yield return new WaitForSeconds(delay);
        if (winningPopUp != null)
            winningPopUp.SetActive(true);

        if (winningAudio != null)
        {
            winningAudio.Play();
            StartCoroutine(CloseEverythingAfterAudio()); // Wait for audio
        }
        else
        {
            CloseEverything(); // No audio (نفس منطقك القديم بالضبط)
            // لاحظ: حسب منطقك القديم، ما نفتح SR هنا إذا مافي صوت فوز
        }
    }

    private IEnumerator CloseEverythingAfterAudio() // Waits for audio to finish
    {
        yield return new WaitUntil(() => !winningAudio.isPlaying);
        CloseEverything();

        // افتح srCanvas فقط هنا (بعد انتهاء صوت الفوز) — نفس منطقك القديم
        if (srCanvas != null)
            srCanvas.SetActive(true);
    }

    private void CloseEverything() // Hides tracing UI and enables movement again
    {
        if (winningPopUp != null)
            winningPopUp.SetActive(false);
        if (PopUpLetterTracing != null)
            PopUpLetterTracing.SetActive(false);
        if (LetterTracingCanvas != null)
            LetterTracingCanvas.SetActive(false);

        if (playerScript != null)
        {
            playerScript.canMove = true; // Re-enable movement
            playerScript.Idle(); // Set idle animation
        }
    }

    private void SaveTracingResult() // Saves the final result (locally)
    {
        string letterKey = "Letter_" + currentLetter + "_Errors";
        PlayerPrefs.SetInt(letterKey, attemptErrorCount); // Save error count
        PlayerPrefs.Save(); // Apply changes
    }

    private void AwardBadgeIfNotAlready() // Awards badge in Firebase if not earned yet
    {
        string selectedChildKey = CoinsManager.instance.SelectedChildKey;
        if (string.IsNullOrEmpty(selectedChildKey))
        {
            Debug.LogError("Selected child key is not set!");
            return;
        }

        string badgePath = $"parents/{parentId}/children/{selectedChildKey}/letters/{currentLetter}/badge";
        var badgeRef = FirebaseDatabase.DefaultInstance.RootReference.Child(badgePath);

        badgeRef.GetValueAsync().ContinueWith(task =>
        {
            if (task.IsCompleted && task.Result != null)
            {
                bool alreadyAwarded = false;
                if (task.Result.Exists && bool.TryParse(task.Result.Value.ToString(), out alreadyAwarded) && alreadyAwarded)
                    return;

                badgeRef.SetValueAsync(true); // Award badge
            }
            else
            {
                badgeRef.SetValueAsync(true); // Award if error occurred
            }
        });
    }


    public void SafeStartNewAttempt()
    {
        // فعّلي الجيم أوبجكت قبل تشغيل أي كوروتين/تجهيز
        if (!gameObject.activeInHierarchy) gameObject.SetActive(true);
        StartNewAttempt();
    }

    public void SetCanTraceAfterAudioSafe(AudioSource audio)
    {
        // فعّلي الجيم أوبجكت ثم استعملي النسخة الأصلية
        if (!gameObject.activeInHierarchy) gameObject.SetActive(true);
        SetCanTraceAfterAudio(audio);
    }







// ====================== Ensure letter has "badge" once (مثل W1/W2) ======================
private IEnumerator EnsureLetterNodeInitialized(string parentId, string selectedChildKey, string letter)
{
    if (string.IsNullOrEmpty(parentId) || string.IsNullOrEmpty(selectedChildKey) || string.IsNullOrEmpty(letter))
        yield break;

    string letterPath = $"parents/{parentId}/children/{selectedChildKey}/letters/{letter}";
    var getTask = FirebaseDatabase.DefaultInstance.RootReference.Child(letterPath).GetValueAsync();
    yield return new WaitUntil(() => getTask.IsCompleted);

    if (getTask.Exception != null) yield break;

    var snap = getTask.Result;
    if (!snap.HasChild("badge"))
    {
        var updates = new Dictionary<string, object>
        {
            [$"{letterPath}/badge"] = false
        };
        FirebaseDatabase.DefaultInstance.RootReference.UpdateChildrenAsync(updates);
    }
}


}
