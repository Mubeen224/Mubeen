using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ArabicSupport;
using Mankibo;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Whisper;
using Whisper.Utils;
using Firebase.Auth;
using Firebase.Database;

public class WhisperSR : MonoBehaviour
{
    // WHISPER ---------------------------------------------------------------
    [Header("Whisper")]
    [Tooltip("Reference to WhisperManager on the scene")]
    public WhisperManager whisper;

    [Tooltip("Microphone recorder from the package")]
    public MicrophoneRecord mic;

    // UI -------------------------------------------------------------------
    [Header("UI")]
    public GameObject srCanvas;
    public Button micButton;
    public TMP_Text promptText;
    public TMP_Text letterText;
    public TMP_FontAsset arabicFont; // Font asset that supports Arabic characters correctly

    // MESSAGES --------------------------------------------------------------
    [Header("Messages (Arabic)")]
    [TextArea] public string initialPromptAr = "انطِق الحَرف";
    [TextArea] public string listeningPromptAr = "جارِي الاستِماع ...";
    [TextArea] public string correctMsgAr = "إجابة صحيحة!";
    [TextArea] public string wrongMsgAr = "حاول مرة أخرى!";
    [TextArea] public string checkingMsgAr = "جارٍ التحقُّق من الإجابة ...";

    // PROMPT COLORS ---------------------------------------------------------
    [Header("Prompt Colors")]
    public Color correctColor = new Color(0f, 0.6f, 0.2f); // green
    public Color wrongColor = new Color(0.8f, 0f, 0.1f);  // red
    public Color neutralColor = Color.white;

    // FEEDBACK (WIN/LOSE) ---------------------------------------------------
    [Header("Win / Lose Feedback")]
    public GameObject winPopup;
    public AudioSource winAudio;
    public AudioSource tryAgainAudio;
    public float showInstructionDelay = 1.0f; // after fail audio ends, delay before instruction

    // INSTRUCTION AUDIOS ---------------------------------
    [Header("Instruction Audios")]
    [Tooltip("Parent object that contains child AudioSources.")]
    public Transform lettersAudiosRoot;
    public bool playInstructionOnEnable = true; // auto-play when SR opens & on NextRound
    public float instructionDelay = 0.1f;       // Delay for UI to settle

    // LETTERS POOL ----------------------------------------------------------
    [Header("Letters Pool")]
    public string[] letters = new[] { "س", "ش", "ص", "ض", "ظ", "ذ", "ر", "خ", "غ" };

    // ===== Quiz Attempt (Firebase)  =====
    [Header("Attempts/Coins (Firebase)")]
    public int coinsOnSuccess = 5;
    private bool attemptInitialized = false;
    private int attemptErrorCount = 0;
    private bool rewardedThisRound = false;
    private string parentId;          // Firebase parent user id
    private string selectedChildKey;  // Selected child key under the parent in Firebase
    private string currentLetter;     // the letter tracked in this attempt

    // STATE -----------------------------------------------------------------
    private string _targetLetter; // The chosen letter for the round
    private string _targetSyllable; // Alwyas FATHA
    private bool _isRecording;
    private bool _isProcessing;

    // PLAYER CONTROL --------------------------------------------------------
    private World4 _player; // Reference to the player character script in the scene

    // AUTO-STOP ON VOICE ----------------------------------------------------
    [Header("Auto Stop on Voice")]
    public bool autoStopOnVoice = true;
    [Range(0.001f, 0.2f)]               // RMS threshold above which we consider audio to contain voice
    public float voiceThreshold = 0.03f;
    public int voiceWindowMS = 200;     // Window size in milliseconds for voice detection RMS calculation
    public float minRecordTime = 0.3f; // Minimum time the recording must run before it can auto-stop
    public float maxRecordTime = 1.2f;  // Maximum allowed recording duration (safety timeout)
    private Coroutine _autoStopCR;

    // UNITY LIFECYCLE -------------------------------------------------------
    private void Reset()
    {
        srCanvas = gameObject; // When the component is reset, default the SR canvas to this GameObject
    }

    private void Awake()
    {
        if (mic == null) // Ensure there is always a MicrophoneRecord component
        {
            mic = GetComponent<MicrophoneRecord>(); // Get an existing component

            if (mic == null) mic = gameObject.AddComponent<MicrophoneRecord>(); // If missing, add one at runtime
        }

        _player = FindFirstObjectByType<World4>(); // Cache reference to the player in the scene
    }

    private void OnEnable()
    {
        _isProcessing = false;

        PickNewTarget(); // Each time SR opens, pick a new random letter

        mic.OnRecordStop += OnRecordStop; // Subscribe to mic stop event to handle results when recording ends

        // Make sure mic button is wired only once to ToggleRecording
        micButton.onClick.RemoveAllListeners();
        micButton.onClick.AddListener(ToggleRecording);

        SetPromptArabic(initialPromptAr, neutralColor); // Set initial prompt text and color

        UpdateLetterUI(); // Show the chosen letter on the UI

        SetMicVisual(false); // Make the mic button visual set to "record" state

        _isRecording = false; // Initially recording off

        if (winPopup) winPopup.SetActive(false);

        if (playInstructionOnEnable)
        {
            if (micButton) micButton.interactable = false; // Disable mic button while instruction audio is playing
 
            StartCoroutine(PlayInstructionForCurrentTarget(instructionDelay)); // play audio corresponding to the target letter
        }

        // --- Firebase identities ---
        // Get current authenticated user id, or use fallback debug id
        parentId = FirebaseAuth.DefaultInstance.CurrentUser != null
            ? FirebaseAuth.DefaultInstance.CurrentUser.UserId
            : "debug_parent";

        currentLetter = _targetLetter;                   // The letter used to tag quiz attempts in Firebase

        StartCoroutine(InitChildThenStartQuizAttempt()); // Initialize the child key and create a quiz attempt entry
    }

    private void OnDisable()
    {
        mic.OnRecordStop -= OnRecordStop;       // Unsubscribe from mic callback to avoid memory leaks

        micButton.onClick.RemoveAllListeners(); // Remove button listeners to avoid double-subscriptions

        if (_autoStopCR != null) // Stop auto-stop coroutine if it is running
        {
            StopCoroutine(_autoStopCR);
            _autoStopCR = null;
        }

        if (_isRecording) SafeStopRecording(); // If it's still recording, stop cleanly
    }

    // PUBLIC API ------------------------------------------------------------
    public void StartTask()
    {
        if (srCanvas != null) srCanvas.SetActive(true); // Show SR canvas if it exists

        enabled = true; // Enable this component so OnEnable is triggered
    }

    public void NextRound()
    {
        if (winPopup) winPopup.SetActive(false); // Hide win popup at the start of the next round

        PickNewTarget(); // Pick a letter
        UpdateLetterUI(); // SHow the letter on UI

        currentLetter = _targetLetter; // Sync currentLetter with the new target (for Firebase paths)

        StartCoroutine(StartNewQuizAttempt());

        attemptErrorCount = 0; // Reset error count
        rewardedThisRound = false; // Reset flag

        SetPromptArabic(initialPromptAr, neutralColor); // Reset prompt (UI)

        if (playInstructionOnEnable) // Optionally replay the instruction audio at the start of next round
        {
            if (micButton) micButton.interactable = false; // Disable mic button while instruction plays
            StartCoroutine(PlayInstructionForCurrentTarget(instructionDelay));
        }
        else
        {
            if (micButton) micButton.interactable = true;
        }
    }

    public void CloseSRAndResume()
    {
        if (winPopup) winPopup.SetActive(false); // Hide Winning pop up
        if (srCanvas) srCanvas.SetActive(false); // Hide SR canvas

        ResumePlayerMovement(); // Allow player to move again
    }

    // TARGET SELECTION ------------------------------------------------------
    private void PickNewTarget()
    {
        _targetLetter = (letters == null || letters.Length == 0) // Choose a random letter from the letter pool
            ? "س"
            : letters[UnityEngine.Random.Range(0, letters.Length)];

        _targetSyllable = BuildSyllable(_targetLetter); // Build the FATHA syllable
    }

    private static string BuildSyllable(string letter)
    {
        return string.Concat(letter, "ا"); // Append "ا" to letter to create Fatha-style syllable
    }

    // MIC CONTROL -----------------------------------------------------------
    private void ToggleRecording()
    {
        if (_isProcessing)
            return;

        if (_isRecording) // If it's already recording, stop
        {
            SafeStopRecording();
        }
        else
        {
            StartRecording(); // Otherwise, start a new recording session
        }
    }

    private IEnumerator AutoStopWhenVoice()
    {
        float startTime = Time.time; // Record start time of this session

        while (_isRecording && Time.time - startTime < minRecordTime) // Wait until the minimum recording time passes
            yield return null;

        while (_isRecording) // After minimum time, check conditions each frame
        {
            if (Time.time - startTime >= maxRecordTime) // If it exceeded maximum allowed time, stop recording
            {
                SafeStopRecording();
                yield break;
            }

            if (DetectVoice(voiceThreshold, voiceWindowMS)) // If voice is detected by RMS threshold, stop recording
            {
                SafeStopRecording();
                yield break;
            }

            yield return null; // Wait until next frame
        }
    }

    private bool DetectVoice(float threshold, int windowMs)
    {
        try
        {
            var mt = mic.GetType(); // Get type info for reflection-based access to mic properties

            // Try to retrieve the current recording AudioClip from mic
            var clipObj =
                mt.GetProperty("Clip")?.GetValue(mic) ??
                mt.GetProperty("RecordingClip")?.GetValue(mic) ??
                mt.GetField("Clip")?.GetValue(mic) ??
                mt.GetField("RecordingClip")?.GetValue(mic);

            // Cast to AudioClip
            var clip = clipObj as AudioClip;
            if (clip == null || !clip) return false;

            int sampleRate = AudioSettings.outputSampleRate; // Start with system output sample rate

            // Try to get microphone frequency if exposed by the mic component
            var freqObj =
                mt.GetProperty("Frequency")?.GetValue(mic) ??
                mt.GetProperty("SampleRate")?.GetValue(mic) ??
                mt.GetField("Frequency")?.GetValue(mic) ??
                mt.GetField("SampleRate")?.GetValue(mic);

            if (freqObj is int f && f > 0) sampleRate = f; // Override sampleRate if mic has explicit frequency property

            int channels = clip.channels; // Number of audio channels in the recorded clip

            int windowSamples = Mathf.Max(1, (int)(sampleRate * (windowMs / 1000f)) * channels); // Compute number of samples for the given time window

            // Current position of the microphone in its recording buffer
            int micPos = Microphone.GetPosition(null);
            if (micPos <= 0) return false;

            // Determine start index for RMS window; handle wrap-around
            int start = micPos - windowSamples;
            if (start < 0) start += clip.samples;

            float[] buffer = new float[windowSamples]; // Allocate buffer to hold samples for RMS calculation

            // If window is fully inside buffer range
            if (start + windowSamples <= clip.samples)
            {
                // Copy samples directly
                clip.GetData(buffer, start);
            }
            else
            {
                // Handle wrap-around when window reaches end of ring buffer
                int firstLen = clip.samples - start;
                float[] tmpA = new float[firstLen];
                float[] tmpB = new float[windowSamples - firstLen];

                clip.GetData(tmpA, start); // Get samples from end of buffer

                clip.GetData(tmpB, 0); // Get samples from beginning of buffer

                // Merge both parts into buffer
                Array.Copy(tmpA, 0, buffer, 0, firstLen);
                Array.Copy(tmpB, 0, buffer, firstLen, tmpB.Length);
            }

            // Sum of squares for RMS computation
            double sum = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                float s = buffer[i];
                sum += s * s;
            }

            double rms = Math.Sqrt(sum / Math.Max(1, buffer.Length)); // Compute RMS of the window

            return rms >= threshold; // Compare against threshold to decide if there's voice
        }
        catch
        {
            return false; // If anything fails (reflection or audio reading), assume no voice
        }
    }

    private void StartRecording()
    {
        SetMicVisual(true); // Update mic button label to reflect recording state
        SetPromptArabic(listeningPromptAr, neutralColor); // Show "جاري الاستماع" prompt text
        mic.StartRecord(); // Start microphone recording via mic component

        _isRecording = true; // Mark that it's currently recording

        if (autoStopOnVoice) // start auto-stop coroutine to detect voice / timeout
        {
            if (_autoStopCR != null) StopCoroutine(_autoStopCR); // Stop any previous auto-stop coroutine JUST IN CASE
            _autoStopCR = StartCoroutine(AutoStopWhenVoice()); // Start a fresh auto-stop routine
        }
    }

    private void SafeStopRecording()
    {
        if (_autoStopCR != null) // Stop auto-stop coroutine if running
        {
            StopCoroutine(_autoStopCR);
            _autoStopCR = null;
        }

        try
        {
            mic.StopRecord();
        }
        catch
        {
            // Ignore any mic stop exceptions (in case mic already stopped)
        }

        _isRecording = false; // Mark as not recording anymore
        SetMicVisual(false); // Update mic button visual back to idle state
    }

    // TRANSCRIBE & DECIDE ---------------------------------------------------
    private async void OnRecordStop(AudioChunk recorded)
    {
        _isProcessing = true;
        if (micButton) micButton.interactable = false;

        SetPromptArabic(checkingMsgAr, neutralColor);

        try
        {
            var raw = await TranscribeFromChunk(recorded); // Send recorded audio to Whisper and wait for transcription result
            string text = ExtractText(raw); // Try to extract the best text string from the result object

            Debug.Log($"[WhisperSR] Heard: \"{text}\""); // Log what Whisper heard

            string normalized = NormalizeArabic(text); // Normalize Arabic text (remove tatweel, unify forms, strip diacritics)
            normalized = CollapseLongVowels(normalized); // Collapse long vowel repetitions and extra spaces
            bool isHit = IsMatchSyllableFirst(normalized, _targetLetter, _targetSyllable); // Check if the normalized text matches target syllable/letter

            if (isHit) // Answer is correct
            {
                SetPromptArabic(correctMsgAr, correctColor); // Green success message
                StartCoroutine(HandleSuccess()); // Start succcess process (pop up, audio, coins)
            }
            else
            {
                SetPromptArabic(wrongMsgAr, wrongColor); // Red lose message
                StartCoroutine(HandleFailure()); // Start lose process (pop up, audio, count)
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Whisper transcription error: {e}"); // Log any exception in transcription or processing

            SetPromptArabic(wrongMsgAr, wrongColor); // Treat it as a failed attempt visually
            StartCoroutine(HandleFailure()); // Go through lose logic
        }
    }

    // SUCCESS / FAILURE FLOWS ----------------------------------------------
    private IEnumerator HandleSuccess()
    {
        yield return new WaitForSeconds(0.4f); // Small delay to keep success text visible

        SaveSRSuccess_QuizAttempt(); // Save successful quiz attempt
        AwardCoinsOnce(coinsOnSuccess); // Get coins

        if (winPopup) winPopup.SetActive(true); // Win pop up
        if (micButton) micButton.interactable = false; // Disable mic

        if (winAudio)
        {
            winAudio.Stop();
            winAudio.Play();

            while (winAudio.isPlaying) yield return null; // Wait until the success audio finishes playing
        }
        else
        {
            yield return new WaitForSeconds(0.6f); // Fallback small delay if no audio is set
        }

        if (winPopup) winPopup.SetActive(false); // Hide win pop up
        if (srCanvas) srCanvas.SetActive(false); // Close SR canvas
        ResumePlayerMovement(); // Allow player movement

        _isProcessing = false;
        if (micButton) micButton.interactable = true; // Re-enable mic button for future interactions
    }

    private IEnumerator HandleFailure()
    {
        if (micButton) micButton.interactable = false; // Disable mic button while handling failure (pop up, audio, etc.)

        attemptErrorCount++; // Increase error count for this attempt
        SaveAttemptError_QuizAttempt(); // Save latest error count to Firebase quiz attempt

        if (tryAgainAudio)
        {
            tryAgainAudio.Stop();
            tryAgainAudio.Play();

            while (tryAgainAudio.isPlaying) yield return null; // Wait until audio finishes to avoid overlapping prompts
        }

        if (showInstructionDelay > 0f)
            yield return new WaitForSeconds(showInstructionDelay); // extra delay before re-showing instructions

        SetPromptArabic(initialPromptAr, neutralColor); // Reset prompt back to initial message
        UpdateLetterUI(); // Refresh letter in UI

        _isProcessing = false;
        if (micButton) micButton.interactable = true; // Re-enable mic button so child can try again
    }

    // ====== CHILD / QUIZ ATTEMPT INIT =====================================
    IEnumerator InitChildThenStartQuizAttempt()
    {
        var cm = FindObjectOfType<CoinsManager>(); // Try to get selected child from CoinsManager first

        // If there is a CoinsManager and a selected child key, use that
        if (cm != null && !string.IsNullOrEmpty(CoinsManager.instance.SelectedChildKey))
        {
            selectedChildKey = CoinsManager.instance.SelectedChildKey;
        }
        else
        {
            yield return StartCoroutine(FindSelectedOrDisplayedChildKey()); // Otherwise, query Firebase to find selected/displayed child
        }

        yield return StartCoroutine(StartNewQuizAttempt()); // Once child key is known, start a new quiz attempt node
    }

    IEnumerator FindSelectedOrDisplayedChildKey()
    {
        if (string.IsNullOrEmpty(parentId)) yield break; // If no parentId, stop

        var db = FirebaseDatabase.DefaultInstance; // Firebase reference
        var childrenRef = db.RootReference.Child("parents").Child(parentId).Child("children"); // Build path to this parent's children list
        var getChildren = childrenRef.GetValueAsync(); // Request children snapshot from Firebase
        yield return new WaitUntil(() => getChildren.IsCompleted); // Wait until Firebase finishes the request

        // If there's an error or no data, just exit
        if (getChildren.Exception != null || getChildren.Result == null || !getChildren.Result.Exists)
            yield break;

        // First: look for child with selected = true
        foreach (var ch in getChildren.Result.Children)
        {
            bool isSel = false;
            bool.TryParse(ch.Child("selected")?.Value?.ToString(), out isSel); // Try parse "selected" as bool

            if (isSel) { selectedChildKey = ch.Key; yield break; } // If a selected child is found, use that key and stop
        }

        // Second: if none selected, look for child with displayed = true
        foreach (var ch in getChildren.Result.Children)
        {
            bool disp = false;
            bool.TryParse(ch.Child("displayed")?.Value?.ToString(), out disp); // Try parse "displayed" as bool

            if (disp) { selectedChildKey = ch.Key; yield break; } // Use the first displayed child as fallback
        }
    }

    // Start/Reset the single Quiz_attempt node for the current letter
    IEnumerator StartNewQuizAttempt()
    {
        attemptInitialized = false; // Reset attempt initialized flag

        // If currentLetter is empty, sync it with the latest target letter
        if (string.IsNullOrEmpty(currentLetter))
            currentLetter = string.IsNullOrEmpty(_targetLetter) ? "" : _targetLetter;

        // Ensure we know which child this attempt belongs to
        if (string.IsNullOrEmpty(selectedChildKey))
            yield return StartCoroutine(FindSelectedOrDisplayedChildKey());

        // 1) Delete Quiz_attempt under ALL other letters so there's only one active place
        yield return StartCoroutine(DeleteQuizAttemptsExceptCurrentLetter());

        // 2) overwrite Quiz_attempt/1 for the CURRENT letter
        attemptErrorCount = 0;
        rewardedThisRound = false;

        // Build Firebase multi-update dictionary to init fields
        var initUpdates = new Dictionary<string, object>
        {
            // Clear or ensure node exists
            [$"{GetQuizAttemptNodePath()}"] = null,

            // Start errors count at 0
            [$"{GetQuizAttemptNodePath()}/errors"] = 0,

            // Mark as not finished yet
            [$"{GetQuizAttemptNodePath()}/finished"] = false
        };

        FirebaseDatabase.DefaultInstance.RootReference.UpdateChildrenAsync(initUpdates); // Apply the update to the root reference

        // Mark attempt as initialized so other code can safely update it
        attemptInitialized = true;
        yield break;
    }

    // Remove Quiz_attempt from all other letters (keeps only current)
    IEnumerator DeleteQuizAttemptsExceptCurrentLetter()
    {
        if (letters == null || letters.Length == 0) yield break; // If no letters defined, nothing to clean

        var db = FirebaseDatabase.DefaultInstance.RootReference; // Get root reference for Firebase

        List<Task> ops = new List<Task>(); // Collect async operations for each delete

        // Loop through all letters in the pool
        foreach (var l in letters)
        {
            if (string.IsNullOrEmpty(l)) continue; // Skip empty entries
            if (l == currentLetter) continue; // Skip the current letter

            // Build path to Quiz_attempt for this other letter
            string path = $"parents/{parentId}/children/{selectedChildKey}/letters/{l}/activities/SR/Quiz_attempt";
            ops.Add(db.Child(path).RemoveValueAsync()); // Schedule deletion of that node
        }

        // Wait for all delete tasks to complete (errors ignored)
        foreach (var t in ops)
        {
            while (!t.IsCompleted) yield return null;
        }
    }

    // Paths for Quiz_attempt
    string GetQuizAttemptBasePath()
    {
        // Base path for all SR attempts of the current letter for the selected child
        return $"parents/{parentId}/children/{selectedChildKey}/letters/{currentLetter}/activities/SR/Quiz_attempt";
    }

    string GetQuizAttemptNodePath()
    {
        // Always use attempt index "1" (single active attempt node)
        return $"{GetQuizAttemptBasePath()}/1";
    }

    // Update only errors/finished on failure
    void SaveAttemptError_QuizAttempt()
    {
        if (!attemptInitialized) return; // If attempt wasn't initialized yet, skip to avoid invalid writes
        var root = FirebaseDatabase.DefaultInstance.RootReference; // Root reference for multi-path update

        // Update error count and keep finished=false
        var updates = new Dictionary<string, object>
        {
            [$"{GetQuizAttemptNodePath()}/errors"] = attemptErrorCount, // Write current error count
            [$"{GetQuizAttemptNodePath()}/finished"] = false            // Mark that attempt is not finished yet
        };

        root.UpdateChildrenAsync(updates); // Apply partial update
    }

    // Mark finished true on success
    void SaveSRSuccess_QuizAttempt()
    {
        if (!attemptInitialized) return; // Skip if attempt has not been set up yet
        var root = FirebaseDatabase.DefaultInstance.RootReference; // Root reference for partial update

        // Update error count and mark finished=true
        var updates = new Dictionary<string, object>
        {
            [$"{GetQuizAttemptNodePath()}/errors"] = attemptErrorCount, // Persist final error count for this attempt
            [$"{GetQuizAttemptNodePath()}/finished"] = true             // Mark this attempt as successfully finished
        };

        root.UpdateChildrenAsync(updates); // Apply update to Firebase
    }

    // COINS -----------------------------------------------------------------
    void AwardCoinsOnce(int amount)
    {
        if (rewardedThisRound) return; // If already rewarded this round, do nothing
        rewardedThisRound = true;      // Set flag so user don't get coins twice for the same round

        // Try to use CoinsManager if present
        var cm = FindObjectOfType<CoinsManager>();
        if (cm != null)
        {
            // Let CoinsManager handle Firebase coin updates
            cm.AddCoinsToSelectedChild(amount);
            return;
        }

        StartCoroutine(AddCoinsDirectToFirebase(amount)); // write coins directly to Firebase (FALLBACK)
    }

    IEnumerator AddCoinsDirectToFirebase(int amount)
    {
        var auth = FirebaseAuth.DefaultInstance;   // Get current authenticated user from Firebase
        if (auth.CurrentUser == null) yield break; // If no logged-in user, skip coin update

        string pid = auth.CurrentUser.UserId; // Parent id for the Firebase path
        var db = FirebaseDatabase.DefaultInstance; // Firebase database reference

        // Make sure there's a child key
        string childKey = selectedChildKey;
        if (string.IsNullOrEmpty(childKey))
        {
            // If not, try to resolve it from DB
            yield return StartCoroutine(FindSelectedOrDisplayedChildKey());
            childKey = selectedChildKey;

            if (string.IsNullOrEmpty(childKey)) yield break; // If still empty, stop
        }

        string coinsPath = $"parents/{pid}/children/{childKey}/coins"; // Path to the "coins" field for this child
        var coinsRef = db.RootReference.Child(coinsPath);              // Reference to the coins node

        // Run a transaction so increments are safe under concurrency
        var tx = coinsRef.RunTransaction(mutable =>
        {
            long current = 0;

            // Parse current coin value if exists
            if (mutable.Value != null)
            {
                if (mutable.Value is long l) current = l;
                else if (mutable.Value is double d) current = (long)d;
                else long.TryParse(mutable.Value.ToString(), out current);
            }

            mutable.Value = current + amount;          // Increment by the given amount (5)
            return TransactionResult.Success(mutable); // Return success with updated value
        });

        yield return new WaitUntil(() => tx.IsCompleted); // Wait until the transaction finishes
    }

    // INSTRUCTION AUDIOS -----------------------------
    private IEnumerator PlayInstructionForCurrentTarget(float delay)
    {
        // If there's no target letter or syllable or no audio root, skip
        if (string.IsNullOrEmpty(_targetLetter) || string.IsNullOrEmpty(_targetSyllable) || !lettersAudiosRoot)
        {
            // Make sure mic is usable again
            if (micButton) micButton.interactable = true;
            yield break;
        }

        if (delay > 0f) yield return new WaitForSeconds(delay); // delay before playing audio (let UI settle)
        if (tryAgainAudio && tryAgainAudio.isPlaying) tryAgainAudio.Stop(); // Stop any "try again" audio that might still be playing
        if (winAudio && winAudio.isPlaying) winAudio.Stop(); // Stop any previous win audio

        AudioSource src = FindInstructionSource(); // find the audio clip for the current letter Fatha syllable

        // If none found, re-enable mic and exit
        if (!src)
        {
            if (micButton) micButton.interactable = true;
            yield break;
        }

        if (micButton) micButton.interactable = false; // Disable mic while instruction audio is playing

        // Play instruction clip from start
        src.Stop();
        src.Play();

        while (src.isPlaying) yield return null; // Wait until the audio is done
        if (micButton) micButton.interactable = true; // Re-enable mic once instruction is finished
    }

    private AudioSource FindInstructionSource()
    {
        string display = _targetSyllable;          // سا شا etc ......
        string fallback = _targetLetter + "_Fatha"; //Fallback name: letter + "_Fatha" ("س_Fatha")
        var sources = lettersAudiosRoot.GetComponentsInChildren<AudioSource>(true); // Get all AudioSources under the lettersAudiosRoot

        // Search each audio source by GameObject name and clip name
        foreach (var s in sources)
        {
            if (!s) continue;

            string goName = s.gameObject.name.Trim(); // Name of the GameObject holding the AudioSource
            string clipName = s.clip ? s.clip.name.Trim() : string.Empty; // Name of the audio clip itself (if assigned)

            if (goName == display || goName == fallback) return s; // Match using GameObject name
            if (!string.IsNullOrEmpty(clipName) && (clipName == display || clipName == fallback)) return s; // Or match using audio clip name
        }

        return null; // no match
    }

    // PLAYER HELPERS --------------------------------------------------------
    private PopUpTrigger _popup; // Cached reference to PopUpTrigger for movement control

    private PopUpTrigger Popup => _popup ??= FindFirstObjectByType<PopUpTrigger>(); // find PopUpTrigger only once

    private void ResumePlayerMovement()
    {
        Popup?.SetMovement(true); // Tell global popup controller to re-enable movement

        if (_player == null) _player = FindFirstObjectByType<World4>(); // ensure there is player reference

        // Allow the player to move and set to idle animation state
        if (_player != null)
        {
            _player.canMove = true;
            _player.Idle();
        }

        // Reset any virtual joystick or UI movement controller
        var uiCtrl = FindFirstObjectByType<CharacterUIControllerW4>();
        uiCtrl?.StopMoving();

        if (Time.timeScale == 0f) Time.timeScale = 1f;
    }

    // ARABIC HELPERS --------------------------------------------------------
    private string NormalizeArabic(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty; // If text is null/empty, return an empty string

        s = s.Trim(); // Trim spaces at both ends
        s = new string(s.Where(ch => ch != '\u0640' && (ch < '\u064B' || ch > '\u0652')).ToArray()); // Remove tatweel and diacritics while reading
        s = s.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا'); // Normalize different forms of أ to bare 'ا'
        s = s.Replace('ى', 'ي').Replace('ة', 'ه');         // Normalize ى ئ and ه ة
        s = s.Trim('\"', '\'', '“', '”', '’', '‘', '(', ')', '[', ']', '{', '}', '.', ',', '!', '?', ':', ';'); // Strip common punctuation characters from the ends

        return s; // Return normalized string
    }

    private string CollapseLongVowels(string s)
    {
        if (string.IsNullOrEmpty(s)) return s; // If string is null or empty, nothing to collapse

        s = Regex.Replace(s, "ا{2,}", "ا");   // Replace multiple consecutive 'ا' with a single one
        s = Regex.Replace(s, "و{2,}", "و");   // Replace multiple consecutive 'و' with a single one
        s = Regex.Replace(s, "ي{2,}", "ي");   // Replace multiple consecutive 'ي' with a single one
        s = Regex.Replace(s, "\\s{2,}", " "); // Collapse extra spaces

        return s; // Return cleaned-up text
    }

    // MATCHING --------------------------------------------------------------
    private bool IsMatchSyllableFirst(string recognizedNorm, string targetLetter, string targetSyllable)
    {
        if (string.IsNullOrWhiteSpace(recognizedNorm)) return false; // If there's no recognized text, nothing to match

        var tokens = recognizedNorm.Split(new[] // Split the recognized text into tokens using common separators
        {
            ' ', '،', ',', '.', '!', '?', '؛', ':', 'ـ', '-', '_',
            '\n', '\r', '\t', '\"', '«', '»', '“', '”', '(', ')', '[', ']', '{', '}'
        }, StringSplitOptions.RemoveEmptyEntries);

        string normTargetSyllable = NormalizeArabic(targetSyllable); // Normalize the target Fatha syllable
        string normLetter = NormalizeArabic(targetLetter); // Normalize the bare letter

        // 1) Exact match of syllable "سا"
        if (tokens.Any(tok => tok == normTargetSyllable)) return true;

        // 2) Syllable appears inside a longer token (e.g., "سارة" contains "سا")
        if (tokens.Any(tok => tok.Contains(normTargetSyllable))) return true;

        // 3) Fallback: accept just the letter "س" as a valid hit
        if (tokens.Any(tok => tok == normLetter)) return true;

        // If none of the above, it's not a match
        return false;
    }

    // UI HELPERS ------------------------------------------------------------
    private void SetMicVisual(bool recording)
    {
        // Try to get TMP text inside the mic button
        var tmp = micButton.GetComponentInChildren<TMP_Text>();
        if (tmp != null)
        {
            tmp.text = recording ? "إيقاف" : "تسجيل"; // Show "إيقاف" text while recording, otherwise "تسجيل"
            return;
        }

        // Fallback: legacy Text component
        var legacy = micButton.GetComponentInChildren<Text>();
        if (legacy != null) legacy.text = recording ? "إيقاف" : "تسجيل";
    }

    private void SetPromptArabic(string txt, Color color)
    {
        if (!promptText) return; // If the prompt Text component is missing, do nothing

        string shaped = ArabicFixer.Fix(txt ?? "", showTashkeel: false, useHinduNumbers: false); // Shape Arabic text properly for display

        if (arabicFont) promptText.font = arabicFont; // Apply Arabic-supporting font

        promptText.isRightToLeftText = false; // manually control direction; FALSE
        promptText.alignment = TextAlignmentOptions.Center; // Center-align prompt text
        promptText.color = color;  // Apply the given color

        promptText.text = shaped; // Assign the processed text to the UI
    }

    private void UpdateLetterUI()
    {
        if (!letterText) return; // If no letter text element is assigned, exit safely

        string toDisplay = _targetLetter; // Display ONLY the plain letter, no diacritics and no long vowel
        string shaped = ArabicFixer.Fix(toDisplay, showTashkeel: false, useHinduNumbers: false); // Shape Arabic for correct visual appearance

        if (arabicFont) letterText.font = arabicFont; // Use the Arabic font

        letterText.isRightToLeftText = false;
        letterText.alignment = TextAlignmentOptions.Center;
        letterText.fontSize = Mathf.Max(letterText.fontSize, 100);

        letterText.text = shaped;
    }

    // WHISPER INTEROP -------------------------------------------------------
    private string ExtractText(object result)
    {
        if (result == null) return string.Empty; // If result is null, there is no transcript
        if (result is string s) return s;        // If result is already a string, return it

        var t = result.GetType(); // Use reflection to search for common string properties

        // Try common property names that might store the text result
        foreach (var name in new[] { "Text", "text", "Result", "result", "Transcript", "transcript", "FinalText", "finalText" })
        {
            var p = t.GetProperty(name);
            if (p != null && p.PropertyType == typeof(string))
            {
                var val = (string)p.GetValue(result);
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
        }

        // If not found in properties, try common field names
        foreach (var name in new[] { "Text", "text", "Result", "result", "Transcript", "transcript", "FinalText", "finalText" })
        {
            var f = t.GetField(name);
            if (f != null && f.FieldType == typeof(string))
            {
                var val = (string)f.GetValue(result);
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
        }

        // If result has segments (like Whisper default output), try to join them
        object segsObj = t.GetProperty("Segments")?.GetValue(result) ?? t.GetField("Segments")?.GetValue(result)
                       ?? t.GetProperty("segments")?.GetValue(result) ?? t.GetField("segments")?.GetValue(result);

        // If segments are enumerable, extract text from each
        if (segsObj is System.Collections.IEnumerable segs)
        {
            var parts = new List<string>();
            foreach (var seg in segs)
            {
                var st = seg.GetType();

                // Try property or field "Text"/"text" on each segment
                var p = st.GetProperty("Text") ?? st.GetProperty("text");
                var f = st.GetField("Text") ?? st.GetField("text");

                string segText = p?.GetValue(seg) as string ?? f?.GetValue(seg) as string;

                // Only add non-empty segment text
                if (!string.IsNullOrWhiteSpace(segText)) parts.Add(segText);
            }

            // Join all segment texts into one line if any found
            if (parts.Count > 0) return string.Join(" ", parts);
        }

        // As a last resort, use object's ToString()
        return result.ToString() ?? string.Empty;
    }

    private async Task<object> TranscribeFromChunk(AudioChunk chunk)
    {
        var t = chunk.GetType(); // Get runtime type of chunk (Whisper plugin specific)

        // get an AudioClip directly from the chunk
        var clipProp = t.GetProperty("Clip");
        if (clipProp != null)
        {
            var clipVal = clipProp.GetValue(chunk) as AudioClip;
            if (clipVal != null)
                // Ask Whisper to transcribe the AudioClip
                return await whisper.GetTextAsync(clipVal);
        }

        // Fallback: manually get float[] samples from chunk
        float[] samples =
            t.GetField("Samples")?.GetValue(chunk) as float[] ??
            t.GetProperty("Samples")?.GetValue(chunk) as float[] ??
            t.GetField("Data")?.GetValue(chunk) as float[] ??
            t.GetProperty("Data")?.GetValue(chunk) as float[];

        int sampleRate = 16000; // Default sample rate

        // Try get sample rate / frequency fields or properties
        var freqField = t.GetField("Frequency") ?? t.GetField("SampleRate");
        var freqProp = t.GetProperty("Frequency") ?? t.GetProperty("SampleRate");

        if (freqField != null) sampleRate = (int)freqField.GetValue(chunk); // Override default sampleRate if a field exists
        if (freqProp != null) sampleRate = (int)freqProp.GetValue(chunk);   // Override default sampleRate if a property exists

        int channels = 1; // Default to mono unless chunk says otherwise

        // Try retrieving channels or channel count info
        var chField = t.GetField("Channels") ?? t.GetField("ChannelCount");
        var chProp = t.GetProperty("Channels") ?? t.GetProperty("ChannelCount");

        if (chField != null) channels = (int)chField.GetValue(chunk); // Override channels from field if present
        if (chProp != null) channels = (int)chProp.GetValue(chunk);   // Override channels from property if present

        // If we have raw samples, transcribe using Whisper's float[] overload
        if (samples != null)
            return await whisper.GetTextAsync(samples, sampleRate, channels);

        // As a last fallback, try to get the last recorded clip from mic
        var micType = mic.GetType();
        var lastClipProp = micType.GetProperty("Clip") ?? micType.GetProperty("RecordedClip") ?? micType.GetProperty("LastClip");

        if (lastClipProp != null)
        {
            var clip = lastClipProp.GetValue(mic) as AudioClip;
            if (clip != null)
                return await whisper.GetTextAsync(clip);
        }

        // If it reaches here, it couldn't find any valid audio data
        throw new InvalidOperationException("Could not read audio from AudioChunk for this package version.");
    }
}