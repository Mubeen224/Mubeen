using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Firebase.Database;
using Firebase.Auth;
using Mankibo;
using UnityEngine.InputSystem;   // << سطر جديد مهم

public class LetterTracingW2 : MonoBehaviour
{
    // =========================================================
    // Letter & UI
    // =========================================================
    [Header("Letter Info")]
    public string currentLetter = "س";

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

    [Header("Tracing Display Objects")]
    public GameObject TracingPointsGroup;
    public GameObject LetterImageObj;

    // =========================================================
    // Speech (بعد التتريس)
    // =========================================================
    [Header("Speech (after Tracing)")]
    public GameObject speechCanvas;
    public WhisperSR_W2 speechSR;
    public AudioSource speechInstructionAudio;
    public Button speechCloseButton;

    // =========================================================
    // AR Canvas & Math
    // =========================================================
    [Header("AR Canvas & Close")]
    public GameObject arPopup;
    public Button goToARButton;
    public AudioSource arPopupAudio;
    public Button arCloseButton;
    public MathExitManagerW2 mathExitManager;
    public int tracingIndexInManager = 0;

    // =========================================================
    // Tracing Data
    // =========================================================
    [Header("Tracing Segments")]
    public List<SegmentPoints> segments = new List<SegmentPoints>();
    public List<LineRenderer> segmentLineRenderers = new List<LineRenderer>();

    [Header("Zones")]
    public List<RectTransform> lineZones;

    [Header("Tracing Settings")]
    [Range(50f, 500f)] public float traceRadius = 250f;
    public float pointSpacing = 0.05f;
    public float outOfBoundsLimit = 0.5f;

    // States
    private int currentAttempt = 1;
    private int attemptErrorCount = 0;
    private bool attemptInitialized = false;

    private List<List<bool>> segmentTraced;
    private List<List<Vector3>> segmentTrails = new List<List<Vector3>>();
    private int currentSegment = 0;
    private int currentPointInSegment = 0;
    private bool canTrace = false;
    private bool isDrawing = false;
    private bool startedFromFirstPoint = false;
    private bool waitingForNextSegment = false;
    private float outOfBoundsTimer = 0f;

    private World2 playerScript;
    private string parentId;

    [System.Serializable]
    public class SegmentPoints { public List<RectTransform> points; }

    // =========================================================
    // Lifecycle
    // =========================================================
    private void OnEnable()
    {
        playerScript = FindObjectOfType<World2>();

        parentId = FirebaseAuth.DefaultInstance.CurrentUser != null
            ? FirebaseAuth.DefaultInstance.CurrentUser.UserId
            : "debug_parent";

        attemptInitialized = false;

        if (TracingPointsGroup) TracingPointsGroup.SetActive(false);
        if (LetterImageObj) LetterImageObj.SetActive(false);
        if (arPopup) arPopup.SetActive(false);

        EnsureEventSystem();

        if (closeButton)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(OnTracingCloseButton);
        }
    }

    public void StartNewAttempt() => StartCoroutine(LoadLastAttemptNumberAndStart());

    private IEnumerator LoadLastAttemptNumberAndStart()
    {
        string selectedChildKey = CoinsManager.instance.SelectedChildKey;

        // NEW: ضمن تهيئة عقدة الحرف (badge=false) مرة واحدة مثل W1
        yield return StartCoroutine(EnsureLetterNodeInitialized(parentId, selectedChildKey, currentLetter));

        string attemptsPath =
            $"parents/{parentId}/children/{selectedChildKey}/letters/{currentLetter}/activities/tracing/attempts";

        var task = FirebaseDatabase.DefaultInstance.RootReference.Child(attemptsPath).GetValueAsync();
        yield return new WaitUntil(() => task.IsCompleted);

        int maxAttempt = 0;
        if (task.Exception == null && task.Result != null && task.Result.Exists)
            foreach (var att in task.Result.Children)
                if (int.TryParse(att.Key, out int n) && n > maxAttempt) maxAttempt = n;

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
        yield return new WaitUntil(() => animalAudio == null || !animalAudio.isPlaying);
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

    // =========================================================
    // Tracing Core
    // =========================================================
    private void AddFingerPoint(Vector2 screenPos)
    {
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 10f));
        worldPos.z = 0;
        segmentTrails[currentSegment].Add(worldPos);

        var lr = segmentLineRenderers[currentSegment];
        if (lr != null)
        {
            lr.positionCount = segmentTrails[currentSegment].Count;
            lr.SetPositions(segmentTrails[currentSegment].ToArray());
        }
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
            var lr = segmentLineRenderers[currentSegment];
            if (lr != null)
            {
                lr.positionCount = segmentTrails[currentSegment].Count;
                lr.SetPositions(segmentTrails[currentSegment].ToArray());
            }
        }

        if (writing && !writing.isPlaying) writing.Play();

        if (i < segment.Count && !traced[i] && IsTouchWithinPoint(screenPos, segment[i], traceRadius))
        {
            traced[i] = true;
            var img = segment[i].GetComponent<Image>(); if (img) img.color = Color.green;
            currentPointInSegment++;
        }
        else if (isDown && !IsTouchInsideAnyZone(screenPos)) TriggerError();

        if (!IsTouchInsideAnyZone(screenPos))
        {
            outOfBoundsTimer += Time.deltaTime;
            if (outOfBoundsTimer > outOfBoundsLimit) { TriggerError(); outOfBoundsTimer = 0f; }
        }
        else outOfBoundsTimer = 0f;
    }

    private bool IsTouchWithinPoint(Vector2 touch, RectTransform point, float radius)
    {
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(Camera.main, point.position);
        return Vector2.Distance(screenPoint, touch) <= radius;
    }

    private bool IsTouchInsideAnyZone(Vector2 screenPos)
    {
        foreach (var zone in lineZones)
            if (RectTransformUtility.RectangleContainsScreenPoint(zone, screenPos, Camera.main))
                return true;
        return false;
    }

    private void TriggerError()
    {
        attemptErrorCount++;

        foreach (var seg in segments)
            foreach (RectTransform pt in seg.points)
            {
                var img = pt.GetComponent<Image>();
                if (img) img.color = Color.red; // null-safe
            }

        foreach (var lr in segmentLineRenderers) if (lr != null) lr.positionCount = 0;
        foreach (var trail in segmentTrails) trail.Clear();

        if (errorAudio) errorAudio.Play();

        SaveAttemptError();
        StartCoroutine(ResetAndReinitAfterError());
    }

    private void SaveAttemptError()
    {
        string selectedChildKey = CoinsManager.instance.SelectedChildKey;
        if (string.IsNullOrEmpty(selectedChildKey)) return;

        string basePath =
            $"parents/{parentId}/children/{selectedChildKey}/letters/{currentLetter}/activities/tracing/attempts/{currentAttempt}";

        var updates = new Dictionary<string, object>
        {
            [$"{basePath}/errors"] = attemptErrorCount,
            [$"{basePath}/successes"] = 0,       // مطابق W1
            [$"{basePath}/finished"] = false,
            [$"{basePath}/ts"] = ServerValue.Timestamp
        };
        FirebaseDatabase.DefaultInstance.RootReference.UpdateChildrenAsync(updates);
    }

    private IEnumerator ResetAndReinitAfterError()
    {
        float wait = (errorAudio && errorAudio.clip) ? errorAudio.clip.length : 0.3f;
        yield return new WaitForSeconds(wait);
        InitializeTracing();
        if (TracingPointsGroup) TracingPointsGroup.SetActive(true);
        if (LetterImageObj) LetterImageObj.SetActive(true);
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
        waitingForNextSegment = false; isDrawing = false; startedFromFirstPoint = false;

        foreach (var lr in segmentLineRenderers) if (lr != null) lr.positionCount = 0;
        if (writing) writing.Stop();

        foreach (var seg in segments)
            foreach (var pt in seg.points)
            {
                var img = pt.GetComponent<Image>();
                if (img) img.color = Color.white; // null-safe
            }
    }

    private bool AllSegmentPointsTraced(int segIndex)
    {
        foreach (bool traced in segmentTraced[segIndex]) if (!traced) return false;
        return true;
    }

    // =========================================================
    // Success Flow (→ Speech → AR)
    // =========================================================
    private void FinishLetterTracing()
    {
        canTrace = false;
        if (writing) writing.Stop();

        CoinsManager.instance.AddCoinsToSelectedChild(5);
        SaveTracingSuccess();
        StartCoroutine(ShowWinningThenSpeech());
    }

    private IEnumerator ShowWinningThenSpeech()
    {
        if (winningPopUp) winningPopUp.SetActive(true);
        if (winningAudio) { winningAudio.Play(); yield return new WaitUntil(() => !winningAudio.isPlaying); }
        if (winningPopUp) winningPopUp.SetActive(false);
        OpenSpeechAfterTracing();
    }

    private void OpenSpeechAfterTracing()
    {
        if (LetterTracingCanvas) LetterTracingCanvas.SetActive(false);

        if (speechSR == null || speechSR.whisper == null || speechCanvas == null)
        {
            Debug.LogWarning("[LetterTracingW2] Speech references missing → Skipping to AR.");
            SwitchToARPopup();
            return;
        }

        if (!speechCanvas.activeSelf) speechCanvas.SetActive(true);

        // زر الإغلاق: يوقف صوت التعليمات ويهدم SR ويرجع للرياضيات
        if (speechCloseButton != null && mathExitManager != null)
        {
            speechCloseButton.onClick.RemoveAllListeners();
            speechCloseButton.onClick.AddListener(() =>
            {
                try { if (speechInstructionAudio) speechInstructionAudio.Stop(); } catch { }
                if (speechSR != null) speechSR.Teardown();
                if (speechCanvas) speechCanvas.SetActive(false);
                mathExitManager.OpenMathFromTracing(() => { if (speechCanvas) speechCanvas.SetActive(false); }, this, speechCanvas);
            });
        }

        // اقفل زر الميك قبل التعليمات
        if (speechSR != null && speechSR.micButton != null)
            speechSR.micButton.interactable = false;

        // NEW: منطق Whisper مطابق W1 مع fallback
        StartCoroutine(EnsureWhisperReadyThenStart());
    }

    private IEnumerator EnsureWhisperReadyThenStart()
    {
        if (speechSR == null || speechSR.whisper == null)
        {
            Debug.LogWarning("[LetterTracingW2] Speech references missing → Skipping to AR.");
            SwitchToARPopup();
            yield break;
        }

        var wm = speechSR.whisper;
        speechSR.resumePlayerOnSuccess = false;
        speechSR.onSuccess = OnSpeechSuccess;
        speechSR.SetLetter(currentLetter);

        // لا تشغّل تعليمه الداخلي — نحن من نشغّل صوت الشخصية هنا
        speechSR.playInstructionOnEnable = false;
        speechSR.StartTask();

        // حمّل النموذج إن لزم
        if (!wm.IsLoaded && !wm.IsLoading)
        {
            var initTask = wm.InitModel();
            while (!initTask.IsCompleted) yield return null;
        }
        while (wm.IsLoading) yield return null;

        if (!wm.IsLoaded)
        {
            Debug.LogError("[LetterTracingW2] Whisper model FAILED to load. Skipping to AR.");
            SwitchToARPopup();
            yield break;
        }

        // أبقِ زر الميك مغلقًا حتى ينتهي صوت التعليمات
        if (speechSR.micButton != null) speechSR.micButton.interactable = false;

        if (speechInstructionAudio != null && speechInstructionAudio.clip != null)
        {
            try { speechInstructionAudio.Stop(); } catch { }
            speechInstructionAudio.Play();

            while (speechInstructionAudio.isPlaying) yield return null;

            if (speechSR.micButton != null) speechSR.micButton.interactable = true;
        }
        else
        {
            if (speechSR.micButton != null) speechSR.micButton.interactable = true;
        }
    }

    private void OnSpeechSuccess()
    {
        if (speechCanvas) speechCanvas.SetActive(false);
        SwitchToARPopup();
    }

    private void SwitchToARPopup()
    {
        if (LetterTracingCanvas) LetterTracingCanvas.SetActive(false);
        EnsureEventSystem();

        if (arPopup)
        {
            arPopup.SetActive(true);
            PrepareARRootForInput(arPopup);
        }

        if (arPopupAudio) arPopupAudio.Play();

        if (goToARButton)
        {
            goToARButton.onClick.RemoveAllListeners();
            goToARButton.onClick.AddListener(GoToARScene);
        }

        if (arCloseButton && mathExitManager)
        {
            arCloseButton.onClick.RemoveAllListeners();
            arCloseButton.onClick.AddListener(() =>
            {
                if (arPopup) arPopup.SetActive(false);
                mathExitManager.OpenMathFromAR(onCorrect: CloseEverything, arCanvasToReturn: arPopup, arReturnAudio: arPopupAudio);
            });
        }
    }

    private void GoToARScene()
    {
        var player = FindObjectOfType<World2>();
        if (player != null) GameSessionworld2.SavePlayerState(player.transform);
        GameSessionworld2.LaunchAR(currentLetter);
    }

    private void SaveTracingSuccess()
    {
        string selectedChildKey = CoinsManager.instance.SelectedChildKey;
        if (string.IsNullOrEmpty(selectedChildKey)) return;

        string basePath =
            $"parents/{parentId}/children/{selectedChildKey}/letters/{currentLetter}/activities/tracing/attempts/{currentAttempt}";

        var updates = new Dictionary<string, object>
        {
            [$"{basePath}/errors"] = attemptErrorCount,
            [$"{basePath}/successes"] = 1,
            [$"{basePath}/finished"] = true,
            [$"{basePath}/ts"] = ServerValue.Timestamp
        };
        FirebaseDatabase.DefaultInstance.RootReference.UpdateChildrenAsync(updates);
    }

    // =========================================================
    // Helpers
    // =========================================================
    private void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        UnityEngine.Object.DontDestroyOnLoad(es);
    }

    private void PrepareARRootForInput(GameObject root)
    {
        var arCanvas = root.GetComponentInParent<Canvas>(true);
        if (arCanvas)
        {
            if (!arCanvas.gameObject.activeInHierarchy) arCanvas.gameObject.SetActive(true);
            if (!arCanvas.GetComponent<GraphicRaycaster>()) arCanvas.gameObject.AddComponent<GraphicRaycaster>();
        }
    }

    public void OnTracingCloseButton()
    {
        if (!mathExitManager) return;
        if (LetterTracingCanvas) LetterTracingCanvas.SetActive(false);
        mathExitManager.OpenMathFromTracing(() => { if (LetterTracingCanvas) LetterTracingCanvas.SetActive(false); }, this, LetterTracingCanvas);
    }

    private void CloseEverything()
    {
        if (winningPopUp) winningPopUp.SetActive(false);
        if (PopUpLetterTracing) PopUpLetterTracing.SetActive(false);
        if (LetterTracingCanvas) LetterTracingCanvas.SetActive(false);
        if (speechCanvas) speechCanvas.SetActive(false);
        if (arPopup) arPopup.SetActive(false);

        if (playerScript != null)
        {
            playerScript.canMove = true;
            playerScript.Idle();
        }
    }

    // ====================== Ensure letter has "badge" once (مثل W1) ======================
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
            var updates = new Dictionary<string, object> { [$"{letterPath}/badge"] = false };
            FirebaseDatabase.DefaultInstance.RootReference.UpdateChildrenAsync(updates);
        }
    }
}
