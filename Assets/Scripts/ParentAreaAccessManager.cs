using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ParentAreaAccessManager : MonoBehaviour
{
    [Header("Math Canvas Elements")]
    public GameObject mathCanvas;            // Panel for math challenge
    public TMP_Text equationText;            // Text to display the math equation
    public TMP_InputField answerInput;       // Input field for user's answer
    public TMP_Text emptyFieldWarning;       // Warning if no answer is provided
    public Button answerButton;              // Button to submit answer
    public GameObject wrongAnswerPanel;      // Panel to show when answer is wrong
    public Button wrongOkayButton;           // Button to close wrong answer panel

    private int correctAnswer = 0;           // Stores the correct answer to the equation
    private string targetSceneName = "ParentINFO"; // Default target scene if not specified

    private void Start()
    {
        // Attach listeners to buttons
        if (answerButton != null)
            answerButton.onClick.AddListener(CheckAnswer);

        if (wrongOkayButton != null)
            wrongOkayButton.onClick.AddListener(CloseWrongPanel);
    }

    // Public method to start the access verification for a specific scene
    public void RequestAccess(string sceneName)
    {
        targetSceneName = sceneName;
        GenerateRandomEquation();

        if (mathCanvas != null)
            mathCanvas.SetActive(true);
    }

    // Generates a random multiplication question for verification
    private void GenerateRandomEquation()
    {
        int num1 = UnityEngine.Random.Range(1, 10);
        int num2 = UnityEngine.Random.Range(1, 10);
        correctAnswer = num1 * num2;

        // Display equation using Arabic numerals
        if (equationText != null)
        {
            string arabicNum1 = ToArabicNumbers(num1);
            string arabicNum2 = ToArabicNumbers(num2);
            equationText.text = $"{arabicNum1} × {arabicNum2} = ؟";
        }

        if (answerInput != null)
            answerInput.text = ""; // Clear previous input
    }

    // Checks the user's answer and navigates if correct
    private void CheckAnswer()
    {
        if (string.IsNullOrEmpty(answerInput.text))
        {
            // Show warning if input is empty
            if (emptyFieldWarning != null)
            {
                emptyFieldWarning.text = "ﻻ ﻳﻤﻜﻦ ﺗﺮﻙ ﺍﻹﺟﺎﺑﺔ ﻓﺎﺭﻏﺔ!";
                emptyFieldWarning.gameObject.SetActive(true);
            }
            return;
        }

        // Hide warning if previously shown
        if (emptyFieldWarning != null)
            emptyFieldWarning.gameObject.SetActive(false);

        // Try to parse user input
        if (int.TryParse(answerInput.text, out int playerAnswer))
        {
            if (playerAnswer == correctAnswer)
            {
                // Correct answer: proceed to target scene
                LoadTargetScene();
            }
            else
            {
                // Wrong answer: show error panel
                if (wrongAnswerPanel != null)
                    wrongAnswerPanel.SetActive(true);
            }
        }
        else
        {
            Debug.LogWarning("The input is not a valid number.");
        }
    }

    // Closes the wrong answer panel and hides math canvas
    private void CloseWrongPanel()
    {
        if (wrongAnswerPanel != null)
            wrongAnswerPanel.SetActive(false);

        if (mathCanvas != null)
            mathCanvas.SetActive(false);
    }

    // Loads the scene that was requested after successful verification
    private void LoadTargetScene()
    {
        if (mathCanvas != null)
            mathCanvas.SetActive(false);

        UnityEngine.SceneManagement.SceneManager.LoadScene(targetSceneName);
    }

    // Converts Western numerals to Arabic numerals for UI display
    private string ToArabicNumbers(int number)
    {
        string[] arabicDigits = { "٠", "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩" };
        char[] numberChars = number.ToString().ToCharArray();
        string result = "";

        foreach (char c in numberChars)
        {
            if (char.IsDigit(c))
                result += arabicDigits[c - '0'];
            else
                result += c;
        }
        return result;
    }
}
