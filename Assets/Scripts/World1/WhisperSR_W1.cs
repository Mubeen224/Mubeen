// Arabic/UI
using ArabicSupport;
using Mankibo;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Firebase
using Firebase.Auth;
using Firebase.Database;

// Whisper package
using Whisper;
using Whisper.Utils;

public class WhisperSR_W1 : MonoBehaviour
{
    [Header("Whisper")]
    public WhisperManager whisper;
    public MicrophoneRecord mic;

    [Header("UI")]
    public GameObject srCanvas;
    public Button micButton;
    public TMP_Text promptText;
    public TMP_Text letterText;
    public TMP_FontAsset arabicFont;

    [Header("Messages (Arabic)")]
    [TextArea] public string initialPromptAr = "انطِق الحرف";
    [TextArea] public string listeningPromptAr = "جارِي الاستِماع ...";
    [TextArea] public string checkingMsgAr = "جارٍ التحقُّق من الإجابة ...";
    [TextArea] public string correctMsgAr = "إجابة صحيحة!";
    [TextArea] public string wrongMsgAr = "حاول مرة أخرى!";

    [Header("Prompt Colors")]
    public Color correctColor = new Color(0f, 0.6f, 0.2f);
    public Color wrongColor = new Color(0.8f, 0f, 0.1f);
    public Color neutralColor = Color.white;

    [Header("Win / Lose Feedback")]
    public GameObject winPopup;
    public AudioSource winAudio;
    public AudioSource tryAgainAudio;
    public float showInstructionDelay = 0.6f;

    [Header("Instruction VO (optional)")]
    public Transform lettersAudiosRoot;
    public AudioSource openingAudio;
    public bool playInstructionOnEnable = true;
    public float instructionDelay = 0.15f;

    [Header("Flow Options")]
    public bool resumePlayerOnSuccess = false;
    public Action onSuccess;

    [Header("This Canvas Letter")]
    public string fixedLetter = ""; // Letter for this SR canvas (comes from LetterTracingW1 or GameSession)

    [Header("Auto Stop on Voice (VAD) — like WhisperSR3")]
    [Tooltip("Auto stop when voice energy exceeds threshold")]
    public bool autoStopOnVoice = true;
    [Tooltip("Threshold (0..1). Try 0.02 – 0.06")]
    [Range(0.001f, 0.2f)] public float voiceThreshold = 0.035f;
    [Tooltip("Analysis window in milliseconds")]
    public int voiceWindowMS = 220;
    [Tooltip("Minimum record time before allowing auto stop")]
    public float minRecordTime = 0.15f;
    [Tooltip("Safety limit: force stop after this time (seconds)")]
    public float maxRecordTime = 5f;

    // ====== Firebase Attempts (same pattern as Letter Tracing) ======
    private bool attemptInitialized = false;
    private int currentAttempt = 0;
    private int attemptErrorCount = 0;
    private string parentId;
    private string selectedChildKey;

    // Internals
    private string _targetLetter = "?";
    private bool _isRecording;
    private bool _instructionPlaying = false;
    private bool _isTranscribing;
    private Coroutine _autoStopCR;
    private Coroutine _waitModelCR;
    private World1 _player;

    // Prevent giving coins multiple times for the same success
    private bool _coinsGivenOnThisSuccess = false;

    private void Reset()
    {
        if (srCanvas == null) srCanvas = gameObject;
    }

    private void Awake()
    {
        if (mic == null)
        {
            mic = GetComponent<MicrophoneRecord>();
            if (mic == null) mic = gameObject.AddComponent<MicrophoneRecord>();
        }
        _player = FindFirstObjectByType<World1>(FindObjectsInactive.Include);
    }

    private void OnEnable()
    {
        // Resolve Firebase identity
        parentId = FirebaseAuth.DefaultInstance.CurrentUser != null
            ? FirebaseAuth.DefaultInstance.CurrentUser.UserId
            : "debug_parent";

        // Resolve selected child key (from CoinsManager if available)
        selectedChildKey = CoinsManager.instance != null
            ? CoinsManager.instance.SelectedChildKey
            : null;

        // Resolve target letter for this SR task
        _targetLetter = ResolveLetter();
        if (string.IsNullOrWhiteSpace(_targetLetter)) _targetLetter = "?";

        // Ensure base node for this letter exists in DB (badge=false if missing)
        StartCoroutine(EnsureLetterNodeInitialized(parentId, selectedChildKey, _targetLetter));

        // Hook microphone stop callback
        if (mic != null) mic.OnRecordStop += OnRecordStop;

        // Setup mic button: click = start SR attempt
        if (micButton)
        {
            micButton.onClick.RemoveAllListeners();
            micButton.onClick.AddListener(TryStartRecordingOnce);

            // Disable the button initially (grey) until model is ready
            micButton.interactable = false;
        }

        // Setup Whisper model
        if (whisper != null)
        {
            whisper.language = "ar";
            whisper.translateToEnglish = false;
            whisper.noContext = true;
            whisper.singleSegment = true;
            whisper.initialPrompt = $"أجب بحرف عربي واحد فقط بدون أي كلمات إضافية: {_targetLetter}";

            // Start loading the model if not already loading/loaded
            if (!whisper.IsLoaded && !whisper.IsLoading)
                _ = whisper.InitModel();

            // Start coroutine that waits for the model to be fully ready before enabling the mic button
            if (_waitModelCR != null) StopCoroutine(_waitModelCR);
            _waitModelCR = StartCoroutine(WaitForWhisperModelReady());
        }

        SetPromptArabic(initialPromptAr, neutralColor);
        UpdateLetterUI();
        SetMicVisual(false);
        _isRecording = false;
        _isTranscribing = false;

        if (winPopup) winPopup.SetActive(false);

        // Optionally play opening instruction VO
        if (playInstructionOnEnable)
        {
            // Keep button disabled while instruction audio is playing
            if (micButton) micButton.interactable = false;
            StartCoroutine(PlayOpening(instructionDelay));
        }
        else
        {
            if (micButton && whisper == null)
                micButton.interactable = true;
        }

        // Attempts are not initialized yet — first recording will create a new attempt
        attemptInitialized = false;
        attemptErrorCount = 0;
        _coinsGivenOnThisSuccess = false;
    }

    private void OnDisable()
    {
        if (mic != null) mic.OnRecordStop -= OnRecordStop;
        if (micButton) micButton.onClick.RemoveAllListeners();

        if (_autoStopCR != null)
        {
            StopCoroutine(_autoStopCR);
            _autoStopCR = null;
        }

        if (_waitModelCR != null)
        {
            StopCoroutine(_waitModelCR);
            _waitModelCR = null;
        }

        if (_isRecording) SafeStopRecording();
        _instructionPlaying = false;
        _isTranscribing = false;
    }

    // ===== Public API =====
    public void SetLetter(string letter)
    {
        fixedLetter = string.IsNullOrWhiteSpace(letter) ? "" : letter.Trim();
        _targetLetter = ResolveLetter();
        UpdateLetterUI();

        if (whisper != null)
            whisper.initialPrompt = $"أجب بحرف عربي واحد فقط بدون أي كلمات إضافية: {_targetLetter}";
    }

    public void StartTask()
    {
        if (srCanvas != null) srCanvas.SetActive(true);
        enabled = true;
    }

    public void CloseSRAndResume()
    {
        if (winPopup) winPopup.SetActive(false);
        if (srCanvas) srCanvas.SetActive(false);
        ResumePlayerMovement();
    }

    public void Teardown()
    {
        try
        {
            StopAllCoroutines();
            if (mic != null) mic.OnRecordStop -= OnRecordStop;
            if (tryAgainAudio) tryAgainAudio.Stop();
            if (winAudio) winAudio.Stop();
            if (_isRecording) SafeStopRecording();
            if (srCanvas) srCanvas.SetActive(false);
            enabled = false;
            _instructionPlaying = false;
            _isTranscribing = false;
        }
        catch { }
    }

    // ===== Mic logic (SR3-style) =====
    private void TryStartRecordingOnce()
    {
        if (_instructionPlaying) return;
        if (_isTranscribing) return;
        if (_isRecording) return;

        if (!attemptInitialized)
        {
            StartCoroutine(StartNewAttemptAndRecord());
        }
        else
        {
            StartRecording();
        }
    }

    private IEnumerator StartNewAttemptAndRecord()
    {
        yield return StartCoroutine(StartNewAttempt());
        StartRecording();
    }

    private void StartRecording()
    {
        if (micButton) micButton.interactable = false;

        SetMicVisual(true);
        SetPromptArabic(listeningPromptAr, neutralColor);

        mic.StartRecord();
        _isRecording = true;

        if (autoStopOnVoice)
        {
            if (_autoStopCR != null) StopCoroutine(_autoStopCR);
            _autoStopCR = StartCoroutine(AutoStopWhenVoice());
        }
    }

    private IEnumerator AutoStopWhenVoice()
    {
        float startTime = Time.time;

        while (_isRecording && Time.time - startTime < minRecordTime)
            yield return null;

        while (_isRecording)
        {
            if (Time.time - startTime >= maxRecordTime)
            {
                SafeStopRecording();
                break;
            }

            if (DetectVoice(voiceThreshold, voiceWindowMS))
            {
                SafeStopRecording();
                break;
            }

            yield return null;
        }

        _autoStopCR = null;
    }

    private bool DetectVoice(float threshold, int windowMs)
    {
        try
        {
            var mt = mic.GetType();
            var clipObj =
                mt.GetProperty("Clip")?.GetValue(mic) ??
                mt.GetProperty("RecordingClip")?.GetValue(mic) ??
                mt.GetField("Clip")?.GetValue(mic) ??
                mt.GetField("RecordingClip")?.GetValue(mic);

            var clip = clipObj as AudioClip;
            if (clip == null || !clip) return false;

            int sampleRate = AudioSettings.outputSampleRate;
            var freqObj =
                mt.GetProperty("Frequency")?.GetValue(mic) ??
                mt.GetProperty("SampleRate")?.GetValue(mic) ??
                mt.GetField("Frequency")?.GetValue(mic) ??
                mt.GetField("SampleRate")?.GetValue(mic);
            if (freqObj is int f && f > 0) sampleRate = f;

            int channels = clip.channels;
            int windowSamples = Mathf.Max(1, (int)(sampleRate * (windowMs / 1000f)) * channels);

            int micPos = Microphone.GetPosition(null);
            if (micPos <= 0) return false;

            int start = micPos - windowSamples;
            if (start < 0) start += clip.samples;

            float[] buffer = new float[windowSamples];
            if (start + windowSamples <= clip.samples)
            {
                clip.GetData(buffer, start);
            }
            else
            {
                int firstLen = clip.samples - start;
                float[] a = new float[firstLen];
                float[] b = new float[windowSamples - firstLen];
                clip.GetData(a, start);
                clip.GetData(b, 0);
                Array.Copy(a, 0, buffer, 0, firstLen);
                Array.Copy(b, 0, buffer, firstLen, b.Length);
            }

            double sum = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                float s = buffer[i];
                sum += s * s;
            }
            double rms = Math.Sqrt(sum / Math.Max(1, buffer.Length));

            return rms >= threshold;
        }
        catch
        {
            return false;
        }
    }

    private void SafeStopRecording()
    {
        if (_autoStopCR != null)
        {
            StopCoroutine(_autoStopCR);
            _autoStopCR = null;
        }

        try { mic.StopRecord(); } catch { }
        _isRecording = false;

        SetMicVisual(false);
        // زر المايك يبقى مقفول لين يخلص التحليل
    }

    // ===== Transcribe (SR3-style) =====
    private async void OnRecordStop(AudioChunk recorded)
    {
        _isTranscribing = true;

        // نص خاص أثناء التحليل
        SetPromptArabic(checkingMsgAr, neutralColor);

        try
        {
            if (whisper == null || !whisper.IsLoaded)
            {
                Debug.LogError("[WhisperSR_W1] WhisperManager is not ready.");
                FailFlow();
                return;
            }

            var raw = await TranscribeFromChunk(recorded);
            string text = ExtractText(raw);

            string normalized = CollapseLongVowels(NormalizeArabic(text));
            Debug.Log($"[SR_W1] Raw='{text}'  Norm='{normalized}'  Target='{_targetLetter}'");

            bool isHit = IsMatchLetterLenient(normalized, _targetLetter);

            if (isHit)
            {
                SetPromptArabic(correctMsgAr, correctColor);

                GiveCoinsSafely(5);
                SaveSRSuccess();

                if (gameObject.activeInHierarchy)
                    StartCoroutine(HandleSuccessOnce());
            }
            else
            {
                attemptErrorCount++;
                SaveAttemptError();

                SetPromptArabic(wrongMsgAr, wrongColor);

                if (gameObject.activeInHierarchy)
                    StartCoroutine(HandleFailure());
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[WhisperSR_W1] Exception: {e}");

            attemptErrorCount++;
            SaveAttemptError();

            SetPromptArabic(wrongMsgAr, wrongColor);

            if (gameObject.activeInHierarchy)
                StartCoroutine(HandleFailure());
        }
        finally
        {
            _isTranscribing = false;

            if (!gameObject.activeInHierarchy && micButton)
                micButton.interactable = true;
        }
    }

    private IEnumerator HandleSuccessOnce()
    {
        yield return new WaitForSeconds(0.35f);

        if (winPopup) winPopup.SetActive(true);
        if (micButton) micButton.interactable = false;

        if (winAudio)
        {
            winAudio.Stop();
            winAudio.Play();
            while (winAudio.isPlaying) yield return null;
        }
        else
        {
            yield return new WaitForSeconds(0.5f);
        }

        if (winPopup) winPopup.SetActive(false);
        if (srCanvas) srCanvas.SetActive(false);

        if (resumePlayerOnSuccess) ResumePlayerMovement();
        onSuccess?.Invoke();

        if (micButton) micButton.interactable = true;
    }

    private IEnumerator HandleFailure()
    {
        if (micButton) micButton.interactable = false;

        if (tryAgainAudio)
        {
            tryAgainAudio.Stop();
            tryAgainAudio.Play();
            while (tryAgainAudio.isPlaying) yield return null;
        }

        if (showInstructionDelay > 0f)
            yield return new WaitForSeconds(showInstructionDelay);

        SetPromptArabic(initialPromptAr, neutralColor);
        UpdateLetterUI();

        if (micButton) micButton.interactable = true;
    }

    private void FailFlow()
    {
        SetPromptArabic(wrongMsgAr, wrongColor);
        if (gameObject.activeInHierarchy) StartCoroutine(HandleFailure());
    }

    // ===== Instruction VO =====
    private IEnumerator PlayOpening(float delay)
    {
        _instructionPlaying = true;
        if (micButton) micButton.interactable = false;

        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        if (openingAudio)
        {
            openingAudio.Stop();
            openingAudio.Play();
            while (openingAudio.isPlaying) yield return null;
        }
        else
        {
            AudioSource s = FindLetterVoice(_targetLetter);
            if (s)
            {
                s.Stop();
                s.Play();
                while (s.isPlaying) yield return null;
            }
        }

        _instructionPlaying = false;

        if (micButton && (!_isRecording && !_isTranscribing))
        {
            if (whisper == null || whisper.IsLoaded)
                micButton.interactable = true;
        }
    }

    private AudioSource FindLetterVoice(string letter)
    {
        if (!lettersAudiosRoot) return null;
        var sources = lettersAudiosRoot.GetComponentsInChildren<AudioSource>(true);
        foreach (var s in sources)
        {
            if (!s) continue;
            string goName = s.gameObject.name.Trim();
            string clipName = s.clip ? s.clip.name.Trim() : string.Empty;
            if (goName == letter || clipName == letter ||
                goName == (letter + "_Letter") || clipName == (letter + "_Letter"))
                return s;
        }
        return null;
    }

    // ===== Matching logic (tuned for خ / ذ / ر + أكثر تسامحاً) =====
    private bool IsMatchLetterLenient(string recognizedNorm, string letter)
    {
        if (string.IsNullOrWhiteSpace(recognizedNorm) || string.IsNullOrWhiteSpace(letter))
            return false;

        // نطبّق نفس التطبيع على النص والحرف
        string L = NormalizeArabic(letter);
        string norm = NormalizeArabic(CollapseLongVowels(recognizedNorm));

        List<string> accepted = new() { L };

        // صيغ شائعة لكل حرف
        if (L == "خ") accepted.AddRange(new[] { "خا", "خاء", "الخ", "الخا", "الخاء" });
        else if (L == "ذ") accepted.AddRange(new[] { "ذ", "ذال", "ذا", "الذ", "الذال" });
        else if (L == "ر") accepted.AddRange(new[] { "را", "راء", "الرا", "الراء" });

        var tokens = norm.Split(new[]
        {
            ' ', '،', ',', '.', '!', '?', '؛', ':', 'ـ', '-', '_',
            '\n', '\r', '\t', '\"', '«', '»', '“', '”', '(', ')', '[', ']', '{', '}'
        }, StringSplitOptions.RemoveEmptyEntries);

        char targetChar = L[0];

        foreach (var tokRaw in tokens)
        {
            string tok = NormalizeArabic(tokRaw);
            if (string.IsNullOrWhiteSpace(tok))
                continue;

            // 1) مطابقة مباشرة مع القائمة المسموح بها
            if (accepted.Contains(tok))
                return true;

            // 2) حرف + حرف مد (خا / خو / خي)
            if (tok.Length == 2 && tok[0] == targetChar &&
                (tok[1] == 'ا' || tok[1] == 'و' || tok[1] == 'ي'))
                return true;

            // 3) صيغة "الـ" قبل الحرف (الرا / الذال .. إلخ)
            if (tok.StartsWith("ال") && tok.Length <= 5)
            {
                int idx = tok.IndexOf(targetChar);
                if (idx == 2) // بعد "ال"
                    return true;
            }

            // 4) إذا التوكن يحتوي الحرف المطلوب وطوله قصير نعتبره صح
            // مثال: "اخجح" فيها خ، وطولها 4 → نقبلها
            int pos = tok.IndexOf(targetChar);
            if (pos >= 0 && tok.Length <= 4)
                return true;

            // 5) لو النص بين علامات اقتباس
            var inner = tok.Trim('\'', '\"', '“', '”', '‘', '’');
            if (!string.IsNullOrEmpty(inner) && accepted.Contains(inner))
                return true;
        }

        return false;
    }

    // ===== Transcription helpers =====
    private async Task<object> TranscribeFromChunk(AudioChunk chunk)
    {
        var t = chunk.GetType();

        var clipProp = t.GetProperty("Clip") ?? t.GetProperty("clip");
        if (clipProp != null)
        {
            var clipVal = clipProp.GetValue(chunk) as AudioClip;
            if (clipVal != null)
                return await whisper.GetTextAsync(clipVal);
        }

        float[] samples =
            t.GetField("Samples")?.GetValue(chunk) as float[] ??
            t.GetProperty("Samples")?.GetValue(chunk) as float[] ??
            t.GetField("Data")?.GetValue(chunk) as float[] ??
            t.GetProperty("Data")?.GetValue(chunk) as float[];

        int sampleRate = 16000;
        var freqField = t.GetField("Frequency") ?? t.GetField("SampleRate");
        var freqProp = t.GetProperty("Frequency") ?? t.GetProperty("SampleRate");
        if (freqField != null) sampleRate = (int)freqField.GetValue(chunk);
        if (freqProp != null) sampleRate = (int)freqProp.GetValue(chunk);

        int channels = 1;
        var chField = t.GetField("Channels") ?? t.GetField("ChannelCount");
        var chProp = t.GetProperty("Channels") ?? t.GetProperty("ChannelCount");
        if (chField != null) channels = (int)chField.GetValue(chunk);
        if (chProp != null) channels = (int)chProp.GetValue(chunk);

        if (samples != null && samples.Length > 0)
            return await whisper.GetTextAsync(samples, sampleRate, channels);

        if (mic != null)
        {
            var micType = mic.GetType();
            var lastClipProp = micType.GetProperty("Clip") ??
                               micType.GetProperty("RecordedClip") ??
                               micType.GetProperty("LastClip");
            if (lastClipProp != null)
            {
                var clip = lastClipProp.GetValue(mic) as AudioClip;
                if (clip != null)
                    return await whisper.GetTextAsync(clip);
            }
        }

        Debug.LogWarning("[WhisperSR_W1] No valid audio data found to transcribe.");
        return string.Empty;
    }

    private string ExtractText(object result)
    {
        if (result == null) return string.Empty;
        if (result is string s) return s;

        var t = result.GetType();

        foreach (var name in new[] { "Text", "text", "Result", "result", "Transcript", "transcript", "FinalText", "finalText" })
        {
            var p = t.GetProperty(name);
            if (p != null && p.PropertyType == typeof(string))
            {
                var val = (string)p.GetValue(result);
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
        }

        foreach (var name in new[] { "Text", "text", "Result", "result", "Transcript", "transcript", "FinalText", "finalText" })
        {
            var f = t.GetField(name);
            if (f != null && f.FieldType == typeof(string))
            {
                var val = (string)f.GetValue(result);
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
        }

        object segsObj = t.GetProperty("Segments")?.GetValue(result) ??
                         t.GetField("Segments")?.GetValue(result) ??
                         t.GetProperty("segments")?.GetValue(result) ??
                         t.GetField("segments")?.GetValue(result);

        if (segsObj is System.Collections.IEnumerable segs)
        {
            var parts = new List<string>();
            foreach (var seg in segs)
            {
                var st = seg.GetType();
                var p = st.GetProperty("Text") ?? st.GetProperty("text");
                var f = st.GetField("Text") ?? st.GetField("text");
                string segText = p?.GetValue(seg) as string ?? f?.GetValue(seg) as string;
                if (!string.IsNullOrWhiteSpace(segText)) parts.Add(segText);
            }
            if (parts.Count > 0) return string.Join(" ", parts);
        }

        return result.ToString() ?? string.Empty;
    }

    // ===== Arabic helpers =====
    private string NormalizeArabic(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;

        Span<char> buf = stackalloc char[s.Length];
        int j = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];

            if (ch == '\u0640') continue;

            if (ch >= '\u064B' && ch <= '\u0652') continue;

            if (ch == 'أ' || ch == 'إ' || ch == 'آ') ch = 'ا';
            else if (ch == 'ى') ch = 'ي';
            else if (ch == 'ة') ch = 'ه';

            buf[j++] = ch;
        }
        var trimmed = new string(buf[..j]).Trim();
        char[] trimChars =
        {
            '"', '\'', '“', '”', '’', '‘',
            '(', ')', '[', ']', '{', '}',
            '.', ',', '!', '?', ';', ':', '،', '-'
        };
        return trimmed.Trim(trimChars);
    }

    private string CollapseLongVowels(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
        char prev = '\0';
        foreach (var ch in s)
        {
            if ((ch == 'ا' || ch == 'و' || ch == 'ي') && ch == prev) continue;
            if (ch == ' ' && prev == ' ') continue;
            sb.Append(ch);
            prev = ch;
        }
        return sb.ToString();
    }

    private void SetMicVisual(bool recording)
    {
        var tmp = micButton ? micButton.GetComponentInChildren<TMP_Text>() : null;
        if (tmp != null)
        {
            tmp.text = recording ? "إيقاف" : "تسجيل";
            return;
        }
        var legacy = micButton ? micButton.GetComponentInChildren<Text>() : null;
        if (legacy != null) legacy.text = recording ? "إيقاف" : "تسجيل";
    }

    private void SetPromptArabic(string txt, Color color)
    {
        if (!promptText) return;
        string shaped = ArabicFixer.Fix(txt ?? "", false, false);
        if (arabicFont) promptText.font = arabicFont;
        promptText.isRightToLeftText = false;
        promptText.alignment = TextAlignmentOptions.Center;
        promptText.color = color;
        promptText.text = shaped;
    }

    private void UpdateLetterUI()
    {
        if (!letterText) return;

        string display = string.IsNullOrWhiteSpace(_targetLetter) ? "?" : _targetLetter + "\u064E";
        string shaped = display;

        if (arabicFont) letterText.font = arabicFont;
        letterText.isRightToLeftText = true;
        letterText.alignment = TextAlignmentOptions.Center;
        letterText.fontSize = Mathf.Max(letterText.fontSize, 8);
        letterText.text = shaped;
    }

    private string ResolveLetter()
    {
        if (!string.IsNullOrWhiteSpace(fixedLetter)) return fixedLetter.Trim();
        if (!string.IsNullOrWhiteSpace(GameSession.CurrentLetter)) return GameSession.CurrentLetter.Trim();
        return "?";
    }

    private void ResumePlayerMovement()
    {
        if (_player == null) _player = FindFirstObjectByType<World1>(FindObjectsInactive.Include);
        if (_player != null)
        {
            _player.canMove = true;
            _player.Idle();
        }
    }

    // ====================== Ensure letter node has "badge" once (same as tracing) ======================
    private IEnumerator EnsureLetterNodeInitialized(string parentId, string selectedChildKey, string letter)
    {
        if (string.IsNullOrEmpty(parentId) || string.IsNullOrEmpty(selectedChildKey) || string.IsNullOrEmpty(letter))
            yield break;

        string letterPath = $"parents/{parentId}/children/{selectedChildKey}/letters/{letter}";
        var getTask = FirebaseDatabase.DefaultInstance.RootReference.Child(letterPath).GetValueAsync();
        yield return new WaitUntil(() => getTask.IsCompleted);

        if (getTask.Exception != null) yield break;

        var snap = getTask.Result;
        if (snap == null || !snap.HasChild("badge"))
        {
            var updates = new Dictionary<string, object>
            {
                [$"{letterPath}/badge"] = false
            };
            FirebaseDatabase.DefaultInstance.RootReference.UpdateChildrenAsync(updates);
        }
    }

    // ====================== Attempts API (SR) ======================
    private IEnumerator StartNewAttempt()
    {
        attemptInitialized = false;
        attemptErrorCount = 0;
        _coinsGivenOnThisSuccess = false;

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

    private void SaveAttemptError()
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

    private void SaveSRSuccess()
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

    private string GetAttemptsPath()
    {
        return $"parents/{parentId}/children/{selectedChildKey}/letters/{_targetLetter}/activities/SR/attempts";
    }

    private string GetAttemptPath() => $"{GetAttemptsPath()}/{currentAttempt}";

    private IEnumerator FindSelectedOrDisplayedChildKey()
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
            if (isSel)
            {
                selectedChildKey = ch.Key;
                yield break;
            }
        }

        foreach (var ch in getChildren.Result.Children)
        {
            bool disp = false;
            bool.TryParse(ch.Child("displayed")?.Value?.ToString(), out disp);
            if (disp)
            {
                selectedChildKey = ch.Key;
                yield break;
            }
        }
    }

    // ====================== Coins (via CoinsManager or Firebase Transaction) ======================
    private void GiveCoinsSafely(int amount)
    {
        if (_coinsGivenOnThisSuccess) return;
        _coinsGivenOnThisSuccess = true;

        if (CoinsManager.instance != null)
        {
            try
            {
                CoinsManager.instance.AddCoinsToSelectedChild(amount);
                Debug.Log($"[WhisperSR_W1] Coins added via CoinsManager (+{amount}).");
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WhisperSR_W1] CoinsManager failed, will fallback to Firebase transaction: " + e);
            }
        }

        StartCoroutine(AddCoinsDirectly(amount));
    }

    private IEnumerator AddCoinsDirectly(int amount)
    {
        if (string.IsNullOrEmpty(selectedChildKey))
            yield return StartCoroutine(FindSelectedOrDisplayedChildKey());

        if (string.IsNullOrEmpty(parentId) || string.IsNullOrEmpty(selectedChildKey))
        {
            Debug.LogWarning("[WhisperSR_W1] Cannot add coins: parentId or selectedChildKey is empty.");
            yield break;
        }

        var db = FirebaseDatabase.DefaultInstance;
        var coinsRef = db.RootReference
            .Child("parents").Child(parentId)
            .Child("children").Child(selectedChildKey)
            .Child("coins");

        var trxTask = coinsRef.RunTransaction(mutable =>
        {
            long current = 0;
            try
            {
                if (mutable.Value is long l) current = l;
                else if (mutable.Value is string s && long.TryParse(s, out var v)) current = v;
            }
            catch { }

            mutable.Value = current + amount;
            return TransactionResult.Success(mutable);
        });

        yield return new WaitUntil(() => trxTask.IsCompleted);

        if (trxTask.Exception != null)
        {
            Debug.LogWarning("[WhisperSR_W1] Firebase coins transaction failed: " + trxTask.Exception);
        }
        else
        {
            Debug.Log("[WhisperSR_W1] Coins added via Firebase transaction (+" + amount + ").");
        }
    }

    // ====================== Wait for Whisper model readiness before enabling mic button ======================
    private IEnumerator WaitForWhisperModelReady()
    {
        yield return null;

        if (whisper == null)
        {
            _waitModelCR = null;
            yield break;
        }

        while (!whisper.IsLoaded || whisper.IsLoading)
            yield return null;

        while (_instructionPlaying || _isRecording || _isTranscribing)
            yield return null;

        if (micButton)
            micButton.interactable = true;

        _waitModelCR = null;
    }
}
