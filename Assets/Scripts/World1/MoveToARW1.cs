using UnityEngine;
using UnityEngine.UI;

public class MoveToARW1 : MonoBehaviour
{
    [Header("Only For Letter")]
    public string onlyForLetter = "ذ"; // حارس

    [Header("Intro Canvas")]
    public GameObject introCanvas;
    public Button startButton;
    public AudioSource introAudio;

    [Header("Cards Canvas")]
    public GameObject cardsPopupAR;
    public CardRoundManagerW1 roundManager;

    private void Awake()
    {
        if (startButton)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(OpenCards);
        }
    }

    private void OnEnable()
    {
        // لو مش حرف ذ اقفل نفسك
        if (!string.IsNullOrEmpty(onlyForLetter) && GameSession.CurrentLetter != onlyForLetter)
        {
            gameObject.SetActive(false);
            return;
        }

        if (startButton) startButton.interactable = false;

        if (introAudio != null && introAudio.clip != null)
        {
            introAudio.Stop();
            introAudio.Play();
            Invoke(nameof(EnableButton), introAudio.clip.length);
        }
        else
        {
            EnableButton();
        }
    }

    private void EnableButton()
    {
        if (startButton) startButton.interactable = true;
    }

    // يُستدعى عند الضغط على زر "لنبدأ البحث"
    private void OpenCards()
    {
        // أظهر نافذة البطاقات (بدون أي تعديل على ترتيب الكانفس)
        if (cardsPopupAR) cardsPopupAR.SetActive(true);

        // أعثر على المدير إن لم يكن معيّنًا
        if (!roundManager)
        {
            if (cardsPopupAR) roundManager = cardsPopupAR.GetComponentInChildren<CardRoundManagerW1>(true);
            if (!roundManager) roundManager = GetComponentInChildren<CardRoundManagerW1>(true);
        }

        // اضبطي الحرف وشغّلي الجولة
        if (roundManager)
        {
            var letter = string.IsNullOrEmpty(GameSession.CurrentLetter) ? "ذ" : GameSession.CurrentLetter;
            roundManager.SetCurrentLetter(letter);
            try { CardButtonW1.ResetInputLock(); } catch { }
            roundManager.RefreshCards();
        }
        else
        {
            Debug.LogWarning("[MoveToARW1] CardRoundManagerW1 not found; cannot refresh cards.");
        }

        // أخفِ الـIntro الآن
        if (introCanvas) introCanvas.SetActive(false);
        else gameObject.SetActive(false);
    }
}
