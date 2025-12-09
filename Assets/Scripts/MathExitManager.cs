using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mankibo;
using System.Linq;

public class MathExitManager : MonoBehaviour
{
    public enum ExitOrigin { Tracing, AR, Speech, Cards }

    [Header("Math Pop-Up Objects")]
    public GameObject mathCanvas;       // Main math canvas
    public GameObject wrongAnswerPanel; // Popup shown when answer is wrong
    public TMP_Text equationText;       // Equation text
    public TMP_InputField answerInput;  // Answer box
    public TMP_Text emptyFieldWarning;  // "Empty" warning
    public Button answerButton;         // Submit
    public Button wrongOkayButton;      // OK button on wrong panel
    public Button backButton;           // Back button on math panel

    [Header("Origin Canvases")]
    public GameObject letterTracingCanvas;
    public GameObject arCanvas;
    public GameObject speechCanvas;
    public GameObject cardsCanvas;

    [Header("Current Tracing Panel")]
    public LetterTracingW4 currentTracingScript;

    [Header("Player Reference")]
    public World4 playerScript;

    // === Helper to reach PopUpTrigger for SetMovement(true/false) ===
    private PopUpTrigger _popup;
    private PopUpTrigger Popup => _popup ??= FindFirstObjectByType<PopUpTrigger>();

    private ExitOrigin origin = ExitOrigin.Tracing;
    private int correctAnswer = 0;

    private void Start()
    {
        if (answerButton != null) answerButton.onClick.AddListener(CheckAnswer);
        if (wrongOkayButton != null) wrongOkayButton.onClick.AddListener(ReturnToOriginAfterWrongAnswer);
        if (backButton != null) backButton.onClick.AddListener(BackFromMathToOrigin);
        if (playerScript == null) playerScript = FindFirstObjectByType<World4>();
    }

    // ---- Public setters for origin ----
    public void SetOriginToTracing(GameObject tracingCanvas, LetterTracingW4 tracingScript)
    {
        origin = ExitOrigin.Tracing;
        letterTracingCanvas = tracingCanvas;
        currentTracingScript = tracingScript;
    }

    public void SetOriginToAR(GameObject arPopupRoot)
    {
        origin = ExitOrigin.AR;
        arCanvas = arPopupRoot;
        currentTracingScript = null;
    }

    public void SetOriginToSpeech(GameObject speechPopupRoot)
    {
        origin = ExitOrigin.Speech;
        speechCanvas = speechPopupRoot;
        currentTracingScript = null;
    }

    public void SetOriginToCards(GameObject cardsPopupRoot)
    {
        origin = ExitOrigin.Cards;
        cardsCanvas = cardsPopupRoot;
        currentTracingScript = null;
    }

    // ---- Open math popup ----
    public void OpenMathCanvas()
    {
        GenerateRandomEquation();
        StopAudioForOrigin();

        if (mathCanvas) mathCanvas.SetActive(true);

        switch (origin)
        {
            case ExitOrigin.Tracing: if (letterTracingCanvas) letterTracingCanvas.SetActive(false); break;
            case ExitOrigin.AR: if (arCanvas) arCanvas.SetActive(false); break;
            case ExitOrigin.Speech: if (speechCanvas) speechCanvas.SetActive(false); break;
            case ExitOrigin.Cards: if (cardsCanvas) cardsCanvas.SetActive(false); break;
        }

        // Fully freeze input + locomotion and clear run anim
        Popup?.SetMovement(false);

        if (playerScript != null) { playerScript.canMove = false; playerScript.Idle(); }
    }

    // ---- Math logic ----
    private void GenerateRandomEquation()
    {
        int num1 = Random.Range(1, 10);
        int num2 = Random.Range(1, 10);
        correctAnswer = num1 * num2;

        if (equationText != null)
        {
            string arabicNum1 = ToArabicNumbers(num1);
            string arabicNum2 = ToArabicNumbers(num2);
            equationText.text = $"{arabicNum1} x {arabicNum2} = ?";
        }

        if (answerInput) answerInput.text = "";
        if (emptyFieldWarning) emptyFieldWarning.gameObject.SetActive(false);
    }

    private void CheckAnswer()
    {
        if (answerInput == null)
            return;

        if (string.IsNullOrEmpty(answerInput.text))
        {
            if (emptyFieldWarning)
            {
                emptyFieldWarning.text = "ﻻ ﻳﻤﻜﻦ ﺗﺮﻙ ﺍﻹﺟﺎﺑﺔ ﻓﺎﺭﻏﺔ!";
                emptyFieldWarning.gameObject.SetActive(true);
            }
            return;
        }

        if (emptyFieldWarning) emptyFieldWarning.gameObject.SetActive(false);

        if (int.TryParse(answerInput.text, out int playerAnswer))
        {
            if (playerAnswer == correctAnswer)
            {
                if (mathCanvas) mathCanvas.SetActive(false);

                // Hide the origin canvas that led here
                switch (origin)
                {
                    case ExitOrigin.Tracing: if (letterTracingCanvas) letterTracingCanvas.SetActive(false); break;
                    case ExitOrigin.AR: if (arCanvas) arCanvas.SetActive(false); break;
                    case ExitOrigin.Speech: if (speechCanvas) speechCanvas.SetActive(false); break;
                    case ExitOrigin.Cards: if (cardsCanvas) cardsCanvas.SetActive(false); break;
                }

                // Clear latched input, reset anim to Idle, re-enable controls
                UnfreezeAllControls();
                return;
            }
            else
            {
                if (wrongAnswerPanel) wrongAnswerPanel.SetActive(true);
                if (mathCanvas) mathCanvas.SetActive(false);
            }
        }
    }

    // ---- Wrong / Back ----
    private void ReturnToOriginAfterWrongAnswer()
    {
        if (wrongAnswerPanel) wrongAnswerPanel.SetActive(false);
        if (mathCanvas) mathCanvas.SetActive(false);

        switch (origin)
        {
            case ExitOrigin.Tracing:
                if (letterTracingCanvas) letterTracingCanvas.SetActive(true);
                if (currentTracingScript) currentTracingScript.gameObject.SetActive(true);
                if (playerScript) playerScript.canMove = false;
                ReplayLetterAudioIfTracing();
                break;

            case ExitOrigin.AR:
                if (arCanvas) arCanvas.SetActive(true);
                if (playerScript) playerScript.canMove = false;
                ReplayLetterAudioIfAR();
                break;

            case ExitOrigin.Speech:
                if (speechCanvas) speechCanvas.SetActive(true);
                if (playerScript) playerScript.canMove = false;
                break;

            case ExitOrigin.Cards:
                if (cardsCanvas) cardsCanvas.SetActive(true);
                if (playerScript) playerScript.canMove = false;
                break;
        }
        // Note: we intentionally keep movement disabled here.
    }

    private void BackFromMathToOrigin()
    {
        if (mathCanvas) mathCanvas.SetActive(false);

        switch (origin)
        {
            case ExitOrigin.Tracing:
                if (letterTracingCanvas) letterTracingCanvas.SetActive(true);
                if (currentTracingScript) currentTracingScript.gameObject.SetActive(true);
                if (playerScript) playerScript.canMove = false;
                ReplayLetterAudioIfTracing();
                break;

            case ExitOrigin.AR:
                if (arCanvas) arCanvas.SetActive(true);
                if (playerScript) playerScript.canMove = false;
                ReplayLetterAudioIfAR();
                break;

            case ExitOrigin.Speech:
                if (speechCanvas) speechCanvas.SetActive(true);
                if (playerScript) playerScript.canMove = false;
                break;

            case ExitOrigin.Cards:
                if (cardsCanvas) cardsCanvas.SetActive(true);
                if (playerScript) playerScript.canMove = false;
                break;
        }
        // Note: we intentionally keep movement disabled here.
    }

    // ---- Helpers ----
    private void ReplayLetterAudioIfTracing()
    {
        if (currentTracingScript != null && currentTracingScript.letterAudio != null)
        {
            if (currentTracingScript.LetterImageObj) currentTracingScript.LetterImageObj.SetActive(false);
            if (currentTracingScript.TracingPointsGroup) currentTracingScript.TracingPointsGroup.SetActive(false);

            currentTracingScript.letterAudio.Play();
            currentTracingScript.SetCanTraceAfterAudio(currentTracingScript.letterAudio);
        }
    }

    private void ReplayLetterAudioIfAR()
    {
        if (!arCanvas) return;

        var audios = arCanvas.GetComponentsInChildren<AudioSource>(true);
        foreach (var a in audios)
        {
            if (!a || a.clip == null) continue;
            a.Stop();
            a.Play();
            break;
        }
    }

    private void UnfreezeAllControls()
    {
        // 1) Global gate
        Popup?.SetMovement(true);

        // 2) Player
        if (playerScript != null)
        {
            playerScript.canMove = true;
            playerScript.Idle();
        }

        // 3) Clear any latched joystick input
        var uiCtrl = FindFirstObjectByType<CharacterUIControllerW4>();
        uiCtrl?.StopMoving();

        // 4) Safety: resume time
        if (Time.timeScale == 0f) Time.timeScale = 1f;
    }

    private void StopAudioForOrigin()
    {
        switch (origin)
        {
            case ExitOrigin.Tracing:
                StopAllAudioUnder(letterTracingCanvas);
                break;

            case ExitOrigin.AR:
                // Stop any audio under the AR popup
                StopAllAudioUnder(arCanvas);

                // ALSO stop the explicit arAudio on PopUpTrigger (sits as a sibling in hierarchy)
                var popup = Object.FindFirstObjectByType<PopUpTrigger>();
                if (popup != null && popup.arAudio != null)
                {
                    popup.arAudio.playOnAwake = false;
                    popup.arAudio.loop = false;
                    if (popup.arAudio.isPlaying) popup.arAudio.Stop();
                }
                break;

            case ExitOrigin.Speech:
                StopAllAudioUnder(speechCanvas);
                break;

            case ExitOrigin.Cards:
                StopAllAudioUnder(cardsCanvas);
                break;
        }
    }

    private void StopAllAudioUnder(GameObject root)
    {
        if (!root) return;
        var audios = root.GetComponentsInChildren<AudioSource>(true);
        foreach (var a in audios)
        {
            if (!a) continue;
            a.playOnAwake = false;
            a.loop = false;
            if (a.isPlaying) a.Stop();
        }
    }

    private string ToArabicNumbers(int number)
    {
        string[] arabicDigits = { "٠", "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩" };
        var chars = number.ToString().ToCharArray();
        string result = "";
        foreach (char c in chars)
            result += char.IsDigit(c) ? arabicDigits[c - '0'] : c.ToString();
        return result;
    }
}