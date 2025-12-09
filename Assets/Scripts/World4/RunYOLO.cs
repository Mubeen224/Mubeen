using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ArabicSupport;
using TMPro;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Firebase.Auth;
using Firebase.Database;
using System.Text;

public class RunYOLO : MonoBehaviour
{
    [Header("Model + Labels")]
    public ModelAsset modelAsset;
    public TextAsset classesAsset;
    public TextAsset classesArabicAsset;

    [Header("UI")]
    public RawImage displayImage;          // 640x640
    public Texture2D borderTexture;        // 9-sliced optional
    public TMP_FontAsset arabicFont;       // TMP font that supports Arabic
    public TMP_Text targetInfoText;        // shows target word/letter

    [Header("Webcam")]
    public bool useFrontFacing = false;
    public int requestedWidth = 640;
    public int requestedHeight = 480;
    public int requestedFps = 24;

    [Header("NMS thresholds")]
    [Range(0f, 1f)] public float iouThreshold = 0.50f;
    [Range(0f, 1f)] public float scoreThreshold = 0.50f;

    [Header("Runtime")]
    public BackendType backend = BackendType.GPUCompute;
    public bool mirrorHorizontally = true;

    [Header("Target (from PopUpTrigger)")]
    public string targetLetter { get; private set; }
    public string targetWordAr { get; private set; }

    [Header("Timer")]
    public TMP_Text timerText;                 // AR UI
    public float roundDuration = 40f;
    public Color normalTimeColor = Color.white;
    public Color lowTimeColor = new Color(0.9f, 0.2f, 0.2f);
    public AudioSource warningSfx;             // plays at 20 seconds left
    public AudioSource timeUpSfx;

    float timeLeft;
    bool timerRunning;
    Coroutine timerCo;

    [Header("Submission")]
    public Button submitButton;               // hide by default
    public GameObject successPopup;           // CorrectPanel
    public GameObject failPopup;              // WrongPanel
    public float popupDelaySeconds = 1.75f;

    [Header("Win/Lose Audio")]
    public AudioSource winSfx;   // plays when successPopup opens
    public AudioSource loseSfx;  // plays when failPopup opens

    [Header("Win/Lose Canvas (parent of the popups)")]
    public Canvas winLoseCanvas;

    [Header("Submission thresholds")]
    [Range(0f, 1f)] public float requireScoreToSubmit = 0.50f;
    public float buttonGraceSeconds = 0.4f;

    [Header("Times Up")]
    public GameObject timesUpPopup;           // assign TimesUpPanel
    public float timesUpHoldSeconds = 5f;     // how long the panel stays up

    [Header("Attempts/Coins (Firebase)")]
    public int coinsOnSuccess = 5;

    // ==== QuizAttempt constants ====
    private const string QUIZ_NODE = "Quiz_attempt";
    private const int QUIZ_ATTEMPT_NUMBER = 1; // always overwrite attempt "1"

    private bool attemptInitialized = false;
    private int attemptErrorCount = 0;
    private string parentId;
    private string selectedChildKey;
    private string currentLetter;
    private bool rewardedThisRound = false;

    // --- runtime state ---
    float lastAnyDetectionTime = -999f;
    float lastTargetDetectionTime = -999f;
    bool submitLocked;

    // ===== Internals =====
    private Transform displayLocation;
    private Worker worker;
    private string[] labels;          // EN
    private string[] labelsAr;        // AR
    private RenderTexture modelInputRT;   // 640x640
    private Sprite borderSprite;

    private const int imageWidth = 640, imageHeight = 640;
    private WebCamTexture webcam;
    private bool webcamReady;

    private readonly List<GameObject> boxPool = new();
    private Tensor<float> centersToCorners;

    private static readonly Dictionary<string, string> EnToAr = BuildEnToAr();
    private static readonly HashSet<string> _allowedLetters =
        new HashSet<string>(new[] { "ش", "ذ", "ر", "س", "ص", "غ", "خ", "ظ", "ض" });

    private HashSet<string> targetKeysLower = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly Color defaultColor = new Color(0f, 0.55f, 1f, 1f);
    private readonly Color matchColor = new Color(0.0f, 0.7f, 0.1f, 1f);

    public struct BoundingBox
    {
        public float centerX, centerY, width, height;
        public string label;
        public bool isMatch;
    }

    void Start()
    {
        Application.targetFrameRate = 30;

        if (classesAsset == null || string.IsNullOrEmpty(classesAsset.text))
        {
            Debug.LogError("[RunYOLO] classesAsset missing/empty.");
            enabled = false; return;
        }

        labels = classesAsset.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        if (classesArabicAsset != null && !string.IsNullOrEmpty(classesArabicAsset.text))
        {
            labelsAr = classesArabicAsset.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (labelsAr.Length != labels.Length)
            {
                Debug.LogWarning($"[RunYOLO] Arabic file lines ({labelsAr.Length}) != English ({labels.Length}). Ignoring Arabic file.");
                labelsAr = null;
            }
        }

        LoadModel();

        modelInputRT = new RenderTexture(imageWidth, imageHeight, 0, RenderTextureFormat.ARGB32)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        modelInputRT.Create();

        displayLocation = displayImage.transform;

        if (borderTexture != null)
            borderSprite = Sprite.Create(borderTexture, new Rect(0, 0, borderTexture.width, borderTexture.height), new Vector2(0.5f, 0.5f));

        SetupWebcam();

        // ========== Target data ==========
        targetLetter = FetchTargetLetter();
        targetWordAr = FetchTargetWordAr();
        targetWordAr = CanonicalizeArabicWord(targetWordAr); // normalize Arabic word

        targetKeysLower = FetchTargetKeysLower();
        ExpandDetectKeysForArabicWord(targetWordAr, targetKeysLower); // add synonyms’ labels

        // currentLetter used for attempts path
        currentLetter = string.IsNullOrEmpty(targetLetter) ? "" : targetLetter;

        // Firebase identity + start attempt
        parentId = FirebaseAuth.DefaultInstance.CurrentUser != null
            ? FirebaseAuth.DefaultInstance.CurrentUser.UserId
            : "debug_parent";

        StartCoroutine(InitChildThenStartAttempt());
        // ======================================================

        if (targetInfoText != null)
        {
            string info = "";
            if (!string.IsNullOrEmpty(targetWordAr) && !string.IsNullOrEmpty(targetLetter))
                info = $"ﻦﻋ ﺚﺤﺑ: {targetWordAr} - ﻑﺮﺤﻟﺍ: {targetLetter}";
            else if (!string.IsNullOrEmpty(targetLetter))
                info = $"ابحث عن شيء يبدأ بحرف: {targetLetter}";
            else
                info = "—";
            targetInfoText.text = info;
        }

        // UI init
        if (submitButton)
        {
            submitButton.gameObject.SetActive(false);
            submitButton.onClick.RemoveAllListeners();
            submitButton.onClick.AddListener(OnSubmitPressed);
        }
        if (successPopup) successPopup.SetActive(false);
        if (failPopup) failPopup.SetActive(false);
        if (timesUpPopup) timesUpPopup.SetActive(false);

        if (winLoseCanvas)
        {
            winLoseCanvas.sortingOrder = 100;
            winLoseCanvas.gameObject.SetActive(true);
        }

        BeginRoundTimer();
        UpdateTargetInfoUI();

        Debug.Log($"[RunYOLO] Target letter: {targetLetter} | word: {targetWordAr} | keys: {string.Join(",", targetKeysLower)}");
    }

    void LoadModel()
    {
        var model1 = ModelLoader.Load(modelAsset);

        centersToCorners = new Tensor<float>(new TensorShape(4, 4), new float[]
        {
            1, 0, 1, 0,
            0, 1, 0, 1,
            -0.5f, 0, 0.5f, 0,
            0, -0.5f, 0, 0.5f
        });

        int numClasses = Mathf.Max(1, labels.Length);

        var graph = new FunctionalGraph();
        var inputs = graph.AddInputs(model1);
        var pred = Functional.Forward(model1, inputs)[0]; // (1, 4+numClasses, 8400)

        var boxCxCyWh = pred[0, 0..4, ..].Transpose(0, 1);        // (8400,4)
        var allScores = pred[0, 4..(4 + numClasses), ..];         // (numClasses,8400)
        var scoresMax = Functional.ReduceMax(allScores, 0);       // (8400)
        var classIDs = Functional.ArgMax(allScores, 0);           // (8400)
        var boxXyXy = Functional.MatMul(boxCxCyWh, Functional.Constant(centersToCorners)); // (8400,4)

        var keepIdx = Functional.NMS(boxXyXy, scoresMax, iouThreshold, scoreThreshold);

        var outBoxes = Functional.IndexSelect(boxXyXy, 0, keepIdx);   // (N,4)
        var outScores = Functional.IndexSelect(scoresMax, 0, keepIdx); // (N)
        var outClassId = Functional.IndexSelect(classIDs, 0, keepIdx);  // (N)

        worker = new Worker(graph.Compile(outBoxes, outScores, outClassId), backend);
    }

    void SetupWebcam()
    {
        var devices = WebCamTexture.devices;
        if (devices == null || devices.Length == 0)
        {
            Debug.LogError("No webcam devices found.");
            webcamReady = false; return;
        }

        int chosen = 0;

        // On Android: always BACK cam
#if UNITY_ANDROID && !UNITY_EDITOR
        for (int i = 0; i < devices.Length; i++)
        {
            if (!devices[i].isFrontFacing)
            {
                chosen = i;
                break;
            }
        }
#else
        // On other platforms, use front cam
        if (useFrontFacing)
            for (int i = 0; i < devices.Length; i++)
                if (devices[i].isFrontFacing) { chosen = i; break; }
#endif

        webcam = new WebCamTexture(devices[chosen].name, requestedWidth, requestedHeight, requestedFps);
        webcam.Play();

        displayImage.texture = modelInputRT;
        webcamReady = true;
    }

    void Update()
    {
        if (!webcamReady || webcam == null || !webcam.isPlaying || !webcam.didUpdateThisFrame)
            return;

        BlitWebcamToSquare(webcam, modelInputRT, useFrontFacing && mirrorHorizontally);
        ExecuteML();

        if (Input.GetKeyDown(KeyCode.Escape)) Application.Quit();
    }

    // Center-crop + optional mirror
    void BlitWebcamToSquare(Texture src, RenderTexture dst, bool mirrorX)
    {
        float srcW = src.width, srcH = src.height;
        if (srcW <= 0 || srcH <= 0) return;

        float aspect = srcW / srcH;
        Vector2 scale = Vector2.one, offset = Vector2.zero;

        if (aspect > 1f) { scale.x = srcH / srcW; offset.x = (1f - scale.x) * 0.5f; }
        else if (aspect < 1f) { scale.y = srcW / srcH; offset.y = (1f - scale.y) * 0.5f; }

        if (mirrorX) { scale.x = -scale.x; offset.x = 1f - offset.x; }

        Graphics.Blit(src, dst, scale, offset);
    }

    public void ExecuteML()
    {
        ClearAnnotations();

        bool anyDetectedNow = false;
        bool targetDetectedNow = false;

        using var inputTensor = new Tensor<float>(new TensorShape(1, 3, imageHeight, imageWidth));
        TextureConverter.ToTensor(modelInputRT, inputTensor, default);
        worker.Schedule(inputTensor);

        using var boxes = (worker.PeekOutput("output_0") as Tensor<float>).ReadbackAndClone();
        using var scores = (worker.PeekOutput("output_1") as Tensor<float>).ReadbackAndClone();
        using var clsIDs = (worker.PeekOutput("output_2") as Tensor<int>).ReadbackAndClone();

        float dispW = displayImage.rectTransform.rect.width;
        float dispH = displayImage.rectTransform.rect.height;
        float sX = dispW / imageWidth, sY = dispH / imageHeight;

        int N = boxes.shape[0];
        if (N <= 0) return;

        var dets = new List<(int i, float conf)>(N);
        for (int i = 0; i < N; i++) dets.Add((i, scores[i]));
        dets.Sort((a, b) => b.conf.CompareTo(a.conf));

        int maxDraw = Mathf.Min(100, dets.Count);
        for (int k = 0; k < maxDraw; k++)
        {
            int i = dets[k].i;
            float conf = dets[k].conf;

            float x1 = Mathf.Clamp(boxes[i, 0], 0, imageWidth);
            float y1 = Mathf.Clamp(boxes[i, 1], 0, imageHeight);
            float x2 = Mathf.Clamp(boxes[i, 2], 0, imageWidth);
            float y2 = Mathf.Clamp(boxes[i, 3], 0, imageHeight);

            if (useFrontFacing && mirrorHorizontally)
            {
                float nx1 = imageWidth - x2;
                float nx2 = imageWidth - x1;
                x1 = nx1; x2 = nx2;
            }

            float w = (x2 - x1) * sX;
            float h = (y2 - y1) * sY;
            if (w < 16f || h < 16f) continue;

            float cx = ((x1 + x2) * 0.5f) * sX - dispW * 0.5f;
            float cy = ((y1 + y2) * 0.5f) * sY - dispH * 0.5f;

            int cls = Mathf.Clamp(clsIDs[i], 0, labels.Length - 1);
            string enName = labels[cls];

            // Arabic label for the box
            string arName = GetArabicName(cls, enName);

            // Decide if detection matches the target (English keys + Arabic canonical)
            bool isTarget = IsMatchToTarget(enName, arName);

            string shaped = ArabicFixer.Fix(arName, false, false);
            string percent = (conf * 100f).ToString("0.#", CultureInfo.InvariantCulture);
            string label = $"{shaped} \u200E{percent}%";

            DrawBox(new BoundingBox
            {
                centerX = cx,
                centerY = cy,
                width = w,
                height = h,
                label = label,
                isMatch = isTarget
            }, k, dispH * 0.05f);

            if (conf >= requireScoreToSubmit) anyDetectedNow = true;
            if (isTarget && conf >= requireScoreToSubmit) targetDetectedNow = true;
        }

        if (anyDetectedNow) lastAnyDetectionTime = Time.time;
        if (targetDetectedNow) lastTargetDetectionTime = Time.time;

        bool showSubmit = attemptInitialized &&
                  (Time.time - lastAnyDetectionTime) <= buttonGraceSeconds &&
                  !submitLocked;
        if (submitButton && submitButton.gameObject.activeSelf != showSubmit)
            submitButton.gameObject.SetActive(showSubmit);
    }

    // ===== Fetch target data saved by PopUpTrigger =====
    public static string FetchTargetLetter()
    {
        string letter = PlayerStateStore.targetLetter;
        if (string.IsNullOrEmpty(letter))
            letter = PlayerPrefs.GetString("TargetLetter", "");
        if (!string.IsNullOrEmpty(letter) && !_allowedLetters.Contains(letter))
        {
            Debug.LogWarning($"[RunYOLO] Received letter '{letter}' which is not in the allowed set. Ignoring.");
            return string.Empty;
        }
        return letter ?? string.Empty;
    }

    public static string FetchTargetWordAr()
    {
        string w = PlayerStateStore.targetWordAr;
        if (string.IsNullOrEmpty(w))
            w = PlayerPrefs.GetString("TargetWordAr", "");
        return w ?? string.Empty;
    }

    public static HashSet<string> FetchTargetKeysLower()
    {
        if (PlayerStateStore.targetDetectKeys != null && PlayerStateStore.targetDetectKeys.Count > 0)
            return new HashSet<string>(PlayerStateStore.targetDetectKeys, StringComparer.OrdinalIgnoreCase);

        string packed = PlayerPrefs.GetString("TargetDetectKeys", "");
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(packed))
        {
            foreach (var k in packed.Split('|'))
            {
                var t = k.Trim();
                if (!string.IsNullOrEmpty(t)) set.Add(t);
            }
        }
        return set;
    }

    void UpdateTargetInfoUI()
    {
        if (!targetInfoText) return;

        // Build the Arabic sentence
        string msg = string.IsNullOrEmpty(targetWordAr)
            ? $"ابحث عن شيء يبدأ بحرف {targetLetter}"
            : $"ابحث عن {targetWordAr} — الحرف {targetLetter}";

        // Use ArabicFixer OR RTL flag — not both.
        targetInfoText.isRightToLeftText = false;
        targetInfoText.alignment = TextAlignmentOptions.Center;
        if (arabicFont) targetInfoText.font = arabicFont;
        targetInfoText.text = ArabicFixer.Fix(msg, false, false);
    }

    static readonly char[] ArabicIndicDigits =
    {
        '\u0660','\u0661','\u0662','\u0663','\u0664','\u0665','\u0666','\u0667','\u0668','\u0669'
    };

    static string ToArabicIndic(string latinDigits)
    {
        if (string.IsNullOrEmpty(latinDigits)) return latinDigits;
        var sb = new StringBuilder(latinDigits.Length);
        foreach (var ch in latinDigits)
        {
            if (ch >= '0' && ch <= '9') sb.Append(ArabicIndicDigits[ch - '0']);
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    // =============== Submit Answer ===============
    void OnSubmitPressed()
    {
        if (!attemptInitialized)
        {
            Debug.LogWarning("[RunYOLO] Submit pressed before attempt init; ignoring.");
            submitLocked = false;
            if (submitButton) submitButton.interactable = true;
            return;
        }
        if (submitLocked) return;
        submitLocked = true;
        if (submitButton) submitButton.interactable = false;

        bool success = (Time.time - lastTargetDetectionTime) <= buttonGraceSeconds;

        if (success)
        {
            SaveARSuccess();
            AwardCoinsOnce(coinsOnSuccess);
            StartCoroutine(ShowSuccessThenExit());
        }
        else
        {
            attemptErrorCount++;
            SaveAttemptError();
            StartCoroutine(ShowFailThenContinue());
        }
    }

    IEnumerator ShowSuccessThenExit()
    {
        if (winLoseCanvas) winLoseCanvas.sortingOrder = 100;
        if (successPopup) successPopup.SetActive(true);

        bool waitedOnAudio = false;

        if (winSfx != null && winSfx.clip != null)
        {
            winSfx.Stop();
            winSfx.Play();

            while (!winSfx.isPlaying) yield return null;   // wait for start
            while (winSfx.isPlaying) yield return null;    // wait for end

            waitedOnAudio = true;
        }

        // fallback to fixed delay if no audio assigned
        if (!waitedOnAudio && popupDelaySeconds > 0f)
            yield return new WaitForSeconds(popupDelaySeconds);

        if (successPopup) successPopup.SetActive(false);   // close when audio finishes

        string returnScene = string.IsNullOrEmpty(PlayerStateStore.sceneName) ? "World(4)" : PlayerStateStore.sceneName;

        // Tell World4 to ignore the next AR trigger once
        PlayerStateStore.suppressNextARTrigger = true;

        // release camera before going back to World4
        yield return StartCoroutine(SafeStopCameraBeforeScene());
        SceneManager.LoadScene(returnScene, LoadSceneMode.Single);
    }

    IEnumerator ShowFailThenContinue()
    {
        if (winLoseCanvas) winLoseCanvas.sortingOrder = 100;
        if (failPopup) failPopup.SetActive(true);

        bool waitedOnAudio = false;

        if (loseSfx != null && loseSfx.clip != null)
        {
            loseSfx.Stop();
            loseSfx.Play();

            while (!loseSfx.isPlaying) yield return null;  // wait for start
            while (loseSfx.isPlaying) yield return null;   // wait for end

            waitedOnAudio = true;
        }

        // fallback to fixed delay if no audio assigned
        if (!waitedOnAudio && popupDelaySeconds > 0f)
            yield return new WaitForSeconds(popupDelaySeconds);

        if (failPopup) failPopup.SetActive(false);         // close when audio finishes

        // resume round
        submitLocked = false;
        if (submitButton)
        {
            submitButton.interactable = true;
            bool showSubmit = (Time.time - lastAnyDetectionTime) <= buttonGraceSeconds;
            submitButton.gameObject.SetActive(showSubmit);
        }
    }

    // Main matching logic: English keys (+ synonyms) + Arabic canonical synonyms
    private bool IsMatchToTarget(string modelLabelEn, string modelLabelAr)
    {
        // 1) English-based matching
        if (targetKeysLower != null && targetKeysLower.Count > 0)
        {
            if (targetKeysLower.Contains(modelLabelEn))
                return true;

            string norm = NormalizeKey(modelLabelEn);
            foreach (var key in targetKeysLower)
            {
                if (string.Equals(NormalizeKey(key), norm, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        // 2) Arabic-based canonical matching for synonyms
        if (!string.IsNullOrEmpty(targetWordAr) && !string.IsNullOrEmpty(modelLabelAr))
        {
            string canonTarget = CanonicalizeArabicWord(targetWordAr);
            string canonLabel = CanonicalizeArabicWord(modelLabelAr);

            if (!string.IsNullOrEmpty(canonTarget) &&
                !string.IsNullOrEmpty(canonLabel) &&
                string.Equals(canonTarget, canonLabel, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Canonical Arabic word mapping (for target + Arabic labels)
    private static string CanonicalizeArabicWord(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        s = s.Trim();

        // ===== شبشب group =====
        if (s == "شبشب")
            return "شبشب";

        // These should behave like شبشب
        if (s == "كعب عالي" ||
            s == "حذاء جلدي" ||
            s == "حذاء جلدى")
            return "شبشب";

        // ===== صحن group =====
        if (s == "صحن")
            return "صحن";

        if (s == "طبق" ||
            s == "وعاء")
            return "صحن";

        // ===== صنبور group =====
        if (s == "صنبور")
            return "صنبور";

        // حنفيه / حنفية
        if (s.StartsWith("حنفي", StringComparison.Ordinal))
            return "صنبور";

        // ===== صورة group =====
        if (s == "صورة")
            return "صورة";

        if (s == "صورة/إطار" ||
            s == "صورة / إطار")
            return "صورة";

        // ===== سيارة group =====
        if (s == "سيارة SUV" || s == "سيارة")
            return "سيارة";

        // ===== سلسال group =====
        if (s == "سلسال")
            return "سلسال";

        // قلادة should behave like سلسال
        if (s == "قلادة")
            return "سلسال";

        // Default: no change
        return s;
    }

    // Expand YOLO keys based on Arabic canonical word
    void ExpandDetectKeysForArabicWord(string arWord, HashSet<string> keys)
    {
        if (keys == null) return;
        if (string.IsNullOrWhiteSpace(arWord)) return;

        string canon = CanonicalizeArabicWord(arWord);

        switch (canon)
        {
            case "شبشب":
                // شبشب should also accept different shoe types
                keys.Add("Slippers");
                keys.Add("Sandals");
                keys.Add("Leather Shoes");
                keys.Add("High Heels");
                break;

            case "صحن":
                // صحن = Plate / Bowl
                keys.Add("Plate");
                keys.Add("Bowl");
                break;

            case "صنبور":
                // صنبور = Faucet / Tap
                keys.Add("Faucet");
                break;

            case "صورة":
                // صورة = Picture/Frame
                keys.Add("Picture/Frame");
                break;

            case "سيارة":
                // سيارة = Car / SUV
                keys.Add("Car");
                keys.Add("SUV");
                break;

            case "سلسال":
                // سلسال
                keys.Add("Necklace");
                keys.Add("Pendant");
                break;
        }
    }

    private string GetArabicName(int classIndex, string fallbackEnglish)
    {
        if (labelsAr != null && classIndex >= 0 && classIndex < labelsAr.Length)
            return labelsAr[classIndex];

        if (TryGetFromDict(fallbackEnglish, out var ar)) return ar;

        string norm = NormalizeKey(fallbackEnglish);
        if (TryGetFromDict(norm, out ar)) return ar;

        return fallbackEnglish;
    }

    private static string NormalizeKey(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = s.Replace('-', ' ')
             .Replace('_', ' ')
             .Replace('/', ' ')
             .Replace("  ", " ")
             .Trim();
        return s;
    }

    private static bool TryGetFromDict(string key, out string value)
    {
        if (EnToAr.TryGetValue(key, out value)) return true;

        if (key.Contains("Watch", StringComparison.OrdinalIgnoreCase) && EnToAr.TryGetValue("Watch", out value)) return true;
        if (key.Contains("Clock", StringComparison.OrdinalIgnoreCase) && EnToAr.TryGetValue("Clock", out value)) return true;
        if (key.Contains("Blackboard", StringComparison.OrdinalIgnoreCase) && EnToAr.TryGetValue("Blackboard", out value)) return true;
        if (key.Contains("Whiteboard", StringComparison.OrdinalIgnoreCase) && EnToAr.TryGetValue("Whiteboard", out value)) return true;

        return false;
    }

    public void DrawBox(BoundingBox box, int id, float fontSize)
    {
        GameObject panel;
        if (id < boxPool.Count) { panel = boxPool[id]; panel.SetActive(true); }
        else { panel = CreateNewBox(defaultColor); }

        panel.transform.localPosition = new Vector3(box.centerX, -box.centerY, 0f);

        var rt = panel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(box.width, box.height);

        var img = panel.GetComponent<Image>();
        img.color = defaultColor;

        var tmp = panel.GetComponentInChildren<TextMeshProUGUI>();
        tmp.text = box.label;
        tmp.fontSize = Mathf.Clamp((int)fontSize, 18, 48);
        tmp.alignment = TextAlignmentOptions.MidlineRight;
    }

    public GameObject CreateNewBox(Color color)
    {
        var panel = new GameObject("ObjectBox");
        var panelRT = panel.AddComponent<RectTransform>();
        panel.AddComponent<CanvasRenderer>();
        var img = panel.AddComponent<Image>();
        img.color = color;
        if (borderSprite != null) { img.sprite = borderSprite; img.type = Image.Type.Sliced; }
        else { img.type = Image.Type.Simple; }
        panel.transform.SetParent(displayLocation, false);

        var c = panel.AddComponent<Canvas>();
        c.overrideSorting = true;
        c.sortingOrder = 20;

        var bgObj = new GameObject("LabelBackground");
        var bgRT = bgObj.AddComponent<RectTransform>();
        bgObj.AddComponent<CanvasRenderer>();
        var bgImg = bgObj.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.55f);
        bgObj.transform.SetParent(panel.transform, false);

        bgRT.anchorMin = new Vector2(0f, 1f);
        bgRT.anchorMax = new Vector2(1f, 1f);
        bgRT.pivot = new Vector2(1f, 1f);
        bgRT.offsetMin = new Vector2(0f, -36f);
        bgRT.offsetMax = new Vector2(0f, 0f);

        var text = new GameObject("ObjectLabel");
        var txtRT = text.AddComponent<RectTransform>();
        text.AddComponent<CanvasRenderer>();
        var tmp = text.AddComponent<TextMeshProUGUI>();

        if (arabicFont != null) tmp.font = arabicFont;
        tmp.enableWordWrapping = false;
        tmp.richText = false;
        tmp.color = Color.white;
        tmp.fontSize = 28;
        tmp.alignment = TextAlignmentOptions.MidlineRight;
        tmp.raycastTarget = false;

        text.transform.SetParent(bgObj.transform, false);
        txtRT.anchorMin = new Vector2(0f, 0f);
        txtRT.anchorMax = new Vector2(1f, 1f);
        txtRT.offsetMin = new Vector2(8f, 2f);
        txtRT.offsetMax = new Vector2(-8f, -2f);

        panelRT.sizeDelta = new Vector2(100, 60);

        boxPool.Add(panel);
        return panel;
    }

    public void ClearAnnotations()
    {
        for (int i = 0; i < boxPool.Count; i++)
            boxPool[i].SetActive(false);
    }

    void OnDestroy()
    {
        centersToCorners?.Dispose();
        worker?.Dispose();

        if (webcam != null)
        {
            if (webcam.isPlaying) webcam.Stop();
            Destroy(webcam);
        }

        if (modelInputRT != null)
        {
            modelInputRT.Release();
            Destroy(modelInputRT);
        }

        if (timerCo != null) StopCoroutine(timerCo);
    }

    // ===== Timer =====
    bool warningPlayed = false;
    public void BeginRoundTimer()
    {
        if (timerCo != null) StopCoroutine(timerCo);
        timeLeft = Mathf.Max(1f, roundDuration);
        timerRunning = true;
        warningPlayed = false;   // reset
        UpdateTimerUI();
        timerCo = StartCoroutine(TimerRoutine());
    }

    IEnumerator TimerRoutine()
    {
        while (timeLeft > 0f)
        {
            timeLeft -= Time.deltaTime;
            UpdateTimerUI();

            // Play warning once at <= 20s
            if (!warningPlayed && timeLeft <= 20f)
            {
                if (warningSfx) warningSfx.Play();
                warningPlayed = true;
            }

            yield return null;
        }

        timeLeft = 0f;
        UpdateTimerUI();
        timerRunning = false;
        OnTimeUp();
    }

    void UpdateTimerUI()
    {
        if (!timerText) return;

        int sec = Mathf.CeilToInt(timeLeft);
        timerText.isRightToLeftText = false;
        timerText.text = ToArabicIndic(sec.ToString());

        if (sec <= 10)
        {
            timerText.color = lowTimeColor;
            timerText.alpha = Mathf.Lerp(0.5f, 1f, Mathf.PingPong(Time.time * 4f, 1f));
        }
        else
        {
            timerText.color = normalTimeColor;
            timerText.alpha = 1f;
        }
    }

    void OnTimeUp()
    {
        // lock interactions & hide submit
        submitLocked = true;
        if (submitButton)
        {
            submitButton.interactable = false;
            submitButton.gameObject.SetActive(false);
        }

        // ensure popup canvas is on top
        if (winLoseCanvas) winLoseCanvas.sortingOrder = 100;

        StartCoroutine(TimesUpFlow_PlayAudioAndRestart());
    }

    public void ResetTimer(float newDuration = -1f)
    {
        if (newDuration > 0f) roundDuration = newDuration;
        BeginRoundTimer();
    }

    IEnumerator TimesUpFlow_PlayAudioAndRestart()
    {
        if (timesUpPopup) timesUpPopup.SetActive(true);

        bool waitedOnAudio = false;
        if (timeUpSfx != null && timeUpSfx.clip != null)
        {
            timeUpSfx.Stop();
            timeUpSfx.Play();
            while (!timeUpSfx.isPlaying) yield return null;
            while (timeUpSfx.isPlaying) yield return null;
            waitedOnAudio = true;
        }
        if (!waitedOnAudio && timesUpHoldSeconds > 0f)
            yield return new WaitForSeconds(timesUpHoldSeconds);

        if (timesUpPopup) timesUpPopup.SetActive(false);

        // Mark attempt finished
        SaveTimeUpAndFinalizeAttempt();

        // Refresh ONLY the object, keep the same letter
        RefreshTargetObjectKeepingLetter();

        // Start a fresh attempt for the (same letter, new object)
        yield return StartCoroutine(StartNewAttempt());

        // Clear detection state and restart timer
        ResetRoundState();
        ResetTimer();
    }

    private void RefreshTargetObjectKeepingLetter()
    {
        // figure out which letter the round is on
        string letter = !string.IsNullOrEmpty(currentLetter) ? currentLetter : targetLetter;
        if (string.IsNullOrEmpty(letter))
        {
            Debug.LogWarning("[RunYOLO] RefreshTargetObjectKeepingLetter: letter is empty.");
            return;
        }

        // ask PopUpTrigger for a different object for the same letter
        if (PopUpTrigger.TryPickRandomWordForLetter(letter, out var newWordAr, out var newKeys, excludeWordAr: targetWordAr))
        {
            // persist to shared state
            PlayerStateStore.targetWordAr = newWordAr;
            PlayerStateStore.targetDetectKeys = newKeys;

            PlayerPrefs.SetString("TargetWordAr", newWordAr ?? "");
            PlayerPrefs.SetString("TargetDetectKeys", string.Join("|", newKeys ?? new List<string>()));
            PlayerPrefs.Save();

            // update local runtime fields
            targetWordAr = CanonicalizeArabicWord(newWordAr);
            targetKeysLower = new HashSet<string>(newKeys ?? new List<string>(), System.StringComparer.OrdinalIgnoreCase);
            ExpandDetectKeysForArabicWord(targetWordAr, targetKeysLower);

            // refresh HUD
            UpdateTargetInfoUI();

            // reset submit visibility; it will reappear after new detections
            submitLocked = false;
            if (submitButton)
            {
                submitButton.interactable = true;
                submitButton.gameObject.SetActive(false);
            }

            Debug.Log($"[RunYOLO] TimeUp → new object for '{letter}': {newWordAr} ({string.Join(",", newKeys)})");
        }
        else
        {
            Debug.LogWarning("[RunYOLO] TimeUp: no alternative object available (keeping current).");
        }
    }

    void ResetRoundState()
    {
        lastAnyDetectionTime = -999f;
        lastTargetDetectionTime = -999f;

        submitLocked = false;
        if (submitButton)
        {
            submitButton.interactable = true;
            // stays hidden until a detection appears again
            submitButton.gameObject.SetActive(false);
        }
    }
    // ===== End of Timer =====

    // ===== Coins + Attempts =====
    IEnumerator InitChildThenStartAttempt()
    {
        // Try CoinsManager’s selected child first if it exists
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
    }

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

    // ---- Quiz_attempt for AR ----
    IEnumerator StartNewAttempt()
    {
        attemptInitialized = false;
        attemptErrorCount = 0;
        rewardedThisRound = false;

        if (string.IsNullOrEmpty(currentLetter))
            currentLetter = string.IsNullOrEmpty(targetLetter) ? "" : targetLetter;

        if (string.IsNullOrEmpty(selectedChildKey))
            yield return StartCoroutine(FindSelectedOrDisplayedChildKey());

        // 1) Remove ALL AR + CARDS Quiz_attempts for ALL letters
        yield return StartCoroutine(DeleteAllArAndCardsQuizAttempts());

        // 2) Initialize the single quiz attempt for the current letter
        var initUpdates = new Dictionary<string, object>
        {
            [$"{GetQuizAttemptPath()}/errors"] = 0,
            [$"{GetQuizAttemptPath()}/finished"] = false
        };
        FirebaseDatabase.DefaultInstance.RootReference.UpdateChildrenAsync(initUpdates);

        attemptInitialized = true;
    }

    // Path to: parents/{parentId}/children/{selectedChildKey}/letters/{currentLetter}/activities/ar/Quiz_attempt/1
    string GetQuizAttemptPath()
    {
        return $"parents/{parentId}/children/{selectedChildKey}/letters/{currentLetter}/activities/ar/{QUIZ_NODE}/{QUIZ_ATTEMPT_NUMBER}";
    }

    // Delete ALL AR + CARDS Quiz_attempts for ALL letters for this child
    IEnumerator DeleteAllArAndCardsQuizAttempts()
    {
        if (string.IsNullOrEmpty(parentId) || string.IsNullOrEmpty(selectedChildKey))
            yield break;

        var db = FirebaseDatabase.DefaultInstance;
        string lettersPath = $"parents/{parentId}/children/{selectedChildKey}/letters";

        var task = db.RootReference.Child(lettersPath).GetValueAsync();
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.Exception != null || task.Result == null || !task.Result.Exists)
            yield break;

        var updates = new Dictionary<string, object>();

        foreach (var letterNode in task.Result.Children)
        {
            string letterKey = letterNode.Key;
            if (string.IsNullOrEmpty(letterKey)) continue;

            string basePath = $"{lettersPath}/{letterKey}/activities";

            // Delete AR quiz attempt
            updates[$"{basePath}/ar/{QUIZ_NODE}"] = null;

            // Delete CARDS quiz attempt
            updates[$"{basePath}/cards/Quiz_attempt"] = null;
        }

        if (updates.Count > 0)
            db.RootReference.UpdateChildrenAsync(updates);
    }

    void SaveAttemptError()
    {
        if (!attemptInitialized) return;

        var root = FirebaseDatabase.DefaultInstance.RootReference;
        var updates = new Dictionary<string, object>
        {
            [$"{GetQuizAttemptPath()}/errors"] = attemptErrorCount,
            [$"{GetQuizAttemptPath()}/finished"] = false
        };
        root.UpdateChildrenAsync(updates);
    }

    void SaveARSuccess()
    {
        if (!attemptInitialized) return;

        var root = FirebaseDatabase.DefaultInstance.RootReference;
        var updates = new Dictionary<string, object>
        {
            [$"{GetQuizAttemptPath()}/errors"] = attemptErrorCount,
            [$"{GetQuizAttemptPath()}/finished"] = true
        };
        root.UpdateChildrenAsync(updates);
    }

    void SaveTimeUpAndFinalizeAttempt()
    {
        if (!attemptInitialized) return;

        var root = FirebaseDatabase.DefaultInstance.RootReference;
        var updates = new Dictionary<string, object>
        {
            [$"{GetQuizAttemptPath()}/errors"] = attemptErrorCount,
            [$"{GetQuizAttemptPath()}/finished"] = true
        };
        root.UpdateChildrenAsync(updates);
    }

    void AwardCoinsOnce(int amount)
    {
        if (rewardedThisRound) return;
        rewardedThisRound = true;

        var cm = FindObjectOfType<CoinsManager>();
        if (cm != null)
        {
            cm.AddCoinsToSelectedChild(amount);
            return;
        }
        StartCoroutine(AddCoinsDirectToFirebase(amount));
    }

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

        yield return new WaitUntil(() => tx.IsCompleted);
    }
    // ===== End of Coins + Attempts =====

    // ===== Camera Release =====
    bool _shuttingDown = false;

    void SafeStopCameraImmediate()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;

        // Dispose NN worker first (can hold GPU/RT refs)
        try { worker?.Dispose(); worker = null; } catch { }

        // Stop & destroy webcam
        try
        {
            if (webcam != null)
            {
                if (webcam.isPlaying) webcam.Stop();
                Destroy(webcam);
                webcam = null;
            }
        }
        catch { }

        // Release RT
        try
        {
            if (modelInputRT != null)
            {
                modelInputRT.Release();
                Destroy(modelInputRT);
                modelInputRT = null;
            }
        }
        catch { }
    }

    IEnumerator SafeStopCameraBeforeScene()
    {
        SafeStopCameraImmediate();
        // give OS one frame + tiny delay to fully release device
        yield return null;
        yield return new WaitForSeconds(0.05f);
    }

    void OnDisable() { SafeStopCameraImmediate(); }
    void OnApplicationQuit() { SafeStopCameraImmediate(); }

    // ===== End of Camera Release =====
    // ===== Fallback dictionary =====
    private static Dictionary<string, string> BuildEnToAr()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Map(string en, string ar) { d[en] = ar; }

        Map("Person", "ÔÎÕ");
        Map("Car", "ÓíÇÑÉ");
        Map("SUV", "ÓíÇÑÉ SUV");
        Map("Ring", "ÎÇÊã");
        Map("Bracelet", "ÓæÇÑ");
        Map("Necklace", "ÞáÇÏÉ");
        Map("Watch / Clock", "ÓÇÚÉ");
        Map("Watch", "ÓÇÚÉ íÏ");
        Map("Clock", "ÓÇÚÉ ÍÇÆØ");
        Map("Lamp", "ãÕÈÇÍ");
        Map("Glasses", "äÙøÇÑÉ");
        Map("Bottle", "ÒÌÇÌÉ");
        Map("Desk", "ãßÊÈ");
        Map("Cup", "ßæÈ");
        Map("Street Lights", "ÃÚãÏÉ ÅäÇÑÉ ÇáÔæÇÑÚ");
        Map("Cabinet/shelf", "ÎÒÇäÉ/ÑÝ");
        Map("Handbag/Satchel", "ÍÞíÈÉ íÏ/ÍÞíÈÉ ßÊÝ");
        Map("Plate", "ØÈÞ");
        Map("Picture/Frame", "ÕæÑÉ/ÅØÇÑ");
        Map("Helmet", "ÎæÐÉ");
        Map("Book", "ßÊÇÈ");
        Map("Gloves", "ÞÝÇÒÇÊ");
        Map("Storage box", "ÕäÏæÞ ÊÎÒíä");
        Map("Boat", "ÞÇÑÈ");
        Map("Leather Shoes", "ÃÍÐíÉ ÌáÏíÉ");
        Map("Flower", "ÒåÑÉ");
        Map("Sandals", "ÕäÇÏá");
        Map("Slippers", "ÔÈÔÈ");
        Map("Faucet", "ÍäÝíÉ");
        Map("Soap", "ÕÇÈæä");
        Map("Tie", "ÑÈØÉ ÚäÞ");
        Map("Remote", "ÑíãæÊ");
        Map("Pomegranate", "ÑãÇä");
        Map("Belt", "ÍÒÇã");
        Map("Bow Tie", "ÑÈØÉ ÝÑÇÔÉ");
        Map("Kettle", "ÛáÇíÉ");
        Map("Washing Machine/Drying Machine", "ÛÓÇáÉ/ãÌÝÝ");
        Map("Dishwasher", "ÛÓÇáÉ ÕÍæä");
        Map("Bread", "ÎÈÒ");
        Map("Green Vegetables", "ÎÖÑæÇÊ æÑÞíÉ");
        Map("Cucumber", "ÎíÇÑ");
        Map("Blender", "ÎáÇØ");
        Map("Peach", "ÎæÎ");
        Map("Lettuce", "ÎÓ");

        return d;
    }
}
