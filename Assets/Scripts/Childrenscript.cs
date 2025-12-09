using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase;
using Firebase.Auth;
using Firebase.Database;
using UnityEngine.SceneManagement;
using ArabicSupport;   // لإصلاح العربية في العرض

public class Childrenscript : MonoBehaviour
{
    [Header("Child UI Elements")]
    public Image image1, image2, image3, image4, image5; // Child character images
    public Image background1, background2, background3, background4, background5; // Background frames
    public TMP_Text name1, name2, name3, name4, name5; // Child name labels
    public GameObject selected1, selected2, selected3, selected4, selected5; // Selection indicators

    [Header("Buttons")]
    public Button deleatchild1, deleatchild2, deleatchild3, deleatchild4, deleatchild5; // Delete buttons
    public Button editchild1, editchild2, editchild3, editchild4, editchild5; // Edit buttons
    public Button selectchild1, selectchild2, selectchild3, selectchild4, selectchild5; // Select buttons
    public Button addChildButton; // Add child button

    [Header("Delete Child Popup Elements")]
    public GameObject deleteChildPopup; // Popup for delete confirmation
    public Button deleteChildYesButton;
    public Button deleteChildNoButton;
    private int childToDeleteIndex; // Stores index of child to delete

    [Header("Edit Child Popup Elements")]
    public GameObject editChildPopup; // Popup for editing child info
    public Button editChildYesButton;
    public Button editChildCancelButton;
    public TMP_InputField editChildNameInput; // Name input field (نص خام)
    public TMP_Text editChildNameDisplay;     // نص للعرض بالعربي المشبوك
    public TMP_InputField yearInput, monthInput, dayInput; // Date input fields
    private int childToEditIndex; // Stores index of child to edit

    [Header("Add Child Popup Elements")]
    public GameObject addChildPopup; // Popup shown when child limit is reached
    public Button closeAddChildPopupButton;

    [Header("Progress Bar")]
    public Slider progressBar; // UI slider for number of added children
    public TMP_Text accountRatioText; // Shows X / 5 children text

    [Header("Invalid Name Popup")]
    public GameObject invalidNamePopup; // Popup for duplicate names
    public Button closeInvalidNamePopupButton;

    private FirebaseAuth auth;
    private FirebaseDatabase database;
    private string parentId;
    private Dictionary<int, GameObject> childObjects;

    void Start()
    {
        // Initialize Firebase
        auth = FirebaseAuth.DefaultInstance;
        database = FirebaseDatabase.DefaultInstance;

        if (auth.CurrentUser != null)
        {
            parentId = auth.CurrentUser.UserId;
            StartCoroutine(LoadChildren()); // Load child data from Firebase
        }
        else
        {
            Debug.LogError("No authenticated user found.");
            return;
        }

        // ربط حدث تغيير النص في خانة التعديل مع عرض الاسم المشبوك
        if (editChildNameInput != null)
        {
            editChildNameInput.onValueChanged.AddListener(OnEditNameChanged);
        }

        // Setup delete button listeners
        deleatchild1.onClick.AddListener(() => ShowDeletePopup(0));
        deleatchild2.onClick.AddListener(() => ShowDeletePopup(1));
        deleatchild3.onClick.AddListener(() => ShowDeletePopup(2));
        deleatchild4.onClick.AddListener(() => ShowDeletePopup(3));
        deleatchild5.onClick.AddListener(() => ShowDeletePopup(4));
        deleteChildYesButton.onClick.AddListener(() => StartCoroutine(DeleteChild()));
        deleteChildNoButton.onClick.AddListener(() => deleteChildPopup.SetActive(false));

        // Setup edit button listeners
        editchild1.onClick.AddListener(() => ShowEditPopup(0));
        editchild2.onClick.AddListener(() => ShowEditPopup(1));
        editchild3.onClick.AddListener(() => ShowEditPopup(2));
        editchild4.onClick.AddListener(() => ShowEditPopup(3));
        editchild5.onClick.AddListener(() => ShowEditPopup(4));
        editChildYesButton.onClick.AddListener(() => StartCoroutine(EditChild()));
        editChildCancelButton.onClick.AddListener(() => editChildPopup.SetActive(false));

        // Setup select button listeners
        selectchild1.onClick.AddListener(() => StartCoroutine(SelectChild(0)));
        selectchild2.onClick.AddListener(() => StartCoroutine(SelectChild(1)));
        selectchild3.onClick.AddListener(() => StartCoroutine(SelectChild(2)));
        selectchild4.onClick.AddListener(() => StartCoroutine(SelectChild(3)));
        selectchild5.onClick.AddListener(() => StartCoroutine(SelectChild(4)));

        addChildButton.onClick.AddListener(CheckAddChildEligibility);
        closeAddChildPopupButton.onClick.AddListener(() => addChildPopup.SetActive(false));
        closeInvalidNamePopupButton.onClick.AddListener(CloseInvalidNamePopup);

        // Hide popups on start
        deleteChildPopup.SetActive(false);
        editChildPopup.SetActive(false);
        addChildPopup.SetActive(false);
    }

    IEnumerator LoadChildren()
    {
        // Load all 5 children from Firebase
        DatabaseReference dbRef = database.RootReference.Child("parents").Child(parentId).Child("children");
        var dbTask = dbRef.GetValueAsync();
        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError("Failed to load children data: " + dbTask.Exception);
            yield break;
        }

        DataSnapshot snapshot = dbTask.Result;
        int childCount = 0;

        // Store UI elements in arrays for easier access
        Image[] images = { image1, image2, image3, image4, image5 };
        Image[] backgrounds = { background1, background2, background3, background4, background5 };
        TMP_Text[] names = { name1, name2, name3, name4, name5 };
        GameObject[] selectedIcons = { selected1, selected2, selected3, selected4, selected5 };

        for (int i = 0; i < 5; i++)
        {
            // If child exists at index
            if (snapshot.Child(i.ToString()).HasChild("name") && snapshot.Child(i.ToString()).Child("name").Value.ToString() != "null")
            {
                childCount++;
                string rawName = snapshot.Child(i.ToString()).Child("name").Value.ToString();
                names[i].text = ArabicFixer.Fix(rawName); // Fix Arabic display
                backgrounds[i].sprite = Resources.Load<Sprite>(snapshot.Child(i.ToString()).Child("background").Value.ToString());
                images[i].sprite = Resources.Load<Sprite>(snapshot.Child(i.ToString()).Child("image").Value.ToString());
                selectedIcons[i].SetActive(snapshot.Child(i.ToString()).Child("selected").Value.ToString() == "True");
            }
            else
            {
                names[i].gameObject.transform.parent.gameObject.SetActive(false); // Hide empty slot
            }
        }

        UpdateProgressBar(childCount);
    }

    void UpdateProgressBar(int childCount)
    {
        progressBar.value = childCount / 5f; // Set progress value
        accountRatioText.text = $"{childCount} / 5"; // Show X/5 label
    }

    void ShowDeletePopup(int index)
    {
        childToDeleteIndex = index;
        deleteChildPopup.SetActive(true); // Show confirmation popup
    }

    public IEnumerator DeleteChild()
    {
        // Clear data for the child to delete
        DatabaseReference dbRef = database.RootReference.Child("parents").Child(parentId).Child("children").Child(childToDeleteIndex.ToString());
        var updateTask = dbRef.SetValueAsync(new Dictionary<string, object>
        {
            { "name", null },
            { "day", null },
            { "month", null },
            { "year", null },
            { "image", null },
            { "keys", null },
            { "coins", null },
            { "gender", false },
            { "selected", false }
        });

        yield return new WaitUntil(() => updateTask.IsCompleted);

        if (updateTask.Exception != null)
        {
            Debug.LogError("Failed to delete child: " + updateTask.Exception);
            yield break;
        }

        deleteChildPopup.SetActive(false);
        SceneManager.LoadScene("Children"); // Refresh scene
    }

    public void OnDeleteChildButtonClick()
    {
        StartCoroutine(DeleteChild()); // Trigger deletion
    }

    void ShowEditPopup(int index)
    {
        childToEditIndex = index;
        editChildNameInput.interactable = true;
        editChildNameInput.text = "";

        if (editChildNameDisplay != null)
            editChildNameDisplay.text = ""; // نفرغ العرض أيضاً

        invalidNamePopup.SetActive(false);
        StartCoroutine(LoadChildData());
        editChildPopup.SetActive(true);
    }

    IEnumerator LoadChildData()
    {
        // Load existing child info into popup fields
        DatabaseReference dbRef = database.RootReference.Child("parents").Child(parentId).Child("children").Child(childToEditIndex.ToString());
        var dbTask = dbRef.GetValueAsync();
        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError("Failed to load child data: " + dbTask.Exception);
            yield break;
        }

        DataSnapshot snapshot = dbTask.Result;
        if (snapshot.Exists)
        {
            if (snapshot.HasChild("day")) dayInput.text = snapshot.Child("day").Value.ToString();
            if (snapshot.HasChild("month")) monthInput.text = snapshot.Child("month").Value.ToString();
            if (snapshot.HasChild("year")) yearInput.text = snapshot.Child("year").Value.ToString();
            if (snapshot.HasChild("name"))
            {
                string rawName = snapshot.Child("name").Value.ToString();

                // النص الخام في الـ Input
                editChildNameInput.text = rawName;

                // الاسم المشبوك في الـ Display
                if (editChildNameDisplay != null)
                    editChildNameDisplay.text = ArabicFixer.Fix(rawName);

                editChildNameInput.interactable = true;
            }
        }
    }

    // يتم استدعاؤها كلما المستخدم يغيّر الاسم في خانة التعديل
    void OnEditNameChanged(string value)
    {
        if (editChildNameDisplay != null)
        {
            editChildNameDisplay.text = ArabicFixer.Fix(value);
        }
    }

    public IEnumerator EditChild()
    {
        // Validate and save edited child info
        string newName = editChildNameInput.text.Trim();  // نستخدم النص الخام
        int day, month, year;
        bool validDay = int.TryParse(dayInput.text, out day);
        bool validMonth = int.TryParse(monthInput.text, out month);
        bool validYear = int.TryParse(yearInput.text, out year);

        if (string.IsNullOrEmpty(newName) || !validDay || !validMonth || !validYear || !IsValidDate(day, month, year))
        {
            Debug.LogError("Invalid input.");
            yield break;
        }

        // Check for duplicate name
        DatabaseReference dbRef = database.RootReference.Child("parents").Child(parentId).Child("children");
        var dbTask = dbRef.GetValueAsync();
        yield return new WaitUntil(() => dbTask.IsCompleted);

        DataSnapshot snapshot = dbTask.Result;
        foreach (DataSnapshot childSnapshot in snapshot.Children)
        {
            if (childSnapshot.HasChild("name") && childSnapshot.Child("name").Value.ToString() == newName)
            {
                invalidNamePopup.SetActive(true);
                yield break;
            }
        }

        // Update child data
        Dictionary<string, object> updates = new Dictionary<string, object>
        {
            { "name", newName },
            { "day", day },
            { "month", month },
            { "year", year }
        };

        var updateTask = dbRef.Child(childToEditIndex.ToString()).UpdateChildrenAsync(updates);
        yield return new WaitUntil(() => updateTask.IsCompleted);

        if (updateTask.Exception != null)
        {
            Debug.LogError("Update failed: " + updateTask.Exception);
            yield break;
        }

        editChildPopup.SetActive(false);
        SceneManager.LoadScene("Children"); // Refresh view
    }

    void CloseInvalidNamePopup()
    {
        invalidNamePopup.SetActive(false); // Hide duplicate name warning
    }

    bool IsValidDate(int day, int month, int year)
    {
        if (month < 1 || month > 12) return false;
        int[] daysInMonth = { 31, (IsLeapYear(year) ? 29 : 28), 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
        if (day < 1 || day > daysInMonth[month - 1]) return false;
        System.DateTime currentDate = System.DateTime.Now;
        System.DateTime inputDate = new System.DateTime(year, month, day);
        return inputDate <= currentDate;
    }

    bool IsLeapYear(int year)
    {
        return (year % 4 == 0 && year % 100 != 0) || (year % 400 == 0);
    }

    public void CheckAddChildEligibility()
    {
        StartCoroutine(CheckChildCount()); // Check limit before adding child
    }

    IEnumerator CheckChildCount()
    {
        DatabaseReference dbRef = database.RootReference.Child("parents").Child(parentId).Child("children");
        var dbTask = dbRef.GetValueAsync();
        yield return new WaitUntil(() => dbTask.IsCompleted);

        DataSnapshot snapshot = dbTask.Result;
        int childCount = 0;

        for (int i = 0; i < 5; i++)
        {
            if (snapshot.Child(i.ToString()).HasChild("name") && snapshot.Child(i.ToString()).Child("name").Value.ToString() != "null")
            {
                childCount++;
            }
        }

        if (childCount >= 5)
        {
            addChildPopup.SetActive(true); // Show limit reached popup
        }
        else
        {
            SceneManager.LoadScene("AddYourChild"); // Navigate to add screen
        }
    }

    IEnumerator SelectChild(int index)
    {
        // Save selected child and unselect others
        DatabaseReference dbRef = database.RootReference.Child("parents").Child(parentId).Child("children");
        var dbTask = dbRef.GetValueAsync();
        yield return new WaitUntil(() => dbTask.IsCompleted);

        DataSnapshot snapshot = dbTask.Result;

        if (snapshot.Child(index.ToString()).Exists)
        {
            string childName = snapshot.Child(index.ToString()).Child("name").Value.ToString();
            string childImage = snapshot.Child(index.ToString()).Child("image").Value.ToString();

            PlayerPrefs.SetString("SelectedChildName", childName);
            PlayerPrefs.SetString("SelectedChildImage", childImage);

            PlayerPrefs.SetString("SelectedChildKey", index.ToString());
            PlayerPrefs.Save();

            var updateTask1 = dbRef.Child(index.ToString()).Child("selected").SetValueAsync(true);
            yield return new WaitUntil(() => updateTask1.IsCompleted);

            for (int i = 0; i < 5; i++)
            {
                if (i != index)
                {
                    var updateTask2 = dbRef.Child(i.ToString()).Child("selected").SetValueAsync(false);
                    yield return new WaitUntil(() => updateTask2.IsCompleted);
                }
            }

            SceneManager.LoadScene("HomePage"); // Proceed to home
        }
    }

}
