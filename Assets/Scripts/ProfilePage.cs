using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Firebase.Database;
using Firebase.Auth;
using System.Collections;

public class ProfileScript : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_Text childNameText;       // Display for child name
    public TMP_Text childAgeText;        // Display for child age
    public TMP_Text childGenderText;     // Display for child gender
    public TMP_Text coinText;            // Display for coin count
    public TMP_Text starText;            // Display for star count

    [Header("Profile Images")]
    public Image childProfileImage;      // Display for child's profile picture
    public Image genderIconImage;        // Display for gender icon

    [Header("Badges Elements")]
    public BadgeUI[] badges;             // Array of badge UI elements
    public Sprite badgeSprite;           // Sprite to use for earned badges

    // List of letters that have badge states (must match the badge UI order)
    private string[] badgeLetters = new string[] { "خ", "ذ", "ر", "س", "ش", "ص", "ض", "ظ", "غ" };

    private string parentId;
    private string selectedChildKey;

    [System.Serializable]
    public class BadgeUI
    {
        public GameObject badgeObject;   // The entire badge UI object
        public Image badgeImage;         // The image part of the badge
        public TMP_Text badgeText;       // The label (e.g., "Champion of letter خ")
    }

    void Start()
    {
        // Get current Firebase authenticated user
        FirebaseUser user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user != null)
        {
            parentId = user.UserId;
            StartCoroutine(LoadSelectedChildProfile()); // Start loading selected child's data
        }
    }

    IEnumerator LoadSelectedChildProfile()
    {
        // Get reference to the current parent's children in the database
        DatabaseReference dbRef = FirebaseDatabase.DefaultInstance.RootReference
            .Child("parents").Child(parentId).Child("children");

        var dbTask = dbRef.GetValueAsync();
        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError("Failed to load children: " + dbTask.Exception);
            yield break;
        }

        DataSnapshot snapshot = dbTask.Result;

        // Loop through children to find the selected one
        foreach (var child in snapshot.Children)
        {
            if (child.HasChild("selected") && child.Child("selected").Value != null
                && child.Child("selected").Value.ToString().ToLower() == "true")
            {
                selectedChildKey = child.Key;

                // Load and display child name
                string rawName = child.Child("name").Value != null ? child.Child("name").Value.ToString() : "";
                SetArabicText(childNameText, rawName);

                // Load and calculate age
                int year = child.HasChild("year") ? int.Parse(child.Child("year").Value.ToString()) : 0;
                int month = child.HasChild("month") ? int.Parse(child.Child("month").Value.ToString()) : 1;
                int day = child.HasChild("day") ? int.Parse(child.Child("day").Value.ToString()) : 1;
                int age = CalculateAge(day, month, year);
                SetArabicText(childAgeText, ToArabicNumbers(age));

                // Load gender info
                bool isGirl = child.HasChild("gender") && child.Child("gender").Value.ToString().ToLower() == "true";
                SetArabicText(childGenderText, isGirl ? "أنثى" : "ذكر");

                // Load gender icon
                if (genderIconImage != null)
                {
                    string genderIconPath = isGirl ? "Sprites/Icons/female_icon" : "Sprites/Icons/male_icon";
                    Sprite genderIconSprite = Resources.Load<Sprite>(genderIconPath);
                    if (genderIconSprite != null)
                        genderIconImage.sprite = genderIconSprite;
                }

                // Load child profile image
                if (child.HasChild("image") && child.Child("image").Value != null && childProfileImage != null)
                {
                    string imagePath = child.Child("image").Value.ToString();
                    Sprite childSprite = Resources.Load<Sprite>(imagePath);
                    if (childSprite != null)
                        childProfileImage.sprite = childSprite;
                }

                // Load and display coins
                int coins = child.HasChild("coins") ? int.Parse(child.Child("coins").Value.ToString()) : 0;
                SetArabicText(coinText, ToArabicNumbers(coins));

                // Load and display stars
                int stars = child.HasChild("stars") ? int.Parse(child.Child("stars").Value.ToString()) : 0;
                SetArabicText(starText, ToArabicNumbers(stars));

                // Display earned badges
                if (badges != null && badges.Length == badgeLetters.Length)
                {
                    // Hide all badges initially
                    for (int i = 0; i < badges.Length; i++)
                        badges[i].badgeObject.SetActive(false);

                    // Check badge status per letter
                    if (child.HasChild("letters"))
                    {
                        var lettersSnapshot = child.Child("letters");
                        for (int i = 0; i < badgeLetters.Length; i++)
                        {
                            string letter = badgeLetters[i];
                            if (lettersSnapshot.HasChild(letter) && lettersSnapshot.Child(letter).HasChild("badge"))
                            {
                                bool gotBadge = lettersSnapshot.Child(letter).Child("badge").Value.ToString().ToLower() == "true";
                                if (gotBadge)
                                {
                                    badges[i].badgeObject.SetActive(true);
                                    if (badges[i].badgeImage != null && badgeSprite != null)
                                        badges[i].badgeImage.sprite = badgeSprite;
                                    if (badges[i].badgeText != null)
                                        badges[i].badgeText.text = ArabicSupport.ArabicFixer.Fix("بطل حرف " + letter);
                                }
                            }
                        }
                    }
                }

                yield break; // Exit once selected child is found and processed
            }
        }
    }

    // Utility: Fixes Arabic text rendering
    void SetArabicText(TMP_Text textField, string text)
    {
        textField.text = ArabicSupport.ArabicFixer.Fix(text);
    }

    // Utility: Converts integer to Arabic numeral characters
    string ToArabicNumbers(int number)
    {
        string[] arabicDigits = { "٠", "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩" };
        string numStr = number.ToString();
        string result = "";
        foreach (char c in numStr)
        {
            if (char.IsDigit(c))
                result += arabicDigits[(int)char.GetNumericValue(c)];
            else
                result += c;
        }
        return result;
    }

    // Utility: Calculates age based on birthdate
    int CalculateAge(int day, int month, int year)
    {
        System.DateTime today = System.DateTime.Today;
        System.DateTime birthDate = new System.DateTime(year, month, day);
        int age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age)) age--;
        return age;
    }
}
