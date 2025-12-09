using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class CardButtonW1 : MonoBehaviour
{
    [Header("Correctness")]
    public bool isCorrect = false;

    [Header("UI References")]
    public Image cardImage;
    public Sprite wrongSprite;
    public GameObject winPopup;

    [Header("Audio")]
    public AudioSource sfxSource;
    public AudioClip applauseClip;
    public AudioClip winClip;
    public AudioClip wrongLetterClip;
    public Button closeButton;

    [Header("Timings")]
    public float wrongDisplayTime = 2f;
    public float winPopupDuration = 10f;

    [HideInInspector] public Sprite originalSprite;

    private Button btn;
    private CardRoundManagerW1 roundManager;
    private static bool inputLocked = false;

    private void Awake()
    {
        btn = GetComponent<Button>();
        if (cardImage) originalSprite = cardImage.sprite;
        roundManager = GetComponentInParent<CardRoundManagerW1>();
    }

    public void OnCardSelected()
    {
        if (inputLocked) return;
        if (isCorrect) StartCoroutine(HandleWin());
        else StartCoroutine(HandleWrong());
    }

    private IEnumerator HandleWrong()
    {
        inputLocked = true;
        SetAllCardsInteractable(false);

        Sprite prev = cardImage ? cardImage.sprite : null;
        if (cardImage && wrongSprite) cardImage.sprite = wrongSprite;

        float wait = wrongDisplayTime;
        if (wrongLetterClip && sfxSource)
        {
            sfxSource.PlayOneShot(wrongLetterClip);
            wait = Mathf.Max(wait, wrongLetterClip.length);
        }

        if (roundManager) roundManager.RegisterWrong();

        yield return new WaitForSeconds(wait);

        if (cardImage && prev) cardImage.sprite = prev;

        SetAllCardsInteractable(true);
        inputLocked = false;
    }

    private IEnumerator HandleWin()
    {
        inputLocked = true;
        SetAllCardsInteractable(false);

        if (winPopup) winPopup.SetActive(true);

        if (applauseClip && sfxSource)
        {
            sfxSource.PlayOneShot(applauseClip);
            yield return new WaitForSeconds(4f);
            sfxSource.Stop();
        }

        if (winClip && sfxSource) sfxSource.PlayOneShot(winClip);

        if (roundManager)
        {
            roundManager.RegisterSuccess();
            roundManager.AddCoins(5);
        }

        yield return new WaitForSeconds(winPopupDuration);

        if (winPopup) winPopup.SetActive(false);

        SetAllCardsInteractable(true);
        inputLocked = false;

        var canvas = GetComponentInParent<Canvas>();
        if (canvas) canvas.gameObject.SetActive(false);

        var player = FindObjectOfType<Mankibo.World1>();
        if (player) player.canMove = true;
    }

    private void SetAllCardsInteractable(bool state)
    {
        var parent = transform.parent;
        if (!parent) return;

        foreach (var b in parent.GetComponentsInChildren<Button>(true))
            b.interactable = state;
    }

    public static void ResetInputLock() { inputLocked = false; }

    public void ResetCardVisual()
    {
        if (cardImage) cardImage.sprite = originalSprite;
        if (winPopup) winPopup.SetActive(false);

        if (!btn) btn = GetComponent<Button>();
        if (btn) btn.interactable = true;
    }
}
