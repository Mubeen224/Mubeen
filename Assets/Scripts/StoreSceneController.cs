using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Firebase.Auth;
using Firebase.Database;
using TMPro;
using UISwitcher; // for UISwitcher.UISwitcher
using UnityEngine;
using UnityEngine.UI;

public class StoreSceneController : MonoBehaviour
{
    [Header("Preview Area (Left)")]
    [SerializeField] private Image previewImage;

    [Header("Preview Buttons (Archer, Knight, Rogue, Wizard)")]
    [SerializeField] private Button archerPreviewBtn;
    [SerializeField] private Button knightPreviewBtn;
    [SerializeField] private Button roguePreviewBtn;
    [SerializeField] private Button wizardPreviewBtn;

    [Header("Character Sprites")]
    [SerializeField] private Sprite archerSprite;
    [SerializeField] private Sprite knightSprite;
    [SerializeField] private Sprite rogueSprite;
    [SerializeField] private Sprite wizardSprite;

    [Header("Character AudioSources (one per character)")]
    [SerializeField] private AudioSource archerAudio;
    [SerializeField] private AudioSource knightAudio;
    [SerializeField] private AudioSource rogueAudio;
    [SerializeField] private AudioSource wizardAudio;

    [Header("Selection Toggle (single toggle for displayed character)")]
    [SerializeField] private UISwitcher.UISwitcher selectToggle;

    [Header("Purchase UI")]
    [SerializeField] private Button purchaseButton;                 // Visible only if displayed character is NOT purchased
    [SerializeField] private GameObject purchaseConfirmPopup;       // "Buy character" alert
    [SerializeField] private Button purchaseYesButton;
    [SerializeField] private Button purchaseNoButton;
    [SerializeField] private GameObject purchaseSuccessPopup;       // Shows for 5 seconds
    [SerializeField] private GameObject notEnoughMoneyPopup;        // "Not enough money" alert
    [SerializeField] private Button notEnoughCloseButton;

    [Header("Success Popup Content")]
    [SerializeField] private Image purchaseSuccessCharacterImage;   // shows the purchased character sprite

    [Header("Popup Sounds (one per popup)")]
    [SerializeField] private AudioSource confirmPopupAudio;         // plays when purchaseConfirmPopup opens
    [SerializeField] private AudioSource successPopupAudio;         // plays when purchaseSuccessPopup opens
    [SerializeField] private AudioSource notEnoughPopupAudio;       // plays when notEnoughMoneyPopup opens

    [Header("Selected Highlights (white circles behind icons)")]
    [SerializeField] private Image archerSelectedBG;
    [SerializeField] private Image knightSelectedBG;
    [SerializeField] private Image rogueSelectedBG;
    [SerializeField] private Image wizardSelectedBG;

    [Header("Price Badges (show only if NOT purchased)")]
    [SerializeField] private GameObject archerPriceGO;
    [SerializeField] private GameObject knightPriceGO;
    [SerializeField] private GameObject roguePriceGO;
    [SerializeField] private GameObject wizardPriceGO;

    [Header("Coins UI")]
    [SerializeField] private TMP_Text coinsText;                    // displays child's coins

    [Header("Optional UI")]
    [SerializeField] private TMP_Text statusText;

    // -------- Firebase --------
    private FirebaseAuth auth;
    private FirebaseDatabase db;
    private string parentId;
    private string selectedChildIndex;
    private DatabaseReference childRef;

    // -------- Model --------
    [Serializable]
    public class Character
    {
        public string name;
        public float price;
        public bool purchased;
        public bool selected;
        public bool displayed;

        public Character() { }
        public Character(string name, float price, bool purchased, bool selected, bool displayed)
        {
            this.name = name;
            this.price = price;
            this.purchased = purchased;
            this.selected = selected;
            this.displayed = displayed;
        }
    }

    private readonly string[] CharacterOrder = { "Archer", "Knight", "Rogue", "Wizard" };
    private List<Character> characters = new List<Character>();
    private Dictionary<string, Sprite> spriteByName;

    private int coins = 0;   // child's current coins (from DB)
    private bool suppressToggleEvent = false; // prevent feedback loop when we update toggle programmatically

    private void Awake()
    {
        // Map sprites by character name
        spriteByName = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase)
        {
            { "Archer", archerSprite },
            { "Knight", knightSprite },
            { "Rogue",  rogueSprite  },
            { "Wizard", wizardSprite }
        };

        // Ensure character audio sources don't auto-play
        if (archerAudio) { archerAudio.playOnAwake = false; archerAudio.loop = false; }
        if (knightAudio) { knightAudio.playOnAwake = false; knightAudio.loop = false; }
        if (rogueAudio) { rogueAudio.playOnAwake = false; rogueAudio.loop = false; }
        if (wizardAudio) { wizardAudio.playOnAwake = false; wizardAudio.loop = false; }

        // Ensure popup audio sources don't auto-play
        if (confirmPopupAudio) { confirmPopupAudio.playOnAwake = false; confirmPopupAudio.loop = false; }
        if (successPopupAudio) { successPopupAudio.playOnAwake = false; successPopupAudio.loop = false; }
        if (notEnoughPopupAudio) { notEnoughPopupAudio.playOnAwake = false; notEnoughPopupAudio.loop = false; }

        // Hide selection highlights initially
        SetSelectionHighlights(false, false, false, false);

        // Hide popups initially
        if (purchaseConfirmPopup) purchaseConfirmPopup.SetActive(false);
        if (purchaseSuccessPopup) purchaseSuccessPopup.SetActive(false);
        if (notEnoughMoneyPopup) notEnoughMoneyPopup.SetActive(false);

        // Hide toggle until data loads
        if (selectToggle) selectToggle.gameObject.SetActive(false);
    }

    private void Start()
    {
        auth = FirebaseAuth.DefaultInstance;
        db = FirebaseDatabase.DefaultInstance;

        if (auth.CurrentUser == null)
        {
            SetStatus("لم يتم العثور على مستخدم مسجَّل الدخول.");
            return;
        }

        parentId = auth.CurrentUser.UserId;

        // Preview buttons (indices 0..3 for current UI)
        if (archerPreviewBtn) archerPreviewBtn.onClick.AddListener(() => OnPreviewPressed(0));
        if (knightPreviewBtn) knightPreviewBtn.onClick.AddListener(() => OnPreviewPressed(1));
        if (roguePreviewBtn) roguePreviewBtn.onClick.AddListener(() => OnPreviewPressed(2));
        if (wizardPreviewBtn) wizardPreviewBtn.onClick.AddListener(() => OnPreviewPressed(3));

        // Purchase flow
        if (purchaseButton) purchaseButton.onClick.AddListener(OnPurchasePressed);
        if (purchaseYesButton) purchaseYesButton.onClick.AddListener(OnPurchaseYes);

        // Confirm popup: Cancel -> stop its audio + hide
        if (purchaseNoButton) purchaseNoButton.onClick.AddListener(() =>
        {
            HidePopup(purchaseConfirmPopup, confirmPopupAudio);
        });

        // Not-enough-money popup: Close (X) -> stop its audio + hide
        if (notEnoughCloseButton) notEnoughCloseButton.onClick.AddListener(() =>
        {
            HidePopup(notEnoughMoneyPopup, notEnoughPopupAudio);
        });

        // Ensure popups start hidden
        if (purchaseConfirmPopup) purchaseConfirmPopup.SetActive(false);
        if (purchaseSuccessPopup) purchaseSuccessPopup.SetActive(false);
        if (notEnoughMoneyPopup) notEnoughMoneyPopup.SetActive(false);

        StartCoroutine(LoadSelectedChildAndCharacters());
    }

    // ------------------ Data Loading ------------------

    private IEnumerator LoadSelectedChildAndCharacters()
    {
        SetStatus("جارِ تحميل بيانات الطفل المختار...");

        var childrenRef = db.RootReference.Child("parents").Child(parentId).Child("children");
        var getChildrenTask = childrenRef.GetValueAsync();
        yield return new WaitUntil(() => getChildrenTask.IsCompleted);

        if (getChildrenTask.Exception != null)
        {
            Debug.LogError(getChildrenTask.Exception);
            SetStatus("حدث خطأ أثناء جلب بيانات الأطفال.");
            yield break;
        }

        var snap = getChildrenTask.Result;
        if (!snap.Exists)
        {
            SetStatus("لا توجد حسابات أطفال لهذا المستخدم.");
            yield break;
        }

        // Find selected child
        DataSnapshot selectedChildSnap = null;
        foreach (var child in snap.Children)
        {
            if (child.HasChild("selected") &&
                bool.TryParse(child.Child("selected").Value?.ToString() ?? "false", out bool isSel) && isSel)
            {
                selectedChildSnap = child;
                selectedChildIndex = child.Key;
                break;
            }
        }
        if (selectedChildSnap == null)
        {
            SetStatus("لم يتم اختيار طفل. الرجاء اختيار طفل من شاشة الأطفال.");
            yield break;
        }

        childRef = childrenRef.Child(selectedChildIndex);

        // Read coins
        if (selectedChildSnap.HasChild("coins") && selectedChildSnap.Child("coins").Value != null)
        {
            int.TryParse(selectedChildSnap.Child("coins").Value.ToString(), out coins);
        }
        else
        {
            coins = 0;
        }
        UpdateCoinsUI();

        // Read characters array (length-agnostic)
        if (selectedChildSnap.HasChild("characters"))
        {
            characters = ReadCharactersFromSnapshot(selectedChildSnap.Child("characters"));

            // Ensure exactly one displayed; if none, force index 0 if available
            int d = GetDisplayedIndex(characters);
            if (d == -1 && characters.Count > 0)
            {
                ForceSingleDisplayedLocal(0);
                yield return StartCoroutine(PushDisplayedFlagsToFirebase(0));
            }
        }
        else
        {
            // Fallback seed
            characters = new List<Character>
            {
                new Character("Archer", 0f,  true,  true,  true),
                new Character("Knight", 55f, false, false, false),
                new Character("Rogue",  55f, false, false, false),
                new Character("Wizard", 55f, false, false, false)
            };
            yield return StartCoroutine(PushDisplayedFlagsToFirebase(0));
        }

        // Apply UI for currently displayed
        int idxToShow = GetDisplayedIndex(characters);
        if (idxToShow < 0) idxToShow = 0;

        UpdatePreviewSprite(idxToShow);
        UpdatePurchaseButtonVisibility(idxToShow);
        UpdateSelectToggleUI(idxToShow);
        UpdateSelectionHighlights();
        UpdatePriceBadges(); // <— new

        SetStatus("");
    }

    private List<Character> ReadCharactersFromSnapshot(DataSnapshot charsSnap)
    {
        var list = new List<Character>();
        var temp = new SortedDictionary<int, DataSnapshot>();

        foreach (var c in charsSnap.Children)
            if (int.TryParse(c.Key, out int i)) temp[i] = c;

        if (temp.Count == 0)
        {
            foreach (var c in charsSnap.Children) list.Add(ParseCharacter(c));
        }
        else
        {
            foreach (var kv in temp) list.Add(ParseCharacter(kv.Value));
        }
        return list;
    }

    private Character ParseCharacter(DataSnapshot s)
    {
        string name = s.HasChild("name") ? s.Child("name").Value.ToString() : "Archer";
        float price = 0f; if (s.HasChild("price")) float.TryParse(s.Child("price").Value.ToString(), out price);
        bool purchased = ReadBool(s, "purchased");
        bool selected = ReadBool(s, "selected");
        bool displayed = ReadBool(s, "displayed");
        return new Character(name, price, purchased, selected, displayed);
    }

    private bool ReadBool(DataSnapshot s, string key)
    {
        if (!s.HasChild(key) || s.Child(key).Value == null) return false;
        bool b; bool.TryParse(s.Child(key).Value.ToString(), out b);
        return b;
    }

    // ------------------ UI Actions ------------------

    private void OnPreviewPressed(int idx)
    {
        if (characters == null || characters.Count == 0) return;
        if (idx < 0 || idx >= characters.Count) return;

        // Flip displayed locally
        ForceSingleDisplayedLocal(idx);

        // Update UI immediately
        UpdatePreviewSprite(idx);
        UpdatePurchaseButtonVisibility(idx);
        UpdateSelectToggleUI(idx);
        UpdateSelectionHighlights();
        UpdatePriceBadges(); // <— new (no harm to call on preview)

        // Preview SFX
        PlayCharacterAudio(idx);

        // Push displayed flags to DB
        StartCoroutine(PushDisplayedFlagsToFirebase(idx));
    }

    /// <summary>
    /// Hook this to the toggle's On Value Changed (Boolean).
    /// If the displayed character is purchased and not selected, this selects it.
    /// If it is already selected, does nothing (cannot unselect).
    /// </summary>
    public void OnSelectToggleChanged(bool isOn)
    {
        if (suppressToggleEvent) return;
        if (characters == null || characters.Count == 0) return;

        int idx = GetDisplayedIndex(characters);
        if (idx < 0 || idx >= characters.Count) return;

        // Toggle visible only when purchased, but guard anyway
        if (!characters[idx].purchased)
        {
            suppressToggleEvent = true;
            SetToggleVisual(false);
            suppressToggleEvent = false;
            return;
        }

        // Cannot unselect: if user tries to turn OFF while selected, snap back ON
        if (!isOn && characters[idx].selected)
        {
            suppressToggleEvent = true;
            SetToggleVisual(true);
            suppressToggleEvent = false;
            return;
        }

        // If already selected and user pressed ON again, nothing to do
        if (characters[idx].selected)
        {
            suppressToggleEvent = true;
            SetToggleVisual(true);
            suppressToggleEvent = false;
            return;
        }

        // Selecting a new character: set exactly one selected
        for (int i = 0; i < characters.Count; i++)
            characters[i].selected = (i == idx);

        UpdateSelectionHighlights();

        // Snap UI to ON
        suppressToggleEvent = true;
        SetToggleVisual(true);
        suppressToggleEvent = false;

        // Persist
        StartCoroutine(PushSelectedFlagsToFirebase(idx));
    }

    private void OnPurchasePressed()
    {
        int idx = GetDisplayedIndex(characters);
        if (idx < 0 || idx >= characters.Count) return;
        if (characters[idx].purchased) return;

        ShowPopup(purchaseConfirmPopup, confirmPopupAudio);
    }

    private void OnPurchaseYes()
    {
        HidePopup(purchaseConfirmPopup, confirmPopupAudio);
        StartCoroutine(PurchaseDisplayedCharacter());
    }

    // ------------------ Firebase Updates ------------------

    private IEnumerator PushDisplayedFlagsToFirebase(int idxTrue)
    {
        if (childRef == null) yield break;

        var updates = new Dictionary<string, object>();
        for (int i = 0; i < characters.Count; i++)
            updates[$"characters/{i}/displayed"] = (i == idxTrue);

        var task = childRef.UpdateChildrenAsync(updates);
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.Exception != null)
        {
            Debug.LogError(task.Exception);
            SetStatus("تعذّر حفظ حالة المعاينة.");
        }
    }

    private IEnumerator PushSelectedFlagsToFirebase(int idxTrue)
    {
        if (childRef == null) yield break;

        var updates = new Dictionary<string, object>();
        for (int i = 0; i < characters.Count; i++)
            updates[$"characters/{i}/selected"] = (i == idxTrue);

        var task = childRef.UpdateChildrenAsync(updates);
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.Exception != null)
        {
            Debug.LogError(task.Exception);
            SetStatus("تعذّر حفظ الشخصية المختارة.");
        }
    }

    // Purchase routine
    private IEnumerator PurchaseDisplayedCharacter()
    {
        if (childRef == null || characters == null || characters.Count == 0) yield break;

        int idx = GetDisplayedIndex(characters);
        if (idx < 0 || idx >= characters.Count) yield break;

        // 1) Fresh coins
        var coinsRef = childRef.Child("coins");
        var coinsTask = coinsRef.GetValueAsync();
        yield return new WaitUntil(() => coinsTask.IsCompleted);

        if (coinsTask.Exception != null)
        {
            Debug.LogError(coinsTask.Exception);
            SetStatus("حدث خطأ أثناء قراءة الرصيد.");
            yield break;
        }

        int currentCoins = coins;
        if (coinsTask.Result != null && coinsTask.Result.Exists && coinsTask.Result.Value != null)
            int.TryParse(coinsTask.Result.Value.ToString(), out currentCoins);

        int price = Mathf.RoundToInt(characters[idx].price);

        // 2) Not enough
        if (currentCoins < price)
        {
            ShowPopup(notEnoughMoneyPopup, notEnoughPopupAudio);
            yield break;
        }

        // 3) Commit purchase
        int newCoins = currentCoins - price;
        var updates = new Dictionary<string, object>
        {
            ["coins"] = newCoins,
            [$"characters/{idx}/purchased"] = true
        };

        var updateTask = childRef.UpdateChildrenAsync(updates);
        yield return new WaitUntil(() => updateTask.IsCompleted);

        if (updateTask.Exception != null)
        {
            Debug.LogError(updateTask.Exception);
            SetStatus("تعذّر إتمام عملية الشراء.");
            yield break;
        }

        // 4) Local state + UI
        coins = newCoins;
        characters[idx].purchased = true;
        UpdateCoinsUI();
        UpdatePurchaseButtonVisibility(idx);
        UpdateSelectToggleUI(idx);
        UpdatePriceBadges(); // <— update price icons after purchase

        // 5) Success popup
        SetSuccessPopupCharacterImage(idx);
        ShowPopup(purchaseSuccessPopup, successPopupAudio);
        yield return new WaitForSeconds(5f);
        HidePopup(purchaseSuccessPopup, successPopupAudio);
    }

    // ------------------ UI Helpers ------------------

    private void UpdatePreviewSprite(int idx)
    {
        string chName = SafeCharName(idx);
        if (spriteByName.TryGetValue(chName, out Sprite sprite) && sprite != null)
            previewImage.sprite = sprite;
        else
            previewImage.sprite = null;
    }

    private void UpdatePurchaseButtonVisibility(int displayedIdx)
    {
        if (purchaseButton == null) return;
        bool show = (displayedIdx >= 0 && displayedIdx < characters.Count && !characters[displayedIdx].purchased);
        purchaseButton.gameObject.SetActive(show);
    }

    /// <summary>
    /// Show/hide the single toggle based on purchase state and reflect selected (ON/green) vs not selected (OFF/red).
    /// </summary>
    private void UpdateSelectToggleUI(int displayedIdx)
    {
        if (!selectToggle) return;

        bool inRange = displayedIdx >= 0 && displayedIdx < characters.Count;
        bool purchased = inRange && characters[displayedIdx].purchased;
        bool selected = inRange && characters[displayedIdx].selected;

        // Toggle appears only if purchased
        selectToggle.gameObject.SetActive(purchased);
        if (!purchased) return;

        // Reflect state: ON (green) if selected, OFF (red) if not
        suppressToggleEvent = true;
        SetToggleVisual(selected);
        suppressToggleEvent = false;
    }

    // NEW: show price badges (one per icon) only when NOT purchased
    private void UpdatePriceBadges()
    {
        if (archerPriceGO) archerPriceGO.SetActive(characters.Count > 0 && !characters[0].purchased);
        if (knightPriceGO) knightPriceGO.SetActive(characters.Count > 1 && !characters[1].purchased);
        if (roguePriceGO) roguePriceGO.SetActive(characters.Count > 2 && !characters[2].purchased);
        if (wizardPriceGO) wizardPriceGO.SetActive(characters.Count > 3 && !characters[3].purchased);
    }

    // Sets the UISwitcher visual state. Supports either a public "isOn" or a nullable "Value" property.
    private void SetToggleVisual(bool on)
    {
        if (!selectToggle) return;

        // Try common "isOn" field/property first
        try
        {
            var type = selectToggle.GetType();
            var fieldIsOn = type.GetField("isOn");
            var propIsOn = type.GetProperty("isOn");
            if (fieldIsOn != null) { fieldIsOn.SetValue(selectToggle, on); return; }
            if (propIsOn != null) { propIsOn.SetValue(selectToggle, on, null); return; }

            // Fallback to nullable bool? property named "Value"
            var propValue = type.GetProperty("Value");
            if (propValue != null && propValue.PropertyType == typeof(bool?))
                propValue.SetValue(selectToggle, on ? (bool?)true : (bool?)false, null);
        }
        catch { /* safe no-op if UISwitcher API differs */ }
    }

    private void UpdateSelectionHighlights()
    {
        bool a = characters.Count > 0 && characters[0].selected;
        bool k = characters.Count > 1 && characters[1].selected;
        bool r = characters.Count > 2 && characters[2].selected;
        bool w = characters.Count > 3 && characters[3].selected;
        SetSelectionHighlights(a, k, r, w);
    }

    private void SetSelectionHighlights(bool archerOn, bool knightOn, bool rogueOn, bool wizardOn)
    {
        if (archerSelectedBG) archerSelectedBG.gameObject.SetActive(archerOn);
        if (knightSelectedBG) knightSelectedBG.gameObject.SetActive(knightOn);
        if (rogueSelectedBG) rogueSelectedBG.gameObject.SetActive(rogueOn);
        if (wizardSelectedBG) wizardSelectedBG.gameObject.SetActive(wizardOn);
    }

    private void PlayCharacterAudio(int idx)
    {
        if (archerAudio) archerAudio.Stop();
        if (knightAudio) knightAudio.Stop();
        if (rogueAudio) rogueAudio.Stop();
        if (wizardAudio) wizardAudio.Stop();

        switch (idx)
        {
            case 0: if (archerAudio) archerAudio.Play(); break;
            case 1: if (knightAudio) knightAudio.Play(); break;
            case 2: if (rogueAudio) rogueAudio.Play(); break;
            case 3: if (wizardAudio) wizardAudio.Play(); break;
        }
    }

    private void UpdateCoinsUI()
    {
        if (coinsText != null) coinsText.text = ToArabicDigits(coins);
    }

    private void SetSuccessPopupCharacterImage(int idx)
    {
        if (!purchaseSuccessCharacterImage) return;

        string chName = SafeCharName(idx);
        if (spriteByName.TryGetValue(chName, out var sprite) && sprite != null)
        {
            purchaseSuccessCharacterImage.sprite = sprite;
            purchaseSuccessCharacterImage.enabled = true;
        }
        else
        {
            purchaseSuccessCharacterImage.sprite = null;
            purchaseSuccessCharacterImage.enabled = false;
        }
    }

    private void ShowPopup(GameObject popup, AudioSource audio)
    {
        if (popup) popup.SetActive(true);
        if (audio)
        {
            audio.Stop();
            audio.Play();
        }
    }

    private void HidePopup(GameObject popup, AudioSource audio)
    {
        if (audio && audio.isPlaying) audio.Stop();
        if (popup) popup.SetActive(false);
    }

    private string SafeCharName(int idx)
    {
        if (idx < 0 || idx >= characters.Count) return "Archer";
        var n = characters[idx].name;
        if (string.IsNullOrEmpty(n)) return CharacterOrder[Mathf.Clamp(idx, 0, CharacterOrder.Length - 1)];
        return n;
    }

    private int GetDisplayedIndex(List<Character> list)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i].displayed) return i;
        return -1;
    }

    private void ForceSingleDisplayedLocal(int idxTrue)
    {
        for (int i = 0; i < characters.Count; i++)
            characters[i].displayed = (i == idxTrue);
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        if (!string.IsNullOrEmpty(msg)) Debug.Log($"[Store] {msg}");
    }

    private static readonly char[] ArabicIndicDigits =
        { '٠','١','٢','٣','٤','٥','٦','٧','٨','٩' };

    private string ToArabicDigits(int value)
    {
        var s = value.ToString();
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch >= '0' && ch <= '9') sb.Append(ArabicIndicDigits[ch - '0']);
            else sb.Append(ch);
        }
        return sb.ToString();
    }
}
