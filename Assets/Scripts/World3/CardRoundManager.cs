using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


using Firebase.Auth; // Firebase Authentication access
using Firebase.Database; // Firebase Realtime Database access
using System.Collections; // for IEnumerator and Coroutines

[System.Serializable]  // allows this class to appear in Inspector
public class CardData
{
    public string name;             // card name shown in Inspector
    public Sprite mainSprite;        // main image displayed on the card
    public Sprite wrongSprite;       // image to show when chosen wrong
    public AudioClip voiceClip;     // audio clip related to the card

    [Header("Special for correct cards")] // grouping correct-card-only fields
    public GameObject winPopup;      // popup shown when selecting correct card
    public AudioClip applauseClip;    // applause sound for success
    public AudioClip winClip;        // voice for correct answer
}

public class CardRoundManager : MonoBehaviour  // main manager for the Cards game round
{
    [Header("Card Setup")]
    public List<CardButton> cardButtons;   // the 4 fixed card buttons in the scene
    public List<CardData> correctCards;    // list of all correct cards for the letter
    public List<CardData> wrongCards;      // list of all wrong cards 

    [Header("Audio Source")]
    public AudioSource sfxSource;         // audio source 

    [Header("Letter (per canvas)")]
    [Tooltip("GameSession.CurrentLetter.")]
    [SerializeField] public string currentLetter = ""; // letter connected to this scene

    // ===== Attempts (Firebase) =====
    private int currentAttempt = 0;  // current attempt index for Firebase
    private int attemptErrorCount = 0;  // number of wrong answers in this attempt
    private bool attemptInitialized = false; // ensures attempt data is loaded before saving


    private string parentId; // logged parent UID for Firebase paths
    private string selectedChildKey;  // current selected or displayed child key

    private void OnEnable() // runs whenever this canvas becomes active
    {
        
        if (string.IsNullOrEmpty(currentLetter) && !string.IsNullOrEmpty(GameSession.CurrentLetter))
            currentLetter = GameSession.CurrentLetter; // load current letter from session if empty


        
        StartCoroutine(StartNewAttempt()); // begin tracking a new attempt in Firebase
       
        RefreshCards(); // generate random round of correct + wrong cards
    }

    
    public void SetCurrentLetter(string letter) => currentLetter = letter; // sets current letter manually

    public void RefreshCards()
    {
        // checks card lists and required counts
        if (cardButtons == null || cardButtons.Count < 4 ||
            correctCards == null || correctCards.Count == 0 ||
            wrongCards == null || wrongCards.Count < 3)
        {
            Debug.LogWarning("checks card"); // warning for missing setup
            return; // stop if setup is invalid
        }

        
        var chosenCorrect = correctCards[Random.Range(0, correctCards.Count)]; // pick random correct card


       
        var chosenWrongs = new List<CardData>(); // container for wrong cards
        var pool = new List<CardData>(wrongCards); // copy of wrong cards to remove from
        for (int i = 0; i < 3; i++)
        {
            int idx = Random.Range(0, pool.Count); // random index from pool
            chosenWrongs.Add(pool[idx]); // add this wrong card
            pool.RemoveAt(idx); // remove so it won't repeat
        }

       
        var round = new List<CardData>(); // temporary list to hold 4 chosen cards
        round.Add(chosenCorrect);  // add the one correct card
        round.AddRange(chosenWrongs);  // add the 3 wrong cards

       
        for (int i = 0; i < round.Count; i++)
        {
            var temp = round[i];  // store current card
            int rand = Random.Range(i, round.Count); // random index to swap with
            round[i] = round[rand]; // swap
            round[rand] = temp;  // finish swap
        }

       
        for (int i = 0; i < 4; i++)
        {
            var data = round[i]; // card data for this UI button
            var cb = cardButtons[i];  // reference to card button script

            if (cb.cardImage == null)
            {
                var icon = cb.transform.Find("Icon"); // find icon child if image missing
                if (icon) cb.cardImage = icon.GetComponent<Image>();  // assign image
            }

            // set card sprite if valid
            if (cb.cardImage != null && data.mainSprite != null)
                cb.cardImage.sprite = data.mainSprite; // display main sprite

            cb.isCorrect       = (data == chosenCorrect); // mark this card if it's the correct one
            cb.wrongSprite     = data.wrongSprite != null ? data.wrongSprite : data.mainSprite; // fallback wrong sprite
            cb.wrongLetterClip = data.voiceClip;  // wrong-choice audio clip
            cb.sfxSource       = sfxSource;  // shared audio source

            if (cb.isCorrect)
            {
                cb.winPopup     = data.winPopup; // assign win popup
                cb.applauseClip = data.applauseClip; // applause sound
                cb.winClip      = data.winClip;  // "well done" sound
            }
            else
            {
                cb.winPopup     = null; // no win popup for wrong card
                cb.applauseClip = null; // no applause
                cb.winClip      = null; // no win clip
            }

            var btn = cb.GetComponent<Button>(); // get UI button component
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners(); // remove old listeners
                btn.onClick.AddListener(cb.OnCardSelected); // add new click handler
                btn.interactable = true;// enable card click
            }
        }
    }

    // ======== Firebase Attempts =========
    private IEnumerator StartNewAttempt()
    {
        attemptInitialized = false; // mark attempt as not ready yet
        attemptErrorCount = 0;  // reset error counter

        parentId = FirebaseAuth.DefaultInstance.CurrentUser != null 
            ? FirebaseAuth.DefaultInstance.CurrentUser.UserId  // get logged parent ID
            : "debug_parent"; // fallback for debug mode

        // CoinsManager
        selectedChildKey = (CoinsManager.instance != null) ? CoinsManager.instance.SelectedChildKey : null;  // get selected child key and fallback if manager missing

       
        if (string.IsNullOrEmpty(selectedChildKey)) 
            yield return StartCoroutine(FindSelectedOrDisplayedChildKey()); // find child key manually

        if (string.IsNullOrEmpty(currentLetter))
        {
            Debug.LogWarning("CardRoundManager: currentLetter is empty. Attempts will be saved under '?'"); // warn missing letter
            currentLetter = "?"; // fallback for safety
        }

        if (string.IsNullOrEmpty(selectedChildKey))
        {
            Debug.LogWarning("CardRoundManager: SelectedChildKey is empty. Attempts won't be saved this session."); // warn no child
            yield break; // cannot save attempts
        }

        
        string attemptsPath = GetAttemptsPath(); // get base attempts path
        var getTask = FirebaseDatabase.DefaultInstance.RootReference.Child(attemptsPath).GetValueAsync(); // get attempts list
        yield return new WaitUntil(() => getTask.IsCompleted); // wait for result


        int maxAttempt = 0; // will detect highest attempt number
        if (getTask.Exception == null && getTask.Result != null && getTask.Result.Exists)
        {
            foreach (var att in getTask.Result.Children) // loop through attempts
            {
                if (int.TryParse(att.Key, out int n) && n > maxAttempt)
                    maxAttempt = n; // track highest attempt index
            }
        }

        currentAttempt = maxAttempt + 1; // next attempt number
        attemptInitialized = true; // attempt is ready to record

        var init = new Dictionary<string, object> // setup initial attempt data
        {
            [$"{GetAttemptPath()}/errors"]    = 0, // start with 0 errors
            [$"{GetAttemptPath()}/successes"] = 0, // start with no successes
            [$"{GetAttemptPath()}/finished"]  = false, // attempt not finished
            [$"{GetAttemptPath()}/ts"]        = ServerValue.Timestamp // store timestamp
        };
        FirebaseDatabase.DefaultInstance.RootReference.UpdateChildrenAsync(init); // write to Firebase
    }

    private IEnumerator FindSelectedOrDisplayedChildKey()
    {
        if (string.IsNullOrEmpty(parentId)) yield break; // cannot proceed if parent missing

        var db = FirebaseDatabase.DefaultInstance; // database reference
        var childrenRef = db.RootReference.Child("parents").Child(parentId).Child("children"); // parent children path
        var getChildren = childrenRef.GetValueAsync(); // fetch children list
        yield return new WaitUntil(() => getChildren.IsCompleted);  // wait for result

        if (getChildren.Exception != null || getChildren.Result == null || !getChildren.Result.Exists)
            yield break; // no children found

        // selected == true
        foreach (var ch in getChildren.Result.Children) // loop through children
        {
            bool isSel = false; // local flag
            bool.TryParse(ch.Child("selected")?.Value?.ToString(), out isSel); // read "selected"
            if (isSel) { selectedChildKey = ch.Key; yield break; } // assign if found
        }

        // displayed == true
        foreach (var ch in getChildren.Result.Children) // fallback loop
        {
            bool disp = false; // local flag
            bool.TryParse(ch.Child("displayed")?.Value?.ToString(), out disp); // read "displayed"
            if (disp) { selectedChildKey = ch.Key; yield break; } // assign if found
        }
    }

    
    public void RegisterWrong()
    {
        if (!attemptInitialized || string.IsNullOrEmpty(selectedChildKey)) return; // ensure attempt + child valid

        attemptErrorCount++; // increase error counter

        var upd = new Dictionary<string, object> // updated attempt info
        {
            [$"{GetAttemptPath()}/errors"]   = attemptErrorCount, // update errors count
            [$"{GetAttemptPath()}/finished"] = false, // mark attempt as ongoing
            [$"{GetAttemptPath()}/ts"]       = ServerValue.Timestamp // update timestamp
        };
        FirebaseDatabase.DefaultInstance.RootReference.UpdateChildrenAsync(upd); // write to Firebase



       
StartCoroutine(CheckBadgeEligibilityAndSet()); // check badge eligibility after wrong answer

    }

    
    public void RegisterSuccess()
    {
        if (!attemptInitialized || string.IsNullOrEmpty(selectedChildKey)) return; // ensure valid attempt

        var upd = new Dictionary<string, object> // updated attempt success info
        {
            [$"{GetAttemptPath()}/errors"]    = attemptErrorCount, // save existing errors
            [$"{GetAttemptPath()}/successes"] = 1, // mark success = 1
            [$"{GetAttemptPath()}/finished"]  = true, // attempt finished successfully
            [$"{GetAttemptPath()}/ts"]        = ServerValue.Timestamp // update timestamp
        };
        FirebaseDatabase.DefaultInstance.RootReference.UpdateChildrenAsync(upd); // save to Firebase


StartCoroutine(CheckBadgeEligibilityAndSet()); // evaluate badge completion



    }

   
    public void AddCoins(int amount)
    {
        if (CoinsManager.instance != null)
        {
            CoinsManager.instance.AddCoinsToSelectedChild(amount); // add coins through manager
        }
        else
        {
            StartCoroutine(AddCoinsDirectToFirebase(amount)); // fallback: write directly to database
        }
    }

    private IEnumerator AddCoinsDirectToFirebase(int amount) 
    {
        var auth = FirebaseAuth.DefaultInstance; // firebase auth reference
        if (auth.CurrentUser == null) yield break; // cannot add coins if not logged in

        string pid = auth.CurrentUser.UserId; // get parent ID
        var db = FirebaseDatabase.DefaultInstance; // database reference

        string childKey = selectedChildKey; // local child key
        if (string.IsNullOrEmpty(childKey))
        {
            yield return StartCoroutine(FindSelectedOrDisplayedChildKey()); // attempt to retrieve child key
            childKey = selectedChildKey; // assign updated key
            if (string.IsNullOrEmpty(childKey)) yield break; // stop if still missing
        }

        string coinsPath = $"parents/{pid}/children/{childKey}/coins"; // path to coins node
        var coinsRef = db.RootReference.Child(coinsPath); // coins database reference

        var tx = coinsRef.RunTransaction(mutable => // begin transaction to update coins safely

        {
            long current = 0;  // current coin value
            if (mutable.Value != null) // read existing coins
            {
                if (mutable.Value is long l) current = l;  // cast from long
                else if (mutable.Value is double d) current = (long)d; // convert double to long
                else long.TryParse(mutable.Value.ToString(), out current); // fallback parse
            }
            mutable.Value = current + amount; // increase coins
            return TransactionResult.Success(mutable); // complete transaction
        });

        yield return new WaitUntil(() => tx.IsCompleted); // wait for transaction to finish
    }

    private string GetAttemptsPath()
    {
        return $"parents/{parentId}/children/{selectedChildKey}/letters/{currentLetter}/activities/cards/attempts"; // full path to attempts
    }

    private string GetAttemptPath()
    {
        return $"{GetAttemptsPath()}/{currentAttempt}"; // path to the specific current attempt
    }




// ==== Badge Logic ===
IEnumerator CheckBadgeEligibilityAndSet()
{
   
    if (string.IsNullOrEmpty(parentId))
    {
        var auth = FirebaseAuth.DefaultInstance; // get auth reference
        if (auth.CurrentUser == null) yield break; // stop if no parent logged in
        parentId = auth.CurrentUser.UserId; // assign parent ID
    }

    if (string.IsNullOrEmpty(selectedChildKey))
        yield return StartCoroutine(FindSelectedOrDisplayedChildKey());  // ensure child key is available

    if (string.IsNullOrEmpty(parentId) || string.IsNullOrEmpty(selectedChildKey) || string.IsNullOrEmpty(currentLetter))
        yield break; // cannot continue badge logic without essential data

    var db = FirebaseDatabase.DefaultInstance; // database reference

    
    string letterPath = $"parents/{parentId}/children/{selectedChildKey}/letters/{currentLetter}"; // base path for this letter
    var badgeTask = db.RootReference.Child(letterPath).Child("badge").GetValueAsync(); // fetch badge field
    yield return new WaitUntil(() => badgeTask.IsCompleted); // wait for result
    if (badgeTask.Exception == null && badgeTask.Result != null && badgeTask.Result.Exists)
    {
        bool already = false; // flag to detect badge state
        bool.TryParse(badgeTask.Result.Value?.ToString(), out already); // parse badge value
        if (already) yield break; // badge already earned
    }

   
    string tracingAttemptsPath = $"{letterPath}/activities/tracing/attempts"; // path to tracing attempts
    string srAttemptsPath      = $"{letterPath}/activities/SR/attempts"; // path to SR attempts

    var tracingTask = db.RootReference.Child(tracingAttemptsPath).GetValueAsync(); // fetch tracing attempts
    var srTask      = db.RootReference.Child(srAttemptsPath).GetValueAsync(); // fetch SR attempts

    // انتظر المهمتين معًا
    while (!tracingTask.IsCompleted || !srTask.IsCompleted) yield return null; // wait for both attempts to load

    bool tracingFinished = AnyAttemptFinished(tracingTask); // check tracing completion
    bool srFinished      = AnyAttemptFinished(srTask); // check SR completion

    if (tracingFinished && srFinished)
    {
        var updates = new Dictionary<string, object>
        {
            [$"{letterPath}/badge"] = true  // set badge to true
        };
        db.RootReference.UpdateChildrenAsync(updates); // update badge in Firebase
    }
}

private bool AnyAttemptFinished(System.Threading.Tasks.Task<Firebase.Database.DataSnapshot> t)
{
    try
    {
        if (t.Exception != null || t.Result == null || !t.Result.Exists) return false; // stop if no data or error
        foreach (var att in t.Result.Children) // loop through all attempts
        {
            var finishedNode = att.Child("finished"); // get "finished" field
            if (finishedNode != null && finishedNode.Exists)
            {
                if (bool.TryParse(finishedNode.Value?.ToString(), out bool fin) && fin)
                    return true; // true if attempt is marked finished
            }
            
            var succNode = att.Child("successes"); // get "successes" field
            if (succNode != null && succNode.Exists)
            {
                if (int.TryParse(succNode.Value?.ToString(), out int succ) && succ > 0)
                    return true; // success achieved → attempt finished
            }
        }
    }
    catch { } // ignored — returns false below
    return false; // no finished attempts found
}


} //End class
