using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Firebase;
using Firebase.Auth;
using Firebase.Database;
using TMPro;

public class AuthHandler : MonoBehaviour
{
    [Header("Firebase")]
    private FirebaseAuth auth; // Firebase Authentication instance
    private FirebaseUser user; // Currently authenticated user
    private DatabaseReference databaseReference; // Reference to Firebase Realtime Database

    [Header("Signup Fields")]
    public TMP_InputField signupEmail;       // Input field for sign-up email
    public TMP_InputField signupPassword;    // Input field for sign-up password
    public TMP_InputField confirmPassword;   // Input field to confirm password
    public Button signupButton;              // Sign-up button

    [Header("Login Fields")]
    public TMP_InputField loginEmail;        // Input field for login email
    public TMP_InputField loginPassword;     // Input field for login password
    public Button loginButton;               // Login button

    [Header("UI Panels")]
    public GameObject loginPanel;            // Panel for login UI
    public GameObject signupPanel;           // Panel for sign-up UI

    void Start()
    {
        // Initialize Firebase Auth and Database
        auth = FirebaseAuth.DefaultInstance;
        databaseReference = FirebaseDatabase.DefaultInstance.RootReference;

        // Connect buttons to their respective methods
        signupButton.onClick.AddListener(() => StartCoroutine(RegisterUser()));
        loginButton.onClick.AddListener(() => StartCoroutine(LoginUser()));
    }

    // Register a new user with Firebase Authentication
    private IEnumerator RegisterUser()
    {
        string email = signupEmail.text;
        string password = signupPassword.text;
        string confirmPass = confirmPassword.text;

        // Check if passwords match
        if (password != confirmPass)
        {
            Debug.LogError("Passwords do not match!");
            yield break;
        }

        // Start Firebase sign-up task
        Task<AuthResult> registerTask = auth.CreateUserWithEmailAndPasswordAsync(email, password);

        // Wait until the task finishes
        yield return new WaitUntil(() => registerTask.IsCompleted);

        if (registerTask.IsFaulted || registerTask.IsCanceled)
        {
            Debug.LogError("User registration failed: " + registerTask.Exception);
            yield break;
        }

        user = registerTask.Result.User;
        Debug.Log("User registered successfully: " + user.Email);

        // Save user data to Realtime Database
        SaveUserData(user.UserId, email);
    }

    // Login existing user with Firebase Authentication
    private IEnumerator LoginUser()
    {
        string email = loginEmail.text;
        string password = loginPassword.text;

        // Start Firebase login task
        Task<AuthResult> loginTask = auth.SignInWithEmailAndPasswordAsync(email, password);

        // Wait until the task finishes
        yield return new WaitUntil(() => loginTask.IsCompleted);

        if (loginTask.IsFaulted || loginTask.IsCanceled)
        {
            Debug.LogError("Login failed: " + loginTask.Exception);
            yield break;
        }

        user = loginTask.Result.User;
        Debug.Log("Login successful: " + user.Email);
    }

    // Save newly registered user info to Firebase Realtime Database
    void SaveUserData(string userId, string email)
    {
        User newUser = new User(email); // Create a new user object
        string json = JsonUtility.ToJson(newUser); // Convert to JSON format
        databaseReference.Child("parents").Child(userId).SetRawJsonValueAsync(json); // Save under "parents/userId"
    }
}

// User data class for Firebase storage
public class User
{
    public string email;
    public User(string _email)
    {
        email = _email;
    }
}
