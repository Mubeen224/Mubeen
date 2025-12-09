using UnityEngine; 
using UnityEngine.UI; 
using TMPro; 
using Mankibo; 
using System; 
using System.Collections.Generic; 

public class MathExitManagerW3 : MonoBehaviour
{
    [Header("Math Canvas Elements")] // UI components for math challenge
    public GameObject mathCanvas; // Canvas showing the math question
    public TMP_Text equationText; // Text for displaying the equation
    public TMP_InputField answerInput; // Input field where player enters their answer
    public TMP_Text emptyFieldWarning; // Warning message if answer field is left empty
    public Button answerButton; // Button to submit the answer
    public Button wrongOkayButton; // Button shown after a wrong answer
    public Button backButton; // Button to go back to tracing
    public GameObject wrongAnswerPanel; // Panel shown if answer is incorrect

    [Header("Tracing Canvases and Scripts")] // Links to tracing canvases and logic scripts
    public List<GameObject> tracingCanvases = new List<GameObject>(); // List of tracing UIs
    public List<LetterTracingW3> tracingScripts = new List<LetterTracingW3>(); // List of tracing logic scripts

    [Header("Player Reference")]
    public World3 playerScript; // Reference to the player controller script

    private int correctAnswer = 0; // Holds the correct answer to the equation
    private Action onSuccessCallback; // Callback to invoke on correct answer
    private int activeTracingIndex = 0; // Index of the current tracing letter
    private int currentTracingIndex = -1; // Index of the last shown tracing canvas

    // Unity Start method, runs on scene start
    private void Start()
    {
        if (playerScript == null)
            playerScript = FindObjectOfType<World3>(); // Auto-find player script if not assigned

        if (answerButton != null)
            answerButton.onClick.AddListener(CheckAnswer); // Set up listener for submit button

        if (wrongOkayButton != null)
            wrongOkayButton.onClick.AddListener(ReturnToMathAfterWrongAnswer); // Listener for retry after wrong answer

        if (backButton != null)
            backButton.onClick.AddListener(BackFromMathToTracing); // Listener for back button
    }

    // Called to open the math challenge and set up the callback
    public void OpenMathCanvasWithCallback(Action callback, int tracingIndex)
    {
        onSuccessCallback = callback; // Store callback for later
        activeTracingIndex = tracingIndex; // Set current active index
        currentTracingIndex = tracingIndex; // Set current tracing canvas index

        GenerateRandomEquation(); // Generate math equation

        if (mathCanvas != null)
            mathCanvas.SetActive(true); // Show math canvas

        // Hide all tracing canvases
        foreach (var canvas in tracingCanvases)
        {
            if (canvas != null)
                canvas.SetActive(false);
        }

        // Stop player movement and idle
        if (playerScript != null)
        {
            playerScript.canMove = false;
            playerScript.Idle();
        }
    }

    // Generates a new random multiplication problem
    private void GenerateRandomEquation()
    {
        int num1 = UnityEngine.Random.Range(1, 10); // Random number between 1 and 9
        int num2 = UnityEngine.Random.Range(1, 10);
        correctAnswer = num1 * num2; // Store correct answer

        if (equationText != null)
        {
            string arabicNum1 = ToArabicNumbers(num1); // Convert to Arabic numeral
            string arabicNum2 = ToArabicNumbers(num2);
            equationText.text = $"{arabicNum1} × {arabicNum2} = ؟"; // Format question
        }

        if (answerInput != null)
            answerInput.text = ""; // Clear any previous input
    }

    // Validates player's input when they click the answer button
    private void CheckAnswer()
    {
        if (string.IsNullOrEmpty(answerInput.text))
        {
            if (emptyFieldWarning != null)
            {
                emptyFieldWarning.text = "ﻻ ﻳﻤﻜﻦ ﺗﺮﻙ ﺍﻹﺟﺎﺑﺔ ﻓﺎﺭﻏﺔ!"; // Show warning in Arabic
                emptyFieldWarning.gameObject.SetActive(true);
            }
            return;
        }

        if (emptyFieldWarning != null)
            emptyFieldWarning.gameObject.SetActive(false); // Hide warning

        if (int.TryParse(answerInput.text, out int playerAnswer)) // Try convert input to integer
        {
            if (playerAnswer == correctAnswer)
                CorrectAnswerAction(); // Success path
            else
                WrongAnswerAction(); // Failure path
        }
        else
        {
            Debug.LogWarning("الإجابة المدخلة ليست رقماً صحيحاً."); // Log if invalid number
        }
    }

    // Handles the case where the user got the right answer
    private void CorrectAnswerAction()
    {
        if (mathCanvas != null)
            mathCanvas.SetActive(false); // Hide math canvas

        // Show the tracing canvas and start tracing
        if (tracingCanvases.Count > activeTracingIndex && tracingCanvases[activeTracingIndex] != null)
        {
            tracingCanvases[activeTracingIndex].SetActive(true);

            if (tracingScripts.Count > activeTracingIndex && tracingScripts[activeTracingIndex] != null)
                tracingScripts[activeTracingIndex].StartNewAttempt();
        }

        if (playerScript != null)
            playerScript.canMove = true; // Allow player to move again

        onSuccessCallback?.Invoke(); // Execute success callback if any
    }

    // Handles wrong answer submission
    private void WrongAnswerAction()
    {
        if (wrongAnswerPanel != null)
            wrongAnswerPanel.SetActive(true); // Show retry panel

        if (mathCanvas != null)
            mathCanvas.SetActive(false); // Hide question

        if (playerScript != null)
            playerScript.canMove = false; // Keep movement locked
    }

    // Retry after pressing OK on the wrong answer panel
    private void ReturnToMathAfterWrongAnswer()
    {
        if (wrongAnswerPanel != null)
            wrongAnswerPanel.SetActive(false); // Hide wrong panel

        if (mathCanvas != null)
            mathCanvas.SetActive(false); // Also hide question

        // Reopen tracing canvas and restart tracing
        if (tracingCanvases.Count > currentTracingIndex && tracingCanvases[currentTracingIndex] != null)
        {
            tracingCanvases[currentTracingIndex].SetActive(true);

            if (tracingScripts.Count > currentTracingIndex && tracingScripts[currentTracingIndex] != null)
                tracingScripts[currentTracingIndex].StartNewAttempt();
        }

        if (playerScript != null)
            playerScript.canMove = false; // Still not allowed to move

        ReplayLetterAudio(); // Replay the audio and set up tracing again
    }

    // If user presses "Back" button, return to tracing directly
    private void BackFromMathToTracing()
    {
        if (mathCanvas != null)
            mathCanvas.SetActive(false); // Hide math

        if (tracingCanvases.Count > activeTracingIndex && tracingCanvases[activeTracingIndex] != null)
            tracingCanvases[activeTracingIndex].SetActive(true); // Re-enable tracing

        ReplayLetterAudio(); // Re-trigger audio and enable tracing
    }

    // Plays the letter audio again and prepares tracing state
    private void ReplayLetterAudio()
    {
        if (tracingScripts.Count > activeTracingIndex && tracingScripts[activeTracingIndex] != null)
        {
            var script = tracingScripts[activeTracingIndex];

            if (script.letterAudio != null)
            {
                if (script.LetterImageObj != null)
                    script.LetterImageObj.SetActive(false); // Hide letter image

                if (script.TracingPointsGroup != null)
                    script.TracingPointsGroup.SetActive(false); // Hide points

                script.letterAudio.Play(); // Play the letter audio
                script.SetCanTraceAfterAudio(script.letterAudio); // Allow tracing after audio
            }
        }
    }

    // Converts a number to Arabic numeral characters
    private string ToArabicNumbers(int number)
    {
        string[] arabicDigits = { "٠", "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩" }; // Arabic numerals
        char[] numberChars = number.ToString().ToCharArray(); // Split digits
        string result = "";

        foreach (char c in numberChars)
        {
            if (char.IsDigit(c))
                result += arabicDigits[c - '0']; // Convert digit to Arabic
            else
                result += c; // Keep non-digit characters as-is
        }
        return result; // Return final string
    }
}
