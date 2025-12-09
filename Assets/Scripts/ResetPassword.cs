using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Firebase;
using Firebase.Auth;
using TMPro;

public class ResetPassword : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_InputField emailInput;         // Email input field
    public Button sendResetLinkButton;       // Button to send reset link
    public Button closeButton;               // Button to close the reset panel
    public TextMeshProUGUI statusText;       // Status message display
    public GameObject resetPasswordPanel;    // Reset password panel
    public GameObject loginPanel;            // Login panel
    public Button forgotPasswordButton;      // "Forgot Password?" button
    public GameObject successPopup;          // Success popup when email is sent
    public Button confirmButton;             // "Done" button inside the success popup

    private FirebaseAuth auth;               // Firebase Authentication instance

    void Start()
    {
        // Get Firebase Auth reference
        auth = FirebaseAuth.DefaultInstance;

        // Assign button click listeners
        sendResetLinkButton.onClick.AddListener(() => StartCoroutine(SendPasswordResetEmail()));
        closeButton.onClick.AddListener(CloseResetPanel);
        forgotPasswordButton.onClick.AddListener(OpenResetPanel); // Show the panel when "Forgot Password?" is clicked
        confirmButton.onClick.AddListener(CloseSuccessPopup);  // Close success popup when "Done" is clicked

        // Hide status text, reset panel, and success popup initially
        statusText.gameObject.SetActive(false);
        resetPasswordPanel.SetActive(false);
        successPopup.SetActive(false); // Hide success popup initially
    }

    // Show the reset password panel
    void OpenResetPanel()
    {
        loginPanel.SetActive(false);  // Hide login panel
        resetPasswordPanel.SetActive(true); // Show reset password panel
    }

    // Send password reset email
    IEnumerator SendPasswordResetEmail()
    {
        string email = emailInput.text.Trim();

        // Check if the email field is empty
        if (string.IsNullOrEmpty(email))
        {
            ShowStatusMessage("ﻲﻧﻭﺮﺘﻜﻟﻹﺍ ﺪﻳﺮﺒﻟﺍ ﻝﺎﺧﺩﺇ ﻰﺟﺮﻳ");
            yield break;
        }

        // Attempt to send password reset email
        var resetTask = auth.SendPasswordResetEmailAsync(email);
        yield return new WaitUntil(() => resetTask.IsCompleted);

        if (resetTask.Exception != null)
        {
            ShowStatusMessage("ﻯﺮﺧﺃ ﺓﺮﻣ ﺔﻟﻭﺎﺤﻤﻟﺍ ﻰﺟﺮﻳ");
        }
        else
        {
            // Hide status messages and show success popup
            statusText.gameObject.SetActive(false);
            resetPasswordPanel.SetActive(false);
            successPopup.SetActive(true);  // Show success popup
        }
    }

    // Display status messages
    void ShowStatusMessage(string message)
    {
        statusText.gameObject.SetActive(true);
        statusText.text = message;
    }

    // Close success popup and return to login panel
    void CloseSuccessPopup()
    {
        successPopup.SetActive(false);
        loginPanel.SetActive(true); // Return to login panel
    }

    // Close reset password panel and return to login panel
    void CloseResetPanel()
    {
        resetPasswordPanel.SetActive(false);
        loginPanel.SetActive(true);  // Show login panel again
    }
}