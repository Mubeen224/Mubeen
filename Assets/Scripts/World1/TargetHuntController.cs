using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Firebase
using Firebase.Auth;
using Firebase.Database;

// Resolve Random conflict: use URandom.Range(...)
using URandom = UnityEngine.Random;

public class TargetHuntController : MonoBehaviour
{
    // ===========================
    // Inspector References
    // ===========================

    [Header("References")]
    public YOLOObject365 yolo;
    public TextMeshProUGUI promptText;
    public TextMeshProUGUI timerText;
    public Button pickButton;
    public RectTransform highlightPrefab;

    [Header("Game Settings")]
    public string[] fallbackTargets = new string[] { "شخص", "كوب", "زجاجة", "كتاب" };
    [Range(0f, 1f)] public float minScore = 0.55f;
    public float minBoxSize = 40f;

    [Header("Timer")]
    public float roundTime = 120f;
    public float warnThreshold = 20f;

    [Header("Popups")]
    public GameObject successPopup;
    public GameObject failPopup;
    public GameObject timeUpPopup;
    public float autoHideSuccessAfter = 2.0f;
    public float autoHideFailAfter = 2.0f;
    public float autoHideTimeUpAfter = 2.5f;

    [Header("Audio (SFX)")]
    public AudioSource audioSource;
    public AudioClip successClip, failClip, timeUpClip, warningClip;

    // ===========================
    // Voice Prompts per Word
    // ===========================

    [Header("Voice Prompts (Per Word)")]
    public AudioSource voiceSource;

    [Serializable]
    public class WordPrompt
    {
        [Tooltip("Arabic word EXACTLY as appears in targets list (e.g., خاتم)")]
        public string word;

        [Tooltip("Full audio prompt clip saying: 'ابحث عن <word>'")]
        public AudioClip fullPromptClip;
    }

    [Tooltip("Map each target word to a full spoken prompt clip")]
    public List<WordPrompt> wordPrompts = new List<WordPrompt>();

    private readonly Dictionary<string, AudioClip> promptMap = new Dictionary<string, AudioClip>();

    [Header("Effects")]
    public float flashDuration = 0.5f;
    public int flashCount = 3;
    public float shakeDuration = 0.5f;
    public float shakeStrength = 10f;

    // ===========================
    // Round State
    // ===========================

    private string currentTargetAr;
    private RectTransform liveHighlight;
    private float timeLeft;
    private bool roundActive, warningPlayed;
    private Coroutine popupRoutine;
    private bool rewardedThisRound;

    // ===========================
    // Attempts / Firebase State
    // ===========================

    private bool attemptInitialized = false;
    private int currentAttempt = 0;
    private int attemptErrorCount = 0;
    private string parentId;
    private string selectedChildKey;
    private string currentLetter;

    // ===========================
    // Letter-specific Target Lists
    // ===========================

    private static readonly Dictionary<string, List<string>> LetterTargets =
        new Dictionary<string, List<string>>()
    {
        { "خ", new List<string> { "خاتم", "خبز", "خيمة", "خيار", "خلاط" } },
        { "ر", new List<string> { "رف", "ربطة عنق", "ريموت" }  }
    };

    private List<string> activeTargets = new List<string>();

    // ==============================
    // Unity Lifecycle
    // ==============================

    void Start()
    {
        currentLetter = string.IsNullOrEmpty(GameSession.CurrentLetter) ? "" : GameSession.CurrentLetter;

        parentId = FirebaseAuth.DefaultInstance.CurrentUser != null
            ? FirebaseAuth.DefaultInstance.CurrentUser.UserId
            : "debug_parent";

        if (pickButton != null)
        {
            pickButton.onClick.RemoveAllListeners();
            pickButton.onClick.AddListener(OnPickPressed);
        }

        if (highlightPrefab != null && yolo != null && yolo.displayImage != null)
        {
            liveHighlight = Instantiate(highlightPrefab, yolo.displayImage.rectTransform.parent);
            liveHighlight.gameObject.SetActive(false);
        }

        PrepareTargetsForCurrentLetter();

        BuildPromptMap();

        HideAllPopups();

        StartCoroutine(InitChildThenStartRound());
    }

    void OnDisable()
    {
        StopVoicePrompt();
        if (yolo != null) yolo.ForceReleaseCamera();
    }

    void OnDestroy()
    {
        if (pickButton != null)
            pickButton.onClick.RemoveListener(OnPickPressed);

        StopVoicePrompt();
        if (yolo != null) yolo.ForceReleaseCamera();
    }

    void OnApplicationPause(bool pause)
    {
        if (pause)
        {
            StopVoicePrompt();
            if (yolo != null) yolo.ForceReleaseCamera();
        }
    }

    // ==============================
    // Initialization
    // ==============================

    IEnumerator InitChildThenStartRound()
    {
        var cm = FindObjectOfType<CoinsManager>();
        if (cm != null && !string.IsNullOrEmpty(CoinsManager.instance.SelectedChildKey))
        {
            selectedChildKey = CoinsManager.instance.SelectedChildKey;
        }
        else
        {
            yield return StartCoroutine(FindSelectedOrDisplayedChildKey());
        }

        yield return StartCoroutine(StartNewAttempt());

        StartNewRound();
        UpdateTimerUI();
    }

    void PrepareTargetsForCurrentLetter()
    {
        if (!string.IsNullOrEmpty(currentLetter) &&
            LetterTargets.TryGetValue(currentLetter, out var list) &&
            list != null && list.Count > 0)
        {
            activeTargets = new List<string>(list);
        }
        else
        {
            activeTargets = new List<string>(fallbackTargets);
        }
    }

    // ==============================
    // Update / Timer
    // ==============================

    void Update()
    {
        if (!roundActive || !attemptInitialized) return;

        timeLeft -= Time.deltaTime;

        if (!warningPlayed && timeLeft <= warnThreshold && timeLeft > 0f)
        {
            warningPlayed = true;

            PlayClip(warningClip);

            if (timerText != null) StartCoroutine(FlashTimer());
            if (pickButton != null) StartCoroutine(ShakeButton(pickButton.transform));
        }

        if (timeLeft <= 0f)
        {
            timeLeft = 0f;
            roundActive = false;

            if (liveHighlight != null) liveHighlight.gameObject.SetActive(false);

            StopVoicePrompt();

            SaveTimeUpAndFinalizeAttempt();

            ShowOnly(timeUpPopup);
            PlayClip(timeUpClip);

            RestartPopupRoutine(AutoHideThen(() =>
            {
                StartCoroutine(StartNewAttempt());
                StartNewRound();
            }, autoHideTimeUpAfter));
        }

        UpdateTimerUI();
    }

    // ==============================
    // Round Control
    // ==============================

    void StartNewRound()
    {
        rewardedThisRound = false;

        string previousTarget = currentTargetAr;
        string nextTarget = "شخص";

        if (activeTargets != null && activeTargets.Count > 0)
        {
            if (activeTargets.Count == 1)
            {
                nextTarget = activeTargets[0];
            }
            else
            {
                string prevNorm = string.IsNullOrEmpty(previousTarget)
                    ? string.Empty
                    : YOLOObject365.CanonicalizeArabic(previousTarget);

                int safety = 0;
                do
                {
                    nextTarget = activeTargets[URandom.Range(0, activeTargets.Count)];
                    safety++;

                    if (string.IsNullOrEmpty(prevNorm) || safety > 10) break;

                } while (YOLOObject365.CanonicalizeArabic(nextTarget) == prevNorm);
            }
        }

        currentTargetAr = nextTarget;

        if (promptText != null)
            promptText.text = YOLOObject365.ShapeArabic($"ابحث عن: {currentTargetAr}");

        if (liveHighlight != null) liveHighlight.gameObject.SetActive(false);

        timeLeft = roundTime;
        roundActive = true;
        warningPlayed = false;

        HideAllPopups();
        UpdateTimerUI();

        PlayTargetPrompt(currentTargetAr);
    }

    void OnPickPressed()
    {
        if (!roundActive || !attemptInitialized || yolo == null) return;

        if (yolo.HasArabicClass(currentTargetAr, minScore, minBoxSize, out var det))
        {
            roundActive = false;

            if (liveHighlight != null)
            {
                PositionHighlight(liveHighlight, det.rectPx);
                liveHighlight.gameObject.SetActive(true);
            }

            StopVoicePrompt();

            AwardCoinsOnce(5);

            SaveARSuccess();

            StartCoroutine(CheckBadgeEligibilityAndSet());

            RestartPopupRoutine(ShowSuccessThenReturn());
        }
        else
        {
            if (liveHighlight != null) liveHighlight.gameObject.SetActive(false);

            attemptErrorCount++;
            SaveAttemptError();

            StopVoicePrompt();

            ShowOnly(failPopup);
            PlayClip(failClip);

            RestartPopupRoutine(AutoHideThen(() =>
            {
                if (promptText != null)
                    promptText.text = YOLOObject365.ShapeArabic($"ابحث عن: {currentTargetAr}");

                PlayTargetPrompt(currentTargetAr);

            }, autoHideFailAfter));
        }
    }

    // ==============================
    // Success Flow
    // ==============================

    IEnumerator ShowSuccessThenReturn()
    {
        ShowOnly(successPopup);

        float duration = autoHideSuccessAfter;
        if (successClip != null) duration = successClip.length;

        PlayClip(successClip);
        yield return new WaitForSeconds(duration);

        HideAllPopups();

        if (yolo != null) yolo.ForceReleaseCamera();

        GameSession.CompleteARAndReturn();
    }

    void AwardCoinsOnce(int amount)
    {
        if (rewardedThisRound) return;
        rewardedThisRound = true;

        var cm = FindObjectOfType<CoinsManager>();
        if (cm != null)
        {
            cm.AddCoinsToSelectedChild(amount);
        }
        else
        {
            StartCoroutine(AddCoinsDirectToFirebase(amount));
        }
    }

    // ==============================
    // Attempts / Firebase Logging
    // ==============================

    IEnumerator StartNewAttempt()
    {
        attemptInitialized = false;
        attemptErrorCount = 0;

        if (string.IsNullOrEmpty(selectedChildKey))
            yield return StartCoroutine(FindSelectedOrDisplayedChildKey());

        string attemptsPath = GetAttemptsPath();
        var task = FirebaseDatabase.DefaultInstance.RootReference.Child(attemptsPath).GetValueAsync();
        yield return new WaitUntil(() => task.IsCompleted);

        int maxAttempt = 0;
        if (task.Exception == null && task.Result != null && task.Result.Exists)
        {
            foreach (var att in task.Result.Children)
            {
                if (int.TryParse(att.Key, out int num) && num > maxAttempt)
                    maxAttempt = num;
            }
        }

        currentAttempt = maxAttempt + 1;
        attemptInitialized = true;

        var initUpdates = new Dictionary<string, object>
        {
            [$"{GetAttemptPath()}/errors"] = 0,
            [$"{GetAttemptPath()}/successes"] = 0,
            [$"{GetAttemptPath()}/finished"] = false,
            [$"{GetAttemptPath()}/ts"] = ServerValue.Timestamp
        };
        FirebaseDatabase.DefaultInstance.RootReference.UpdateChildrenAsync(initUpdates);
    }

    void SaveAttemptError()
    {
        if (!attemptInitialized) return;

        var updates = new Dictionary<string, object>
        {
            [$"{GetAttemptPath()}/errors"] = attemptErrorCount,
            [$"{GetAttemptPath()}/successes"] = 0,
            [$"{GetAttemptPath()}/finished"] = false,
            [$"{GetAttemptPath()}/ts"] = ServerValue.Timestamp
        };
        FirebaseDatabase.DefaultInstance.RootReference.UpdateChildrenAsync(updates);
    }

    void SaveARSuccess()
    {
        if (!attemptInitialized) return;

        var updates = new Dictionary<string, object>
        {
            [$"{GetAttemptPath()}/errors"] = attemptErrorCount,
            [$"{GetAttemptPath()}/successes"] = 1,
            [$"{GetAttemptPath()}/finished"] = true,
            [$"{GetAttemptPath()}/ts"] = ServerValue.Timestamp
        };
        FirebaseDatabase.DefaultInstance.RootReference.UpdateChildrenAsync(updates);
    }

    void SaveTimeUpAndFinalizeAttempt()
    {
        if (!attemptInitialized) return;

        var updates = new Dictionary<string, object>
        {
            [$"{GetAttemptPath()}/errors"] = attemptErrorCount,
            [$"{GetAttemptPath()}/successes"] = 0,
            [$"{GetAttemptPath()}/finished"] = true,
            [$"{GetAttemptPath()}/reason"] = "timeup",
            [$"{GetAttemptPath()}/ts"] = ServerValue.Timestamp
        };
        FirebaseDatabase.DefaultInstance.RootReference.UpdateChildrenAsync(updates);
    }

    string GetAttemptsPath()
    {
        return $"parents/{parentId}/children/{selectedChildKey}/letters/{currentLetter}/activities/ar/attempts";
    }

    string GetAttemptPath() => $"{GetAttemptsPath()}/{currentAttempt}";

    IEnumerator FindSelectedOrDisplayedChildKey()
    {
        if (string.IsNullOrEmpty(parentId)) yield break;

        var db = FirebaseDatabase.DefaultInstance;
        var childrenRef = db.RootReference.Child("parents").Child(parentId).Child("children");
        var getChildren = childrenRef.GetValueAsync();
        yield return new WaitUntil(() => getChildren.IsCompleted);

        if (getChildren.Exception != null || getChildren.Result == null || !getChildren.Result.Exists)
            yield break;

        foreach (var ch in getChildren.Result.Children)
        {
            bool isSel = false;
            bool.TryParse(ch.Child("selected")?.Value?.ToString(), out isSel);
            if (isSel) { selectedChildKey = ch.Key; yield break; }
        }

        foreach (var ch in getChildren.Result.Children)
        {
            bool disp = false;
            bool.TryParse(ch.Child("displayed")?.Value?.ToString(), out disp);
            if (disp) { selectedChildKey = ch.Key; yield break; }
        }
    }

    // ==============================
    // UI Helpers
    // ==============================

    void ShowOnly(GameObject popup)
    {
        HideAllPopups();
        if (popup) popup.SetActive(true);
    }

    public void HideAllPopups()
    {
        if (successPopup) successPopup.SetActive(false);
        if (failPopup) failPopup.SetActive(false);
        if (timeUpPopup) timeUpPopup.SetActive(false);
    }

    IEnumerator AutoHideThen(System.Action afterHide, float delay)
    {
        yield return new WaitForSeconds(delay);
        HideAllPopups();
        afterHide?.Invoke();
    }

    void RestartPopupRoutine(IEnumerator routine)
    {
        if (popupRoutine != null) StopCoroutine(popupRoutine);
        popupRoutine = StartCoroutine(routine);
    }

    void UpdateTimerUI()
    {
        if (timerText == null) return;

        timerText.text = $"{Mathf.CeilToInt(timeLeft)}";

        if (!roundActive) timerText.color = new Color(1f, .5f, .2f);
        else if (timeLeft <= warnThreshold) timerText.color = new Color(1f, .3f, .3f);
        else timerText.color = new Color(.20f, .33f, .55f);
    }

    void PositionHighlight(RectTransform rt, Rect rectPx)
    {
        if (rt == null) return;

        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);

        rt.anchoredPosition = new Vector2(rectPx.x, -rectPx.y);
        rt.sizeDelta = new Vector2(rectPx.width, rectPx.height);
    }

    void PlayClip(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    // ==============================
    // Voice Prompt Logic
    // ==============================

    void BuildPromptMap()
    {
        promptMap.Clear();
        if (wordPrompts == null) return;

        foreach (var e in wordPrompts)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.word) || e.fullPromptClip == null) continue;
            var key = YOLOObject365.CanonicalizeArabic(e.word);
            promptMap[key] = e.fullPromptClip;
        }
    }

    void PlayTargetPrompt(string arabicTarget)
    {
        if (voiceSource == null || string.IsNullOrWhiteSpace(arabicTarget)) return;

        try { voiceSource.Stop(); } catch { }

        var key = YOLOObject365.CanonicalizeArabic(arabicTarget);
        if (promptMap.TryGetValue(key, out var clip) && clip != null)
        {
            voiceSource.PlayOneShot(clip);
        }
    }

    void StopVoicePrompt()
    {
        if (voiceSource != null && voiceSource.isPlaying) voiceSource.Stop();
    }

    // ==============================
    // Coins Fallback (Direct Firebase)
    // ==============================

    IEnumerator AddCoinsDirectToFirebase(int amount)
    {
        var auth = FirebaseAuth.DefaultInstance;
        if (auth.CurrentUser == null) yield break;

        string pid = auth.CurrentUser.UserId;
        var db = FirebaseDatabase.DefaultInstance;

        string childKey = selectedChildKey;
        if (string.IsNullOrEmpty(childKey))
        {
            yield return StartCoroutine(FindSelectedOrDisplayedChildKey());
            childKey = selectedChildKey;
            if (string.IsNullOrEmpty(childKey)) yield break;
        }

        string coinsPath = $"parents/{pid}/children/{childKey}/coins";
        var coinsRef = db.RootReference.Child(coinsPath);

        var tx = coinsRef.RunTransaction(mutable =>
        {
            long current = 0;
            if (mutable.Value != null)
            {
                if (mutable.Value is long l) current = l;
                else if (mutable.Value is double d) current = (long)d;
                else long.TryParse(mutable.Value.ToString(), out current);
            }
            mutable.Value = current + amount;
            return TransactionResult.Success(mutable);
        });

        yield return new WaitForSeconds(0.01f);
        yield return new WaitUntil(() => tx.IsCompleted);
    }

    // ==============================
    // Visual Effects
    // ==============================

    IEnumerator FlashTimer()
    {
        if (timerText == null) yield break;

        Color orig = timerText.color;
        for (int i = 0; i < flashCount; i++)
        {
            timerText.color = Color.red;
            yield return new WaitForSeconds(flashDuration / (flashCount * 2));
            timerText.color = orig;
            yield return new WaitForSeconds(flashDuration / (flashCount * 2));
        }
    }

    IEnumerator ShakeButton(Transform t)
    {
        if (t == null) yield break;

        Vector3 orig = t.localPosition;
        float e = 0f;

        while (e < shakeDuration)
        {
            t.localPosition = orig + new Vector3(
                URandom.Range(-1f, 1f) * shakeStrength,
                URandom.Range(-1f, 1f) * shakeStrength,
                0
            );
            e += Time.deltaTime;
            yield return null;
        }

        t.localPosition = orig;
    }

    // ==============================
    // Badge Logic
    // ==============================

    IEnumerator CheckBadgeEligibilityAndSet()
    {
        if (string.IsNullOrEmpty(parentId))
        {
            var auth = FirebaseAuth.DefaultInstance;
            if (auth.CurrentUser == null) yield break;
            parentId = auth.CurrentUser.UserId;
        }

        if (string.IsNullOrEmpty(selectedChildKey))
            yield return StartCoroutine(FindSelectedOrDisplayedChildKey());

        if (string.IsNullOrEmpty(parentId) || string.IsNullOrEmpty(selectedChildKey) || string.IsNullOrEmpty(currentLetter))
            yield break;

        var db = FirebaseDatabase.DefaultInstance;

        string letterPath = $"parents/{parentId}/children/{selectedChildKey}/letters/{currentLetter}";
        var badgeTask = db.RootReference.Child(letterPath).Child("badge").GetValueAsync();
        yield return new WaitUntil(() => badgeTask.IsCompleted);

        if (badgeTask.Exception == null && badgeTask.Result != null && badgeTask.Result.Exists)
        {
            bool already = false;
            bool.TryParse(badgeTask.Result.Value?.ToString(), out already);
            if (already) yield break;
        }

        string tracingAttemptsPath = $"{letterPath}/activities/tracing/attempts";
        string srAttemptsPath = $"{letterPath}/activities/SR/attempts";

        var tracingTask = db.RootReference.Child(tracingAttemptsPath).GetValueAsync();
        var srTask = db.RootReference.Child(srAttemptsPath).GetValueAsync();

        while (!tracingTask.IsCompleted || !srTask.IsCompleted) yield return null;

        bool tracingFinished = AnyAttemptFinished(tracingTask);
        bool srFinished = AnyAttemptFinished(srTask);

        if (tracingFinished && srFinished)
        {
            var updates = new Dictionary<string, object>
            {
                [$"{letterPath}/badge"] = true
            };
            db.RootReference.UpdateChildrenAsync(updates);
        }
    }

    private bool AnyAttemptFinished(System.Threading.Tasks.Task<DataSnapshot> t)
    {
        try
        {
            if (t.Exception != null || t.Result == null || !t.Result.Exists) return false;

            foreach (var att in t.Result.Children)
            {
                var finishedNode = att.Child("finished");
                if (finishedNode != null && finishedNode.Exists)
                {
                    if (bool.TryParse(finishedNode.Value?.ToString(), out bool fin) && fin)
                        return true;
                }

                var succNode = att.Child("successes");
                if (succNode != null && succNode.Exists)
                {
                    if (int.TryParse(succNode.Value?.ToString(), out int succ) && succ > 0)
                        return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }
}
