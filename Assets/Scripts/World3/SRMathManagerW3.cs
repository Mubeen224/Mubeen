using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using Mankibo;

public class SRMathManagerW3 : MonoBehaviour
{
    [Header("SR Canvases (Multiple: ض/ظ/غ)")]
    [Tooltip("رتّبي هنا كانفاسات السبيتش بنفس ترتيب الفهارس (0: ض ، 1: ظ ، 2: غ)")]
    public List<GameObject> srCanvases = new List<GameObject>();

    [Tooltip("لو عندك كومبوننت WhisperSR3 منفصل لكل كانفس، حطيه هنا بنفس الترتيب. اتركي الخانة null وسيتم إيجاده تلقائياً من الكانفس.")]
    public List<WhisperSR3> srComponents = new List<WhisperSR3>();

    [Header("Math Canvas Elements")]
    public GameObject mathCanvas;
    public TMP_Text equationText;
    public TMP_InputField answerInput;
    public TMP_Text emptyFieldWarning;
    public Button answerButton;
    public GameObject wrongAnswerPanel;
    public Button wrongOkButton;
    public Button mathCloseButton;

    [Header("Player")]
    public World3 playerScript;

    private int correctAnswer = 0;
    private int currentSRIndex = -1; // أي SR فتحنا منه بوابة الماث؟

    private void Awake()
    {
        if (answerButton)   { answerButton.onClick.RemoveAllListeners();   answerButton.onClick.AddListener(CheckAnswer); }
        if (wrongOkButton)  { wrongOkButton.onClick.RemoveAllListeners();  wrongOkButton.onClick.AddListener(BackToSR); }
        if (mathCloseButton){ mathCloseButton.onClick.RemoveAllListeners(); mathCloseButton.onClick.AddListener(BackToSR); }
    }

    // يُستدعى من زر X داخل كانفس SR عبر SRCloseButton مع تمرير index
    public void OpenMathGate(int srIndex)
    {
        if (srIndex < 0 || srIndex >= srCanvases.Count)
        {
            Debug.LogError($"SRMathManagerW3.OpenMathGate: index {srIndex} خارج المدى.");
            return;
        }

        currentSRIndex = srIndex;

        // احصل على كومبوننت الـ SR (إن كان null، استخرجه من نفس الكانفس)
        WhisperSR3 srComp = null;
        if (srIndex < srComponents.Count) srComp = srComponents[srIndex];
        if (srComp == null && srCanvases[srIndex] != null)
            srComp = srCanvases[srIndex].GetComponentInChildren<WhisperSR3>(true);

        // أوقفي التسجيل بأمان من خلال الدالة العامة
        if (srComp != null) { srComp.StopRecordingExternal(); }

        GenerateRandomEquation();

        if (wrongAnswerPanel)  wrongAnswerPanel.SetActive(false);
        if (emptyFieldWarning) emptyFieldWarning.gameObject.SetActive(false);
        if (answerInput)       answerInput.text = "";

        if (mathCanvas) mathCanvas.SetActive(true);
        if (srCanvases[srIndex]) srCanvases[srIndex].SetActive(false);

        if (playerScript) { playerScript.canMove = false; playerScript.Idle(); }
    }

    // رجوع من بوابة الماث إلى نفس SR الذي أتينا منه (في حال خطأ أو ضغط X)
    public void BackToSR()
    {
        if (mathCanvas)       mathCanvas.SetActive(false);
        if (wrongAnswerPanel) wrongAnswerPanel.SetActive(false);

        if (currentSRIndex >= 0 && currentSRIndex < srCanvases.Count)
        {
            var srCanvas = srCanvases[currentSRIndex];
            if (srCanvas) srCanvas.SetActive(true);

            // أبقي اللاعب ثابت داخل الـSR
            if (playerScript) { playerScript.canMove = false; playerScript.Idle(); }
        }
        else
        {
            // في حال ما عندنا انديكس صالح، بس أقفل الماث
            if (playerScript) { playerScript.canMove = true; }
        }
    }

    private void GenerateRandomEquation()
    {
        int n1 = Random.Range(1, 10);
        int n2 = Random.Range(1, 10);
        correctAnswer = n1 * n2;
        if (equationText) equationText.text = $"{ToArabic(n1)} × {ToArabic(n2)} = ؟";
    }

    private void CheckAnswer()
    {
        if (!answerInput || string.IsNullOrWhiteSpace(answerInput.text))
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
            if (playerAnswer == correctAnswer) OnRightAnswer();
            else OnWrongAnswer();
        }
        else
        {
            if (emptyFieldWarning)
            {
                emptyFieldWarning.text = "الإجابة المدخلة ليست رقماً صحيحاً.";
                emptyFieldWarning.gameObject.SetActive(true);
            }
        }
    }

    private void OnRightAnswer()
    {
        // صح → قفل الماث + قفل الـSR الذي أتينا منه + اسمح بالحركة
        if (wrongAnswerPanel) wrongAnswerPanel.SetActive(false);
        if (mathCanvas)       mathCanvas.SetActive(false);

        if (currentSRIndex >= 0 && currentSRIndex < srCanvases.Count)
        {
            var srCanvas = srCanvases[currentSRIndex];
            if (srCanvas) srCanvas.SetActive(false);
        }

        if (playerScript) playerScript.canMove = true;

        currentSRIndex = -1;
    }

    private void OnWrongAnswer()
    {
        // خطأ → أظهر لوحة الخطأ، أقفل الماث مؤقتاً، ثم BackToSR (بالزر OK)
        if (wrongAnswerPanel) wrongAnswerPanel.SetActive(true);
        if (mathCanvas)       mathCanvas.SetActive(false);

        if (playerScript) { playerScript.canMove = false; playerScript.Idle(); }
    }

    private string ToArabic(int n)
    {
        string[] d = {"٠","١","٢","٣","٤","٥","٦","٧","٨","٩"};
        var s = n.ToString();
        var r = "";
        foreach (var ch in s) r += char.IsDigit(ch) ? d[ch - '0'] : ch.ToString();
        return r;
    }
}
