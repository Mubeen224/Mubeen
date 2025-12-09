using Mankibo;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class PopUpTrigger_World2 : MonoBehaviour
{
    [Header("Pop-Up Panels")]
    public GameObject bird1PopUp;        // مثال: س
    public GameObject womanPopUp;        // مثال: ش
    public GameObject bird2PopUp;        // مثال: ص
    public GameObject levelFinishedCanvas;

    [Header("Audio Clips")]
    public AudioSource bird1Audio;
    public AudioSource womanAudio;
    public AudioSource bird2Audio;
    public AudioSource levelFinishedAudio;

    [Header("Close Buttons")]
    public Button bird1CloseButton;
    public Button womanCloseButton;
    public Button bird2CloseButton;
    public Button homePageButton;

    private World2 playerScript;
    private MathExitManagerW2 mathManager;

    private GameObject currentActivePopUp;
    private int currentTracingIndex;

    private GameObject currentTracingCanvas;
    private LetterTracingW2 currentTracingScript;

    public static PopUpTrigger_World2 instance;

    private void Awake() { instance = this; }

    private void Start()
    {
        playerScript = FindObjectOfType<World2>();
        mathManager = FindObjectOfType<MathExitManagerW2>();

        if (bird1PopUp) bird1PopUp.SetActive(false);
        if (womanPopUp) womanPopUp.SetActive(false);
        if (bird2PopUp) bird2PopUp.SetActive(false);
        if (levelFinishedCanvas) levelFinishedCanvas.SetActive(false);

        if (bird1CloseButton) bird1CloseButton.onClick.AddListener(OpenMathBeforeClosing);
        if (womanCloseButton) womanCloseButton.onClick.AddListener(OpenMathBeforeClosing);
        if (bird2CloseButton) bird2CloseButton.onClick.AddListener(OpenMathBeforeClosing);
        if (homePageButton) homePageButton.onClick.AddListener(GoToHomePage);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        var w = other.GetComponent<World2>();

        if (GameSessionworld2.ArePopupsGloballyBlocked(w ? w.transform : null)) return;

        if (gameObject.CompareTag("Bird1")) ShowPopUp(bird1PopUp, bird1Audio, 0);
        else if (gameObject.CompareTag("Woman")) ShowPopUp(womanPopUp, womanAudio, 1);
        else if (gameObject.CompareTag("Bird2")) ShowPopUp(bird2PopUp, bird2Audio, 2);
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

            LetterTracingW2 tracingScript = null;
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
