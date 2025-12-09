using System;
using System.Collections; // needed for IEnumerator / Coroutines
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Firebase.Auth;
using Firebase.Database;

/// <summary>
/// Manages island locking/unlocking logic on the home screen:
/// - Island 0 (green) is always unlocked for a new child.
/// - Island 1 (purple) requires 1 star.
/// - Island 2 (blue)   requires 2 stars.
/// - Island 3 (yellow) requires 2 stars AND all previous islands unlocked (final test island).
///
/// Stars are managed by StarsManager (Firebase):
/// - Reads current stars from StarsManager.CurrentStars
/// - Spends stars using StarsManager.AddStarsToSelectedChild(-requiredStars)
///
/// Visuals:
/// - Locked islands: grey tint + lock icon.
/// - Unlocked islands: normal color, no lock.
/// - When an island is unlocked, player can click it again to enter the world scene.
///
/// Popups:
/// - All popups play a sound when shown.
/// - All popups auto-hide after the sound finishes (clip length),
///   and if there's no clip, they use popupAutoHideDuration.
/// - Confirm popup (Are you sure?) plays a sound but does NOT auto-hide.
///
/// Persistence:
/// - Island unlocked/locked state is stored ONLY in Firebase under:
///   parents/{userId}/children/{selectedChildIndex}/islands/island_i
/// </summary>
public class IslandsUnlockManager : MonoBehaviour
{
    [Header("Islands Settings (ordered: Green, Purple, Blue, Yellow)")]
    [Tooltip("List of all islands that can be unlocked.")]
    public IslandUIData[] islands;

    [Header("Pop-up Canvases")]
    [Tooltip("Canvas shown when player is asked to confirm spending stars to unlock.")]
    public GameObject confirmOpenCanvas;

    [Tooltip("Canvas shown when player does not have enough stars to unlock.")]
    public GameObject notEnoughStarsCanvas;

    [Tooltip("Canvas shown for the final island when previous islands are still locked.")]
    public GameObject previousIslandsCanvas;

    [Tooltip("Canvas shown when an island is successfully unlocked.")]
    public GameObject unlockSuccessCanvas;

    [Header("Buttons inside Confirm Canvas")]
    [Tooltip("Button that actually unlocks the island when pressed.")]
    public Button confirmOpenButton;

    [Tooltip("Optional button that cancels the unlock action and closes the canvas.")]
    public Button cancelOpenButton;

    [Header("Popup Timing")]
    [Tooltip("Fallback duration (in seconds) for auto-hide if no audio clip is assigned.")]
    public float popupAutoHideDuration = 2f;

    [Header("Popup Audio")]
    [Tooltip("AudioSource used to play popup sounds.")]
    public AudioSource audioSource;

    [Tooltip("Sound played when the confirm popup (Are you sure?) appears.")]
    public AudioClip confirmPopupClip;

    [Tooltip("Sound played when 'not enough stars' popup appears.")]
    public AudioClip notEnoughStarsClip;

    [Tooltip("Sound played when 'previous islands not unlocked' popup appears.")]
    public AudioClip previousIslandsClip;

    [Tooltip("Sound played when 'island unlocked successfully' popup appears.")]
    public AudioClip unlockSuccessClip;

    // Index of the island currently waiting to be unlocked after confirmation
    private int pendingIslandIndex = -1;

    // Reference to the running auto-hide coroutine (so we can stop it when needed)
    private Coroutine autoHideCoroutine = null;

    // Firebase info (per parent/child)
    private string userId;
    private int selectedChildIndex = -1;

    private void Start()
    {
        // 1) في البداية نظهر كل الجزر كأنها مغلقة (شكليًا) حتى نقرأ من Firebase
        SetAllIslandsLockedVisual();

        // 2) ربط أزرار التأكيد / الإلغاء في كانفس "هل تريد الفتح؟"
        if (confirmOpenButton != null)
            confirmOpenButton.onClick.AddListener(OnConfirmOpenIsland);

        if (cancelOpenButton != null)
            cancelOpenButton.onClick.AddListener(OnCancelOpenIsland);

        // 3) مزامنة حالة الجزر من Firebase
        StartCoroutine(SyncIslandsWithFirebase());
    }

    /// <summary>
    /// Temporarily shows all islands as locked until real data is loaded from Firebase.
    /// </summary>
    private void SetAllIslandsLockedVisual()
    {
        for (int i = 0; i < islands.Length; i++)
        {
            islands[i].isUnlocked = false;
            UpdateIslandVisual(islands[i]);
        }
    }

    /// <summary>
    /// Synchronizes island states with Firebase for the currently selected child.
    /// </summary>
    private IEnumerator SyncIslandsWithFirebase()
    {
        // Get current user
        FirebaseUser user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null)
        {
            Debug.LogWarning("IslandsUnlockManager: No logged-in user. Using temporary locked visuals only.");
            yield break;
        }

        userId = user.UserId;

        // Wait until StarsManager is ready and selected child index is known
        yield return new WaitUntil(() => StarsManager.instance != null);

        int safetyCounter = 0;
        while (StarsManager.instance.SelectedChildIndex == -1 && safetyCounter < 600)
        {
            safetyCounter++;
            yield return null; // wait a few frames
        }

        selectedChildIndex = StarsManager.instance.SelectedChildIndex;

        if (selectedChildIndex == -1)
        {
            Debug.LogWarning("IslandsUnlockManager: Selected child not determined. Keeping all islands visually locked.");
            yield break;
        }

        string islandsPath = $"parents/{userId}/children/{selectedChildIndex}/islands";

        var task = FirebaseDatabase.DefaultInstance
            .GetReference(islandsPath)
            .GetValueAsync();

        yield return new WaitUntil(() => task.IsCompleted);

        if (task.Exception != null)
        {
            Debug.LogError("IslandsUnlockManager: Failed to load islands from Firebase: " + task.Exception);
            yield break;
        }

        DataSnapshot snapshot = task.Result;

        if (snapshot.Exists)
        {
            // ✅ Use Firebase as the source of truth
            for (int i = 0; i < islands.Length; i++)
            {
                string key = "island_" + i;

                bool unlocked = false;

                if (snapshot.HasChild(key))
                {
                    bool.TryParse(snapshot.Child(key).Value.ToString(), out unlocked);
                }
                else
                {
                    // If Firebase has no entry for this island, treat it as locked
                    unlocked = false;
                }

                islands[i].isUnlocked = unlocked;
                UpdateIslandVisual(islands[i]);
            }
        }
        else
        {
            // ✅ First time for this child: open only island 0, others locked
            for (int i = 0; i < islands.Length; i++)
            {
                bool unlocked = (i == 0); // فقط الأولى مفتوحة

                islands[i].isUnlocked = unlocked;
                UpdateIslandVisual(islands[i]);

                // Save to Firebase
                SaveIslandStateToDatabase(i, unlocked);
            }
        }
    }

    /// <summary>
    /// Helper to save one island's state to Firebase.
    /// </summary>
    private void SaveIslandStateToDatabase(int islandIndex, bool isUnlocked)
    {
        if (string.IsNullOrEmpty(userId) || selectedChildIndex < 0) return;

        string path = $"parents/{userId}/children/{selectedChildIndex}/islands/island_{islandIndex}";
        FirebaseDatabase.DefaultInstance.GetReference(path).SetValueAsync(isUnlocked);
    }

    /// <summary>
    /// Helper to read the latest star value from StarsManager.
    /// If StarsManager is not ready yet, returns 0.
    /// </summary>
    private int GetCurrentStars()
    {
        if (StarsManager.instance != null)
            return StarsManager.instance.CurrentStars;
        return 0;
    }

    /// <summary>
    /// Called from each island button when clicked.
    /// You must pass the island index (0,1,2,3) from the button OnClick.
    /// </summary>
    public void OnIslandClicked(int islandIndex)
    {
        if (islandIndex < 0 || islandIndex >= islands.Length) return;

        IslandUIData island = islands[islandIndex];

        // If island is already unlocked → load/open the island directly
        if (island.isUnlocked)
        {
            if (!string.IsNullOrEmpty(island.sceneName))
            {
                SceneManager.LoadScene(island.sceneName);
            }
            else
            {
                Debug.LogWarning($"Island {island.islandName} is unlocked but has no sceneName assigned.");
            }
            return;
        }

        // Final island: requires all previous unlocked
        if (island.isFinalIsland)
        {
            bool allPreviousUnlocked = true;

            for (int i = 0; i < islandIndex; i++)
            {
                if (!islands[i].isUnlocked)
                {
                    allPreviousUnlocked = false;
                    break;
                }
            }

            if (!allPreviousUnlocked)
            {
                ShowOnlyCanvas(previousIslandsCanvas);
                return;
            }
        }

        // Check stars
        int currentStars = GetCurrentStars();
        if (currentStars < island.requiredStars)
        {
            ShowOnlyCanvas(notEnoughStarsCanvas);
            return;
        }

        // Conditions OK → ask for confirmation
        pendingIslandIndex = islandIndex;
        ShowOnlyCanvas(confirmOpenCanvas); // confirm popup does NOT auto-close
    }

    /// <summary>
    /// Called by the "Open" button inside the confirm canvas.
    /// Actually unlocks the island, spends the stars in Firebase, and updates visuals.
    /// </summary>
    private void OnConfirmOpenIsland()
    {
        if (pendingIslandIndex < 0 || pendingIslandIndex >= islands.Length) return;

        IslandUIData island = islands[pendingIslandIndex];

        // Spend stars through StarsManager (negative value means subtract)
        if (StarsManager.instance != null)
        {
            StarsManager.instance.AddStarsToSelectedChild(-island.requiredStars);
        }
        else
        {
            Debug.LogWarning("StarsManager.instance is null. Cannot update stars in Firebase.");
        }

        // Unlock island & save in Firebase
        island.isUnlocked = true;
        SaveIslandStateToDatabase(pendingIslandIndex, true);

        // Update visuals
        UpdateIslandVisual(island);

        // Show success popup (auto-hide)
        ShowOnlyCanvas(unlockSuccessCanvas);

        pendingIslandIndex = -1;
    }

    /// <summary>
    /// Called by the "Cancel" button inside the confirm canvas.
    /// Simply closes the popup without unlocking.
    /// </summary>
    private void OnCancelOpenIsland()
    {
        pendingIslandIndex = -1;
        HideAllCanvases();
    }

    /// <summary>
    /// Updates the visual state of a single island.
    /// </summary>
    private void UpdateIslandVisual(IslandUIData island)
    {
        if (island == null || island.islandImage == null || island.islandButton == null) return;

        if (island.isUnlocked)
        {
            island.islandButton.interactable = true;
            island.islandImage.color = Color.white;

            if (island.lockIcon != null)
                island.lockIcon.SetActive(false);
        }
        else
        {
            island.islandButton.interactable = true; // still clickable to show popups
            island.islandImage.color = new Color(0.6f, 0.6f, 0.6f, 1f);

            if (island.lockIcon != null)
                island.lockIcon.SetActive(true);
        }
    }

    /// <summary>
    /// Plays the correct sound depending on which popup is shown
    /// and returns the clip duration (0 if none).
    /// </summary>
    private float PlayPopupSoundAndGetDuration(GameObject target)
    {
        if (audioSource == null) return 0f;

        AudioClip clip = null;

        if (target == confirmOpenCanvas)
            clip = confirmPopupClip;
        else if (target == notEnoughStarsCanvas)
            clip = notEnoughStarsClip;
        else if (target == previousIslandsCanvas)
            clip = previousIslandsClip;
        else if (target == unlockSuccessCanvas)
            clip = unlockSuccessClip;

        if (clip != null)
        {
            audioSource.clip = clip;
            audioSource.Play();
            return clip.length;
        }

        return 0f;
    }

    /// <summary>
    /// Shows only the target canvas, plays sound, and auto-hides based on sound length
    /// (except confirmOpenCanvas which never auto-hides).
    /// </summary>
    private void ShowOnlyCanvas(GameObject target)
    {
        if (autoHideCoroutine != null)
        {
            StopCoroutine(autoHideCoroutine);
            autoHideCoroutine = null;
        }

        HideAllCanvases();

        if (target != null)
        {
            target.SetActive(true);

            float soundDuration = PlayPopupSoundAndGetDuration(target);

            if (target != confirmOpenCanvas)
            {
                float delay = soundDuration > 0f ? soundDuration : popupAutoHideDuration;
                if (delay > 0f)
                {
                    autoHideCoroutine = StartCoroutine(AutoHidePopupAfterDelay(delay));
                }
            }
        }
    }

    private IEnumerator AutoHidePopupAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        HideAllCanvases();
        autoHideCoroutine = null;
    }

    private void HideAllCanvases()
    {
        if (confirmOpenCanvas != null) confirmOpenCanvas.SetActive(false);
        if (notEnoughStarsCanvas != null) notEnoughStarsCanvas.SetActive(false);
        if (previousIslandsCanvas != null) previousIslandsCanvas.SetActive(false);
        if (unlockSuccessCanvas != null) unlockSuccessCanvas.SetActive(false);
    }
}

/// <summary>
/// Serializable data container for one island on the home screen.
/// </summary>
[Serializable]
public class IslandUIData
{
    [Header("General Info")]
    public string islandName;

    [Tooltip("Mark this true only for the final test island that requires all previous islands unlocked.")]
    public bool isFinalIsland = false;

    [HideInInspector]
    public bool isUnlocked;   // Runtime state (Firebase)

    [Header("Button & Visuals")]
    public Button islandButton;
    public Image islandImage;
    public GameObject lockIcon;

    [Header("Unlock Requirement")]
    public int requiredStars = 0;

    [Header("Scene")]
    [Tooltip("Name of the world scene to load when this island is unlocked and clicked.")]
    public string sceneName; // e.g., World(1), World(2), World(3), World(4)
}
