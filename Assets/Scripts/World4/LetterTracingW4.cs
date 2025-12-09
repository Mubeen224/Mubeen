using System.Collections;
using System.Collections.Generic;
using Firebase.Auth;
using Firebase.Database;
using Mankibo;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class LetterTracingW4 : MonoBehaviour
{
    [Header("Letter Info")]
    public string currentLetter = "ض";

    [Header("Audio & UI")]
    public AudioSource letterAudio;
    public AudioSource writing;
    public AudioSource errorAudio;
    public GameObject tracingPanel;
    public GameObject winningPopUp;
    public GameObject LetterTracingCanvas;
    public AudioSource winningAudio;
    public Button closeButton;
    public GameObject PopUpLetterTracing;

    [Header("Letters Pool")]
    public string[] letters = new[] { "س", "ش", "ص", "ض", "ظ", "ذ", "ر", "خ", "غ" };

    [Header("Tracing Display Objects")]
    public GameObject TracingPointsGroup; // Group of tracing points
    public GameObject LetterImageObj;

    [Header("Tracing Segments")]
    public List<SegmentPoints> segments = new List<SegmentPoints>(); // Each segment is a group of tracing points
    public List<LineRenderer> segmentLineRenderers = new List<LineRenderer>(); // LineRenderers used for each segment

    [Header("Zones")]
    public List<RectTransform> lineZones; // Valid zones for drawing

    [Header("Tracing Settings")]
    [Range(50f, 500f)]
    public float traceRadius = 250f; // Radius around a point for valid tracing
    public float pointSpacing = 0.05f; // Minimum distance between traced points
    public float outOfBoundsLimit = 0.5f; // Time allowed outside zones before triggering error

    // --- Quiz Attempt Logic (GLOBAL UNIQUE) ---
    private const int FIXED_ATTEMPT_NUMBER = 1; // always keep a single attempt node: Quiz_attempt/1
    private int currentAttempt = FIXED_ATTEMPT_NUMBER;
    private int attemptErrorCount = 0;
    private bool attemptInitialized = false;

    private List<List<bool>> segmentTraced; // Tracks which points are traced
    private List<List<Vector3>> segmentTrails = new List<List<Vector3>>(); // Stores drawn trail points
    private int currentSegment = 0;
    private int currentPointInSegment = 0;
    private bool canTrace = false; // Set to true after letter audio
    private bool isDrawing = false;
    private bool startedFromFirstPoint = false;
    private bool waitingForNextSegment = false;
    private float outOfBoundsTimer = 0f;

    // Firebase identity
    private string parentId;
    private string selectedChildKey;

    // Defer DB writes until identity is ready, then flush
    private bool identityReady => !string.IsNullOrEmpty(parentId) && !string.IsNullOrEmpty(selectedChildKey);
    private bool bufferHasState = false;
    private bool bufferFinished = false;
    private int bufferErrors = 0;
    private bool didInitInDb = false;

    private World4 playerScript; // Reference to the player script

    [System.Serializable]
    public class SegmentPoints
    {
        public List<RectTransform> points; // Points to trace in one segment
    }

    // At top of class:
    private PopUpTrigger _popup;
    private PopUpTrigger Popup => _popup ??= FindFirstObjectByType<PopUpTrigger>();

    private void UnfreezeAllControls()
    {
        // 1) Global gate
        Popup?.SetMovement(true);

        // 2) Player
        if (playerScript != null)
        {
            playerScript.canMove = true;
            playerScript.Idle();
        }

        // 3) Clear any latched joystick input
        var uiCtrl = FindFirstObjectByType<CharacterUIControllerW4>();
        uiCtrl?.StopMoving();

        // 4) Safety
        if (Time.timeScale == 0f) Time.timeScale = 1f;
    }

    private void OnEnable()
    {
        playerScript = FindObjectOfType<World4>();

        // Hide tracing elements until audio is done
        if (TracingPointsGroup) TracingPointsGroup.SetActive(false);
        if (LetterImageObj) LetterImageObj.SetActive(false);

        // Start bootstrap flow:
        //  - Resolve identity (CoinsManager -> Firebase)
        //  - Initialize tracing UI immediately (never block)
        //  - Once identity resolves, set up DB state and flush any buffered state
        StartCoroutine(BootstrapFlow());
    }

    private IEnumerator BootstrapFlow()
    {
        // Resolve parent id
        var auth = FirebaseAuth.DefaultInstance;
        parentId = (auth != null && auth.CurrentUser != null) ? auth.CurrentUser.UserId : null;

        // Quick attempt to read child from CoinsManager (fast path)
        yield return StartCoroutine(TryResolveChildFromCoinsManager(0.5f));

        // If still missing and we have a parent, query Firebase children (selected -> displayed)
        if (string.IsNullOrEmpty(selectedChildKey) && !string.IsNullOrEmpty(parentId))
            yield return StartCoroutine(FindSelectedOrDisplayedChildKey());

        // Initialize UI/gameplay regardless of identity status
        attemptInitialized = true;
        InitializeTracing();

        // If identity is ready now, init DB; else keep polling briefly to catch it soon after
        if (identityReady)
        {
            yield return StartCoroutine(EnsureDbInitThenFlush());
        }
        else
        {
            // Poll a bit while user is already tracing (doesn't block)
            float t = 0f;
            while (!identityReady && t < 5f)
            {
                // Try again to pull from CoinsManager first
                if (CoinsManager.instance != null && !string.IsNullOrEmpty(CoinsManager.instance.SelectedChildKey))
                    selectedChildKey = CoinsManager.instance.SelectedChildKey;
                // If still missing but we have parent, try Firebase once more
                if (!identityReady && !string.IsNullOrEmpty(parentId))
                    yield return StartCoroutine(FindSelectedOrDisplayedChildKey());

                if (identityReady) break;
                t += 0.5f;
                yield return new WaitForSeconds(0.5f);
            }

            if (identityReady)
                yield return StartCoroutine(EnsureDbInitThenFlush());
            else
                Debug.LogWarning("[LetterTracingW4] Proceeding without Firebase identity. Tracing works; DB writes will be skipped.");
        }
    }

    private IEnumerator TryResolveChildFromCoinsManager(float timeout)
    {
        float t = 0f;
        while (t < timeout)
        {
            if (CoinsManager.instance != null && !string.IsNullOrEmpty(CoinsManager.instance.SelectedChildKey))
            {
                selectedChildKey = CoinsManager.instance.SelectedChildKey;
                yield break;
            }
            t += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator FindSelectedOrDisplayedChildKey()
    {
        var db = FirebaseDatabase.DefaultInstance;
        if (db == null || string.IsNullOrEmpty(parentId)) yield break;

        var childrenRef = db.RootReference.Child("parents").Child(parentId).Child("children");
        var task = childrenRef.GetValueAsync();
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.Exception != null || task.Result == null || !task.Result.Exists)
            yield break;

        // Prefer selected == true
        foreach (var ch in task.Result.Children)
        {
            bool isSel = false;
            bool.TryParse(ch.Child("selected")?.Value?.ToString(), out isSel);
            if (isSel) { selectedChildKey = ch.Key; yield break; }
        }

        // Fallback displayed == true
        foreach (var ch in task.Result.Children)
        {
            bool disp = false;
            bool.TryParse(ch.Child("displayed")?.Value?.ToString(), out disp);
            if (disp) { selectedChildKey = ch.Key; yield break; }
        }
    }

    private IEnumerator EnsureDbInitThenFlush()
    {
        if (!identityReady) yield break;

        // Make the latest letter the only one with Quiz_attempt (global-unique), then init attempt=1
        yield return StartCoroutine(ClearOtherLettersAndInitCurrent());

        didInitInDb = true;

        // Flush any buffered state captured while identity was missing
        if (bufferHasState)
        {
            yield return StartCoroutine(WriteAttemptStateToDb(bufferFinished, bufferErrors));
            bufferHasState = false;
        }
    }

    private string LettersRootPath() =>
        $"parents/{parentId}/children/{selectedChildKey}/letters";

    private string CurrentLetterQuizAttemptPath() =>
        $"{LettersRootPath()}/{currentLetter}/activities/tracing/Quiz_attempt";

    private IEnumerator ClearOtherLettersAndInitCurrent()
    {
        if (!identityReady)
            yield break;

        var root = FirebaseDatabase.DefaultInstance.RootReference;

        // 1) Delete Quiz_attempt for ALL other letters
        if (letters != null && letters.Length > 0)
        {
            var deleteTasks = new List<System.Threading.Tasks.Task>();

            foreach (var l in letters)
            {
                if (string.IsNullOrEmpty(l)) continue;
                if (l == currentLetter) continue;   // skip the current tracing letter

                string path = $"parents/{parentId}/children/{selectedChildKey}/letters/{l}/activities/tracing/Quiz_attempt";
                deleteTasks.Add(root.Child(path).RemoveValueAsync());
            }

            // wait for all deletes
            foreach (var t in deleteTasks)
            {
                while (!t.IsCompleted)
                    yield return null;
            }
        }

        // 2) Clean + init Quiz_attempt/1 for the CURRENT letter
        string basePath = CurrentLetterQuizAttemptPath(); // .../letters/{currentLetter}/activities/tracing/Quiz_attempt
        var updates = new Dictionary<string, object>
    {
        { basePath, null }, // wipe any previous attempt node for this letter
        { $"{basePath}/{currentAttempt}/errors", 0 },
        { $"{basePath}/{currentAttempt}/finished", false }
    };

        var initTask = root.UpdateChildrenAsync(updates);
        yield return new WaitUntil(() => initTask.IsCompleted);

        if (initTask.Exception != null)
        {
            Debug.LogWarning($"[LetterTracingW4] DB init for tracing attempt had an issue: {initTask.Exception}");
        }
    }

    // Block tracing until audio finishes
    public void SetCanTraceAfterAudio(AudioSource animalAudio)
    {
        canTrace = false;
        StartCoroutine(WaitForAnimalAudio(animalAudio));
    }

    private IEnumerator WaitForAnimalAudio(AudioSource animalAudio)
    {
        if (animalAudio == null)
        {
            canTrace = true;
            if (TracingPointsGroup) TracingPointsGroup.SetActive(true);
            if (LetterImageObj) LetterImageObj.SetActive(true);
            yield break;
        }

        // Wait for the clip to start, then finish (handles small start delay)
        while (animalAudio.isActiveAndEnabled && !animalAudio.isPlaying) yield return null;
        while (animalAudio.isActiveAndEnabled && animalAudio.isPlaying) yield return null;

        canTrace = true;

        if (TracingPointsGroup) TracingPointsGroup.SetActive(true);
        if (LetterImageObj) LetterImageObj.SetActive(true);
    }

    private void Update()
    {
        if (!canTrace || !attemptInitialized) return;
        if (currentSegment >= segments.Count) return;

        Vector2 screenPos = Vector2.zero;
        bool down = false, up = false, held = false;

        if (Touchscreen.current != null)
        {
            var touch = Touchscreen.current.primaryTouch;

            screenPos = touch.position.ReadValue();

            down = touch.press.wasPressedThisFrame;
            up = touch.press.wasReleasedThisFrame;

            held = touch.press.isPressed &&
                   (touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Moved ||
                    touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Stationary);
        }
        else if (Mouse.current != null)
        {
            screenPos = Mouse.current.position.ReadValue();
            down = Mouse.current.leftButton.wasPressedThisFrame;
            up = Mouse.current.leftButton.wasReleasedThisFrame;
            held = Mouse.current.leftButton.isPressed;
        }
        else
        {
            return;
        }

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

    private void AddFingerPoint(Vector2 screenPos)
    {
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 10f));
        worldPos.z = 0;
        segmentTrails[currentSegment].Add(worldPos);
        segmentLineRenderers[currentSegment].positionCount = segmentTrails[currentSegment].Count;
        segmentLineRenderers[currentSegment].SetPositions(segmentTrails[currentSegment].ToArray());
    }

    private void HandleSegmentTouchStrict(Vector2 screenPos, bool isDown)
    {
        var segment = segments[currentSegment].points;
        var traced = segmentTraced[currentSegment];
        int i = currentPointInSegment;

        Vector3 drawPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 10f));
        drawPos.z = 0;
        if (segmentTrails[currentSegment].Count == 0 ||
            Vector3.Distance(segmentTrails[currentSegment][segmentTrails[currentSegment].Count - 1], drawPos) > pointSpacing)
        {
            segmentTrails[currentSegment].Add(drawPos);
            segmentLineRenderers[currentSegment].positionCount = segmentTrails[currentSegment].Count;
            segmentLineRenderers[currentSegment].SetPositions(segmentTrails[currentSegment].ToArray());
        }
        if (writing != null && !writing.isPlaying) writing.Play();

        if (i < segment.Count && !traced[i] && IsTouchWithinPoint(screenPos, segment[i], traceRadius))
        {
            traced[i] = true;
            var img = segment[i].GetComponent<Image>();
            if (img != null) img.color = Color.green;

            Vector3 ptPos = segment[i].position;
            ptPos.z = 0;
            if (segmentTrails[currentSegment].Count == 0 ||
                Vector3.Distance(segmentTrails[currentSegment][segmentTrails[currentSegment].Count - 1], ptPos) > 0.01f)
            {
                segmentTrails[currentSegment].Add(ptPos);
                segmentLineRenderers[currentSegment].positionCount = segmentTrails[currentSegment].Count;
                segmentLineRenderers[currentSegment].SetPositions(segmentTrails[currentSegment].ToArray());
            }

            currentPointInSegment++;
        }
        else if (isDown)
        {
            for (int j = 0; j < segment.Count; j++)
                if (traced[j] && IsTouchWithinPoint(screenPos, segment[j], traceRadius))
                    return;

            for (int j = 0; j < segment.Count; j++)
                if (!traced[j] && IsTouchWithinPoint(screenPos, segment[j], traceRadius))
                {
                    TriggerError();
                    return;
                }
        }

        if (!IsTouchInsideAnyZone(screenPos))
        {
            outOfBoundsTimer += Time.deltaTime;
            if (outOfBoundsTimer > outOfBoundsLimit)
            {
                TriggerError();
                outOfBoundsTimer = 0f;
            }
        }
        else
        {
            outOfBoundsTimer = 0f;
        }
    }

    private bool IsTouchWithinPoint(Vector2 touch, RectTransform point, float radius)
    {
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(Camera.main, point.position);
        return Vector2.Distance(screenPoint, touch) <= radius;
    }

    private void TriggerError()
    {
        attemptErrorCount++; // Increment error count

        foreach (var seg in segments)
            foreach (RectTransform pt in seg.points)
            {
                var img = pt.GetComponent<Image>();
                if (img) img.color = Color.red;
            }

        foreach (var lr in segmentLineRenderers) lr.positionCount = 0;
        foreach (var trail in segmentTrails) trail.Clear();

        if (errorAudio != null) errorAudio.Play();

        // Save "in-progress" state: finished = false (overwrite latest)
        SaveAttemptState(finished: false);

        StartCoroutine(ResetAndReinitAfterError());
    }

    private IEnumerator ResetAndReinitAfterError()
    {
        float wait = (errorAudio != null && errorAudio.clip != null) ? errorAudio.clip.length : 0.3f;
        yield return new WaitForSeconds(wait);
        InitializeTracing();
    }

    private bool IsTouchInsideAnyZone(Vector2 screenPos)
    {
        foreach (var zone in lineZones)
            if (RectTransformUtility.RectangleContainsScreenPoint(zone, screenPos, Camera.main))
                return true;
        return false;
    }

    private bool AllSegmentPointsTraced(int segIndex)
    {
        foreach (bool traced in segmentTraced[segIndex])
            if (!traced) return false;
        return true;
    }

    private void InitializeTracing()
    {
        currentSegment = 0;
        currentPointInSegment = 0;
        segmentTraced = new List<List<bool>>();
        segmentTrails = new List<List<Vector3>>();
        foreach (var seg in segments)
        {
            segmentTraced.Add(new List<bool>(new bool[seg.points.Count]));
            segmentTrails.Add(new List<Vector3>());
        }
        waitingForNextSegment = false;
        isDrawing = false;
        startedFromFirstPoint = false;
        foreach (var lr in segmentLineRenderers) lr.positionCount = 0;
        if (writing != null) writing.Stop();

        foreach (var seg in segments)
            foreach (RectTransform pt in seg.points)
            {
                var img = pt.GetComponent<Image>();
                if (img) img.color = Color.white;
            }
    }

    private void FinishLetterTracing()
    {
        canTrace = false;
        if (writing != null) writing.Stop();

        SaveAttemptState(finished: true);

        SaveTracingResult_LocalOnly();

        StartCoroutine(ShowWinningPopUpWithDelay(1f));
    }

    private IEnumerator ShowWinningPopUpWithDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (winningPopUp != null) winningPopUp.SetActive(true);

        if (winningAudio != null)
        {
            if (!winningAudio.gameObject.activeInHierarchy)
                winningAudio.gameObject.SetActive(true);
            winningAudio.Play();
            yield return new WaitUntil(() => !winningAudio.isPlaying);
        }

        UnfreezeAllControls();

        if (winningPopUp != null) winningPopUp.SetActive(false);
        if (PopUpLetterTracing != null) PopUpLetterTracing.SetActive(false);
        if (LetterTracingCanvas != null) LetterTracingCanvas.SetActive(false);

        if (playerScript != null)
        {
            playerScript.canMove = true;
            playerScript.Idle();
        }
    }

    // === Public/state save funnels to DB or buffer ===
    private void SaveAttemptState(bool finished)
    {
        if (!attemptInitialized) return;

        if (identityReady && didInitInDb)
        {
            StartCoroutine(WriteAttemptStateToDb(finished, attemptErrorCount));
        }
        else
        {
            // buffer latest state to be flushed once identity/db init is ready
            bufferHasState = true;
            bufferFinished = finished;
            bufferErrors = attemptErrorCount;
        }
    }

    private IEnumerator WriteAttemptStateToDb(bool finished, int errors)
    {
        if (!identityReady) yield break;

        string attPath = $"{CurrentLetterQuizAttemptPath()}/{currentAttempt}";
        var attRef = FirebaseDatabase.DefaultInstance.RootReference.Child(attPath);

        var updates = new Dictionary<string, object>
        {
            { "errors", errors },
            { "finished", finished }
        };

        var task = attRef.UpdateChildrenAsync(updates);
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.Exception != null)
            Debug.LogWarning($"[LetterTracingW4] Save state failed (kept in UI; will try again on next change): {task.Exception}");
    }

    // Local-only persistence (for quick per-letter error history)
    private void SaveTracingResult_LocalOnly()
    {
        string letterKey = "Letter_" + currentLetter + "_Errors";
        PlayerPrefs.SetInt(letterKey, attemptErrorCount);
        PlayerPrefs.Save();
    }
}
