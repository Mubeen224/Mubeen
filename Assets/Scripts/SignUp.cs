using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using Firebase;
using Firebase.Auth;
using Firebase.Database;
using TMPro;
using UnityEngine.SceneManagement;

public class SignUp : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_InputField emailInput;              // Email input field
    public TMP_InputField passwordInput;           // Password input field
    public TMP_InputField confirmPasswordInput;    // Confirm password field
    public Button signUpButton;                    // Sign-Up button
    public TextMeshProUGUI statusText;             // Status message for feedback
    public GameObject accountDeletedPopUp;         // Popup window of deleted acc

    [Header("Password Visibility Toggle")]
    public Button passwordToggleBtn;               // Toggle password visibility
    public Image passwordToggleImage;              // Eye icon
    public Button confirmPasswordToggleBtn;        // Toggle confirm password visibility
    public Image confirmPasswordToggleImage;       // Eye icon for confirm password
    public Sprite showSprite;                      // Open eye icon
    public Sprite hideSprite;                      // Closed eye icon

    private FirebaseAuth auth;                     // Firebase Authentication instance
    private FirebaseDatabase database;             // Firebase Realtime Database instance
    private bool isPasswordVisible = false;        // Track password visibility
    private bool isConfirmPasswordVisible = false; // Track confirm password visibility

    /////////////////////////////////////////////////////////////////////////////////////////////////////

    void Start()
    {
        auth = FirebaseAuth.DefaultInstance;
        database = FirebaseDatabase.DefaultInstance;

        // Add listener to sign-up button
        signUpButton.onClick.AddListener(() =>
        {
            ClearStatusMessage(); // Clear previous messages when button is pressed
            StartCoroutine(RegisterUser());
        });

        // Password visibility toggles
        passwordToggleBtn.onClick.AddListener(TogglePasswordVisibility);
        confirmPasswordToggleBtn.onClick.AddListener(ToggleConfirmPasswordVisibility);

        // Hide status text initially
        statusText.gameObject.SetActive(false);

        // Hide deletion pop up initially
        accountDeletedPopUp.SetActive(false);
    }

    /////////////////////////////////////////////////////////////////////////////////////////////////////

    IEnumerator RegisterUser()
    {
        string email = emailInput.text.Trim();
        string password = passwordInput.text;
        string confirmPassword = confirmPasswordInput.text;

        // Validate inputs
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(confirmPassword))
        {
            ShowStatusMessage("!ﺔﺑﻮﻠﻄﻤﻟﺍ ﺕﺎﻧﺎﻴﺒﻟﺍ ﻊﻴﻤﺟ ﻝﺎﺧﺩﺇ ﻰﺟﺮﻳ", new Color(0.725f, 0f, 0f)); // All fields are required msg
            yield break;
        }

        if (password != confirmPassword)
        {
            ShowStatusMessage("ﻦﻴﺘﻘﺑﺎﻄﺘﻣ ﺮﻴﻏ ﺭﻭﺮﻤﻟﺍ ﺎﺘﻤﻠﻛ", new Color(0.725f, 0f, 0f)); // Passwords are mismatched msg
            passwordInput.text = "";
            confirmPasswordInput.text = "";
            yield break;
        }

        if (!IsValidPassword(password))
        {
            ShowStatusMessage("ﻁﻭﺮﺸﻟﺍ ﻲﻓﻮﺘﺴﺗ ﻻ ﺭﻭﺮﻤﻟﺍ ﺔﻤﻠﻛ", new Color(0.725f, 0f, 0f)); // Invalid password msg
            passwordInput.text = "";
            confirmPasswordInput.text = "";
            yield break;
        }

        // Firebase user registration
        var signUpTask = auth.CreateUserWithEmailAndPasswordAsync(email, password);
        yield return new WaitUntil(() => signUpTask.IsCompleted);

        if (signUpTask.Exception != null)
        {
            HandleFirebaseAuthError(signUpTask.Exception);
            yield break;
        }

        FirebaseUser newUser = signUpTask.Result.User;

        // Send email verification
        var verificationTask = newUser.SendEmailVerificationAsync();
        yield return new WaitUntil(() => verificationTask.IsCompleted);

        if (verificationTask.Exception != null)
        {
            ShowStatusMessage("ﻖﻘﺤﺘﻟﺍ ﺪﻳﺮﺑ ﻝﺎﺳﺭﺇ ﻞﺸﻓ", new Color(0.725f, 0f, 0f)); // Error sending verification link msg
            yield break;
        }

        ShowStatusMessage("ﻙﺪﻳﺮﺑ ﻰﻟﺇ ﻖﻘﺤﺘﻟﺍ ﻂﺑﺍﺭ ﻝﺎﺳﺭﺇ ﻢﺗ\r\nﻪﻨﻣ ﻖﻘﺤﺘﻟﺍ ﻰﺟﺮﻳ", new Color(0.725f, 0f, 0f)); // Verification email sent msg

        // Wait for user to verify email
        yield return StartCoroutine(WaitForEmailVerification(newUser, password));
    }

    /////////////////////////////////////////////////////////////////////////////////////////////////////

    IEnumerator WaitForEmailVerification(FirebaseUser user, string password)
    {
        float elapsedTime = 0f;
        float maxWaitTime = 60f; // Maximum seconds before deleting unverified user (60 seconds)

        while (!user.IsEmailVerified && elapsedTime < maxWaitTime)
        {
            yield return new WaitForSeconds(2); // Check every 2 seconds
            elapsedTime += 2;

            var reloadTask = user.ReloadAsync();
            yield return new WaitUntil(() => reloadTask.IsCompleted);

            if (reloadTask.Exception != null)
            {
                ShowStatusMessage("!ﻖﻘﺤﺘﻟﺍ ﺀﺎﻨﺛﺃ ﺄﻄﺧ ﺙﺪﺣ", new Color(0.725f, 0f, 0f)); // Error msg
                yield break;
            }
        }

        if (user.IsEmailVerified)
        {
            // Store user data in Realtime Database
            StartCoroutine(SaveUserData(user, password));

            // Load children page after successful registration
            SceneManager.LoadScene("AddYourChild");
        }
        else
        {
            // Show the deletion message in the popup
            accountDeletedPopUp.SetActive(true);
            emailInput.text = "";
            passwordInput.text = "";
            confirmPasswordInput.text = "";
            statusText.text = "";

            // Delete the unverified user from Firebase Authentication
            var deleteTask = user.DeleteAsync();
            yield return new WaitUntil(() => deleteTask.IsCompleted);

            if (deleteTask.Exception != null)
            {
                Debug.LogError("Failed to delete unverified user: " + deleteTask.Exception);
            }
        }
    }

    /////////////////////////////////////////////////////////////////////////////////////////////////////

    IEnumerator SaveUserData(FirebaseUser user, string password)
    {
        DatabaseReference dbReference = database.RootReference;
        string parentKey = user.UserId;

        // Hash the password
        string hashedPassword = HashPassword(password);

        // Create an array of 5 children
        Dictionary<string, object>[] childrenArray = new Dictionary<string, object>[5];
        for (int i = 0; i < 5; i++)
        {
            childrenArray[i] = new Dictionary<string, object>
        {
            { "name", null },
            { "day", null },
            { "month", null },
            { "year", null },
            { "image", null },
            { "keys", null },
            { "coins", null },
            { "gender", false },  // Default to false (false = male, true = female)
            { "selected", false }, // Default to false (not selected)
            { "background", null } // attribute for the child's background image
        };
        }

        Dictionary<string, object> parentData = new Dictionary<string, object>
    {
        { "email", user.Email },
        { "password", hashedPassword }, // Store hashed password
        { "children", childrenArray } // Store children array
    };

        var dbTask = dbReference.Child("parents").Child(parentKey).SetValueAsync(parentData);
        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            ShowStatusMessage("ﻡﺪﺨﺘﺴﻤﻟﺍ ﺕﺎﻧﺎﻴﺑ ﻆﻔﺣ ﻲﻓ ﻞﺸﻓ", new Color(0.725f, 0f, 0f)); // Error storing user data msg
        }
        else
        {
            Debug.Log("User info has been stored in the database");
        }
    }


    /////////////////////////////////////////////////////////////////////////////////////////////////////

    // Hashing function using SHA256
    private string HashPassword(string password)
    {
        using (System.Security.Cryptography.SHA256 sha256 = System.Security.Cryptography.SHA256.Create())
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(password);
            byte[] hash = sha256.ComputeHash(bytes);
            return System.BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
    }

    /////////////////////////////////////////////////////////////////////////////////////////////////////

    // Handle Firebase authentication errors
    private void HandleFirebaseAuthError(System.AggregateException exception)
    {
        FirebaseException firebaseEx = exception.GetBaseException() as FirebaseException;
        AuthError errorCode = firebaseEx != null ? (AuthError)firebaseEx.ErrorCode : AuthError.None;

        string message = "ﻞﻴﺠﺴﺘﻟﺍ ﺀﺎﻨﺛﺃ ﺄﻄﺧ ﺙﺪﺣ";

        switch (errorCode)
        {
            case AuthError.EmailAlreadyInUse:
                message = "!ﻞﻌﻔﻟﺎﺑ ﻡﺪﺨﺘﺴﻣ ﻲﻧﻭﺮﺘﻜﻟﻹﺍ ﺪﻳﺮﺒﻟﺍ"; // Email already in use msg
                break;
            case AuthError.InvalidEmail:
                message = "!ﺢﻟﺎﺻ ﺮﻴﻏ ﻲﻧﻭﺮﺘﻜﻟﻹﺍ ﺪﻳﺮﺒﻟﺍ"; // Invalid email msg
                break;
            case AuthError.WeakPassword:
                message = "!ﺔﻔﻴﻌﺿ ﺭﻭﺮﻤﻟﺍ ﺔﻤﻠﻛ"; // Weak password msg
                break;
            default:
                message = ":ﻑﻭﺮﻌﻣ ﺮﻴﻏ ﺄﻄﺧ " + firebaseEx?.Message; // UNkown error msg
                break;
        }

        ShowStatusMessage(message, new Color(0.725f, 0f, 0f));
    }

    /////////////////////////////////////////////////////////////////////////////////////////////////////

    // Validate password strength
    private static bool IsValidPassword(string password)
    {
        string pattern = @"^(?=.*[A-Z])(?=.*[a-z])(?=.*\d)(?=.*[!?@#$%&\-+=]).{8,}$";
        return Regex.IsMatch(password, pattern);
    }

    /////////////////////////////////////////////////////////////////////////////////////////////////////

    // Show status messages
    private void ShowStatusMessage(string message, Color color)
    {
        statusText.gameObject.SetActive(true);
        statusText.text = message;
        statusText.color = color;
    }

    // Clear status message when sign-up button is pressed
    private void ClearStatusMessage()
    {
        statusText.gameObject.SetActive(false);
    }

    /////////////////////////////////////////////////////////////////////////////////////////////////////

    // Toggle visibility of password
    private void TogglePasswordVisibility()
    {
        isPasswordVisible = !isPasswordVisible;
        passwordInput.contentType = isPasswordVisible ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
        passwordToggleImage.sprite = isPasswordVisible ? hideSprite : showSprite;
        passwordInput.ForceLabelUpdate();
    }

    // Toggle visibility of confirm password
    private void ToggleConfirmPasswordVisibility()
    {
        isConfirmPasswordVisible = !isConfirmPasswordVisible;
        confirmPasswordInput.contentType = isConfirmPasswordVisible ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
        confirmPasswordToggleImage.sprite = isConfirmPasswordVisible ? hideSprite : showSprite;
        confirmPasswordInput.ForceLabelUpdate();
    }
}