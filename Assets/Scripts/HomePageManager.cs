using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ArabicSupport;
using Firebase.Auth;
using Firebase.Database;
using Firebase.Extensions;   // for ContinueWithOnMainThread

public class HomePageManager : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_Text childNameText;       // TMP Text لعرض اسم الطفل (فونت BoldVazirmatn من الانسبكتور)
    public Image childProfileImage;      // صورة الطفل (Icon_User)
    public Image topCornerImage;         // صورة الطفل في الزاوية العلوية
    public Button topCornerButton;       // الزر الذي يفتح قائمة البوب أب
    public GameObject popupMenu;         // قائمة البوب أب

    // Firebase
    private FirebaseAuth auth;
    private DatabaseReference dbRoot;
    private string parentId;
    private string selectedChildKey;   // will be filled after we find the selected child

    private void Start()
    {
        // Firebase basic refs
        auth = FirebaseAuth.DefaultInstance;
        dbRoot = FirebaseDatabase.DefaultInstance.RootReference;

        if (auth.CurrentUser == null)
        {
            Debug.LogError("No logged-in user! Cannot load child data.");
            return;
        }

        parentId = auth.CurrentUser.UserId;

        // Load coins/stars for the current child (if your managers use SelectedChildKey internally)
        if (CoinsManager.instance != null)
            CoinsManager.instance.LoadCoins();

        if (StarsManager.instance != null)
            StarsManager.instance.LoadStars();

        // Load the selected child data
        LoadSelectedChildFromDatabase();

        // Toggle popup
        if (topCornerButton != null)
            topCornerButton.onClick.AddListener(TogglePopupMenu);
    }

    private void LoadSelectedChildFromDatabase()
    {
        // parents/{parentId}/children
        DatabaseReference childrenRef = FirebaseDatabase.DefaultInstance
            .GetReference("parents")
            .Child(parentId)
            .Child("children");

        childrenRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError("Error loading children data: " + task.Exception);
                return;
            }

            if (!task.IsCompleted) return;

            DataSnapshot snapshot = task.Result;

            DataSnapshot selectedChild = null;

            // 🔍 Find the child where selected == true
            foreach (DataSnapshot childSnap in snapshot.Children)
            {
                object selectedObj = childSnap.Child("selected").Value;
                bool isSelected = selectedObj != null &&
                                  selectedObj.ToString().ToLower() == "true";

                if (isSelected)
                {
                    selectedChild = childSnap;
                    selectedChildKey = childSnap.Key; // e.g. "0", "1", ...
                    break;
                }
            }

            // If no "selected" found, fallback to index "0" if it exists
            if (selectedChild == null)
            {
                Debug.LogWarning("No selected child found. Falling back to child 0 if available.");
                if (snapshot.HasChild("0"))
                {
                    selectedChild = snapshot.Child("0");
                    selectedChildKey = "0";
                }
                else
                {
                    Debug.LogWarning("No children found for this parent.");
                    return;
                }
            }

            // Optionally store key in PlayerPrefs for other scripts
            if (!string.IsNullOrEmpty(selectedChildKey))
            {
                PlayerPrefs.SetString("SelectedChildKey", selectedChildKey);
                PlayerPrefs.Save();
            }

            // ------- NAME -------
            object nameObj = selectedChild.Child("name").Value;
            string childName = nameObj != null ? nameObj.ToString() : "اسم غير محدد";

            if (childNameText != null)
            {
                childNameText.text = ArabicFixer.Fix(childName, showTashkeel: false, useHinduNumbers: true);
            }

            // ------- IMAGE -------
            object imageObj = selectedChild.Child("image").Value;
            string childImagePath = imageObj != null ? imageObj.ToString() : "";

            if (!string.IsNullOrEmpty(childImagePath))
            {
                Sprite childSprite = Resources.Load<Sprite>(childImagePath);
                if (childSprite != null)
                {
                    if (childProfileImage != null)
                        childProfileImage.sprite = childSprite; // Icon_User

                    if (topCornerImage != null)
                        topCornerImage.sprite = childSprite;    // Top corner
                }
                else
                {
                    Debug.LogError("Sprite not found in Resources at path: " + childImagePath);
                }
            }
        });
    }

    // Show/hide popup
    public void TogglePopupMenu()
    {
        if (popupMenu != null)
        {
            popupMenu.SetActive(!popupMenu.activeSelf);
        }
        else
        {
            Debug.LogError("Popup Menu is not assigned in the Inspector!");
        }
    }
}
