using UnityEngine;
using UnityEngine.SceneManagement;

public class GoToHomePageW1: MonoBehaviour
{
    [Header("UI Elements")]
    public GameObject confirmationCanvas1;

    private void Start()
    {
        if (confirmationCanvas1 != null)
            confirmationCanvas1.SetActive(false); // Hide ConfirmationCanvas at the start
    }

    // Called when the top-left Home button is clicked
    public void ShowConfirmationPopup()
    {
        if (confirmationCanvas1 != null)
            confirmationCanvas1.SetActive(true); // Show the confirmation pop-up
    }

    // Called when the "Back to Home" button inside ConfirmationCanvas is clicked
    public void LoadHomePage()
    {
        SceneManager.LoadScene("HomePage"); // Load HomePage scene
    }

    // Called when the "Stay" button inside ConfirmationCanvas is clicked
    public void StayInGame()
    {
        if (confirmationCanvas1 != null)
            confirmationCanvas1.SetActive(false); // Hide the confirmation pop-up
    }
}
