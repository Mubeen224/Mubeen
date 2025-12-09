using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ArabicSupport;

public class YOLOObject365 : MonoBehaviour
{
    // ===========================
    // Inspector Fields (Config)
    // ===========================

    [Header("Model + Labels")]
    public ModelAsset modelAsset;
    public TextAsset classesAsset;
    public TextAsset classesArabicAsset;

    [Header("UI")]
    public RawImage displayImage;
    public Texture2D borderTexture;
    public TMP_FontAsset arabicFont;

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

    [Header("Performance")]
    [Tooltip("Time interval (in seconds) between each inference to reduce processing load")]
    public float inferenceInterval = 0.4f;
    [Tooltip("Maximum number of detected boxes to draw on the screen")]
    public int maxBoxesToDraw = 10;
    [Tooltip("Minimum box size in pixels (width or height) to accept a detection")]
    public float minBoxSize = 32f;

    // ===========================
    // Internal Runtime Members
    // ===========================

    private Transform displayLocation;
    private Worker worker;
    private string[] labels;
    private string[] labelsAr;
    private RenderTexture modelInputRT;
    private Sprite borderSprite;

    private const int imageWidth = 640, imageHeight = 640;
    private WebCamTexture webcam;
    private bool webcamReady;

    private readonly List<GameObject> boxPool = new();
    private Tensor<float> centersToCorners;

    private Tensor<float> inputTensor;
    private float lastInferenceTime = 0f;

    public bool IsCameraRunning => webcam != null && webcam.isPlaying;

    // ===========================
    // Data Structures
    // ===========================

    public class Detection
    {
        public int classId;
        public string englishName;
        public string arabicName;
        public float confidence;
        public Rect rectPx;
    }

    public List<Detection> LastDetections { get; private set; } = new List<Detection>();

    public struct BoundingBox
    {
        public float centerX, centerY, width, height;
        public string label;
    }

    private static readonly Dictionary<string, string> EnToAr = BuildEnToAr();

    // ===========================
    // Unity Lifecycle
    // ===========================

    void Start()
    {
        Application.targetFrameRate = 30;

        if (classesAsset == null || string.IsNullOrEmpty(classesAsset.text))
        {
            Debug.LogError("[YOLOObject365] classesAsset missing/empty.");
            enabled = false; return;
        }

        labels = classesAsset.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        if (classesArabicAsset != null && !string.IsNullOrEmpty(classesArabicAsset.text))
        {
            labelsAr = classesArabicAsset.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (labelsAr.Length != labels.Length)
            {
                Debug.LogWarning($"[YOLOObject365] Arabic file lines ({labelsAr.Length}) != English ({labels.Length}). Ignoring Arabic file.");
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

        inputTensor = new Tensor<float>(new TensorShape(1, 3, imageHeight, imageWidth));

        if (displayImage != null)
            displayLocation = displayImage.transform;

        if (borderTexture != null)
            borderSprite = Sprite.Create(borderTexture, new Rect(0, 0, borderTexture.width, borderTexture.height), new Vector2(0.5f, 0.5f));

        SetupWebcam();
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

        var pred = Functional.Forward(model1, inputs)[0];

        var boxCxCyWh = pred[0, 0..4, ..].Transpose(0, 1);
        var allScores = pred[0, 4..(4 + numClasses), ..];

        var scoresMax = Functional.ReduceMax(allScores, 0);
        var classIDs = Functional.ArgMax(allScores, 0);

        var boxXyXy = Functional.MatMul(boxCxCyWh, Functional.Constant(centersToCorners));

        var keepIdx = Functional.NMS(boxXyXy, scoresMax, iouThreshold, scoreThreshold);

        var outBoxes = Functional.IndexSelect(boxXyXy, 0, keepIdx);
        var outScores = Functional.IndexSelect(scoresMax, 0, keepIdx);
        var outClassId = Functional.IndexSelect(classIDs, 0, keepIdx);

        worker = new Worker(graph.Compile(outBoxes, outScores, outClassId), backend);
    }

    void SetupWebcam()
    {
        StopAndReleaseWebcam(false);

        var devices = WebCamTexture.devices;
        if (devices == null || devices.Length == 0)
        {
            Debug.LogError("No webcam devices found.");
            webcamReady = false; return;
        }

        int chosen = 0;
        if (useFrontFacing)
            for (int i = 0; i < devices.Length; i++)
                if (devices[i].isFrontFacing) { chosen = i; break; }

        webcam = new WebCamTexture(devices[chosen].name, requestedWidth, requestedHeight, requestedFps);
        webcam.Play();

        if (displayImage != null)
            displayImage.texture = modelInputRT;

        webcamReady = true;
    }

    void Update()
    {
        if (!webcamReady || webcam == null || !webcam.isPlaying || !webcam.didUpdateThisFrame)
            return;

        BlitWebcamToSquare(webcam, modelInputRT, useFrontFacing && mirrorHorizontally);

        if (Time.time - lastInferenceTime >= inferenceInterval)
        {
            ExecuteML();
            lastInferenceTime = Time.time;
        }
    }

    void BlitWebcamToSquare(Texture src, RenderTexture dst, bool mirrorX)
    {
        float srcW = src.width, srcH = src.height;
        if (srcW <= 0 || srcH <= 0) return;

        float aspect = srcW / srcH;
        Vector2 scale = Vector2.one, offset = Vector2.zero;

        if (aspect > 1f)
        {
            scale.x = srcH / srcW;
            offset.x = (1f - scale.x) * 0.5f;
        }
        else if (aspect < 1f)
        {
            scale.y = srcW / srcH;
            offset.y = (1f - scale.y) * 0.5f;
        }

        if (mirrorX)
        {
            scale.x = -scale.x;
            offset.x = 1f - offset.x;
        }

        Graphics.Blit(src, dst, scale, offset);
    }

    // ===========================
    // Inference + Detections
    // ===========================

    public void ExecuteML()
    {
        ClearAnnotations();

        TextureConverter.ToTensor(modelInputRT, inputTensor, default);

        worker.Schedule(inputTensor);

        using var boxes = (worker.PeekOutput("output_0") as Tensor<float>).ReadbackAndClone();
        using var scores = (worker.PeekOutput("output_1") as Tensor<float>).ReadbackAndClone();
        using var clsIDs = (worker.PeekOutput("output_2") as Tensor<int>).ReadbackAndClone();

        float dispW = displayImage.rectTransform.rect.width;
        float dispH = displayImage.rectTransform.rect.height;
        float sX = dispW / imageWidth, sY = dispH / imageHeight;

        LastDetections.Clear();

        int N = boxes.shape[0];
        if (N <= 0) return;

        var dets = new List<(int i, float conf)>(N);
        for (int i = 0; i < N; i++) dets.Add((i, scores[i]));
        dets.Sort((a, b) => b.conf.CompareTo(a.conf));

        int maxDraw = Mathf.Min(maxBoxesToDraw, dets.Count);

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

            if (w < minBoxSize || h < minBoxSize) continue;

            float cx = ((x1 + x2) * 0.5f) * sX - dispW * 0.5f;
            float cy = ((y1 + y2) * 0.5f) * sY - dispH * 0.5f;

            int cls = Mathf.Clamp(clsIDs[i], 0, labels.Length - 1);

            string enName = labels[cls];
            string arName = GetArabicName(cls, enName);
            arName = CanonicalizeArabic(arName);
            string label = MakeArabicLabel(arName, conf);

            var rectPx = new Rect(
                x1 * sX,
                (imageHeight - y2) * sY,
                (x2 - x1) * sX,
                (y2 - y1) * sY
            );

            LastDetections.Add(new Detection
            {
                classId = cls,
                englishName = enName,
                arabicName = arName,
                confidence = conf,
                rectPx = rectPx
            });

            DrawBox(new BoundingBox
            {
                centerX = cx,
                centerY = cy,
                width = w,
                height = h,
                label = label
            }, k, dispH * 0.05f);
        }
    }

    // ===========================
    // Arabic-only Matching
    // ===========================

    public bool HasArabicClass(string arabicQuery, float minScore, float minBoxSizeForMatch, out Detection found)
    {
        found = null;
        if (string.IsNullOrWhiteSpace(arabicQuery)) return false;

        string q = CanonicalizeArabic(arabicQuery);

        foreach (var d in LastDetections)
        {
            if (d.confidence < minScore) continue;
            if (Mathf.Min(d.rectPx.width, d.rectPx.height) < minBoxSizeForMatch) continue;

            string nameNorm = CanonicalizeArabic(d.arabicName);

            if (nameNorm == q || IsArabicSynonym(nameNorm, q))
            {
                found = d;
                return true;
            }
        }
        return false;
    }

    // ===========================
    // Arabic Display Helpers
    // ===========================

    public static string MakeArabicLabel(string rawArabicName, float confidence01)
    {
        string shaped = ArabicFixer.Fix(rawArabicName, false, false);
        string percent = (confidence01 * 100f).ToString("0.#", CultureInfo.InvariantCulture);
        return $"{shaped} \u200E{percent}%";
    }

    public static string ShapeArabic(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return ArabicFixer.Fix(s, false, false);
    }

    // ===========================
    // Label Translation (EN -> AR)
    // ===========================

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

        if (key.IndexOf("Watch", StringComparison.OrdinalIgnoreCase) >= 0 && EnToAr.TryGetValue("Watch", out value)) return true;
        if (key.IndexOf("Clock", StringComparison.OrdinalIgnoreCase) >= 0 && EnToAr.TryGetValue("Clock", out value)) return true;
        if (key.IndexOf("Blackboard", StringComparison.OrdinalIgnoreCase) >= 0 && EnToAr.TryGetValue("Blackboard", out value)) return true;
        if (key.IndexOf("Whiteboard", StringComparison.OrdinalIgnoreCase) >= 0 && EnToAr.TryGetValue("Whiteboard", out value)) return true;

        return false;
    }

    // ===========================
    // Arabic Normalization / Synonyms
    // ===========================

    public static string NormalizeArabic(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;

        s = s.Replace("\u0640", "");
        s = s.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا');
        s = s.Replace('ى', 'ي');
        s = s.Replace('ة', 'ه');

        var outList = new List<char>(s.Length);
        foreach (var ch in s)
        {
            int code = ch;
            bool isDiac = (code >= 0x064B && code <= 0x065F) || code == 0x0670;
            if (!isDiac) outList.Add(ch);
        }

        s = new string(outList.ToArray());
        s = s.Replace("ـ", "").Replace("  ", " ").Trim();
        return s;
    }

    public static string CanonicalizeArabic(string s)
    {
        s = NormalizeArabic(s);
        if (string.IsNullOrEmpty(s)) return s;

        switch (s)
        {
            // ===== مجموعة صحن =====
            case "طبق":
            case "وعاء":
            case "صحن":
                return "صحن";

            // ===== مجموعة صنبور =====
            case "حنفيه":
            case "حنفيه ماء":
            case "حنفية":
            case "صنبور":
                return "صنبور";

            // ===== مجموعة صورة =====
            case "صوره":
            case "صورة":
            case "صورة/اطار":
            case "صوره/اطار":
            case "صورة/إطار":
                return "صورة";

            // ===== مجموعة سيارة / SUV =====
            case "سياره":
            case "سياره suv":
            case "سيارة":
            case "سيارة suv":
                return "سيارة";

            // ===== مجموعة قلادة / سلسال =====
            case "قلاده":
            case "قلادة":
            case "سلسال":
                return "سلسال";

            default:
                return s;
        }
}

private static bool IsArabicSynonym(string a, string b)
    {
        if (a == b) return true;

        if ((a == "مكنسه" && b == "مقشه") || (a == "مقشه" && b == "مكنسه")) return true;

        return false;
    }

    // ===========================
    // UI Drawing + Pooling
    // ===========================

    public void DrawBox(BoundingBox box, int id, float fontSize)
    {
        GameObject panel;

        if (id < boxPool.Count) { panel = boxPool[id]; panel.SetActive(true); }
        else { panel = CreateNewBox(new Color(0f, 0.55f, 1f, 1f)); }

        panel.transform.localPosition = new Vector3(box.centerX, -box.centerY, 0f);

        var rt = panel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(box.width, box.height);

        var tmp = panel.GetComponentInChildren<TextMeshProUGUI>();
        tmp.text = box.label;
        tmp.fontSize = Mathf.Clamp((int)fontSize, 18, 48);
        tmp.alignment = TextAlignmentOptions.MidlineRight;
        if (arabicFont != null) tmp.font = arabicFont;
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

    // ===========================
    // Webcam Stop / Release
    // ===========================

    private void StopAndReleaseWebcam(bool clearTexture = true)
    {
        try
        {
            if (webcam != null)
            {
                if (webcam.isPlaying) webcam.Stop();
                if (clearTexture && displayImage != null) displayImage.texture = null;
                Destroy(webcam);
            }
        }
        catch
        {
        }
        finally
        {
            webcam = null;
            webcamReady = false;
        }
    }

    public void ForceReleaseCamera() => StopAndReleaseWebcam();

    private void OnDisable() { StopAndReleaseWebcam(false); }

    private void OnApplicationPause(bool p)
    {
        if (p) StopAndReleaseWebcam(false);
    }

    void OnDestroy()
    {
        StopAndReleaseWebcam();

        centersToCorners?.Dispose();

        inputTensor?.Dispose();
        inputTensor = null;

        worker?.Dispose();
        worker = null;

        if (modelInputRT != null)
        {
            modelInputRT.Release();
            Destroy(modelInputRT);
            modelInputRT = null;
        }
    }

    // ===========================
    // Fallback Dictionary Builder
    // ===========================

    private static Dictionary<string, string> BuildEnToAr()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Map(string en, string ar) { d[en] = ar; }

        Map("Person", "شخص");

        Map("Car", "سيارة");
        Map("SUV", "سيارة");

        Map("Ring", "خاتم");
        Map("Bracelet", "سوار");
        Map("Necklace", "قلادة");

        Map("Watch / Clock", "ساعة");
        Map("Watch", "ساعة يد");
        Map("Clock", "ساعة حائط");

        Map("Lamp", "مصباح");
        Map("Glasses", "نظارة");
        Map("Bottle", "زجاجة");
        Map("Desk", "مكتب");
        Map("Cup", "كوب");

        Map("Street Lights", "أعمدة إنارة الشوارع");
        Map("Cabinet/shelf", "خزانة/رف");
        Map("Handbag/Satchel", "حقيبة يد/حقيبة كتف");

        Map("Plate", "صحن");
        Map("Bowl", "صحن");
        Map("Dish", "صحن");

        Map("Picture", "صورة");
        Map("Picture/Frame", "صورة");

        Map("Helmet", "خوذة");
        Map("Book", "كتاب");
        Map("Gloves", "قفازات");
        Map("Storage box", "صندوق تخزين");
        Map("Boat", "قارب");
        Map("Leather Shoes", "أحذية جلدية");
        Map("Flower", "زهرة");
        Map("Sandals", "صنادل");
        Map("Slippers", "شبشب");

        Map("Faucet", "صنبور");

        Map("Soap", "صابون");
        Map("Tie", "ربطة عنق");
        Map("Remote", "ريموت");
        Map("Pomegranate", "رمان");
        Map("Belt", "حزام");
        Map("Bow Tie", "ربطة فراشة");
        Map("Kettle", "غلاية");
        Map("Washing Machine/Drying Machine", "غسالة/مجفف");
        Map("Dishwasher", "غسالة صحون");
        Map("Bread", "خبز");
        Map("Green Vegetables", "خضروات ورقية");
        Map("Cucumber", "خيار");
        Map("Blender", "خلاط");
        Map("Peach", "خوخ");
        Map("Lettuce", "خس");

        return d;
    }
}
