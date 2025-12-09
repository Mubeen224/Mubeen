using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Firebase.Database;
using Firebase.Auth;
using TMPro;
using PimDeWitte.UnityMainThreadDispatcher;

[System.Serializable]
public class IslandData
{
    public int islandId;           // ID of the island
    public int requiredStars;      // Number of stars required to unlock the island
    public GameObject lockIcon;    // Lock icon displayed if island is locked
    public Button islandButton;    // Button that allows access to the island
}

public class ManageIslands : MonoBehaviour
{
    [Header("Islands Settings")]
    public List<IslandData> islands; // List of all island configurations

    [Header("UI Popups")]
    public GameObject popupWindow;         // Popup shown when stars are not enough
    public TMP_Text popupMessage;          // Message displayed in the popup
    public Button popupOkButton;           // Button to close the popup
    public GameObject confirmWindow;       // Confirmation window before unlocking
    public TMP_Text confirmMessage;        // Message displayed in confirmation window
    public Button confirmYesButton;        // Button to confirm unlocking
    public Button confirmNoButton;         // Button to cancel unlocking

    private string parentId;
    private int selectedChildIndex = -1;
    private int pendingUnlockIslandId = -1;
    private int pendingUnlockRequiredStars = 0;
    private IslandData lastClickedIsland = null;

    private bool buttonsInitialized = false;

    void Start()
    {
        // Get current parent ID and selected child
        parentId = FirebaseAuth.DefaultInstance.CurrentUser.UserId;
        selectedChildIndex = StarsManager.instance.SelectedChildIndex;

        // Initialize island buttons once
        if (!buttonsInitialized)
        {
            foreach (var island in islands)
            {
                int capturedId = island.islandId;
                island.islandButton.onClick.AddListener(() => OnIslandClick(capturedId));
            }

            popupOkButton.onClick.AddListener(OnPopupOkButtonClicked);
            confirmYesButton.onClick.AddListener(ConfirmUnlockIsland);
            confirmNoButton.onClick.AddListener(() => confirmWindow.SetActive(false));

            buttonsInitialized = true;
        }

        LoadChildProgress(); // Load which islands are unlocked
    }

    void OnEnable()
    {
        // Refresh child index and reload progress when re-enabled
        selectedChildIndex = StarsManager.instance.SelectedChildIndex;
        LoadChildProgress();
    }

    // Loads child data from Firebase to check unlocked islands
    public void LoadChildProgress()
    {
        if (selectedChildIndex == -1)
        {
            Debug.LogWarning("Selected child not set!");
            return;
        }

        string path = $"parents/{parentId}/children/{selectedChildIndex}/";

        FirebaseDatabase.DefaultInstance.GetReference(path).GetValueAsync().ContinueWith(task =>
        {
            if (task.IsCompleted && task.Result != null)
            {
                var snapshot = task.Result;
                HashSet<int> unlockedIslands = new HashSet<int>();

                // Read unlocked island IDs from Firebase
                if (snapshot.Child("unlockedIslands").Exists)
                {
                    foreach (var child in snapshot.Child("unlockedIslands").Children)
                    {
                        int unlockedId = 0;
                        int.TryParse(child.Key, out unlockedId);
                        unlockedIslands.Add(unlockedId);
                    }
                }

                // Update UI on main thread
                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                {
                    foreach (var island in islands)
                    {
                        bool isUnlocked = unlockedIslands.Contains(island.islandId) || island.islandId == 1; // Island 1 is always unlocked
                        if (island.lockIcon != null)
                            island.lockIcon.SetActive(!isUnlocked); // Show lock icon if not unlocked

                        island.islandButton.interactable = true; // Allow clicking
                    }
                });
            }
            else
            {
                Debug.LogWarning("Failed to load child data: " + task.Exception);
            }
        });
    }

    // Called when an island button is clicked
    void OnIslandClick(int islandId)
    {
        lastClickedIsland = islands.Find(i => i.islandId == islandId);

        if (lastClickedIsland == null || selectedChildIndex == -1)
            return;

        string childPath = $"parents/{parentId}/children/{selectedChildIndex}";

        FirebaseDatabase.DefaultInstance.GetReference(childPath).GetValueAsync().ContinueWith(task =>
        {
            if (task.IsCompleted && task.Result != null)
            {
                var snapshot = task.Result;
                int starsDb = 0;

                // Get the current number of stars from Firebase
                int.TryParse(snapshot.Child("stars").Value?.ToString() ?? "0", out starsDb);

                // Check if the island is already unlocked
                bool isUnlocked = false;
                if (snapshot.Child("unlockedIslands").Exists)
                {
                    foreach (var child in snapshot.Child("unlockedIslands").Children)
                    {
                        int unlockedId = 0;
                        int.TryParse(child.Key, out unlockedId);
                        if (unlockedId == islandId)
                        {
                            isUnlocked = true;
                            break;
                        }
                    }
                }

                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                {
                    if (isUnlocked || islandId == 1)
                    {
                        EnterIsland(islandId); // Directly enter island
                    }
                    else if (starsDb >= lastClickedIsland.requiredStars)
                    {
                        // Show confirmation popup if stars are enough
                        pendingUnlockIslandId = islandId;
                        pendingUnlockRequiredStars = lastClickedIsland.requiredStars;
                        confirmMessage.text = $"Do you want to unlock this island for {lastClickedIsland.requiredStars} stars?";
                        confirmWindow.SetActive(true);
                    }
                    else
                    {
                        // Not enough stars — show warning popup
                        popupMessage.text = $"You need {lastClickedIsland.requiredStars} stars to unlock this island!";
                        popupWindow.SetActive(true);
                    }
                });
            }
            else
            {
                Debug.LogWarning("Failed to retrieve child data.");
            }
        });
    }

    // Close popup window when OK is clicked
    void OnPopupOkButtonClicked()
    {
        popupWindow.SetActive(false);
    }

    // Confirm and unlock the island by subtracting stars
    void ConfirmUnlockIsland()
    {
        if (selectedChildIndex == -1)
        {
            Debug.LogWarning("Selected child not set!");
            return;
        }

        string childPath = $"parents/{parentId}/children/{selectedChildIndex}";

        FirebaseDatabase.DefaultInstance.GetReference(childPath).GetValueAsync().ContinueWith(task =>
        {
            if (task.IsCompleted && task.Result != null)
            {
                var snapshot = task.Result;
                int starsDb = 0;
                int.TryParse(snapshot.Child("stars").Value?.ToString() ?? "0", out starsDb);

                // Subtract required stars
                int newStars = starsDb - pendingUnlockRequiredStars;
                if (newStars < 0) newStars = 0;

                // Firebase paths to update
                string starsPath = $"parents/{parentId}/children/{selectedChildIndex}/stars";
                string unlockPath = $"parents/{parentId}/children/{selectedChildIndex}/unlockedIslands/{pendingUnlockIslandId}";

                // Update stars and unlock the island
                var starsTask = FirebaseDatabase.DefaultInstance.GetReference(starsPath).SetValueAsync(newStars);
                var unlockTask = FirebaseDatabase.DefaultInstance.GetReference(unlockPath).SetValueAsync(true);

                System.Threading.Tasks.Task.WhenAll(starsTask, unlockTask).ContinueWith(t =>
                {
                    UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    {
                        confirmWindow.SetActive(false);
                        LoadChildProgress(); // Refresh UI
                        EnterIsland(pendingUnlockIslandId); // Enter unlocked island
                    });
                });
            }
            else
            {
                Debug.LogWarning("Failed to refresh child data after confirmation.");
            }
        });
    }

    // Placeholder for entering the island scene
    void EnterIsland(int islandId)
    {
        Debug.Log("Entering island: " + islandId);
        // You can load a scene like: SceneManager.LoadScene("Island" + islandId);
    }
}
