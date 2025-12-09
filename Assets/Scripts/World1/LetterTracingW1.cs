using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Firebase.Database;
using Firebase.Auth;
using Mankibo;
using UnityEngine.InputSystem;   // << سطر جديد مهم

public class LetterTracingW1 : MonoBehaviour
{
    [Header("Card Canvas")]
    public GameObject moveToCardCanvas; // (ذ فقط)

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

    [Header("Tracing Display Objects")]
    public GameObject TracingPointsGroup;
    public GameObject LetterImageObj;

    [Header("Speech (after Tracing)")]
    public GameObject speechCanvas;
    public WhisperSR_W1 speechSR;
    public AudioSource speechInstructionAudio;
    public Button speechCloseButton;

    [Header("AR Canvas & Close")]
    public GameObject arPopup;
    public Button goToARButton;
    public AudioSource arPopupAudio;
    public Button arCloseButton;
    public MathExitManagerW1 mathExitManager;
    public int tracingIndexInManager = 0;

    [Header("Tracing Segments")]
    public List<SegmentPoints> segments = new List<SegmentPoints>();
    public List<LineRenderer> segmentLineRenderers = new List<LineRenderer>();

    [Header("Zones")]
    public List<RectTransform> lineZones;

    [Header("Tracing Settings")]
    [Range(50f, 500f)] public float traceRadius = 250f;
    public float pointSpacing = 0.05f;
    public float outOfBoundsLimit = 0.5f;

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

    private World1 playerScript;
    private string parentId;

    [System.Serializable]
    public class SegmentPoints { public List<RectTransform> points; }

    private bool UsesCardsInsteadOfAR() => currentLetter == "ذ";

    // ====================== Lifecycle ======================
    private void OnEnable()
    {
        playerScript = FindObjectOfType<World1>();

        parentId = FirebaseAuth.DefaultInstance.CurrentUser != null
            ? FirebaseAuth.DefaultInstance.CurrentUser.UserId
            : "debug_parent";

        // لو لسنا على ذ → تأكّدي أن الكروت مطفأة تمامًا
        if (!UsesCardsInsteadOfAR()) ForceCloseCards();

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

        // Ensure the letter node exists with "badge" (default false) once
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


    // ====================== Tracing Core ======================
    private void AddFingerPoint(Vector2 screenPos)
    {
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 10f));
        worldPos.z = 0;
        segmentTrails[currentSegment].Add(worldPos);

        var lr = segmentLineRenderers[currentSegment];
        if (lr) { lr.positionCount = segmentTrails[currentSegment].Count; lr.SetPositions(segmentTrails[currentSegment].ToArray()); }
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
            if (lr) { lr.positionCount = segmentTrails[currentSegment].Count; lr.SetPositions(segmentTrails[currentSegment].ToArray()); }
        }

        if (writing && !writing.isPlaying) writing.Play();

        if (i < segment.Count && !traced[i] && IsTouchWithinPoint(screenPos, segment[i], traceRadius))
        {
            traced[i] = true;
            var img = segment[i].GetComponent<Image>(); if (img) img.color = Color.green;
            currentPointInSegment++;
        }
        else if (isDown && !IsTouchInsideAnyZone(screenPos)) { TriggerError(); }

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
            if (RectTransformUtility.RectangleContainsScreenPoint(zone, screenPos, Camera.main)) return true;
        return false;
    }

    private void TriggerError()
    {
        attemptErrorCount++;

        foreach (var seg in segments)
            foreach (RectTransform pt in seg.points)
            {
                var img = pt.GetComponent<Image>();
                if (img) img.color = Color.red;
            }

        foreach (var lr in segmentLineRenderers) if (lr) lr.positionCount = 0;
        foreach (var trail in segmentTrails) trail.Clear();

        if (errorAudio) errorAudio.Play();

        SaveAttemptError();
        StartCoroutine(ResetAndReinitAfterError());
    }

    private void SaveAttemptError()
    {
        string selectedChildKey = CoinsManager.instance.SelectedChildKey;
        if (string.IsNullOrEmpty(selectedChildKey)) { Debug.LogError("Selected child key is not set!"); return; }

        string basePath =
            $"parents/{parentId}/children/{selectedChildKey}/letters/{currentLetter}/activities/tracing/attempts/{currentAttempt}";

        var updates = new Dictionary<string, object>
        {
            [$"{basePath}/errors"] = attemptErrorCount,
            [$"{basePath}/successes"] = 0,
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

        waitingForNextSegment = false;
        isDrawing = false;
        startedFromFirstPoint = false;

        foreach (var lr in segmentLineRenderers) if (lr) lr.positionCount = 0;
        if (writing) writing.Stop();

        foreach (var seg in segments)
            foreach (var pt in seg.points)
            {
                var img = pt.GetComponent<Image>();
                if (img) img.color = Color.white;
            }
    }

    private bool AllSegmentPointsTraced(int segIndex)
    {
        foreach (bool traced in segmentTraced[segIndex]) if (!traced) return false;
        return true;
    }

    // =================== Success Flow ===================
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

    public void OnCardsFinished()
    {
        if (moveToCardCanvas) moveToCardCanvas.SetActive(false);
        CloseEverything();
    }

    private void OpenSpeechAfterTracing()
    {
        if (!UsesCardsInsteadOfAR()) ForceCloseCards();

        if (LetterTracingCanvas) LetterTracingCanvas.SetActive(false);
        if (moveToCardCanvas) moveToCardCanvas.SetActive(false);

        if (speechSR == null || speechSR.whisper == null || speechCanvas == null)
        {
            Debug.LogWarning("[LetterTracingW1] Speech references missing. Skipping speech and deciding next step.");
            DecideNextIfSpeechUnavailable();
            return;
        }

        // زر الإغلاق للسبيتش: يوقّف الصوت + SR ويعود للرياضيات
        if (speechCloseButton != null && mathExitManager != null)
        {
            speechCloseButton.onClick.RemoveAllListeners();
            speechCloseButton.onClick.AddListener(() =>
            {
                try { if (speechInstructionAudio) speechInstructionAudio.Stop(); } catch { }
                if (speechSR != null) speechSR.Teardown();
                if (speechCanvas) speechCanvas.SetActive(false);

                mathExitManager.OpenMathFromTracing(
                    () => { if (speechCanvas) speechCanvas.SetActive(false); },
                    this,
                    speechCanvas
                );
            });
        }

        if (!speechCanvas.activeSelf) speechCanvas.SetActive(true);

        // اقفل زر الميك مباشرة قبل تشغيل التعليمات
        if (speechSR != null && speechSR.micButton != null)
            speechSR.micButton.interactable = false;

        StartCoroutine(EnsureWhisperReadyThenStart());
    }

    private IEnumerator EnsureWhisperReadyThenStart()
    {
        var wm = speechSR.whisper;
        speechSR.resumePlayerOnSuccess = false;
        speechSR.onSuccess = OnSpeechSuccess;
        speechSR.SetLetter(currentLetter);

        // لا يُشغّل تعليماته الداخلية — نحن من نشغّل صوت الشخصية هنا
        speechSR.playInstructionOnEnable = false;
        speechSR.StartTask();

        if (wm == null)
        {
            Debug.LogError("[Speech] whisper manager reference is null in speechSR.");
            DecideNextIfSpeechUnavailable();
            yield break;
        }

        if (!wm.IsLoaded && !wm.IsLoading)
        {
            var initTask = wm.InitModel();
            while (!initTask.IsCompleted) yield return null;
        }

        while (wm.IsLoading) yield return null;

        if (!wm.IsLoaded)
        {
            string resolvedPath = wm.IsModelPathInStreamingAssets
                ? Path.Combine(Application.streamingAssetsPath, wm.ModelPath)
                : wm.ModelPath;

            long size = 0;
            try { if (File.Exists(resolvedPath)) size = new FileInfo(resolvedPath).Length; } catch { size = 0; }

            Debug.LogError($"[Speech] Whisper model FAILED to load. Path: {resolvedPath} Size: {size} bytes");
            DecideNextIfSpeechUnavailable();
            yield break;
        }

        try
        {
            // شغّل صوت الشخصية
            if (speechInstructionAudio != null && speechInstructionAudio.clip != null)
            {
                try { speechInstructionAudio.Stop(); } catch { }
                speechInstructionAudio.Play();
                // انتظر انتهاء الصوت ثم فعّل زر الميك
                StartCoroutine(EnableMicWhenInstructionEnds());
            }
            else
            {
                // لا يوجد صوت تعليمات — فعّل الزر مباشرة
                if (speechSR != null && speechSR.micButton != null)
                    speechSR.micButton.interactable = true;
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[Speech] Exception while starting speechSR: " + e);
            DecideNextIfSpeechUnavailable();
        }
    }

    // يقفل زر الميك حتى ينتهي صوت التعليمات ثم يفتحه
    private IEnumerator EnableMicWhenInstructionEnds()
    {
        if (speechSR == null || speechSR.micButton == null)
            yield break;

        speechSR.micButton.interactable = false;

        while (speechInstructionAudio != null && speechInstructionAudio.isPlaying)
            yield return null;

        speechSR.micButton.interactable = true;
    }

    // ===== الموحّد لفتح الكروت لحرف ذ (Intro أولاً) =====
    private void OpenCardsNow()
    {
        if (!moveToCardCanvas)
        {
            Debug.LogWarning("[LetterTracingW1] moveToCardCanvas not assigned. Cannot open cards.");
            return;
        }

        // اضبطي الحرف لحارس الـIntro
        GameSession.CurrentLetter = "ذ";

        // فعّلي سلسلة الآباء + CanvasGroup
        ActivateHierarchy(moveToCardCanvas);

        // فعّلي الـIntro فقط وعطّلي البطاقات الآن
        var intro = moveToCardCanvas.GetComponentInChildren<MoveToARW1>(true);
        if (intro)
        {
            intro.enabled = true;
            if (intro.introCanvas) intro.introCanvas.SetActive(true);
            if (intro.cardsPopupAR) intro.cardsPopupAR.SetActive(false);
            intro.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning("[LetterTracingW1] MoveToARW1 not found under moveToCardCanvas.");
        }

        // تأكدي أن CardsRoot مطفأ الآن
        var cardsRoot = FindChildByName(moveToCardCanvas.transform, "CardsRoot");
        if (cardsRoot) cardsRoot.gameObject.SetActive(false);

        // صفّري قفل الإدخال من جولة سابقة (احتياط)
        try { CardButtonW1.ResetInputLock(); } catch { }

        Debug.Log("[LetterTracingW1] Intro cards shown (ذ). Waiting for start button.");
    }

    private void ForceCloseCards()
    {
        if (!moveToCardCanvas) return;

        foreach (var a in moveToCardCanvas.GetComponentsInChildren<AudioSource>(true))
            a.Stop();

        foreach (var m in moveToCardCanvas.GetComponentsInChildren<IntroCardsManagerW1>(true))
            m.enabled = false;
        foreach (var m in moveToCardCanvas.GetComponentsInChildren<MoveToARW1>(true))
            m.enabled = false;

        var cardsRoot = FindChildByName(moveToCardCanvas.transform, "CardsRoot");
        if (cardsRoot) cardsRoot.gameObject.SetActive(false);

        moveToCardCanvas.SetActive(false);

        try { CardButtonW1.ResetInputLock(); } catch { }
    }

    // ================== Fallback / Success routing ==================
    private void DecideNextIfSpeechUnavailable()
    {
        if (speechSR != null) speechSR.Teardown();
        if (speechCanvas) speechCanvas.SetActive(false);
        if (arPopup) arPopup.SetActive(false);

        if (UsesCardsInsteadOfAR())
        {
            if (LetterTracingCanvas) LetterTracingCanvas.SetActive(false);
            Debug.LogWarning("[LetterTracingW1] Speech unavailable → opening cards (ذ).");
            OpenCardsNow();
            return;
        }

        SwitchToARPopup();
    }

    private void OnSpeechSuccess()
    {
        if (speechCanvas) speechCanvas.SetActive(false);

        if (UsesCardsInsteadOfAR()) // ذ
        {
            Debug.Log("[LetterTracingW1] Speech success (ذ) → opening cards.");
            GameSession.CurrentLetter = "ذ"; // تأكيد إضافي للحارس
            if (speechSR != null) speechSR.Teardown();
            if (LetterTracingCanvas) LetterTracingCanvas.SetActive(false);
            OpenCardsNow();
            return;
        }

        SwitchToARPopup();
    }

    private void SwitchToARPopup()
    {
        if (!UsesCardsInsteadOfAR()) ForceCloseCards();
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
        else Debug.LogWarning("[LetterTracingW1] goToARButton غير مُسند.");

        if (arCloseButton && mathExitManager)
        {
            arCloseButton.onClick.RemoveAllListeners();
            arCloseButton.onClick.AddListener(() =>
            {
                if (arPopup) arPopup.SetActive(false);
                mathExitManager.OpenMathFromAR(onCorrect: CloseEverything, arCanvasToReturn: arPopup, arReturnAudio: arPopupAudio);
            });
        }
        else Debug.LogWarning("[LetterTracingW1] arCloseButton أو mathExitManager غير مُسند.");
    }

    private void GoToARScene()
    {
        var player = FindObjectOfType<World1>();
        if (player) GameSession.SavePlayerState(player.transform);
        GameSession.LaunchAR(currentLetter);
    }

    private void SaveTracingSuccess()
    {
        string selectedChildKey = CoinsManager.instance.SelectedChildKey;
        if (string.IsNullOrEmpty(selectedChildKey)) { Debug.LogError("Selected child key is not set!"); return; }

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

    // ====================== Helpers ======================
    private void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(es);
    }

    private void PrepareARRootForInput(GameObject root)
    {
        var arCanvas = root.GetComponentInParent<Canvas>(true);
        if (arCanvas)
        {
            if (!arCanvas.gameObject.activeInHierarchy) arCanvas.gameObject.SetActive(true);
            if (!arCanvas.GetComponent<GraphicRaycaster>()) arCanvas.gameObject.AddComponent<GraphicRaycaster>();
        }
        else
        {
            Debug.LogWarning("[LetterTracingW1] لم يُعثر على Canvas للكائن المحدد.");
        }
    }

    public void OnTracingCloseButton()
    {
        if (!mathExitManager) { Debug.LogWarning("MathExitManagerW1 غير مُسند."); return; }
        if (LetterTracingCanvas) LetterTracingCanvas.SetActive(false);

        mathExitManager.OpenMathFromTracing(
            () => { if (LetterTracingCanvas) LetterTracingCanvas.SetActive(false); },
            this,
            LetterTracingCanvas
        );
    }

    private void CloseEverything()
    {
        ForceCloseCards();
        if (winningPopUp) winningPopUp.SetActive(false);
        if (PopUpLetterTracing) PopUpLetterTracing.SetActive(false);
        if (LetterTracingCanvas) LetterTracingCanvas.SetActive(false);
        if (arPopup) arPopup.SetActive(false);
        if (moveToCardCanvas) moveToCardCanvas.SetActive(false);
        if (speechCanvas) speechCanvas.SetActive(false);

        if (playerScript) { playerScript.canMove = true; playerScript.Idle(); }
    }

    // تفعيل كل سلسلة الآباء + CanvasGroup
    private void ActivateHierarchy(GameObject go)
    {
        if (!go) return;

        // فعّل كل الآباء حتى الجذر
        var t = go.transform;
        while (t != null)
        {
            if (!t.gameObject.activeSelf)
            {
                t.gameObject.SetActive(true);
                Debug.Log($"[LetterTracingW1] Activated parent: {t.name}");
            }
            t = t.parent;
        }

        // فعّل ذات الكائن
        go.SetActive(true);

        // CanvasGroup (لو موجود) → فعّل التفاعل والرسم
        foreach (var cg in go.GetComponentsInChildren<CanvasGroup>(true))
        {
            cg.alpha = Mathf.Max(cg.alpha, 1f);
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }

        EnsureEventSystem();
    }

    // 🔎 helper
    private Transform FindChildByName(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var r = FindChildByName(child, name);
            if (r) return r;
        }
        return null;
    }

    // ====================== NEW: Ensure letter has "badge" once ======================
    private IEnumerator EnsureLetterNodeInitialized(string parentId, string selectedChildKey, string letter)
    {
        if (string.IsNullOrEmpty(parentId) || string.IsNullOrEmpty(selectedChildKey) || string.IsNullOrEmpty(letter))
            yield break;

        string letterPath = $"parents/{parentId}/children/{selectedChildKey}/letters/{letter}";
        var getTask = FirebaseDatabase.DefaultInstance.RootReference.Child(letterPath).GetValueAsync();
        yield return new WaitUntil(() => getTask.IsCompleted);

        if (getTask.Exception != null)
        {
            Debug.LogWarning($"[LetterTracingW1] Failed to read letter node: {getTask.Exception}");
            yield break;
        }

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
