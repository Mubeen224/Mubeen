using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Firebase;
using Firebase.Auth;
using TMPro;
using UnityEngine.SceneManagement;

public class Login : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_InputField emailInput;      // User email input field
    public TMP_InputField passwordInput;   // User password input field
    public Button loginButton;             // Login button
    public Button signUpButton;            // Sign-up navigation button
    public TextMeshProUGUI errorText;      // Error message display
    public GameObject passwordWarningPopup; // Popup for weak password warning
    public Button resetPasswordButton;      // Reset password button inside the popup

    [Header("Panels")]
    public GameObject loginPanel;          // Login panel
    public GameObject signUpPanel;         // Sign-up panel

    private FirebaseAuth auth;             // Firebase Authentication instance



    [Header("Password Visibility Toggle")]
    public Button passwordToggleBtn;        // Button that toggles password visibility
    public Image passwordToggleImage;       // Eye icon image
    public Sprite showSprite;               // Icon for "show password"
    public Sprite hideSprite;               // Icon for "hide password"
    private bool isPasswordVisible = false; // Tracks whether the password is visible





    void Start()
    {
        // Get Firebase Auth reference
        auth = FirebaseAuth.DefaultInstance;

        // Assign button click events
        loginButton.onClick.AddListener(() => StartCoroutine(LoginUser()));
        signUpButton.onClick.AddListener(SwitchToSignUp);

        if (resetPasswordButton != null)
        {
            resetPasswordButton.onClick.AddListener(RedirectToResetPass);
        }

        // Hide error messages and popups initially
        errorText.gameObject.SetActive(false);

        if (passwordWarningPopup != null)
        {
            passwordWarningPopup.SetActive(false);
        }



        // Add listener for the password visibility toggle button
if (passwordToggleBtn != null)
{
    passwordToggleBtn.onClick.AddListener(TogglePasswordVisibility);
}

    }

    IEnumerator LoginUser()
    {
        string email = emailInput.text.Trim();
        string password = passwordInput.text;

        // Check if fields are empty
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            ShowErrorMessage("ﻝﻮﻘﺤﻟﺍ ﻊﻴﻤﺟ ﺀﻞﻣ ﻰﺟﺮﻳ");
            yield break; // Stop execution
        }

        // Attempt Firebase authentication
        var loginTask = auth.SignInWithEmailAndPasswordAsync(email, password);
        yield return new WaitUntil(() => loginTask.IsCompleted);

        if (loginTask.Exception != null)
        {
            // Display error message if login fails
            ShowErrorMessage("ﺭﻭﺮﻤﻟﺍ ﺔﻤﻠﻛ ﻭﺃ ﻲﻧﻭﺮﺘﻜﻟﻹﺍ ﺪﻳﺮﺒﻟﺍ ﻲﻓ ﺄﻄﺧ");
            yield break; // Stop execution
        }
        else
        {
            Debug.Log("Login successful, checking password requirements...");

            // 🔹 If the password does not meet the requirements, show the popup
            if (!IsValidPassword(password))
            {
                Debug.Log("Password does not meet the requirements! Showing warning popup.");

                // Hide the login panel before displaying the popup
                if (loginPanel != null)
                {
                    loginPanel.SetActive(false);
                }

                // Ensure the popup exists before enabling it
                if (passwordWarningPopup != null)
                {
                    passwordWarningPopup.SetActive(true);
                    Debug.Log("✅ Warning popup activated successfully!");
                }
                else
                {
                    Debug.LogError("❌ `passwordWarningPopup` is not assigned in the Inspector!");
                }

                yield break; // Stop execution until password is fixed
            }
            else
            {
                Debug.Log("✅ Strong password! Redirecting to HomePage.");
                SceneManager.LoadScene("HomePage");
            }
        }
    }

    // Display error messages when needed
    void ShowErrorMessage(string message)
    {
        errorText.gameObject.SetActive(true);
        errorText.text = message;
    }

    // Switch to the sign-up panel
    void SwitchToSignUp()
    {
        loginPanel.SetActive(false);
        signUpPanel.SetActive(true);
    }

    // Validate password strength based on the required criteria
    bool IsValidPassword(string password)
    {
        bool hasNumber = password.Any(char.IsDigit);
        bool hasUpperCase = password.Any(char.IsUpper);
        bool hasLowerCase = password.Any(char.IsLower);
        bool hasSpecialChar = password.Any(ch => "+=-&%$#@?!".Contains(ch));
        bool isValidLength = password.Length >= 8;

        // Debugging password validation
        Debug.Log($"🔍 Password check: Length = {isValidLength}, Number = {hasNumber}, Uppercase = {hasUpperCase}, Lowercase = {hasLowerCase}, Special Char = {hasSpecialChar}");

        return hasNumber && hasUpperCase && hasLowerCase && hasSpecialChar && isValidLength;
    }

    // Redirect the user to the ResetPass scene
    public void RedirectToResetPass()
    {
        Debug.Log("🔄 Redirecting user to ResetPasswordWithConditions scene...");
        SceneManager.LoadScene("ResetPasswordWithConditions");
    }






// Toggle visibility of the password input field
private void TogglePasswordVisibility()
{
    // Switch between visible and hidden states
    isPasswordVisible = !isPasswordVisible;

    // Change input field content type (Standard = visible, Password = hidden)
    passwordInput.contentType =
        isPasswordVisible ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;

    // Update the icon based on the state
    passwordToggleImage.sprite =
        isPasswordVisible ? hideSprite : showSprite;

    // Refresh the text display to apply the change
    passwordInput.ForceLabelUpdate();
}











}