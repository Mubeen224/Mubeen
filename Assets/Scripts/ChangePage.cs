using UnityEngine;
using UnityEngine.SceneManagement;

public class ChangePage : MonoBehaviour
{
    // Reference to the Parent Access Manager which handles math verification
    public ParentAreaAccessManager parentAccessManager;

    // Loads World 1 scene
    public void GoToWorld1()
    {
        SceneManager.LoadScene("World(1)");
    }

    // Loads World 2 scene
    public void GoToWorld2()
    {
        SceneManager.LoadScene("World(2)");
    }

    // Loads World 3 scene
    public void GoToWorld3()
    {
        SceneManager.LoadScene("World(3)");
    }

    // Loads World 4 scene
    public void GoToWorld4()
    {
        SceneManager.LoadScene("World(4)");
    }

    // Requests access to Parent Info screen using math challenge
    public void LoadParentInfo()
    {
        if (parentAccessManager != null)
            parentAccessManager.RequestAccess("ParentINFO");
        else
            Debug.LogError("Parent Access Manager not assigned!");
    }

    // Requests access to Children Page using math challenge
    public void GoToChildren()
    {
        if (parentAccessManager != null)
            parentAccessManager.RequestAccess("Children"); // Adjust scene name if needed
        else
            Debug.LogError("Parent Access Manager not assigned!");
    }

    // Loads the store scene
    public void GoTostore()
    {
        SceneManager.LoadScene("store");
    }

    // Loads the home page
    public void GoTohomepage()
    {
        SceneManager.LoadScene("HomePage");
    }

    // Loads the child's profile scene
    public void GoToChildProfile()
    {
        SceneManager.LoadScene("ChildInformation");
    }

    // Loads the child's progress scene
    public void GoToChildProgress()
    {
        SceneManager.LoadScene("childprognew");
    }
}
