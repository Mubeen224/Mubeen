using Mankibo; 
using UnityEngine; 
using UnityEngine.UI; 
using UnityEngine.SceneManagement; 

public class PopUpTriggerWorld3 : MonoBehaviour
{
    [Header("Pop-Up Panels")]
    public GameObject womanPopUp; // Pop-up shown when colliding with the woman character
    public GameObject squirrelPopUp; // Pop-up for the squirrel
    public GameObject owlPopUp; // Pop-up for the owl
    public GameObject levelFinishedCanvas; // Pop-up shown when level is finished

    [Header("Audio Clips")]
    public AudioSource womanAudio; // Audio for the woman pop-up
    public AudioSource squirrelAudio; // Audio for the squirrel pop-up
    public AudioSource owlAudio; // Audio for the owl pop-up
    public AudioSource levelFinishedAudio; // Audio when finishing the level

    [Header("Close Buttons")]
    public Button womanCloseButton; // Close button for the woman pop-up
    public Button squirrelCloseButton; // Close button for the squirrel pop-up
    public Button owlCloseButton; // Close button for the owl pop-up
    public Button homePageButton; // Button to go to the homepage after level finish

    private World3 playerScript; // Reference to the player movement/logic script
    private MathExitManagerW3 mathManager; // Reference to the math challenge manager

    private GameObject currentActivePopUp; // Keeps track of the currently open pop-up
    private int currentTracingIndex; // The index of the tracing canvas/script being used

    public static PopUpTriggerWorld3 instance; // Singleton instance

    private void Awake()
    {
        instance = this; // Assign this object to the singleton instance
    }

    private void Start()
    {
        // Find required scripts
        playerScript = FindObjectOfType<World3>();
        mathManager = FindObjectOfType<MathExitManagerW3>();

        // Hide all pop-up panels at the start
        if (womanPopUp) womanPopUp.SetActive(false);
        if (squirrelPopUp) squirrelPopUp.SetActive(false);
        if (owlPopUp) owlPopUp.SetActive(false);
        if (levelFinishedCanvas) levelFinishedCanvas.SetActive(false);

        // Set up button listeners
        if (womanCloseButton) womanCloseButton.onClick.AddListener(OpenMathBeforeClosing);
        if (squirrelCloseButton) squirrelCloseButton.onClick.AddListener(OpenMathBeforeClosing);
        if (owlCloseButton) owlCloseButton.onClick.AddListener(OpenMathBeforeClosing);
        if (homePageButton) homePageButton.onClick.AddListener(GoToHomePage);
    }

    private void OnTriggerEnter(Collider other)
    {
        // Trigger pop-up if the player collides with a specific object
        if (other.CompareTag("Player"))
        {
            if (gameObject.CompareTag("Woman"))
                ShowPopUp(womanPopUp, womanAudio, 0); // Show woman pop-up and start tracing index 0
            else if (gameObject.CompareTag("Squirrel"))
                ShowPopUp(squirrelPopUp, squirrelAudio, 1); // Tracing index 1
            else if (gameObject.CompareTag("Owl"))
                ShowPopUp(owlPopUp, owlAudio, 2); // Tracing index 2
            else if (gameObject.CompareTag("Finish3"))
                ShowLevelFinished(); // If player reaches end of level
        }
    }

    private void ShowPopUp(GameObject popUp, AudioSource audioSource, int tracingIndex)
    {
        if (popUp) popUp.SetActive(true); // Show the selected pop-up
        if (audioSource) audioSource.Play(); // Play related audio

        if (playerScript != null)
            playerScript.canMove = false; // Prevent player from moving

        currentActivePopUp = popUp; // Store reference to the current pop-up
        currentTracingIndex = tracingIndex; // Save current tracing index

        // Start tracing logic if applicable
        if (mathManager != null && tracingIndex < mathManager.tracingCanvases.Count)
        {
            if (mathManager.tracingCanvases[tracingIndex] != null)
                mathManager.tracingCanvases[tracingIndex].SetActive(true);

            if (mathManager.tracingScripts[tracingIndex] != null)
            {
                var tracingScript = mathManager.tracingScripts[tracingIndex];

                if (!tracingScript.gameObject.activeSelf)
                    tracingScript.gameObject.SetActive(true); // Make sure script object is active

                tracingScript.StartNewAttempt(); // Begin a new tracing session
                tracingScript.SetCanTraceAfterAudio(audioSource); // Wait until audio finishes
            }
        }
    }

    private void OpenMathBeforeClosing()
    {
        // Opens the math challenge and sets a callback to close the pop-up after success
        if (mathManager != null && currentActivePopUp != null)
        {
            mathManager.OpenMathCanvasWithCallback(CloseCurrentPopUp, currentTracingIndex);
        }
    }

    private void CloseCurrentPopUp()
    {
        // Closes the active pop-up
        if (currentActivePopUp != null)
        {
            currentActivePopUp.SetActive(false);
            currentActivePopUp = null;

            if (playerScript != null)
            {
                playerScript.canMove = true; // Allow player movement again
                playerScript.Idle(); // Switch player to idle animation
            }
        }
    }

    private void ShowLevelFinished()
    {
        // Called when the player reaches the finish line

        CoinsManager.instance.AddCoinsToSelectedChild(10); // Award 10 coins
        StarsManager.instance.AddStarsToSelectedChild(1); 

        if (levelFinishedAudio != null)
            levelFinishedAudio.Play(); // Play win sound

        if (levelFinishedCanvas)
            levelFinishedCanvas.SetActive(true); // Show level finish screen

        if (playerScript != null)
        {
            playerScript.canMove = false; // Stop movement
            playerScript.Idle(); // Show idle animation
        }
    }

    public void GoToHomePage()
    {
        // Load the home page scene
        SceneManager.LoadScene("HomePage");
    }
}
