
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Firebase;
using Firebase.Auth;
using TMPro;
using UnityEngine.SceneManagement;

public class PasswordWithConditions : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_InputField newPasswordInput;   // User's new password input field
    public TMP_InputField confirmPasswordInput; // User's confirm password input field
    public Button changePasswordButton;      // Button to change password
    public TextMeshProUGUI statusText;       // UI text to display status messages

    [Header("Panels")]
    public GameObject ResetPasswordWithConditions;  // The main reset password panel
    public GameObject AlertResetPass; //  Success popup panel before redirection

    [Header("Toggle Password Visibility")]
    public Button newPasswordToggleBtn;  // Button to toggle new password visibility
    public Button confirmPasswordToggleBtn; // Button to toggle confirm password visibility
    public Image newPasswordToggleImage;  // Eye icon for new password field
    public Image confirmPasswordToggleImage; // Eye icon for confirm password field
    public Sprite showSprite;  // Eye open icon
    public Sprite hideSprite;  // Eye closed icon

    private FirebaseAuth auth;               // Firebase Authentication instance
    private FirebaseUser user;               // Current authenticated user
    private bool isNewPasswordVisible = false;
    private bool isConfirmPasswordVisible = false;

    void Start()
    {
        // Initialize Firebase Authentication
        auth = FirebaseAuth.DefaultInstance;
        user = auth.CurrentUser;

        // Assign button click events
        changePasswordButton.onClick.AddListener(() => StartCoroutine(ChangePassword()));

        // Hide status text and success popup initially
        statusText.gameObject.SetActive(false);
        AlertResetPass.SetActive(false); // ✅ Hide success popup on start

        // ✅ Assign password toggle button functionality
        newPasswordToggleBtn.onClick.AddListener(() => TogglePasswordVisibility(ref isNewPasswordVisible, newPasswordInput, newPasswordToggleImage));
        confirmPasswordToggleBtn.onClick.AddListener(() => TogglePasswordVisibility(ref isConfirmPasswordVisible, confirmPasswordInput, confirmPasswordToggleImage));
    }

    IEnumerator ChangePassword()
    {
        string newPassword = newPasswordInput.text.Trim();
        string confirmPassword = confirmPasswordInput.text.Trim();

        // Check if fields are empty
        if (string.IsNullOrEmpty(newPassword) || string.IsNullOrEmpty(confirmPassword))
        {
            ShowStatusMessage("ﻝﻮﻘﺤﻟﺍ ﻊﻴﻤﺟ ﺀﻞﻣ ﻰﺟﺮﻳ");
            yield break;
        }

        // Validate password requirements
        if (!IsValidPassword(newPassword))
        {
            ShowStatusMessage("ﺔﺑﻮﻠﻄﻤﻟﺍ ﻁﻭﺮﺸﻟﺍ ﻊﻣ ﻖﻓﺍﻮﺘﺗ ﻻ ﺭﻭﺮﻤﻟﺍ ﺔﻤﻠﻛ");
            yield break;
        }

        // Ensure passwords match
        if (newPassword != confirmPassword)
        {
            ShowStatusMessage("ﻦﻴﺘﻘﺑﺎﻄﺘﻣ ﺮﻴﻏ ﺭﻭﺮﻤﻟﺍ ﺎﺘﻤﻠﻛ");
            yield break;
        }

        // Update password in Firebase
        var passwordTask = user.UpdatePasswordAsync(newPassword);
        yield return new WaitUntil(() => passwordTask.IsCompleted);

        if (passwordTask.Exception != null)
        {
            ShowStatusMessage("ﻯﺮﺧﺃ ﺓﺮﻣ ﺔﻟﻭﺎﺤﻤﻟﺍ ﻰﺟﺮﻳ ،ﺭﻭﺮﻤﻟﺍ ﺔﻤﻠﻛ ﺚﻳﺪﺤﺗ ﺀﺎﻨﺛﺃ ﺄﻄﺧ ﺙﺪﺣ");
        }
        else
        {
            Debug.Log("✅ Password changed successfully! Displaying success message for 5 seconds before redirecting...");

            //  Show success popup
            AlertResetPass.SetActive(true);

            //  Wait for 5 seconds before switching scene
            yield return new WaitForSeconds(4f);

            //  Redirect to login page
            SceneManager.LoadScene("Signup-Login");
        }
    }

    // Display status messages to the user
    void ShowStatusMessage(string message)
    {
        statusText.gameObject.SetActive(true);
        statusText.text = message;
    }

    //  Function to toggle password visibility
    private void TogglePasswordVisibility(ref bool isVisible, TMP_InputField inputField, Image toggleImage)
    {
        isVisible = !isVisible;
        inputField.contentType = isVisible ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
        toggleImage.sprite = isVisible ? hideSprite : showSprite;
        inputField.ForceLabelUpdate();
    }

    // Validate password strength based on security requirements
    bool IsValidPassword(string password)
    {
        bool hasNumber = password.Any(char.IsDigit);
        bool hasUpperCase = password.Any(char.IsUpper);
        bool hasLowerCase = password.Any(char.IsLower);
        bool hasSpecialChar = password.Any(ch => "+=-&%$#@?!".Contains(ch));
        bool isValidLength = password.Length >= 8;

        return hasNumber && hasUpperCase && hasLowerCase && hasSpecialChar && isValidLength;
    }
}