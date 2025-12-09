using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase;
using Firebase.Auth;
using Firebase.Database;
using UnityEngine.SceneManagement;
using ArabicSupport;   // مهم لــ ArabicFixer

public class AddYourChildscript : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_InputField nameInput;   // حقل كتابة اسم الطفل (TMP)
    public TMP_Text nameDisplay;       // نص للعرض بعد التصحيح بالعربية
    public Toggle boyToggle;           // Toggle to select boy
    public Toggle girlToggle;          // Toggle to select girl
    public TMP_InputField dayInput;    // Input field for birth day
    public TMP_InputField monthInput;  // Input field for birth month
    public TMP_InputField yearInput;   // Input field for birth year
    public Button addChildButton;      // Button to trigger add child
    public Image characterImage;       // Displays selected character image

    [Header("Popup Elements")]
    public GameObject popupMessage;    // Popup panel for errors
    public TMP_Text popupText;         // Text field for popup messages
    public Button closePopupButton;    // Button to close the popup

    private FirebaseAuth auth;
    private FirebaseDatabase database;
    private string parentId;

    // Define a struct to hold character attributes
    [System.Serializable]
    public struct Character
    {
        public string name;
        public float price;
        public bool purchased;
        public bool selected;
        public bool displayed;

        public Character(string name, float price, bool purchased, bool selected, bool displayed)
        {
            this.name = name;
            this.price = price;
            this.purchased = purchased;
            this.selected = selected;
            this.displayed = displayed;
        }
    }

    void Start()
    {
        auth = FirebaseAuth.DefaultInstance;
        database = FirebaseDatabase.DefaultInstance;

        // Get current logged-in user
        if (auth.CurrentUser != null)
        {
            parentId = auth.CurrentUser.UserId;
        }
        else
        {
            Debug.LogError("No authenticated user found.");
            return;
        }

        // ربط تغيير الاسم مع نص العرض العربي
        if (nameInput != null)
        {
            nameInput.onValueChanged.AddListener(OnNameChanged);
            OnNameChanged(nameInput.text); // تحديث أولي
        }

        // Assign button actions
        addChildButton.onClick.AddListener(ValidateAndAddChild);
        closePopupButton.onClick.AddListener(ClosePopup);
        popupMessage.SetActive(false);

        // Update image when gender changes
        boyToggle.onValueChanged.AddListener(delegate { UpdateCharacterImage(); });
        girlToggle.onValueChanged.AddListener(delegate { UpdateCharacterImage(); });

        UpdateCharacterImage(); // Load default character image
    }

    private void OnDestroy()
    {
        if (nameInput != null)
            nameInput.onValueChanged.RemoveListener(OnNameChanged);
    }

    // كل ما المستخدم يكتب في حقل الاسم، نحدّث نص العرض العربي
    void OnNameChanged(string value)
    {
        if (nameDisplay != null)
        {
            // نربط الحروف العربية للعرض فقط
            nameDisplay.text = ArabicFixer.Fix(value, showTashkeel: false, useHinduNumbers: true);
        }
    }

    void UpdateCharacterImage()
    {
        // Choose image path based on gender
        bool isGirl = girlToggle.isOn;
        string imagePath = isGirl ? "Sprites/Demo_Character/Character_Sample04" : "Sprites/Demo_Character/Character_Sample01_1";

        Sprite newSprite = Resources.Load<Sprite>(imagePath);
        if (newSprite != null)
        {
            characterImage.sprite = newSprite;
        }
        else
        {
            Debug.LogError("Character image not found at path: " + imagePath);
        }
    }

    void ValidateAndAddChild()
    {
        // Read and trim input values
        string childName = nameInput.text.Trim();   // نحفظ النص الخام كما هو
        string dayText = dayInput.text.Trim();
        string monthText = monthInput.text.Trim();
        string yearText = yearInput.text.Trim();

        // Check if all required fields are filled
        if (string.IsNullOrEmpty(childName) || string.IsNullOrEmpty(dayText) ||
            string.IsNullOrEmpty(monthText) || string.IsNullOrEmpty(yearText) ||
            (!boyToggle.isOn && !girlToggle.isOn))
        {
            ShowPopup("ﻝﻮﻘﺤﻟﺍ ﻊﻴﻤﺟ ﺔﺌﺒﻌﺗ ﻰﺟﺮﻳ");
            return;
        }

        // Parse date input into integers
        bool isDayNum = int.TryParse(dayText, out int day);
        bool isMonthNum = int.TryParse(monthText, out int month);
        bool isYearNum = int.TryParse(yearText, out int year);

        if (!isDayNum)
        {
            ShowPopup("ﺢﻴﺤﺻ ﻲﻤﻗﺭ ﻞﻜﺸﺑ ﻡﻮﻴﻟﺍ ﻝﺎﺧﺩﺇ ﻰﺟﺮﻳ");
            return;
        }
        if (!isMonthNum)
        {
            ShowPopup("ﺢﻴﺤﺻ ﻲﻤﻗﺭ ﻞﻜﺸﺑ ﺮﻬﺸﻟﺍ ﻝﺎﺧﺩﺇ ﻰﺟﺮﻳ");
            return;
        }
        if (!isYearNum)
        {
            ShowPopup("ﺢﻴﺤﺻ ﻲﻤﻗﺭ ﻞﻜﺸﺑ ﺔﻨﺴﻟﺍ ﻝﺎﺧﺩﺇ ﻰﺟﺮﻳ");
            return;
        }

        // Validate logical ranges
        if (month < 1 || month > 12)
        {
            ShowPopup("ﺢﻟﺎﺻ ﺮﻴﻏ ﺮﻬﺸﻟﺍ");
            return;
        }

        int[] daysInMonth = { 31, (IsLeapYear(year) ? 29 : 28), 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
        if (day < 1 || day > daysInMonth[month - 1])
        {
            ShowPopup("ﺢﻴﺤﺻ ﺮﻴﻏ ﻡﻮﻴﻟﺍ ﻢﻗﺭ");
            return;
        }

        if (year < 1900 || year > System.DateTime.Now.Year)
        {
            ShowPopup("ﺔﺤﻴﺤﺻ ﺮﻴﻏ ﺔﻨﺴﻟﺍ");
            return;
        }

        // Validate full date
        System.DateTime currentDate = System.DateTime.Now;
        System.DateTime inputDate;
        try
        {
            inputDate = new System.DateTime(year, month, day);
        }
        catch
        {
            ShowPopup("ﺢﻟﺎﺻ ﺮﻴﻏ ﺩﻼﻴﻤﻟﺍ ﺦﻳﺭﺎﺗ");
            return;
        }

        // Check if date is in the future
        if (inputDate > currentDate)
        {
            ShowPopup("ﻞﺒﻘﺘﺴﻤﻟﺍ ﻲﻓ ﻥﻮﻜﻳ ﻥﺃ ﻦﻜﻤﻳ ﻻ ﺩﻼﻴﻤﻟﺍ ﺦﻳﺭﺎﺗ");
            return;
        }

        // Check age range (4 to 7 years old)
        int age = CalculateAge(day, month, year);
        if (age < 4 || age > 7)
        {
            ShowPopup("ﺕﺍﻮﻨﺳ ٧ ﻭ ٤ ﻦﻴﺑ ﻞﻔﻄﻟﺍ ﺮﻤﻋ ﻥﻮﻜﻳ ﻥﺃ ﺐﺠﻳ");
            return;
        }

        // Prepare data for Firebase
        bool isGirl = girlToggle.isOn;
        string imagePath = isGirl ? "Sprites/Demo_Character/Character_Sample04" : "Sprites/Demo_Character/Character_Sample01_1";
        string backgroundPath = isGirl ? "Sprites/Demo_Character/Frame_Square00_Pink" : "Sprites/Demo_Character/Frame_Square00_Sky";

        StartCoroutine(AddChildToDatabase(childName, day, month, year, isGirl, imagePath, backgroundPath));
    }

    IEnumerator AddChildToDatabase(string name, int day, int month, int year, bool isGirl, string imagePath, string backgroundPath)
    {
        // Get the parent’s child node in Firebase
        DatabaseReference dbRef = database.RootReference.Child("parents").Child(parentId).Child("children");

        var dbTask = dbRef.GetValueAsync();
        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            ShowPopup("حدث خطأ أثناء جلب البيانات.");
            yield break;
        }

        DataSnapshot snapshot = dbTask.Result;

        // Prevent duplicate names
        if (snapshot.Exists)
        {
            foreach (DataSnapshot childSnapshot in snapshot.Children)
            {
                if (childSnapshot.HasChild("name") && childSnapshot.Child("name").Value.ToString() == name)
                {
                    ShowPopup("ﻢﺳﻻﺍ ﺲﻔﻨﺑ ﻞﻔﻃ ﻦﻣ ﺮﺜﻛﺃ ﺔﻴﻤﺴﺗ ﻦﻜﻤﻳ ﻻ");
                    yield break;
                }
            }
        }

        // Find first empty slot (max 5 children)
        int index = -1;
        for (int i = 0; i < 5; i++)
        {
            if (!snapshot.Child(i.ToString()).HasChild("name") || snapshot.Child(i.ToString()).Child("name").Value.ToString() == "null")
            {
                index = i;
                break;
            }
        }

        if (index == -1)
        {
            ShowPopup("ﻝﺎﻔﻃﻷﺍ ﺩﺪﻌﻟ ﻰﻠﻋﻷﺍ ﺪﺤﻟﺍ ﻰﻟﺇ ﻝﻮﺻﻮﻟﺍ ﻢﺗ");
            yield break;
        }

        // Initialize characters array
        List<Character> characters = new List<Character>(4);
        characters.Add(new Character("Archer", 0f, true, true, true)); // Default selected and purchased
        characters.Add(new Character("Knight", 55f, false, false, false));
        characters.Add(new Character("Rogue", 55f, false, false, false));
        characters.Add(new Character("Wizard", 55f, false, false, false));

        // Convert the list of Character structs to a list of dictionaries for Firebase
        List<Dictionary<string, object>> charactersForFirebase = new List<Dictionary<string, object>>();
        foreach (Character character in characters)
        {
            charactersForFirebase.Add(new Dictionary<string, object>
            {
                { "name", character.name },
                { "price", character.price },
                { "purchased", character.purchased },
                { "selected", character.selected },
                { "displayed", character.displayed }
            });
        }

        // Prepare child data dictionary
        Dictionary<string, object> childData = new Dictionary<string, object>
        {
            { "name", name },
            { "day", day },
            { "month", month },
            { "year", year },
            { "image", imagePath },
            { "background", backgroundPath },
            { "gender", isGirl },
            { "selected", true },
            { "coins", 0 },
            { "stars", 0 },
            { "letters", new Dictionary<string, object>() }, // Initialize empty letters progress
            { "characters", charactersForFirebase } // Add the characters array
        };

        // Add child to Firebase at available index
        var addChildTask = dbRef.Child(index.ToString()).SetValueAsync(childData);
        yield return new WaitUntil(() => addChildTask.IsCompleted);

        if (addChildTask.Exception != null)
        {
            ShowPopup("فشل في إضافة الطفل. حاول مرة أخرى.");
            yield break;
        }

        // Load next scene (Children screen)
        SceneManager.LoadScene("Children");
    }

    void ShowPopup(string message)
    {
        popupMessage.SetActive(true);
        popupText.text = message;
    }

    void ClosePopup()
    {
        popupMessage.SetActive(false);
    }

    bool IsLeapYear(int year)
    {
        return (year % 4 == 0 && year % 100 != 0) || (year % 400 == 0); // Leap year logic
    }

    // Calculate the age from birth date
    int CalculateAge(int day, int month, int year)
    {
        System.DateTime today = System.DateTime.Today;
        System.DateTime birthDate = new System.DateTime(year, month, day);
        int age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age)) age--; // Adjust if birthday hasn't occurred yet this year
        return age;
    }
}
