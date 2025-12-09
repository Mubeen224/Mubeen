using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Firebase.Auth;
using Firebase.Database;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LevelCircleController : MonoBehaviour
{
    [Header("Debugging")]
    public bool debugLogs = true;

    // ===================== COMPLETED LETTERS PIE =====================
    [Header("Completed Letters (green circular pie)")]
    [Tooltip("Assign the FILLING Image for the completed letters pie.")]
    public Image pie;

    [Header("Letters considered badgable (fixed 9)")]
    public string[] fixedLetters = new string[] { "خ", "ذ", "ر", "س", "ش", "ص", "ض", "ظ", "غ" };

    [Header("Behavior")]
    public bool liveFirebaseUpdates = true;

    private const int DENOMINATOR = 9;

    // Firebase
    private FirebaseAuth auth;
    private FirebaseDatabase db;
    private string parentId;
    private string selectedChildKey;
    private DatabaseReference childrenRef;
    private DatabaseReference lettersRef;

    [Header("Mastered count label (TMP only, Arabic digits)")]
    public TMP_Text masteredCountTMP;

    // ===================== TRACING SLIDER (single, optional) =====================
    [Header("Tracing progress (single red slider for a chosen letter)")]
    public Slider tracingSlider;
    public string letterForTracing = "خ";
    public bool sliderValueIsNormalized = true;

    // ===================== 9 letters × 3 sliders =====================
    [Serializable]
    public class LetterActivityRow
    {
        [Header("Identify the letter & its label")]
        public string letter;
        public TMP_Text letterLabel;

        [Header("Sliders")]
        public Slider tracingSlider; // red
        public Slider speechSlider; // blue
        public Slider otherSlider; // green

        [Header("Activity keys (DB paths under activities/*)")]
        public string speechActivityKey = "speech";
        public string otherActivityKey = "ar";
    }

    [Header("Letter rows (configure 9 entries for خ ذ ر س ش ص ض ظ غ)")]
    public LetterActivityRow[] letterRows;

    // ===================== QUIZ VISUALS (3 letters + stars) =====================
    [Serializable]
    public class QuizLetterSlot
    {
        public TMP_Text letterTMP;
        public RectTransform starsHolder;

        [Tooltip("If true, we show/hide the yellow fill with SetActive. If false, we animate the Image.fillAmount of the yellow fill.")]
        public bool starFillBySetActive = true;

        [Tooltip("Optional: exact name of the yellow fill child (e.g., \"Star_01_Full\"). If empty, we'll look for any child that contains \"_Full\".")]
        public string starFullNodeName = "";
    }

    [Header("Quiz visuals (3 letters of most recent quiz)")]
    public int quizMaxScore = 10;
    public QuizLetterSlot[] quizSlots = new QuizLetterSlot[3];

    // ===================== QUIZ OVERALL PIE =====================
    [Header("Quiz Overall (filling wedge Image)")]
    [Tooltip("Assign the FILLING parent image (child is just background).")]
    public Image quizOverallPie;
    [Tooltip("Just the number (no % sign)")]
    public TMP_Text quizOverallPercentTMP;

    [Header("Quiz Overall Fill Behavior")]
    [Tooltip("If ON, the pie fill uses the reversed digits of the displayed percent (e.g., 58 -> fill 85%).")]
    public bool useReversedArabicPercent = false;

    [Header("Quiz Overall External Filler (SimplePieFill)")]
    [Tooltip("Assign the SimplePieFill component that controls the quiz overall pie Image.")]
    public SimplePieFill quizOverallPieFiller;

    // ===================== UNITY LIFECYCLE =====================

    void Awake()
    {
        Log("Awake() START");

        if (pie == null) Debug.LogWarning("[LevelCircleController] 'pie' (completed letters Image) is not assigned.");
        // if (quizOverallPie == null) Debug.LogWarning("[LevelCircleController] 'quizOverallPie' is not assigned.");
        if (quizOverallPieFiller == null) Debug.LogWarning("[LevelCircleController] 'quizOverallPieFiller' is not assigned (quiz overall pie will not update).");

        InitRowLabels();
        InitQuizSlotsPlaceholders();
        ApplyZeroState();

        Log("Awake() END");

        MakeAllSlidersReadOnly();
    }

    void OnEnable()
    {
        Log("OnEnable() START");

        auth = FirebaseAuth.DefaultInstance;
        db = FirebaseDatabase.DefaultInstance;

        if (auth.CurrentUser == null)
        {
            Debug.LogWarning("[LevelCircleController] No Firebase user is signed in (OnEnable).");
            ApplyZeroState();
            Log("OnEnable() END (no user)");
            return;
        }

        parentId = auth.CurrentUser.UserId;
        Log($"OnEnable(): parentId = {parentId}");

        childrenRef = db.RootReference.Child("parents").Child(parentId).Child("children");
        Log($"OnEnable(): childrenRef path = /parents/{parentId}/children");

        // Every time this object is enabled, refresh the currently selected child.
        StartCoroutine(RefreshForSelectedChild());

        Log("OnEnable() END (coroutine started)");
    }

    void OnDisable()
    {
        Log("OnDisable() START");
        if (lettersRef != null && liveFirebaseUpdates)
        {
            Log($"OnDisable(): Removing ValueChanged listener from {lettersRef.Key}");
            lettersRef.ValueChanged -= OnLettersValueChanged;
            lettersRef = null;
        }
        Log("OnDisable() END");
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        Log("OnValidate() START");
        InitRowLabels();
        InitQuizSlotsPlaceholders();
        ApplyZeroState();
        Log("OnValidate() END");
    }
#endif

    // ===================== HIGH-LEVEL STATE =====================

    void ApplyZeroState()
    {
        Log("ApplyZeroState(): Setting all UI to zero/default state");
        ApplyFill(0, DENOMINATOR);
        UpdateMasteredText(0);
        UpdateTracingSlider(0f);
        UpdateAllLetterRowsToFraction(0f);
        ClearQuizSlotsUI();
        UpdateQuizOverallUI(0f);
    }

    void ApplyLettersSnapshot(DataSnapshot lettersSnap)
    {
        if (lettersSnap == null)
        {
            Log("ApplyLettersSnapshot(): lettersSnap is NULL → zero state");
            ApplyZeroState();
            return;
        }

        if (!lettersSnap.Exists)
        {
            Log("ApplyLettersSnapshot(): lettersSnap.Exists == false → zero state");
            ApplyZeroState();
            return;
        }

        Log($"ApplyLettersSnapshot(): lettersSnap exists, child count = {lettersSnap.ChildrenCount}");

        int count = CountBadgedLetters(lettersSnap, fixedLetters);
        Log($"ApplyLettersSnapshot(): badged letters count = {count}");

        ApplyFill(count, DENOMINATOR);
        UpdateMasteredText(count);

        float tracingFraction = ComputeActivityFraction(lettersSnap, letterForTracing, "tracing");
        Log($"ApplyLettersSnapshot(): tracingFraction for letter '{letterForTracing}' = {tracingFraction}");
        UpdateTracingSlider(tracingFraction);

        Log("ApplyLettersSnapshot(): Updating all letter rows from snapshot");
        UpdateAllLetterRowsFromSnapshot(lettersSnap);

        Log("ApplyLettersSnapshot(): Updating quiz visuals from snapshot");
        float overall = UpdateQuizVisualsFromSnapshot(lettersSnap);
        Log($"ApplyLettersSnapshot(): overall quiz fraction = {overall}");

        UpdateQuizOverallUI(overall);
    }

    // ===================== FIREBASE: LOAD CURRENT CHILD =====================

    IEnumerator RefreshForSelectedChild()
    {
        Log("RefreshForSelectedChild(): START");

        if (childrenRef == null)
        {
            Log("RefreshForSelectedChild(): childrenRef is NULL → zero state");
            ApplyZeroState();
            yield break;
        }

        Log($"RefreshForSelectedChild(): Calling GetValueAsync() on path = {childrenRef.ToString()}");

        var getChildrenTask = childrenRef.GetValueAsync();
        yield return new WaitUntil(() => getChildrenTask.IsCompleted);

        if (getChildrenTask.Exception != null)
        {
            Debug.LogError("[LevelCircleController] Failed to load children: " + getChildrenTask.Exception);
            Log("RefreshForSelectedChild(): GetValueAsync() FAILED → zero state");
            ApplyZeroState();
            yield break;
        }

        var snap = getChildrenTask.Result;
        if (snap == null)
        {
            Log("RefreshForSelectedChild(): children snapshot is NULL → zero state");
            ApplyZeroState();
            yield break;
        }

        if (!snap.Exists)
        {
            Debug.LogWarning("[LevelCircleController] No children found for this parent.");
            Log("RefreshForSelectedChild(): snap.Exists == false → zero state");
            ApplyZeroState();
            yield break;
        }

        Log($"RefreshForSelectedChild(): Children snapshot exists, count = {snap.ChildrenCount}");

        // List children keys for debugging
        foreach (var child in snap.Children)
        {
            Log($"RefreshForSelectedChild(): Found child node key = {child.Key}");
        }

        // Find the child that has selected == true (exactly like StoreSceneController)
        DataSnapshot selectedChildSnap = null;
        int selectedCount = 0;

        foreach (var child in snap.Children)
        {
            bool isSel = false;
            if (child.HasChild("selected") && child.Child("selected").Value != null)
            {
                bool.TryParse(child.Child("selected").Value.ToString(), out isSel);
            }

            Log($"RefreshForSelectedChild(): child key = {child.Key}, selected flag (parsed) = {isSel}, raw = {child.Child("selected").Value}");

            if (isSel)
            {
                selectedCount++;
                if (selectedChildSnap == null)
                {
                    selectedChildSnap = child;
                    selectedChildKey = child.Key;
                    Log($"RefreshForSelectedChild(): Currently chosen selected child key = {selectedChildKey}");
                }
            }
        }

        Log($"RefreshForSelectedChild(): selectedCount = {selectedCount}");

        if (selectedChildSnap == null)
        {
            Debug.LogWarning("[LevelCircleController] No child marked as selected in DB.");
            Log("RefreshForSelectedChild(): No selected child found → zero state");
            ApplyZeroState();
            yield break;
        }

        Debug.Log($"[LevelCircleController] Using selected child key = {selectedChildKey}, selectedCount={selectedCount}");
        Log($"RefreshForSelectedChild(): Using selected child key = {selectedChildKey}, selectedCount = {selectedCount}");

        // Use this child's letters snapshot
        DataSnapshot lettersSnap = selectedChildSnap.HasChild("letters")
            ? selectedChildSnap.Child("letters")
            : null;

        if (lettersSnap == null)
        {
            Log($"RefreshForSelectedChild(): selected child '{selectedChildKey}' has NO 'letters' node → zero state");
        }
        else
        {
            Log($"RefreshForSelectedChild(): selected child '{selectedChildKey}' has 'letters' node, children = {lettersSnap.ChildrenCount}");
        }

        ApplyLettersSnapshot(lettersSnap);

        // Register live updates for this child only
        if (liveFirebaseUpdates)
        {
            Log("RefreshForSelectedChild(): liveFirebaseUpdates is TRUE, registering ValueChanged");

            if (lettersRef != null)
            {
                Log("RefreshForSelectedChild(): Removing previous ValueChanged listener");
                lettersRef.ValueChanged -= OnLettersValueChanged;
            }

            lettersRef = childrenRef.Child(selectedChildKey).Child("letters");
            lettersRef.ValueChanged += OnLettersValueChanged;

            Debug.Log($"[LevelCircleController] Listening to ValueChanged on /parents/{parentId}/children/{selectedChildKey}/letters");
            Log($"RefreshForSelectedChild(): Now listening to ValueChanged on /parents/{parentId}/children/{selectedChildKey}/letters");
        }
        else
        {
            Log("RefreshForSelectedChild(): liveFirebaseUpdates is FALSE, not registering ValueChanged");
        }

        Log("RefreshForSelectedChild(): END");
    }

    void OnLettersValueChanged(object sender, ValueChangedEventArgs e)
    {
        Log("OnLettersValueChanged(): START");

        if (e.DatabaseError != null)
        {
            Debug.LogError("[LevelCircleController] Firebase error: " + e.DatabaseError.Message);
            Log($"OnLettersValueChanged(): DatabaseError = {e.DatabaseError.Message}");
            return;
        }

        if (e.Snapshot == null)
        {
            Log("OnLettersValueChanged(): Snapshot is NULL → zero state");
            ApplyZeroState();
            return;
        }

        Log($"OnLettersValueChanged(): Snapshot exists, children = {e.Snapshot.ChildrenCount}");
        Debug.Log("[LevelCircleController] Letters ValueChanged received.");
        ApplyLettersSnapshot(e.Snapshot);

        Log("OnLettersValueChanged(): END");
    }

    // ===================== PIE HELPERS =====================

    void CalibrateRadialStartFromScreenTop(Image img)
    {
        if (img == null)
        {
            Log("CalibrateRadialStartFromScreenTop(): img is NULL");
            return;
        }

        Log($"CalibrateRadialStartFromScreenTop(): Before calibration, z rot = {img.rectTransform.eulerAngles.z}");

        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Radial360;
        img.preserveAspect = true;

        float z = img.rectTransform.eulerAngles.z;
        float zNorm = (z % 360f + 360f) % 360f;
        int steps = Mathf.RoundToInt(zNorm / 90f) % 4;

        int[] map = { 2, 1, 0, 3 };
        img.fillOrigin = map[steps];
        img.fillClockwise = true;

        Log($"CalibrateRadialStartFromScreenTop(): zNorm = {zNorm}, steps = {steps}, fillOrigin = {img.fillOrigin}, fillClockwise = {img.fillClockwise}");
    }

    int CountBadgedLetters(DataSnapshot lettersSnap, string[] letters)
    {
        Log("CountBadgedLetters(): START");
        int count = 0;
        if (lettersSnap == null)
        {
            Log("CountBadgedLetters(): lettersSnap is NULL → 0");
            return 0;
        }

        foreach (var letter in letters)
        {
            Log($"CountBadgedLetters(): Checking letter '{letter}'");
            if (!lettersSnap.HasChild(letter))
            {
                Log($"CountBadgedLetters(): lettersSnap does NOT have child '{letter}'");
                continue;
            }

            var letterSnap = lettersSnap.Child(letter);

            if (letterSnap.HasChild("badge") && letterSnap.Child("badge").Value != null)
            {
                string v = letterSnap.Child("badge").Value.ToString().ToLower();
                Log($"CountBadgedLetters(): letter '{letter}', badge raw value = {letterSnap.Child("badge").Value}, lower = {v}");
                if (v == "true" || v == "1")
                {
                    count++;
                    Log($"CountBadgedLetters(): letter '{letter}' counted as badged, current count = {count}");
                }
            }
            else
            {
                Log($"CountBadgedLetters(): letter '{letter}' has NO 'badge' child");
            }
        }
        int clamped = Mathf.Min(count, DENOMINATOR);
        Log($"CountBadgedLetters(): END, final count = {count}, clamped = {clamped}");
        return clamped;
    }

    void ApplyFill(int completed, int total)
    {
        Log($"ApplyFill(): completed = {completed}, total = {total}");
        float denom = Mathf.Max(1, total);
        float clamped = Mathf.Clamp(completed, 0, total);
        float fraction = clamped / denom;
        Log($"ApplyFill(): fraction = {fraction}");

        if (pie != null)
        {
            CalibrateRadialStartFromScreenTop(pie);
            pie.fillAmount = Mathf.Clamp01(fraction);
            Log($"ApplyFill(): pie.fillAmount = {pie.fillAmount}");
        }
        else
        {
            Log("ApplyFill(): pie is NULL (not assigned)");
        }
    }

    void UpdateMasteredText(int completed)
    {
        if (masteredCountTMP == null)
        {
            Log("UpdateMasteredText(): masteredCountTMP is NULL");
            return;
        }
        string txt = ToArabicDigits(completed);
        masteredCountTMP.text = txt;
        Log($"UpdateMasteredText(): completed = {completed}, text = {txt}");
    }

    // ===================== ACTIVITY FRACTIONS =====================

    float ComputeActivityFraction(DataSnapshot lettersSnap, string letter, string activityKey)
    {
        Log($"ComputeActivityFraction(): START, letter = '{letter}', activityKey = '{activityKey}'");

        if (lettersSnap == null)
        {
            Log("ComputeActivityFraction(): lettersSnap is NULL → 0");
            return 0f;
        }
        if (string.IsNullOrEmpty(letter))
        {
            Log("ComputeActivityFraction(): letter is NULL or empty → 0");
            return 0f;
        }
        if (string.IsNullOrEmpty(activityKey))
        {
            Log("ComputeActivityFraction(): activityKey is NULL or empty → 0");
            return 0f;
        }

        if (!lettersSnap.HasChild(letter))
        {
            Log($"ComputeActivityFraction(): lettersSnap does NOT have letter '{letter}' → 0");
            return 0f;
        }

        var letterNode = lettersSnap.Child(letter);
        var attemptsPath = letterNode
            .Child("activities").Child(activityKey).Child("attempts");

        if (attemptsPath == null)
        {
            Log("ComputeActivityFraction(): attemptsPath is NULL → 0");
            return 0f;
        }

        Log($"ComputeActivityFraction(): attemptsPath.Exists = {attemptsPath.Exists}, children = {attemptsPath.ChildrenCount}");

        if (!attemptsPath.Exists || attemptsPath.ChildrenCount == 0)
        {
            Log("ComputeActivityFraction(): No attempts found → 0");
            return 0f;
        }

        int totalAttempts = 0;
        int sumSuccesses = 0;

        foreach (var attempt in attemptsPath.Children)
        {
            if (attempt == null || attempt.Value == null)
            {
                Log("ComputeActivityFraction(): attempt or attempt.Value is NULL, skipping");
                continue;
            }

            totalAttempts++;

            int s = 0;
            if (attempt.HasChild("successes") && attempt.Child("successes").Value != null)
            {
                int.TryParse(attempt.Child("successes").Value.ToString(), out s);
            }
            sumSuccesses += Mathf.Max(0, s);

            Log($"ComputeActivityFraction(): attempt key = {attempt.Key}, successes = {s}, running sumSuccesses = {sumSuccesses}, totalAttempts = {totalAttempts}");
        }

        if (totalAttempts <= 0)
        {
            Log("ComputeActivityFraction(): totalAttempts <= 0 → 0");
            return 0f;
        }

        float result = Mathf.Clamp01((float)sumSuccesses / (float)totalAttempts);
        Log($"ComputeActivityFraction(): END, fraction = {result}");
        return result;
    }

    // ===================== MULTI-LETTER UI =====================

    void InitRowLabels()
    {
        Log("InitRowLabels(): START");
        if (letterRows == null)
        {
            Log("InitRowLabels(): letterRows is NULL");
            return;
        }

        foreach (var row in letterRows)
        {
            if (row == null)
            {
                Log("InitRowLabels(): row is NULL");
                continue;
            }

            Log($"InitRowLabels(): row letter = '{row.letter}'");
            if (row.letterLabel != null && !string.IsNullOrEmpty(row.letter))
            {
                row.letterLabel.text = row.letter;
                Log($"InitRowLabels(): Set label to '{row.letter}'");
            }
            else
            {
                Log("InitRowLabels(): letterLabel is NULL or letter string is empty");
            }
        }
        Log("InitRowLabels(): END");
    }

    void UpdateAllLetterRowsFromSnapshot(DataSnapshot lettersSnap)
    {
        Log("UpdateAllLetterRowsFromSnapshot(): START");
        if (letterRows == null)
        {
            Log("UpdateAllLetterRowsFromSnapshot(): letterRows is NULL");
            return;
        }

        foreach (var row in letterRows)
        {
            if (row == null)
            {
                Log("UpdateAllLetterRowsFromSnapshot(): row is NULL");
                continue;
            }

            Log($"UpdateAllLetterRowsFromSnapshot(): Processing row for letter '{row.letter}'");

            if (!string.IsNullOrEmpty(row.letter))
            {
                if (row.tracingSlider != null)
                {
                    float f = ComputeActivityFraction(lettersSnap, row.letter, "tracing");
                    Log($"UpdateAllLetterRowsFromSnapshot(): tracing fraction for '{row.letter}' = {f}");
                    SetSliderValue(row.tracingSlider, f);
                }
                else
                {
                    Log($"UpdateAllLetterRowsFromSnapshot(): tracingSlider is NULL for letter '{row.letter}'");
                }

                if (row.speechSlider != null && !string.IsNullOrEmpty(row.speechActivityKey))
                {
                    float f = ComputeActivityFraction(lettersSnap, row.letter, row.speechActivityKey);
                    Log($"UpdateAllLetterRowsFromSnapshot(): speech fraction for '{row.letter}' ({row.speechActivityKey}) = {f}");
                    SetSliderValue(row.speechSlider, f);
                }
                else
                {
                    Log($"UpdateAllLetterRowsFromSnapshot(): speechSlider is NULL or speechActivityKey empty for letter '{row.letter}'");
                }

                if (row.otherSlider != null && !string.IsNullOrEmpty(row.otherActivityKey))
                {
                    float f = ComputeActivityFraction(lettersSnap, row.letter, row.otherActivityKey);
                    Log($"UpdateAllLetterRowsFromSnapshot(): other fraction for '{row.letter}' ({row.otherActivityKey}) = {f}");
                    SetSliderValue(row.otherSlider, f);
                }
                else
                {
                    Log($"UpdateAllLetterRowsFromSnapshot(): otherSlider is NULL or otherActivityKey empty for letter '{row.letter}'");
                }
            }
            else
            {
                Log("UpdateAllLetterRowsFromSnapshot(): row letter is NULL or empty");
            }
        }

        Log("UpdateAllLetterRowsFromSnapshot(): END");
    }

    void UpdateAllLetterRowsToFraction(float fraction01)
    {
        Log($"UpdateAllLetterRowsToFraction(): Setting all sliders to fraction = {fraction01}");

        if (letterRows == null)
        {
            Log("UpdateAllLetterRowsToFraction(): letterRows is NULL");
            return;
        }

        foreach (var row in letterRows)
        {
            if (row == null)
            {
                Log("UpdateAllLetterRowsToFraction(): row is NULL");
                continue;
            }

            if (row.tracingSlider != null) SetSliderValue(row.tracingSlider, fraction01);
            if (row.speechSlider != null) SetSliderValue(row.speechSlider, fraction01);
            if (row.otherSlider != null) SetSliderValue(row.otherSlider, fraction01);
        }

        Log("UpdateAllLetterRowsToFraction(): END");
    }

    void SetSliderValue(Slider s, float fraction01)
    {
        if (s == null)
        {
            Log("SetSliderValue(): slider is NULL");
            return;
        }

        if (sliderValueIsNormalized)
        {
            float v = fraction01 * s.maxValue;
            s.value = Mathf.Clamp(v, s.minValue, s.maxValue);
            Log($"SetSliderValue(): (normalized) fraction = {fraction01}, slider.value = {s.value}, maxValue = {s.maxValue}");
        }
        else
        {
            float percent = fraction01 * 100f;
            s.value = Mathf.Clamp(percent, s.minValue, s.maxValue);
            Log($"SetSliderValue(): (percent) fraction = {fraction01}, percent = {percent}, slider.value = {s.value}");
        }
    }

    void MakeSliderReadOnly(Slider s)
    {
        if (s == null) return;

        // Disable interaction
        s.interactable = false;

        // Optional: remove keyboard/controller navigation too
        var nav = s.navigation;
        nav.mode = Navigation.Mode.None;
        s.navigation = nav;
    }

    void MakeAllSlidersReadOnly()
    {
        // Single tracing slider
        MakeSliderReadOnly(tracingSlider);

        // 9×3 sliders in letterRows
        if (letterRows != null)
        {
            foreach (var row in letterRows)
            {
                if (row == null) continue;
                MakeSliderReadOnly(row.tracingSlider);
                MakeSliderReadOnly(row.speechSlider);
                MakeSliderReadOnly(row.otherSlider);
            }
        }
    }

    // ===================== QUIZ VISUALS & OVERALL =====================

    void InitQuizSlotsPlaceholders()
    {
        Log("InitQuizSlotsPlaceholders(): START");
        if (quizSlots == null)
        {
            Log("InitQuizSlotsPlaceholders(): quizSlots is NULL");
            return;
        }

        for (int i = 0; i < quizSlots.Length; i++)
        {
            var s = quizSlots[i];
            if (s == null)
            {
                Log($"InitQuizSlotsPlaceholders(): slot index {i} is NULL");
                continue;
            }

            Log($"InitQuizSlotsPlaceholders(): Resetting slot index {i}");
            if (s.letterTMP != null) s.letterTMP.text = "";
            if (s.starsHolder != null)
            {
                EnsureBackgroundsVisible(s.starsHolder);
                ApplyStarsToHolder(s.starsHolder, 0f, s.starFillBySetActive, s.starFullNodeName);
            }
        }
        Log("InitQuizSlotsPlaceholders(): END");
    }

    void ClearQuizSlotsUI()
    {
        Log("ClearQuizSlotsUI(): Clearing quiz slots UI");
        InitQuizSlotsPlaceholders();
    }

    // Returns overall fraction 0..1 (average of up to 3 letters)
    float UpdateQuizVisualsFromSnapshot(DataSnapshot lettersSnap)
    {
        Log("UpdateQuizVisualsFromSnapshot(): START");

        if (quizSlots == null)
        {
            Log("UpdateQuizVisualsFromSnapshot(): quizSlots is NULL → 0");
            ClearQuizSlotsUI();
            return 0f;
        }

        if (lettersSnap == null)
        {
            Log("UpdateQuizVisualsFromSnapshot(): lettersSnap is NULL → 0");
            ClearQuizSlotsUI();
            return 0f;
        }

        List<(string letter, float fraction)> results = new List<(string, float)>();

        Log($"UpdateQuizVisualsFromSnapshot(): lettersSnap.ChildrenCount = {lettersSnap.ChildrenCount}");

        foreach (var letterSnap in lettersSnap.Children)
        {
            string letterKey = letterSnap.Key;
            Log($"UpdateQuizVisualsFromSnapshot(): Checking letter '{letterKey}' for quiz data");

            var activitiesSnap = letterSnap.Child("activities");
            if (!activitiesSnap.Exists)
            {
                Log($"UpdateQuizVisualsFromSnapshot(): letter '{letterKey}' has NO 'activities' node, skipping");
                continue;
            }

            bool taken = false;

            foreach (var activity in activitiesSnap.Children)
            {
                if (activity == null)
                {
                    Log("UpdateQuizVisualsFromSnapshot(): activity child is NULL, skipping");
                    continue;
                }

                Log($"UpdateQuizVisualsFromSnapshot(): Checking activity '{activity.Key}' for Quiz_attempt.1");

                var qa1 = activity.Child("Quiz_attempt").Child("1");
                if (!qa1.Exists)
                {
                    Log($"UpdateQuizVisualsFromSnapshot(): 'Quiz_attempt/1' does NOT exist under activity '{activity.Key}'");
                    continue;
                }

                int errors = 0;
                if (qa1.HasChild("errors") && qa1.Child("errors").Value != null)
                {
                    int.TryParse(qa1.Child("errors").Value.ToString(), out errors);
                }

                int maxScore = Mathf.Max(1, quizMaxScore);
                float score = Mathf.Clamp(maxScore - Mathf.Max(0, errors), 0, maxScore);
                float percent01 = score / maxScore;

                Log($"UpdateQuizVisualsFromSnapshot(): letter '{letterKey}', activity '{activity.Key}', errors = {errors}, score = {score}, percent01 = {percent01}");

                results.Add((letterKey, percent01));
                taken = true;
                break;
            }

            if (!taken)
            {
                Log($"UpdateQuizVisualsFromSnapshot(): letter '{letterKey}' has NO quiz attempts in any activity");
            }

            if (taken && results.Count >= quizSlots.Length)
            {
                Log("UpdateQuizVisualsFromSnapshot(): Reached max quizSlots count, stopping collection");
                break;
            }
        }

        Log($"UpdateQuizVisualsFromSnapshot(): Collected {results.Count} quiz letter results");

        int count = Mathf.Min(quizSlots.Length, results.Count);
        for (int i = 0; i < quizSlots.Length; i++)
        {
            var slot = quizSlots[i];
            if (slot == null)
            {
                Log($"UpdateQuizVisualsFromSnapshot(): slot index {i} is NULL, skipping");
                continue;
            }

            if (i < count)
            {
                var item = results[i];
                Log($"UpdateQuizVisualsFromSnapshot(): Filling slot index {i} with letter '{item.letter}', fraction = {item.fraction}");

                if (slot.letterTMP != null) slot.letterTMP.text = item.letter;

                float starsPercentLocal = item.fraction * 100f;
                if (slot.starsHolder != null)
                {
                    EnsureBackgroundsVisible(slot.starsHolder);
                    ApplyStarsToHolder(slot.starsHolder, starsPercentLocal, slot.starFillBySetActive, slot.starFullNodeName);
                }
            }
            else
            {
                Log($"UpdateQuizVisualsFromSnapshot(): Clearing slot index {i}");
                if (slot.letterTMP != null) slot.letterTMP.text = "";
                if (slot.starsHolder != null)
                {
                    EnsureBackgroundsVisible(slot.starsHolder);
                    ApplyStarsToHolder(slot.starsHolder, 0f, slot.starFillBySetActive, slot.starFullNodeName);
                }
            }
        }

        if (count <= 0)
        {
            Log("UpdateQuizVisualsFromSnapshot(): count <= 0 → overall 0");
            return 0f;
        }

        float sum = 0f;
        for (int i = 0; i < count; i++) sum += results[i].fraction;
        float overall = Mathf.Clamp01(sum / count);

        Log($"UpdateQuizVisualsFromSnapshot(): END, overall fraction = {overall}");
        return overall;
    }

    void EnsureBackgroundsVisible(RectTransform holder)
    {
        if (holder == null)
        {
            Log("EnsureBackgroundsVisible(): holder is NULL");
            return;
        }

        Log($"EnsureBackgroundsVisible(): holder '{holder.name}', childCount = {holder.childCount}");

        int n = holder.childCount;
        for (int i = 0; i < n; i++)
        {
            var bg = holder.GetChild(i);
            if (!bg.gameObject.activeSelf) bg.gameObject.SetActive(true);

            var bgImg = bg.GetComponent<Image>();
            if (bgImg != null)
            {
                bgImg.enabled = true;
                var cg = bg.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = 1f;
            }
        }
    }

    Transform ResolveFullNode(Transform backgroundNode, string preferredName)
    {
        if (backgroundNode == null)
        {
            Log("ResolveFullNode(): backgroundNode is NULL");
            return null;
        }

        Log($"ResolveFullNode(): backgroundNode = {backgroundNode.name}, preferredName = '{preferredName}'");

        if (!string.IsNullOrEmpty(preferredName))
        {
            var exact = backgroundNode.Find(preferredName);
            if (exact != null)
            {
                Log($"ResolveFullNode(): Found exact child '{preferredName}'");
                return exact;
            }
            Log("ResolveFullNode(): preferredName not found as child");
        }

        for (int i = 0; i < backgroundNode.childCount; i++)
        {
            var c = backgroundNode.GetChild(i);
            if (c.name.IndexOf("_Full", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Log($"ResolveFullNode(): Found child containing '_Full': {c.name}");
                return c;
            }
        }

        if (backgroundNode.childCount > 0)
        {
            Log($"ResolveFullNode(): Using first child '{backgroundNode.GetChild(0).name}' as fallback");
            return backgroundNode.GetChild(0);
        }

        Log("ResolveFullNode(): No children in backgroundNode");
        return null;
    }

    void ApplyStarsToHolder(RectTransform holder, float percent, bool useSetActive, string fullName)
    {
        if (holder == null)
        {
            Log("ApplyStarsToHolder(): holder is NULL");
            return;
        }

        Log($"ApplyStarsToHolder(): holder = {holder.name}, percent = {percent}, useSetActive = {useSetActive}, fullName = '{fullName}'");

        float starsExact = Mathf.Clamp(percent, 0f, 100f) / 100f * 5f;
        int fullStars = Mathf.FloorToInt(starsExact);
        float partial = Mathf.Clamp01(starsExact - fullStars);

        Log($"ApplyStarsToHolder(): starsExact = {starsExact}, fullStars = {fullStars}, partial = {partial}");

        int childCount = holder.childCount;
        EnsureBackgroundsVisible(holder);

        for (int i = 0; i < childCount; i++)
        {
            Transform bg = holder.GetChild(i);
            Transform fill = ResolveFullNode(bg, fullName);
            if (fill == null)
            {
                Log($"ApplyStarsToHolder(): bg '{bg.name}' has no fill child resolved");
                continue;
            }

            if (useSetActive)
            {
                bool on =
                    (i < fullStars) ||
                    (i == fullStars && partial > 0f && fullStars < 5);

                if (fill.gameObject.activeSelf != on)
                {
                    Log($"ApplyStarsToHolder(): (SetActive) index = {i}, setting active = {on}, fill = {fill.name}");
                    fill.gameObject.SetActive(on);
                }
            }
            else
            {
                var img = fill.GetComponent<Image>();
                if (img == null)
                {
                    bool on =
                        (i < fullStars) ||
                        (i == fullStars && partial > 0f && fullStars < 5);
                    fill.gameObject.SetActive(on);
                    Log($"ApplyStarsToHolder(): (no Image) index = {i}, active = {on}");
                    continue;
                }

                img.enabled = true;
                img.type = Image.Type.Filled;
                img.fillMethod = Image.FillMethod.Horizontal;
                img.fillOrigin = 0;

                if (i < fullStars)
                {
                    img.fillAmount = 1f;
                    if (!fill.gameObject.activeSelf) fill.gameObject.SetActive(true);
                    Log($"ApplyStarsToHolder(): index = {i}, full star, fillAmount = 1");
                }
                else if (i == fullStars && partial > 0f && fullStars < 5)
                {
                    img.fillAmount = partial;
                    if (!fill.gameObject.activeSelf) fill.gameObject.SetActive(true);
                    Log($"ApplyStarsToHolder(): index = {i}, partial star, fillAmount = {partial}");
                }
                else
                {
                    img.fillAmount = 0f;
                    if (fill.gameObject.activeSelf) fill.gameObject.SetActive(false);
                    Log($"ApplyStarsToHolder(): index = {i}, off star, fillAmount = 0");
                }
            }
        }
    }

    void UpdateQuizOverallUI(float overallFraction01)
    {
        Log($"UpdateQuizOverallUI(): overallFraction01 = {overallFraction01}");

        // 0..1 → 0..100 (this is the RAW calculated percent)
        float f = Mathf.Clamp01(overallFraction01);
        int percentInt = Mathf.RoundToInt(f * 100f);

        // 🔁 Send the RAW percent (not reversed, not Arabic digits) to the SimplePieFill
        if (quizOverallPieFiller != null)
        {
            quizOverallPieFiller.SetFill(percentInt);
            Log($"UpdateQuizOverallUI(): Sent raw percent {percentInt} to SimplePieFill.");
        }
        else
        {
            Log("UpdateQuizOverallUI(): quizOverallPieFiller is NULL, pie will not be updated here.");
        }

        // ✅ This is the DISPLAY percent (reversed digits, Arabic), for the label only
        int displayPercent;
        if (percentInt == 100)
        {
            // For a perfect score, do NOT reverse digits — keep natural order (100 -> ١٠٠)
            displayPercent = 100;
        }
        else
        {
            displayPercent = ReverseTwoDigitPercent(percentInt);
        }

        // ----- Text -----
        if (quizOverallPercentTMP != null)
        {
            string txt = ToArabicDigits(displayPercent); // prints ٩٣ instead of ٣٩
            quizOverallPercentTMP.text = txt;
            Log($"UpdateQuizOverallUI(): raw = {percentInt}, display = {displayPercent}, text = {txt}");
        }


        else
        {
            Log("UpdateQuizOverallUI(): quizOverallPercentTMP is NULL");
        }

        // ----- Pie fill ----- (handled by SimplePieFill; keep this commented)
        // if (quizOverallPie != null)
        // {
        //     CalibrateRadialStartFromScreenTop(quizOverallPie);
        //
        //     float displayFraction = Mathf.Clamp01(displayPercent / 100f); // 93% → 0.93
        //     quizOverallPie.fillAmount = displayFraction;
        //
        //     Log($"UpdateQuizOverallUI(): quizOverallPie.fillAmount = {quizOverallPie.fillAmount}");
        // }
        // else
        // {
        //     Log("UpdateQuizOverallUI(): quizOverallPie is NULL");
        // }
    }




    // ===================== TRACING SLIDER =====================

    void UpdateTracingSlider(float fraction01)
    {
        Log($"UpdateTracingSlider(): fraction01 = {fraction01}");

        if (tracingSlider == null)
        {
            Log("UpdateTracingSlider(): tracingSlider is NULL");
            return;
        }

        if (sliderValueIsNormalized)
        {
            float v = fraction01 * tracingSlider.maxValue;
            tracingSlider.value = Mathf.Clamp(v, tracingSlider.minValue, tracingSlider.maxValue);
            Log($"UpdateTracingSlider(): normalized, slider.value = {tracingSlider.value}, maxValue = {tracingSlider.maxValue}");
        }
        else
        {
            float percent = fraction01 * 100f;
            tracingSlider.value = Mathf.Clamp(percent, tracingSlider.minValue, tracingSlider.maxValue);
            Log($"UpdateTracingSlider(): percent mode, slider.value = {tracingSlider.value}");
        }
    }

    // ===================== UTILITIES =====================

    int ReverseTwoDigitPercent(int p)
    {
        int original = p;
        p = Mathf.Clamp(p, 0, 100);
        if (p < 10 || p == 100)
        {
            Log($"ReverseTwoDigitPercent(): original = {original}, clamped = {p}, no reverse applied");
            return p;
        }
        int tens = p / 10;
        int ones = p % 10;
        int result = ones * 10 + tens;
        Log($"ReverseTwoDigitPercent(): original = {original}, clamped = {p}, result = {result}");
        return result;
    }

    string ToArabicDigits(int number)
    {
        string s = Mathf.Max(0, number).ToString();
        StringBuilder sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c >= '0' && c <= '9')
                sb.Append((char)('\u0660' + (c - '0')));
            else
                sb.Append(c);
        }
        string result = sb.ToString();
        Log($"ToArabicDigits(): input = {number}, output = {result}");
        return result;
    }

    // ------------------ NEW: Display formatting for quiz percent ------------------

  
    // ===================== DEBUG HELPER =====================

    void Log(string msg)
    {
        if (!debugLogs) return;
        Debug.Log("[LevelCircleController] " + msg);
    }
}
