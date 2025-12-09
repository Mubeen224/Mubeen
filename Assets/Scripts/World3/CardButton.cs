using System.Collections;// for IEnumerator and coroutines
using UnityEngine; // core Unity engine features
using UnityEngine.UI; // UI components like Image & Button

public class CardButton : MonoBehaviour
{
    [Header("Correctness")]
    public bool isCorrect = false; // determines if this card is the correct answer

    [Header("UI References")]
    public Image cardImage;                  // UI image showing the card icon
    public Sprite wrongSprite;               // sprite shown when the card is answered wrong
    public GameObject winPopup;              // popup shown when the card is answered correctly

    [Header("Audio")]
    public AudioSource sfxSource;            // audio source for sound effects
    public AudioClip applauseClip;           // applause sound for correct answer
    public AudioClip winClip;                // “well done” voice clip.
    public AudioClip wrongLetterClip;        // audio played when child selects wrong card
    public Button closeButton;               // close button for popup

    [Header("Timings")]
    public float wrongDisplayTime = 2f; // time to show wrong sprite before reverting
    public float winPopupDuration  = 10f; // duration of win popup before hiding it

    [HideInInspector] public Sprite originalSprite;  // original sprite stored for reset

    private Button btn;  // button component reference
    private CardRoundManager roundManager;    // reference to round manager for registering attempts
    private static bool inputLocked = false; // prevents fast double-click inputs

    private void Awake()
    {
        btn = GetComponent<Button>(); // cache button reference
        if (cardImage != null) originalSprite = cardImage.sprite; // store initial sprite
        roundManager = GetComponentInParent<CardRoundManager>();  // find round manager in parents
    }

    public void OnCardSelected()
    {
        if (inputLocked) return;  // prevent input if locked

        if (isCorrect) StartCoroutine(HandleWin()); // correct card, win flow

        else           StartCoroutine(HandleWrong()); // wrong card, wrong flow
    }

    private IEnumerator HandleWrong()
    {
        inputLocked = true; // lock input
        SetAllCardsInteractable(false);  // disable card clicks

        Sprite prevSprite = cardImage ? cardImage.sprite : null; // store previous sprite
        if (cardImage && wrongSprite) cardImage.sprite = wrongSprite; // switch to wrong sprite


        float wait = wrongDisplayTime; // default wait time
        if (wrongLetterClip && sfxSource)
        {
            sfxSource.PlayOneShot(wrongLetterClip); // play educational wrong sound
            wait = Mathf.Max(wait, wrongLetterClip.length); // wait for sound length if longer
        }

        if (roundManager != null) roundManager.RegisterWrong(); // register wrong attempt

        yield return new WaitForSeconds(wait);  // wait before reverting

        if (cardImage && prevSprite) cardImage.sprite = prevSprite; // return original sprite

        SetAllCardsInteractable(true); // re-enable all cards
        inputLocked = false; // unlock input
    }

    private IEnumerator HandleWin()
    {
        inputLocked = true; // prevent extra clicks
        SetAllCardsInteractable(false); // disable all card buttons

        if (winPopup) winPopup.SetActive(true); // show win popup

        if (applauseClip && sfxSource)
        {
            sfxSource.PlayOneShot(applauseClip); // play applause
            yield return new WaitForSeconds(4f);  // wait for applause duration
            sfxSource.Stop(); // stop clip if still playing
        }

        if (winClip && sfxSource) sfxSource.PlayOneShot(winClip); // play “well done” sound

        if (roundManager != null)
        {
            roundManager.RegisterSuccess(); // register success in Firebase
            roundManager.AddCoins(5); // reward child with coins
        }

        yield return new WaitForSeconds(winPopupDuration); // wait before closing popup

        if (winPopup) winPopup.SetActive(false); // hide win popup

        SetAllCardsInteractable(true); // re-enable card clicks

        inputLocked = false; // allow inputs again

        
        var canvas = GetComponentInParent<Canvas>(); // find parent canvas
        if (canvas) canvas.gameObject.SetActive(false); // disable card canvas

        var player = FindObjectOfType<Mankibo.World3>(); // find player controller
        if (player) player.canMove = true; // re-enable movement
    }

    private void SetAllCardsInteractable(bool state)
    {
        var parent = transform.parent; // get parent object
        if (!parent) return;

        foreach (var b in parent.GetComponentsInChildren<Button>(true))
            b.interactable = state; // toggle interactability of all card buttons
    }

 
    public void ResetCardVisual()
    {
        if (cardImage) cardImage.sprite = originalSprite;   // restore original card sprite
        if (winPopup)  winPopup.SetActive(false);  // ensure popup is hidden

        if (!btn) btn = GetComponent<Button>(); // ensure button reference
        if (btn) btn.interactable = true; // re-enable card interaction
    }
} // End
