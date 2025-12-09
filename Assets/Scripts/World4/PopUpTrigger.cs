using Mankibo;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Firebase.Auth;
using Firebase.Database;

public static class PlayerStateStore
{
    public static bool hasSnapshot;
    public static Vector3 position;
    public static Quaternion rotation;
    public static bool canMove;
    public static string sceneName;
    // skip AR trigger when coming back from AR
    public static bool suppressNextARTrigger;

    // Random-letter storage for AR scene
    public static string targetLetter;

    // word & detection keys storage (AR object detection)
    public static string targetWordAr;              // Arabic word
    public static List<string> targetDetectKeys;    // EN keys to match model labels

    public static void Capture(Transform player, bool canMoveFlag)
    {
        position = player.position;
        rotation = player.rotation;

        // Always allow movement when he comes back from AR
        canMove = true;

        sceneName = SceneManager.GetActiveScene().name;
        hasSnapshot = true;
    }
}

public class PopUpTrigger : MonoBehaviour
{
    [Header("Pop-Up Panels")]
    public GameObject speechRecognitionPopUp;
    public GameObject arPopUp;               // AR intro popup
    public GameObject levelFinishedCanvas;   // Treasure

    [Header("Level Finished Finale UI")]
    public GameObject levelFinishedPopup;    // PopUp-Finished!
    public Button treasureChestButton;       // Treasure chest
    public AudioSource chestRewardAudio;
    public AudioSource chestOpenSfx;

    [Header("Treasure Reward (Coins)")]
    public int coinsOnTreasure = 10;
    private bool treasureCoinsGiven = false;

    private string parentId;           // Firebase parent user id
    private string selectedChildKey;

    [Header("Letter Tracing Canvas")]
    public GameObject letterTracingCanvas;
    private GameObject currentLetterPanel;

    [Header("Cards Root Canvas")]
    [Tooltip("Parent containing children named 'Cards-ض', 'Cards-ظ', 'Cards-ذ', 'Cards-غ'.")]
    public GameObject cardsRootCanvas;

    // Cache letter-specific cards canvases by name "Cards-LETTER"
    private readonly Dictionary<string, GameObject> _cardsCanvases = new Dictionary<string, GameObject>();

    [Header("Audio Sources")]
    public AudioSource speechRecognitionAudio;
    public AudioSource arAudio;              // plays the letter-specific VO
    public AudioSource levelFinishedAudio;

    [Header("Close Buttons")]
    public Button speechCloseButton;
    public Button arCloseButton;
    public Button homePageButton;

    [Header("Math Canvas Manager")]
    public MathExitManager MathManager;

    // --- AR start flow (Bird2RED only) ---
    [Header("AR Start Flow (Bird2RED only)")]
    public string bird2RedName = "Bird2RED";
    public Button startARButton;
    public string arSceneName = "AR-RunYOLO";
    // -------------------------------------

    // -------- letter VO clips (AR-only) --------
    [Header("AR Letter Audio Clips")]
    public AudioClip clip_س;
    public AudioClip clip_ش;
    public AudioClip clip_ص;
    public AudioClip clip_خ;
    public AudioClip clip_ر;
    // -------------------------------------------

    // Letter Sets
    private static readonly string[] AR_LETTERS = { "س", "ش", "ص", "خ", "ر" };
    private static readonly string[] CARD_LETTERS = { "ض", "ظ", "ذ", "غ" };
    private static readonly string[] ALL_LETTERS = { "س", "ش", "ص", "خ", "ر", "ض", "ظ", "ذ", "غ" };

    private static bool IsArLetter(string L) => System.Array.IndexOf(AR_LETTERS, L) >= 0;
    private static bool IsCardLetter(string L) => System.Array.IndexOf(CARD_LETTERS, L) >= 0;

    // ====== AR: Letter ➜ choices of words (+ detection keys) ======
    private struct WordChoice
    {
        public string wordAr;          // Arabic word shown to the child
        public string[] detectKeysEn;  // Model label keys to match
        public WordChoice(string ar, params string[] keys) { wordAr = ar; detectKeysEn = keys; }
    }
    private static readonly Dictionary<string, List<WordChoice>> _letterToWords = BuildLetterToWords();

    private World4 playerScript;

    private void Start()
    {
        // Get player
        playerScript = FindFirstObjectByType<World4>();

        // Hide all popups
        if (speechRecognitionPopUp) speechRecognitionPopUp.SetActive(false);
        if (arPopUp) arPopUp.SetActive(false);
        if (levelFinishedCanvas) levelFinishedCanvas.SetActive(false);
        if (letterTracingCanvas) letterTracingCanvas.SetActive(false);
        if (cardsRootCanvas) cardsRootCanvas.SetActive(false);

        // --- Firebase identities for coins ---
        var auth = FirebaseAuth.DefaultInstance;
        parentId = auth.CurrentUser != null
            ? auth.CurrentUser.UserId
            : "debug_parent";

        // Try to get selected child from CoinsManager first
        var cm = FindObjectOfType<CoinsManager>();
        if (cm != null && !string.IsNullOrEmpty(CoinsManager.instance.SelectedChildKey))
        {
            selectedChildKey = CoinsManager.instance.SelectedChildKey;
        }
        else
        {
            // fallback: find selected/displayed child directly from DB
            StartCoroutine(FindSelectedOrDisplayedChildKey());
        }

        // Build lookup for Cards child-canvases if a root is assigned
        CacheCardsChildren();

        // Find close buttons if not assigned in Inspector
        if (speechCloseButton == null && speechRecognitionPopUp != null)
            speechCloseButton = FindButtonIn(speechRecognitionPopUp, "btn-Close");

        if (arCloseButton == null && arPopUp != null)
            arCloseButton = FindButtonIn(arPopUp, "btn-Close");

        // Hook close buttons → open Math exit canvas
        if (speechCloseButton)
        {
            speechCloseButton.onClick.RemoveAllListeners();
            speechCloseButton.onClick.AddListener(() =>
            {
                if (MathManager != null)
                {
                    MathManager.SetOriginToSpeech(speechRecognitionPopUp);
                    MathManager.OpenMathCanvas();
                }
            });
        }

        if (arCloseButton)
        {
            arCloseButton.onClick.RemoveAllListeners();
            arCloseButton.onClick.AddListener(() =>
            {
                if (MathManager != null)
                {
                    MathManager.SetOriginToAR(arPopUp);
                    MathManager.OpenMathCanvas();
                }
            });
        }

        if (homePageButton)
        {
            homePageButton.onClick.RemoveAllListeners();
            homePageButton.onClick.AddListener(GoToHomePage);
        }

        if (treasureChestButton != null)
        {
            treasureChestButton.onClick.RemoveAllListeners();
            treasureChestButton.onClick.AddListener(OnTreasureChestClicked);
        }
    }

    // == Final Canvas ==
    private void OnTreasureChestClicked()
    {
        // lock button so child can't spam it
        if (treasureChestButton != null)
            treasureChestButton.interactable = false;

        // play chest opening SFX first
        if (chestOpenSfx != null)
            chestOpenSfx.Play();

        // wait for audio, then show popup + reward voice
        StartCoroutine(ShowPopupAfterChestOpens());
    }

    private IEnumerator ShowPopupAfterChestOpens()
    {
        if (chestOpenSfx != null)
        {
            while (chestOpenSfx.isPlaying)
                yield return null;
        }

        if (levelFinishedPopup != null)
            levelFinishedPopup.SetActive(true);

        // Give coins ONCE when popup appears
        AwardTreasureCoinsOnce(coinsOnTreasure);

        if (chestRewardAudio != null)
            chestRewardAudio.Play();
    }

    private void HideMoveButtons()
    {
        // Find ButtonCanvas in the scene
        var buttonCanvas = GameObject.Find("ButtonCanvas");
        if (!buttonCanvas) return;

        // Find Left and Right under it and hide them
        var left = buttonCanvas.transform.Find("Left");
        if (left) left.gameObject.SetActive(false);

        var right = buttonCanvas.transform.Find("Right");
        if (right) right.gameObject.SetActive(false);
    }

    // ===== COINS LOGIC =====
    void AwardTreasureCoinsOnce(int amount)
    {
        if (treasureCoinsGiven) return;
        treasureCoinsGiven = true;

        var cm = FindObjectOfType<CoinsManager>();
        if (cm != null)
        {
            // CoinsManager already writes to Firebase
            cm.AddCoinsToSelectedChild(amount);
            return;
        }

        // Fallback: write directly to Firebase if CoinsManager is missing
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

    IEnumerator FindSelectedOrDisplayedChildKey()
    {
        if (string.IsNullOrEmpty(parentId)) yield break;

        var db = FirebaseDatabase.DefaultInstance;
        var childrenRef = db.RootReference.Child("parents").Child(parentId).Child("children");
        var getChildren = childrenRef.GetValueAsync();
        yield return new WaitUntil(() => getChildren.IsCompleted);

        if (getChildren.Exception != null || getChildren.Result == null || !getChildren.Result.Exists)
            yield break;

        // 1) Prefer child with selected = true
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

        // 2) Otherwise take child with displayed = true
        foreach (var ch in getChildren.Result.Children)
        {
            bool isDisp = false;
            bool.TryParse(ch.Child("displayed")?.Value?.ToString(), out isDisp);
            if (isDisp)
            {
                selectedChildKey = ch.Key;
                yield break;
            }
        }
    }

    private void ShowLevelFinished()
    {
        if (levelFinishedCanvas)
        {
            levelFinishedCanvas.SetActive(true);

            if (levelFinishedPopup != null)
                levelFinishedPopup.SetActive(false);
        }
        HideMoveButtons();

        // lock chest at the beginning
        if (treasureChestButton != null)
            treasureChestButton.interactable = false;

        // play intro audio
        if (levelFinishedAudio != null)
        {
            levelFinishedAudio.Play();
            StartCoroutine(EnableChestAfterIntro(levelFinishedAudio));
        }

        if (playerScript != null)
        {
            SetMovement(false);
            playerScript.Idle();
        }
    }

    private IEnumerator EnableChestAfterIntro(AudioSource intro)
    {
        if (intro != null)
        {
            while (intro.isPlaying)
                yield return null;
        }

        if (treasureChestButton != null)
            treasureChestButton.interactable = true;
    }

    // == End of Final Canvas ==

    private void CacheCardsChildren()
    {
        _cardsCanvases.Clear();
        if (!cardsRootCanvas) return;

        foreach (Transform child in cardsRootCanvas.transform)
        {
            var n = child.name.Trim();
            if (n.StartsWith("Cards-") && n.Length >= 7)
            {
                string letter = n.Substring("Cards-".Length);
                _cardsCanvases[letter] = child.gameObject;
                child.gameObject.SetActive(false); // keep all off initially
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        // Red bird flow (random letter + AR-or-Cards branching)
        if (gameObject.name == bird2RedName)
        {
            // If returned from AR, skip this trigger ONCE
            if (PlayerStateStore.suppressNextARTrigger)
            {
                PlayerStateStore.suppressNextARTrigger = false;
                return;
            }

            HandleBird2RedFlow();
            return;
        }

        // Legacy triggers by tag
        if (gameObject.CompareTag("Woman")) OpenLetterTracingCanvas();
        else if (gameObject.CompareTag("Bird1")) OpenSpeechRecognitionPopUp();
        else if (gameObject.CompareTag("Bird2")) ShowPopUp(arPopUp, arAudio);
        else if (gameObject.CompareTag("Finish")) ShowLevelFinished();
    }

    // ===================== Speech Recognition =====================
    private void OpenSpeechRecognitionPopUp()
    {
        if (speechRecognitionPopUp)
        {
            speechRecognitionPopUp.SetActive(true);

            // Defensive: ensure no stray audios auto-play inside SR popup
            var stray = speechRecognitionPopUp.GetComponentsInChildren<AudioSource>(true);
            foreach (var s in stray)
            {
                if (!s) continue;
                s.playOnAwake = false;
                s.loop = false;
                if (s.isPlaying) s.Stop();
            }
        }
        if (playerScript != null) SetMovement(false);
    }

    // ========================= AR / CARDS =========================
    // 1) After choosing the letter:
    private void HandleBird2RedFlow()
    {
        if (playerScript != null) SetMovement(false);

        // Choose the letter
        string chosenLetter = PickRandomLetterFromAll();

        GameSession.CurrentLetter = chosenLetter;  // W4CardRoundManager reads this
        W4Session.CurrentLetter = chosenLetter;

        PlayerStateStore.targetLetter = chosenLetter;
        PlayerPrefs.SetString("TargetLetter", chosenLetter);
        PlayerPrefs.Save();

        // wipe any previous AR word/keys so they can't leak
        ClearARTargetPayload();

        if (IsArLetter(chosenLetter))
        {
            if (cardsRootCanvas) cardsRootCanvas.SetActive(false);
            if (arPopUp) arPopUp.SetActive(true);

            if (startARButton == null && arPopUp != null)
                startARButton = FindButtonIn(arPopUp, "btn-StartAR");
            if (startARButton != null)
            {
                startARButton.onClick.RemoveAllListeners();
                startARButton.gameObject.SetActive(false);
                startARButton.onClick.AddListener(OnStartARClicked);
            }

            var vo = GetClipForLetter(chosenLetter);
            if (arAudio != null)
            {
                if (vo != null) arAudio.clip = vo;
                arAudio.Play();
            }
            StartCoroutine(ShowStartAfterAudio(arAudio, startARButton));
        }
        else if (IsCardLetter(chosenLetter))
        {
            if (arPopUp) arPopUp.SetActive(false);
            OpenCardsCanvasForLetter(chosenLetter);
        }
        else
        {
            Debug.LogWarning($"[PopUpTrigger] Unknown letter picked: {chosenLetter}");
        }
    }

    private void OpenCardsCanvasForLetter(string letter)
    {
        if (!cardsRootCanvas)
        {
            Debug.LogWarning("[PopUpTrigger] CardsRootCanvas is not assigned.");
            return;
        }

        // Show card root and disable player movement
        cardsRootCanvas.SetActive(true);
        if (playerScript != null)
            SetMovement(false);

        // Hide all child canvases first
        foreach (var kv in _cardsCanvases)
            if (kv.Value) kv.Value.SetActive(false);

        GameObject subCanvas = null;
        string expectedName = "Cards-" + letter;

        // 1) Try dictionary
        if (_cardsCanvases.TryGetValue(letter, out subCanvas) && subCanvas != null)
        {
            subCanvas.SetActive(true);
        }
        else
        {
            // 2) Try direct child
            var t = cardsRootCanvas.transform.Find(expectedName);
            if (t != null)
            {
                subCanvas = t.gameObject;
                subCanvas.SetActive(true);
                _cardsCanvases[letter] = subCanvas;
            }
            else
            {
                // 3) Deep search
                subCanvas = DeepFind(cardsRootCanvas.transform, expectedName);
                if (subCanvas != null)
                {
                    subCanvas.SetActive(true);
                    _cardsCanvases[letter] = subCanvas;
                }
                else
                {
                    Debug.LogWarning($"[PopUpTrigger] Could not find card canvas '{expectedName}'.");
                    return;
                }
            }
        }

        // ALWAYS hook close button AFTER canvas is active
        if (MathManager != null && subCanvas != null)
        {
            var closeBtn = subCanvas.GetComponentsInChildren<Button>(true)
                                    .FirstOrDefault(b => b.name == "btn-Close");
            if (closeBtn != null)
            {
                closeBtn.onClick.RemoveAllListeners();
                closeBtn.onClick.AddListener(() =>
                {
                    MathManager.SetOriginToCards(cardsRootCanvas);
                    MathManager.OpenMathCanvas();
                });
            }
        }
    }

    // deep search by exact name
    private GameObject DeepFind(Transform root, string name)
    {
        foreach (Transform child in root)
        {
            if (child.name == name) return child.gameObject;
            var found = DeepFind(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private string ListDirectChildren(Transform root)
    {
        var names = new System.Text.StringBuilder();
        foreach (Transform c in root) names.Append(c.name).Append(", ");
        return names.ToString();
    }

    // ========================= AR helpers =========================
    private AudioClip GetClipForLetter(string letter)
    {
        switch (letter)
        {
            case "س": return clip_س;
            case "ش": return clip_ش;
            case "ص": return clip_ص;
            case "خ": return clip_خ;
            case "ر": return clip_ر;
            default: return null; // Cards letters have no AR VO
        }
    }

    private IEnumerator ShowStartAfterAudio(AudioSource audio, Button btn)
    {
        if (audio != null)
        {
            while (!audio.isPlaying) yield return null;
            while (audio.isPlaying) yield return null;
        }
        if (btn != null) btn.gameObject.SetActive(true);
    }

    private void OnStartARClicked()
    {
        // Snapshot the currently active character BEFORE switching scenes
        var activePlayer = FindFirstObjectByType<World4>();
        if (activePlayer != null)
        {
            PlayerStateStore.Capture(activePlayer.transform, activePlayer.canMove);
        }
        else if (playerScript != null)
        {
            // Fallback
            PlayerStateStore.Capture(playerScript.transform, playerScript.canMove);
        }

        // Use the already chosen letter
        string chosenLetter = PlayerStateStore.targetLetter;
        if (string.IsNullOrEmpty(chosenLetter))
            chosenLetter = PlayerPrefs.GetString("TargetLetter", "");

        // Safety fallback
        if (string.IsNullOrEmpty(chosenLetter))
        {
            chosenLetter = PickRandomLetterFromAll();
            PlayerStateStore.targetLetter = chosenLetter;
            W4Session.CurrentLetter = chosenLetter;
            PlayerPrefs.SetString("TargetLetter", chosenLetter);
        }

        // For AR letters, choose a random word + detection keys
        if (IsArLetter(chosenLetter))
        {
            var choice = PickRandomWordForLetter(chosenLetter, out bool ok);
            if (!ok)
            {
                var fallback = _letterToWords.Keys.OrderBy(_ => Random.value).FirstOrDefault();
                if (!string.IsNullOrEmpty(fallback))
                    choice = PickRandomWordForLetter(fallback, out ok);
            }

            PlayerStateStore.targetWordAr = choice.wordAr;
            PlayerStateStore.targetDetectKeys = new List<string>(choice.detectKeysEn);

            PlayerPrefs.SetString("TargetWordAr", choice.wordAr ?? "");
            PlayerPrefs.SetString("TargetDetectKeys", string.Join("|", choice.detectKeysEn ?? System.Array.Empty<string>()));
        }

        PlayerPrefs.Save();
        SceneManager.LoadScene(arSceneName);
    }

    private Button FindButtonIn(GameObject root, string buttonName)
    {
        if (root == null) return null;
        foreach (var b in root.GetComponentsInChildren<Button>(true))
            if (b.name == buttonName) return b;
        return null;
    }

    private string PickRandomLetterFromAll()
    {
        int i = Random.Range(0, ALL_LETTERS.Length);
        return ALL_LETTERS[i];
    }

    private WordChoice PickRandomWordForLetter(string letter, out bool ok)
    {
        ok = _letterToWords.TryGetValue(letter, out var list) && list != null && list.Count > 0;
        if (!ok) return new WordChoice("—", "");

        var candidates = list.FindAll(w => w.detectKeysEn != null && w.detectKeysEn.Length > 0);
        if (candidates.Count == 0)
        {
            ok = false;
            return new WordChoice("—", "");
        }

        int j = Random.Range(0, candidates.Count);
        return candidates[j];
    }

    public static bool TryPickRandomWordForLetter(string letter, out string wordAr, out List<string> keys, string excludeWordAr = null)
    {
        wordAr = null;
        keys = null;

        if (string.IsNullOrEmpty(letter)) return false;
        if (!_letterToWords.TryGetValue(letter, out var list) || list == null || list.Count == 0)
            return false;

        // keep only choices that actually have detect keys
        var candidates = list.Where(w => w.detectKeysEn != null && w.detectKeysEn.Length > 0).ToList();
        if (candidates.Count == 0) return false;

        // avoid picking the same Arabic word again if provided
        if (!string.IsNullOrEmpty(excludeWordAr))
            candidates = candidates.Where(w => !string.Equals(w.wordAr, excludeWordAr, System.StringComparison.Ordinal)).ToList();

        if (candidates.Count == 0) return false;

        int j = UnityEngine.Random.Range(0, candidates.Count);
        var pick = candidates[j];

        wordAr = pick.wordAr;
        keys = new List<string>(pick.detectKeysEn);
        return true;
    }

    // Clears any stale AR object so a new letter can't inherit old word/keys.
    private void ClearARTargetPayload()
    {
        PlayerStateStore.targetWordAr = null;
        PlayerStateStore.targetDetectKeys = null;
        PlayerPrefs.DeleteKey("TargetWordAr");
        PlayerPrefs.DeleteKey("TargetDetectKeys");
    }

    // ================= Legacy tracing helpers =================
    private void OpenLetterTracingCanvas()
    {
        if (!letterTracingCanvas) return;

        letterTracingCanvas.SetActive(true);
        if (playerScript != null) SetMovement(false);

        if (currentLetterPanel != null)
        {
            LetterTracingW4 oldTracingScript = currentLetterPanel.GetComponentInChildren<LetterTracingW4>();
            if (oldTracingScript != null) oldTracingScript.StopAllCoroutines();
            currentLetterPanel.SetActive(false);
            currentLetterPanel = null;
        }
        RandomizeLetterPanel();
    }

    private void RandomizeLetterPanel()
    {
        if (!letterTracingCanvas) return;

        foreach (Transform panel in letterTracingCanvas.transform)
            panel.gameObject.SetActive(false);

        List<GameObject> panels = new List<GameObject>();
        foreach (Transform panel in letterTracingCanvas.transform)
        {
            if (panel.name.Contains("Winning")) continue;
            panels.Add(panel.gameObject);
        }
        if (panels.Count == 0) return;

        int randomIndex = Random.Range(0, panels.Count);
        currentLetterPanel = panels[randomIndex];
        currentLetterPanel.SetActive(true);
        ActivateAllChildren(currentLetterPanel);

        Button closeButton = currentLetterPanel.GetComponentInChildren<Button>();
        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(() =>
            {
                if (MathManager != null)
                {
                    MathManager.SetOriginToTracing(letterTracingCanvas,
                        currentLetterPanel.GetComponentInChildren<LetterTracingW4>());
                    MathManager.OpenMathCanvas();
                }
            });
        }

        AudioSource letterAudio = currentLetterPanel.GetComponentInChildren<AudioSource>();
        if (letterAudio != null)
        {
            letterAudio.Play();

            LetterTracingW4 tracingScript = currentLetterPanel.GetComponentInChildren<LetterTracingW4>();
            if (tracingScript != null)
            {
                if (tracingScript.LetterImageObj != null) tracingScript.LetterImageObj.SetActive(false);
                if (tracingScript.TracingPointsGroup != null) tracingScript.TracingPointsGroup.SetActive(false);
                tracingScript.SetCanTraceAfterAudio(letterAudio);
            }
            if (MathManager != null) MathManager.currentTracingScript = tracingScript;
        }
    }

    private void ActivateAllChildren(GameObject parent)
    {
        foreach (Transform child in parent.transform)
        {
            child.gameObject.SetActive(true);
            ActivateAllChildren(child.gameObject);
        }
    }

    private void ShowPopUp(GameObject popUp, AudioSource audioSource)
    {
        if (popUp) popUp.SetActive(true);
        if (audioSource) audioSource.Play();
        if (playerScript != null) SetMovement(false);
    }

    // ======================= Character Movement =======================
    public void SetMovement(bool state)
    {
        // 1) InputSystem: stop any latched input, then toggle the map
        var wc = FindFirstObjectByType<World4Controller>();
        if (wc != null)
        {
            // Release any held input first
            wc.StopMove();

            if (state) wc.EnablePlayerInput();
            else wc.DisablePlayerInput();
        }

        // 2) Characters: force Idle (speed=0, charMovement=0) and stop SFX
        foreach (var w in FindObjectsOfType<World4>(true))
        {
            if (!w || !w.gameObject.activeInHierarchy) continue;

            // Hard stop locomotion & animation to clear "running" pose
            w.Idle();                 // sets animator "Move" to 0, charMovement = Vector2.zero
            if (!state && w.footstepAudio && w.footstepAudio.isPlaying)
                w.footstepAudio.Stop();

            // Toggle the movement gate
            w.canMove = state;
        }
    }

    public void GoToHomePage() => SceneManager.LoadScene("HomePage");

    // ======================= AR letters ➜ words =======================
    private static Dictionary<string, List<WordChoice>> BuildLetterToWords()
    {
        WordChoice W(string ar, params string[] keys) => new WordChoice(ar, keys);
        var d = new Dictionary<string, List<WordChoice>>();

        d["ش"] = new List<WordChoice> {
            W("شخص", "Person"),
            W("شوكة", "Fork"),
            W("شبشب", "Slippers"),
            W("شريط لاصق", "Adhesive tape", "Tape"),
        };

        d["س"] = new List<WordChoice> {
            W("سوار", "Bracelet"),
            W("سلسال", "Necklace"),
            W("ساعة", "Watch", "Clock"),
            W("سلة", "Basket"),
            W("سجادة", "Carpet"),
            W("سرير", "Bed"),
            W("سكين", "Knife"),
            W("سبورة", "Blackboard/Whiteboard"),
            W("سُلّم", "Ladder"),
            W("سماعة", "Head Phone", "earphone", "Speaker"),
        };

        d["ص"] = new List<WordChoice> {
            W("صحن", "Plate"),
            W("صورة", "Picture/Frame"),
            W("صندوق", "Storage box", "Box"),
            W("صنبور", "Faucet"),
        };

        d["خ"] = new List<WordChoice> {
            W("خاتم", "Ring"),
            W("خبز", "Bread"),
            W("خيمة", "Tent"),
            W("خيار", "Cucumber"),
            W("خلاط", "Blender"),
            W("خزانة", "Cabinet/shelf"),
            W("خوخ", "Peach"),
            W("خس", "Lettuce"),
        };

        d["ر"] = new List<WordChoice> {
            W("رف", "Cabinet/shelf", "Bookcase"),
            W("ربطة عنق", "Tie"),
            W("ريموت", "Remote"),
            W("رمان", "Pomegranate"),
        };

        return d;
    }
}