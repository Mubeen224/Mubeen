using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using Mankibo;

public class CardMathManagerW3 : MonoBehaviour
{
    [Header("Math Canvas Elements (Cards Only)")]
    public GameObject mathCanvas;           
    public TMP_Text equationText;           
    public TMP_InputField answerInput;      
    public TMP_Text emptyFieldWarning;      
    public Button answerButton;             
    public GameObject wrongAnswerPanel;     
    public Button wrongOkButton;            
    public Button mathCloseButton;          

    [Header("Cards (ض/ظ/غ)")]
    public List<GameObject> cardsCanvases = new List<GameObject>(); // AR-ض / AR-ظ / AR-غ
    public string cardsRootName = "CardsRoot"; // مجلد الصور داخل الكانفس

    [Header("Player")]
    public World3 playerScript;

    // الحالة الداخلية
    private int currentCardIndex = -1;  
    private int correctAnswer = 0;

    void Awake()
    {
        if (answerButton) { answerButton.onClick.RemoveAllListeners(); answerButton.onClick.AddListener(CheckAnswer); }
        if (wrongOkButton){ wrongOkButton.onClick.RemoveAllListeners(); wrongOkButton.onClick.AddListener(OnWrongOk); }
        if (mathCloseButton){ mathCloseButton.onClick.RemoveAllListeners(); mathCloseButton.onClick.AddListener(BackToCards); }
    }

    // يُستدعى من زر X في Canvas الكروت
    public void OpenMath(int cardIndex)
    {
        if (cardIndex < 0 || cardIndex >= cardsCanvases.Count)
        {
            Debug.LogError($"CardMathManagerW3.OpenMath: index {cardIndex} خارج المدى.");
            return;
        }

        currentCardIndex = cardIndex;
        GenerateRandomEquation();

        if (wrongAnswerPanel) wrongAnswerPanel.SetActive(false);
        if (mathCanvas)       mathCanvas.SetActive(true);
        if (answerInput)      answerInput.text = "";
        if (emptyFieldWarning) emptyFieldWarning.gameObject.SetActive(false);

        // اخفِ كانفس الكروت
        if (cardsCanvases[currentCardIndex]) cardsCanvases[currentCardIndex].SetActive(false);

        if (playerScript){ playerScript.canMove = false; playerScript.Idle(); }
    }

    // زر الرجوع (X) داخل MathCanvas => رجوع لكانفس الكروت بدون حل
    public void BackToCards()
    {
        if (mathCanvas) mathCanvas.SetActive(false);
        if (wrongAnswerPanel) wrongAnswerPanel.SetActive(false);

        if (currentCardIndex >= 0 && currentCardIndex < cardsCanvases.Count)
        {
            var canvas = cardsCanvases[currentCardIndex];
            if (canvas) canvas.SetActive(true);

            // 🎯 أول ما يرجع → يخفي الصور ويشغل الصوت
            HideCardsAndPlayAudio(canvas);
        }

        if (playerScript){ playerScript.canMove = false; playerScript.Idle(); }

        currentCardIndex = -1;
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
    // ✅ أغلق أي بوب-أب خطأ
    if (wrongAnswerPanel) wrongAnswerPanel.SetActive(false);

    // ✅ أغلق كانفس الماث
    if (mathCanvas) mathCanvas.SetActive(false);

    // ✅ أسمح للاعب بالمشي
    if (playerScript) playerScript.canMove = true;

    // ✅ نظف الحالة
    currentCardIndex = -1;
}


    private void OnWrongAnswer()
    {
        if (wrongAnswerPanel) wrongAnswerPanel.SetActive(true);
        if (mathCanvas)       mathCanvas.SetActive(false);

        if (playerScript){ playerScript.canMove = false; playerScript.Idle(); }
    }

    private void OnWrongOk()
    {
        BackToCards();
    }

    // ------------------ Helpers ------------------

    private void HideCardsAndPlayAudio(GameObject canvas)
    {
        var cardsRoot = string.IsNullOrEmpty(cardsRootName)
            ? canvas.transform
            : FindChildByName(canvas.transform, cardsRootName);

        if (cardsRoot) cardsRoot.gameObject.SetActive(false);

        var audio = canvas.GetComponentInChildren<AudioSource>(true);
        if (audio != null)
        {
            audio.Stop();
            audio.Play();
            StartCoroutine(EnableCardsAfterAudio(audio, canvas));
        }
        else
        {
            EnableCardsAfterAudio(null, canvas); 
        }
    }

    private System.Collections.IEnumerator EnableCardsAfterAudio(AudioSource audio, GameObject canvas)
    {
        if (audio != null) yield return new WaitForSeconds(audio.clip.length);

        var cardsRoot = string.IsNullOrEmpty(cardsRootName)
            ? canvas.transform
            : FindChildByName(canvas.transform, cardsRootName);

        if (cardsRoot) cardsRoot.gameObject.SetActive(true);

        var round = canvas.GetComponentInChildren<CardRoundManager>(true);
        if (round) round.RefreshCards();
    }

    private Transform FindChildByName(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var result = FindChildByName(child, name);
            if (result != null) return result;
        }
        return null;
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
