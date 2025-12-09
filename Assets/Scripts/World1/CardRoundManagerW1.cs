using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
// Firebase
using Firebase.Auth;
using Firebase.Database;

[System.Serializable]
public class CardDataW1
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

public class CardRoundManagerW1 : MonoBehaviour
{
    [Header("Card Setup")]
    public List<CardButtonW1> cardButtons;    // 4 أزرار
    public List<CardDataW1> correctCards;     // الصحيح
    public List<CardDataW1> wrongCards;       // الخاطئ

    [Header("Audio Source")]
    public AudioSource sfxSource;

    [Header("Letter (per canvas)")]
    [SerializeField] public string currentLetter = "";

    // Attempts / Firebase
    private int currentAttempt = 0;
    private int attemptErrorCount = 0;
    private bool attemptInitialized = false;

    private string parentId;
    private string selectedChildKey;

    private void OnEnable()
    {
        if (string.IsNullOrEmpty(currentLetter) && !string.IsNullOrEmpty(GameSession.CurrentLetter))
            currentLetter = GameSession.CurrentLetter;

        StartCoroutine(StartNewAttempt());
        RefreshCards();
    }

    public void SetCurrentLetter(string letter) => currentLetter = letter;

    public void RefreshCards()
    {
        if (cardButtons == null || cardButtons.Count < 4 ||
            correctCards == null || correctCards.Count == 0 ||
            wrongCards == null || wrongCards.Count < 3)
        {
            Debug.LogWarning("⚠️ يلزم 4 بطاقات + (1 صحيح) + (3 خطأ) على الأقل.");
            return;
        }

        var chosenCorrect = correctCards[Random.Range(0, correctCards.Count)];

        var chosenWrongs = new List<CardDataW1>();
        var pool = new List<CardDataW1>(wrongCards);
        for (int i = 0; i < 3; i++)
        {
            int idx = Random.Range(0, pool.Count);
            chosenWrongs.Add(pool[idx]);
            pool.RemoveAt(idx);
        }

        var round = new List<CardDataW1> { chosenCorrect };
        round.AddRange(chosenWrongs);
        for (int i = 0; i < round.Count; i++)
        {
            var tmp = round[i];
            int r = Random.Range(i, round.Count);
            round[i] = round[r];
            round[r] = tmp;
        }

        for (int i = 0; i < 4; i++)
        {
            var data = round[i];
            var cb = cardButtons[i];

            if (cb.cardImage == null)
            {
                var icon = cb.transform.Find("Icon");
                if (icon) cb.cardImage = icon.GetComponent<Image>();
            }

            if (cb.cardImage && data.mainSprite)
            {
                cb.cardImage.sprite = data.mainSprite;
                cb.originalSprite = data.mainSprite;
            }

            cb.isCorrect = (data == chosenCorrect);
            cb.wrongSprite = data.wrongSprite ? data.wrongSprite : data.mainSprite;
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
            if (btn)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(cb.OnCardSelected);
                btn.interactable = true;
            }
        }
    }

    // ===================== Attempts / Firebase =====================
    private IEnumerator StartNewAttempt()
    {
        attemptInitialized = false;
        attemptErrorCount = 0;

        parentId = FirebaseAuth.DefaultInstance.CurrentUser != null
            ? FirebaseAuth.DefaultInstance.CurrentUser.UserId
            : "debug_parent";

        selectedChildKey = (CoinsManager.instance != null) ? CoinsManager.instance.SelectedChildKey : null;
        if (string.IsNullOrEmpty(selectedChildKey))
            yield return StartCoroutine(FindSelectedOrDisplayedChildKey());

        if (string.IsNullOrEmpty(currentLetter)) currentLetter = "?";
        if (string.IsNullOrEmpty(selectedChildKey)) yield break;

        string attemptsPath = GetAttemptsPath();
        var getTask = FirebaseDatabase.DefaultInstance.RootReference.Child(attemptsPath).GetValueAsync();
        yield return new WaitUntil(() => getTask.IsCompleted);

        int maxAttempt = 0;
        if (getTask.Exception == null && getTask.Result != null && getTask.Result.Exists)
            foreach (var att in getTask.Result.Children)
                if (int.TryParse(att.Key, out int n) && n > maxAttempt) maxAttempt = n;

        currentAttempt = maxAttempt + 1;
        attemptInitialized = true;

        var init = new Dictionary<string, object>
        {
            [$"{GetAttemptPath()}/errors"] = 0,
            [$"{GetAttemptPath()}/successes"] = 0,
            [$"{GetAttemptPath()}/finished"] = false,
            [$"{GetAttemptPath()}/ts"] = ServerValue.Timestamp
        };
        FirebaseDatabase.DefaultInstance.RootReference.UpdateChildrenAsync(init);
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

    public void RegisterWrong()
    {
        if (!attemptInitialized || string.IsNullOrEmpty(selectedChildKey)) return;

        attemptErrorCount++;
        var upd = new Dictionary<string, object>
        {
            [$"{GetAttemptPath()}/errors"] = attemptErrorCount,
            [$"{GetAttemptPath()}/finished"] = false,
            [$"{GetAttemptPath()}/ts"] = ServerValue.Timestamp
        };
        FirebaseDatabase.DefaultInstance.RootReference.UpdateChildrenAsync(upd);

        // ✅ بعد تسجيل الخطأ، نفحص استحقاق البادج
        StartCoroutine(CheckBadgeEligibilityAndSet());
    }

    public void RegisterSuccess()
    {
        if (!attemptInitialized || string.IsNullOrEmpty(selectedChildKey)) return;

        var upd = new Dictionary<string, object>
        {
            [$"{GetAttemptPath()}/errors"] = attemptErrorCount,
            [$"{GetAttemptPath()}/successes"] = 1,
            [$"{GetAttemptPath()}/finished"] = true,
            [$"{GetAttemptPath()}/ts"] = ServerValue.Timestamp
        };
        FirebaseDatabase.DefaultInstance.RootReference.UpdateChildrenAsync(upd);

        // ✅ بعد النجاح، نفحص استحقاق البادج
        StartCoroutine(CheckBadgeEligibilityAndSet());
    }

    public void AddCoins(int amount)
    {
        if (CoinsManager.instance != null)
            CoinsManager.instance.AddCoinsToSelectedChild(amount);
        else
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

    private string GetAttemptsPath()
        => $"parents/{parentId}/children/{selectedChildKey}/letters/{currentLetter}/activities/cards/attempts";

    private string GetAttemptPath() => $"{GetAttemptsPath()}/{currentAttempt}";


    // ===================== Badge Logic (نفس المنطق السابق) =====================
    private IEnumerator CheckBadgeEligibilityAndSet()
    {
        // تأكيد وجود parentId
        if (string.IsNullOrEmpty(parentId))
        {
            var auth = FirebaseAuth.DefaultInstance;
            if (auth.CurrentUser == null) yield break;
            parentId = auth.CurrentUser.UserId;
        }

        // تأكيد وجود childKey
        if (string.IsNullOrEmpty(selectedChildKey))
            yield return StartCoroutine(FindSelectedOrDisplayedChildKey());

        if (string.IsNullOrEmpty(parentId) || string.IsNullOrEmpty(selectedChildKey) || string.IsNullOrEmpty(currentLetter))
            yield break;

        var db = FirebaseDatabase.DefaultInstance;

        string letterPath = $"parents/{parentId}/children/{selectedChildKey}/letters/{currentLetter}";

        // 1) أولاً: تأكد أن البادج غير موجود مسبقًا
        var badgeTask = db.RootReference.Child(letterPath).Child("badge").GetValueAsync();
        yield return new WaitUntil(() => badgeTask.IsCompleted);
        if (badgeTask.Exception == null && badgeTask.Result != null && badgeTask.Result.Exists)
        {
            bool already = false;
            bool.TryParse(badgeTask.Result.Value?.ToString(), out already);
            if (already) yield break; // البادج موجود، لا داعي لإعادة الحساب
        }

        // 2) قراءة محاولات التتبع ومحاولات SR
        string tracingAttemptsPath = $"{letterPath}/activities/tracing/attempts";
        string srAttemptsPath = $"{letterPath}/activities/SR/attempts";

        var tracingTask = db.RootReference.Child(tracingAttemptsPath).GetValueAsync();
        var srTask = db.RootReference.Child(srAttemptsPath).GetValueAsync();

        // انتظر المهمتين معًا
        while (!tracingTask.IsCompleted || !srTask.IsCompleted)
            yield return null;

        bool tracingFinished = AnyAttemptFinished(tracingTask);
        bool srFinished = AnyAttemptFinished(srTask);

        // 3) شرط منح البادج: أن يكون tracing + SR منتهيين بنجاح مرة واحدة على الأقل
        if (tracingFinished && srFinished)
        {
            var updates = new Dictionary<string, object>
            {
                [$"{letterPath}/badge"] = true
            };
            db.RootReference.UpdateChildrenAsync(updates);
        }
    }

    private bool AnyAttemptFinished(System.Threading.Tasks.Task<Firebase.Database.DataSnapshot> t)
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
            // تجاهل الأخطاء، نرجع false
        }
        return false;
    }
}
