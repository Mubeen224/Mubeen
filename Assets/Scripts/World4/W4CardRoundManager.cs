using System.Collections;
using System.Collections.Generic;
using Firebase.Auth;
using Firebase.Database;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class W4CardData
{
    public string name;
    public Sprite mainSprite;
    public Sprite wrongSprite;
    public AudioClip voiceClip;

    [Header("Special for correct cards")]
    public GameObject winPopup;
    public AudioClip applauseClip;
    public AudioClip winClip;
}

public class W4CardRoundManager : MonoBehaviour
{
    [Header("Card Setup")]
    public List<W4CardButton> cardButtons;
    public List<W4CardData> correctCards;
    public List<W4CardData> wrongCards;

    [Header("Audio Source")]
    public AudioSource sfxSource;

    [Header("Letter (per canvas)")]
    [SerializeField] public string currentLetter = "";

    [Header("Letters Pool (for clearing other Quiz_attempt nodes)")]
    public string[] letters = new[] { "س", "ش", "ص", "ض", "ظ", "ذ", "ر", "خ", "غ" };

    [Header("Attempts / Coins (Firebase)")]
    public int coinsOnSuccess = 5;

    // ---- Single Quiz_attempt/1 ----
    private const int FIXED_ATTEMPT_NUMBER = 1;
    private int currentAttempt = FIXED_ATTEMPT_NUMBER;
    private int attemptErrorCount = 0;
    private bool attemptInitialized = false;

    // Firebase identity
    private string parentId;
    private string selectedChildKey;

    // Coins: avoid double reward
    private bool rewardedThisRound = false;

    // ───────────────────────────────── OnEnable ─────────────────────────────
    private void OnEnable()
    {
        // Make sure we have a letter
        if (string.IsNullOrEmpty(currentLetter) && !string.IsNullOrEmpty(GameSession.CurrentLetter))
            currentLetter = GameSession.CurrentLetter;

        // Firebase user
        var auth = FirebaseAuth.DefaultInstance;
        parentId = (auth != null && auth.CurrentUser != null)
            ? auth.CurrentUser.UserId
            : "debug_parent";

        // start DB init (child + Quiz_attempt)
        StartCoroutine(InitChildThenStartQuizAttempt());

        // Cards UI refresh
        RefreshCards();
    }

    public void SetCurrentLetter(string letter) => currentLetter = letter;

    // ─────────────────────────────── Cards UI ───────────────────────────────
    public void RefreshCards()
    {
        if (cardButtons == null || cardButtons.Count < 4 ||
            correctCards == null || correctCards.Count == 0 ||
            wrongCards == null || wrongCards.Count < 3)
        {
            Debug.LogWarning("Make sure you have 4 cards, 1 correct and 3 wrong!!");
            return;
        }

        // Pick 1 correct card
        W4CardData chosenCorrect = correctCards[Random.Range(0, correctCards.Count)];

        // Pick 3 wrong cards
        var chosenWrongs = new List<W4CardData>();
        var pool = new List<W4CardData>(wrongCards);
        for (int i = 0; i < 3; i++)
        {
            int idx = Random.Range(0, pool.Count);
            chosenWrongs.Add(pool[idx]);
            pool.RemoveAt(idx);
        }

        // Build round: 1 correct + 3 wrong
        var round = new List<W4CardData>();
        round.Add(chosenCorrect);
        round.AddRange(chosenWrongs);

        // Shuffle
        for (int i = 0; i < round.Count; i++)
        {
            var temp = round[i];
            int rand = Random.Range(i, round.Count);
            round[i] = round[rand];
            round[rand] = temp;
        }

        for (int i = 0; i < 4; i++)
        {
            var data = round[i];
            var cb = cardButtons[i];
            if (cb == null) continue;

            // Try to grab Icon child if cardImage not assigned
            if (cb.cardImage == null)
            {
                var icon = cb.transform.Find("Icon");
                if (icon) cb.cardImage = icon.GetComponent<Image>();
            }

            if (cb.cardImage != null && data.mainSprite != null)
                cb.cardImage.sprite = data.mainSprite;

            cb.isCorrect = (data == chosenCorrect);
            cb.wrongSprite = data.wrongSprite != null ? data.wrongSprite : data.mainSprite;
            cb.wrongLetterClip = data.voiceClip;
            cb.sfxSource = sfxSource;

            if (cb.isCorrect)
            {
                cb.winPopup = data.winPopup;
                cb.applauseClip = data.applauseClip;
                cb.winClip = data.winClip;
            }
            else
            {
                cb.winPopup = null;
                cb.applauseClip = null;
                cb.winClip = null;
            }

            var btn = cb.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(cb.OnCardSelected);
                btn.interactable = true;
            }
        }
    }

    // ─────────────── Child + Quiz_attempt init ─────────────
    private IEnumerator InitChildThenStartQuizAttempt()
    {
        attemptInitialized = false;
        attemptErrorCount = 0;
        rewardedThisRound = false;

        // 1) Try from CoinsManager
        var cm = FindObjectOfType<CoinsManager>();
        if (cm != null && !string.IsNullOrEmpty(CoinsManager.instance.SelectedChildKey))
        {
            selectedChildKey = CoinsManager.instance.SelectedChildKey;
        }
        else
        {
            // 2) Fallback: selected/displayed from DB
            yield return StartCoroutine(FindSelectedOrDisplayedChildKey());
        }

        // 3) Start / reset Quiz_attempt for this letter
        yield return StartCoroutine(StartNewQuizAttempt());

        attemptInitialized = true;
    }

    private IEnumerator FindSelectedOrDisplayedChildKey()
    {
        if (string.IsNullOrEmpty(parentId)) yield break;

        var db = FirebaseDatabase.DefaultInstance;
        var childrenRef = db.RootReference.Child("parents").Child(parentId).Child("children");
        var getChildren = childrenRef.GetValueAsync();
        yield return new WaitUntil(() => getChildren.IsCompleted);

        if (getChildren.Exception != null || getChildren.Result == null || !getChildren.Result.Exists)
            yield break;

        // Prefer selected == true
        foreach (var ch in getChildren.Result.Children)
        {
            bool isSel = false;
            bool.TryParse(ch.Child("selected")?.Value?.ToString(), out isSel);
            if (isSel) { selectedChildKey = ch.Key; yield break; }
        }

        // Fallback displayed == true
        foreach (var ch in getChildren.Result.Children)
        {
            bool disp = false;
            bool.TryParse(ch.Child("displayed")?.Value?.ToString(), out disp);
            if (disp) { selectedChildKey = ch.Key; yield break; }
        }
    }

    // === QUIZ_ATTEMPT SETUP ================
    private IEnumerator StartNewQuizAttempt()
    {
        attemptInitialized = false;

        if (string.IsNullOrEmpty(currentLetter))
            currentLetter = "?";

        if (string.IsNullOrEmpty(selectedChildKey))
            yield return StartCoroutine(FindSelectedOrDisplayedChildKey());

        if (string.IsNullOrEmpty(selectedChildKey))
        {
            Debug.LogWarning("[W4CardRoundManager] No child key; Quiz_attempt will not be saved.");
            yield break;
        }

        // 1) Delete ALL AR + CARDS Quiz_attempts for ALL letters
        yield return StartCoroutine(DeleteAllArAndCardsQuizAttempts());

        // 2) Initialize/overwrite Quiz_attempt/1 for CURRENT letter
        attemptErrorCount = 0;
        rewardedThisRound = false;

        var root = FirebaseDatabase.DefaultInstance.RootReference;
        var initUpdates = new Dictionary<string, object>
        {
            [$"{GetQuizAttemptNodePath()}"] = null,       // wipe / ensure clean node
            [$"{GetQuizAttemptNodePath()}/errors"] = 0,
            [$"{GetQuizAttemptNodePath()}/finished"] = false
        };
        root.UpdateChildrenAsync(initUpdates);  // fire-and-forget

        attemptInitialized = true;
        yield break;
    }

    // Delete ALL AR + CARDS Quiz_attempts for ALL letters for this child
    private IEnumerator DeleteAllArAndCardsQuizAttempts()
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
            updates[$"{basePath}/ar/Quiz_attempt"] = null;

            // Delete CARDS quiz attempt
            updates[$"{basePath}/cards/Quiz_attempt"] = null;
        }

        if (updates.Count > 0)
            db.RootReference.UpdateChildrenAsync(updates);
    }

    private string GetQuizAttemptBasePath()
    {
        return $"parents/{parentId}/children/{selectedChildKey}/letters/{currentLetter}/activities/cards/Quiz_attempt";
    }

    private string GetQuizAttemptNodePath()
    {
        // Always single attempt index "1"
        return $"{GetQuizAttemptBasePath()}/{currentAttempt}";
    }

    // === Save Quiz_attempt ==============================
    private void SaveAttemptError_QuizAttempt()
    {
        if (!attemptInitialized) return;
        if (string.IsNullOrEmpty(parentId) || string.IsNullOrEmpty(selectedChildKey)) return;

        var root = FirebaseDatabase.DefaultInstance.RootReference;
        var updates = new Dictionary<string, object>
        {
            [$"{GetQuizAttemptNodePath()}/errors"] = attemptErrorCount,
            [$"{GetQuizAttemptNodePath()}/finished"] = false
        };
        root.UpdateChildrenAsync(updates);
    }

    private void SaveCardsSuccess_QuizAttempt()
    {
        if (!attemptInitialized) return;
        if (string.IsNullOrEmpty(parentId) || string.IsNullOrEmpty(selectedChildKey)) return;

        var root = FirebaseDatabase.DefaultInstance.RootReference;
        var updates = new Dictionary<string, object>
        {
            [$"{GetQuizAttemptNodePath()}/errors"] = attemptErrorCount,
            [$"{GetQuizAttemptNodePath()}/finished"] = true
        };
        root.UpdateChildrenAsync(updates);
    }

    // ───────────────────── Public API used by W4CardButton ──────────────────
    public void RegisterWrong()
    {
        if (!attemptInitialized) return;

        attemptErrorCount++;
        SaveAttemptError_QuizAttempt();
        Debug.Log($"[W4CardRoundManager] RegisterWrong -> errors={attemptErrorCount}");
    }

    public void RegisterSuccess()
    {
        if (!attemptInitialized) return;

        SaveCardsSuccess_QuizAttempt();
        AwardCoinsOnce(coinsOnSuccess);
        Debug.Log($"[W4CardRoundManager] RegisterSuccess -> errors={attemptErrorCount}, +{coinsOnSuccess} coins");

        // Re-enable character movement after a successful cards round
        var pop = FindObjectOfType<PopUpTrigger>();
        if (pop != null)
        {
            pop.SetMovement(true);
        }
    }

    public void AddCoins(int amount)
    {
        AwardCoinsOnce(amount);
    }

    // ─────────────────────────── Coins handling ─────────────────────────────
    private void AwardCoinsOnce(int amount)
    {
        if (rewardedThisRound) return;
        rewardedThisRound = true;

        // Prefer CoinsManager if present
        var cm = FindObjectOfType<CoinsManager>();
        if (cm != null)
        {
            cm.AddCoinsToSelectedChild(amount);
            return;
        }

        // Fallback: write directly to Firebase
        StartCoroutine(AddCoinsDirectToFirebase(amount));
    }

    private IEnumerator AddCoinsDirectToFirebase(int amount)
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
}
