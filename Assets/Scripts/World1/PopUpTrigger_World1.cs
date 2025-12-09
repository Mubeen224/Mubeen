using Mankibo;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class PopUpTriggerWorld1 : MonoBehaviour
{
    [Header("Pop-Up Panels")]
    public GameObject womanPopUp;        // Horse => خ
    public GameObject squirrelPopUp;     // Wolf  => ذ
    public GameObject owlPopUp;          // Boy   => ر
    public GameObject levelFinishedCanvas;

    [Header("Audio Clips")]
    public AudioSource womanAudio;
    public AudioSource squirrelAudio;
    public AudioSource owlAudio;
    public AudioSource levelFinishedAudio;

    [Header("Close Buttons")]
    public Button womanCloseButton;
    public Button squirrelCloseButton;
    public Button owlCloseButton;
    public Button homePageButton;

    private World1 playerScript;
    private MathExitManagerW1 mathManager;

    private GameObject currentActivePopUp;
    private int currentTracingIndex;

    
    private GameObject currentTracingCanvas;
    private LetterTracingW1 currentTracingScript;

    public static PopUpTriggerWorld1 instance;

    private void Awake() { instance = this; }

    private void Start()
    {
        playerScript = FindObjectOfType<World1>();
        mathManager = FindObjectOfType<MathExitManagerW1>();

        if (womanPopUp) womanPopUp.SetActive(false);
        if (squirrelPopUp) squirrelPopUp.SetActive(false);
        if (owlPopUp) owlPopUp.SetActive(false);
        if (levelFinishedCanvas) levelFinishedCanvas.SetActive(false);

        if (womanCloseButton) womanCloseButton.onClick.AddListener(OpenMathBeforeClosing);
        if (squirrelCloseButton) squirrelCloseButton.onClick.AddListener(OpenMathBeforeClosing);
        if (owlCloseButton) owlCloseButton.onClick.AddListener(OpenMathBeforeClosing);
        if (homePageButton) homePageButton.onClick.AddListener(GoToHomePage);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        var w = other.GetComponent<World1>();


        if (GameSession.ArePopupsGloballyBlocked(w ? w.transform : null)) return;

        if (gameObject.CompareTag("Horse")) ShowPopUp(womanPopUp, womanAudio, 0);
        else if (gameObject.CompareTag("Wolf")) ShowPopUp(squirrelPopUp, squirrelAudio, 1);
        else if (gameObject.CompareTag("Boy")) ShowPopUp(owlPopUp, owlAudio, 2);
        else if (gameObject.CompareTag("FinishW1")) ShowLevelFinished();
    }

    private void ShowPopUp(GameObject popUp, AudioSource audioSource, int tracingIndex)
    {
        if (popUp) popUp.SetActive(true);
        if (audioSource) audioSource.Play();

        if (playerScript != null)
        {
            playerScript.canMove = false;
            playerScript.Idle();
        }

        currentActivePopUp = popUp;
        currentTracingIndex = tracingIndex;

  
        if (mathManager != null &&
            tracingIndex >= 0 && tracingIndex < mathManager.tracingCanvases.Count)
        {
            var canvas = mathManager.tracingCanvases[tracingIndex];
            if (canvas) canvas.SetActive(true);

            LetterTracingW1 tracingScript = null;
            if (tracingIndex < mathManager.tracingScripts.Count)
            {
                tracingScript = mathManager.tracingScripts[tracingIndex];
                if (tracingScript != null)
                {
                    if (!tracingScript.gameObject.activeSelf)
                        tracingScript.gameObject.SetActive(true);

                    tracingScript.StartNewAttempt();
                    tracingScript.SetCanTraceAfterAudio(audioSource);
                }
            }

            currentTracingCanvas = canvas;
            currentTracingScript = tracingScript;
        }
    }

    private void OpenMathBeforeClosing()
    {
        if (mathManager == null || currentActivePopUp == null) return;

        currentActivePopUp.SetActive(false);

        mathManager.OpenMathFromTracing(
            onCorrect: CloseCurrentPopUp,          
            scriptRef: currentTracingScript,
            canvasRef: currentTracingCanvas
        );
    }

    private void CloseCurrentPopUp()
    {
        if (currentActivePopUp != null)
        {
            currentActivePopUp.SetActive(false);
            currentActivePopUp = null;
        }

        if (playerScript != null)
        {
            playerScript.canMove = true;
            playerScript.Idle();
        }
    }

    private void ShowLevelFinished()
    {
        CoinsManager.instance.AddCoinsToSelectedChild(10);
        StarsManager.instance.AddStarsToSelectedChild(1);

        if (levelFinishedAudio) levelFinishedAudio.Play();
        if (levelFinishedCanvas) levelFinishedCanvas.SetActive(true);

        if (playerScript != null)
        {
            playerScript.canMove = false;
            playerScript.Idle();
        }
    }

    public void GoToHomePage() => SceneManager.LoadScene("HomePage");
}
