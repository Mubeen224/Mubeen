using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase;
using Firebase.Database;
using Firebase.Auth;
using UnityEngine.SceneManagement;

public class manage_child_deleat: MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_InputField nameInput;
    public Toggle genderMale;
    public Toggle genderFemale;
    public TMP_InputField dayInput;
    public TMP_InputField monthInput;
    public TMP_InputField yearInput;
    public Button addChildButton;
    public Button deleteChildButton;
    public Transform childListContainer;
    public GameObject childPrefab;
    public Slider progressBar;
    public TextMeshProUGUI progressText;

    private DatabaseReference databaseReference;
    private FirebaseAuth auth;
    private string parentId;
    private int childCount = 0;
    private const int maxChildren = 4;

    public void Start()
    {
        auth = FirebaseAuth.DefaultInstance;
        databaseReference = FirebaseDatabase.DefaultInstance.RootReference;

        FirebaseUser user = auth.CurrentUser;
        if (user != null)
        {
            parentId = user.UserId;
            CheckUserStatus(); // Check if user is new or existing
        }

        addChildButton.onClick.AddListener(AddChild);
    }

    public void CheckUserStatus()
    {
        // Fetch parent data from the database
        databaseReference.Child("parents").Child(parentId).Child("children").GetValueAsync().ContinueWith(task =>
        {
            if (task.IsCompleted)
            {
                DataSnapshot snapshot = task.Result;
                childCount = (int)snapshot.ChildrenCount;
                UpdateProgressBar();

                if (childCount == 0)
                {
                    // No children → Direct to Children Scene (Empty UI)
                    SceneManager.LoadScene("Children");
                }
                else
                {
                    // Existing children → Load them & Direct to HomePage
                    foreach (DataSnapshot child in snapshot.Children)
                    {
                        string childName = child.Child("name").Value.ToString();
                        string gender = child.Child("gender").Value.ToString();
                        DisplayChild(childName, gender, child.Key);
                    }
                    SceneManager.LoadScene("HomePage");
                }
            }
        });
    }

    public void AddChild()
    {
        if (childCount >= maxChildren)
        {
            Debug.Log("Maximum number of children reached.");
            return;
        }

        string childName = nameInput.text;
        if (string.IsNullOrEmpty(childName) || string.IsNullOrEmpty(dayInput.text) ||
            string.IsNullOrEmpty(monthInput.text) || string.IsNullOrEmpty(yearInput.text))
        {
            Debug.LogError("All fields must be filled!");
            return;
        }

        string gender = genderMale.isOn ? "boy" : "girl";
        string dob = $"{dayInput.text}/{monthInput.text}/{yearInput.text}";
        string imagePath = gender == "boy" ?
            "Assets/Layer Lab/GUI-BlueSky/ResourcesData/Sprites/Demo/Demo_Character/Character_Sample01_1.png" :
            "Assets/Layer Lab/GUI-BlueSky/ResourcesData/Sprites/Demo/Demo_Character/Character_Sample04.png";

        string childId = databaseReference.Child("parents").Child(parentId).Child("children").Push().Key;
        Child newChild = new Child(childName, gender, dob, imagePath);

        databaseReference.Child("parents").Child(parentId).Child("children").Child(childId).SetRawJsonValueAsync(JsonUtility.ToJson(newChild)).ContinueWith(task =>
        {
            if (task.IsCompleted)
            {
                childCount++;
                UpdateProgressBar();
                DisplayChild(childName, gender, childId);
            }
        });
    }

    public void DisplayChild(string name, string gender, string childId)
    {
        GameObject newChildUI = Instantiate(childPrefab, childListContainer);
        newChildUI.transform.Find("name").GetComponent<TextMeshProUGUI>().text = name;
        newChildUI.transform.Find("button_delete").GetComponent<Button>().onClick.AddListener(() => DeleteChild(childId, newChildUI));
    }

    public void DeleteChild(string childId, GameObject childUI)
    {
        databaseReference.Child("parents").Child(parentId).Child("children").Child(childId).RemoveValueAsync().ContinueWith(task =>
        {
            if (task.IsCompleted)
            {
                Destroy(childUI);
                childCount--;
                UpdateProgressBar();
            }
        });
    }

    public void UpdateProgressBar()
    {
        progressBar.value = (float)childCount / maxChildren;
        progressText.text = $"{childCount}/{maxChildren}";
    }
}

[System.Serializable]
public class Child
{
    public string name;
    public string gender;
    public string dob;
    public string imagePath;
    public int coins = 0;
    public int keys = 0;

    public Child(string name, string gender, string dob, string imagePath)
    {
        this.name = name;
        this.gender = gender;
        this.dob = dob;
        this.imagePath = imagePath;
    }
}

