using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Global static session manager used to:
/// 1) Launch AR scene from the letter world.
/// 2) Save/restore player state when returning.
/// 3) Track which letter was just learned.
/// 4) (اختياري) Suppress popups right after returning.
/// </summary>
public static class GameSession
{
    // ===========================
    // Session / Scene State
    // ===========================

    public static string CurrentLetter;  // Letter currently being learned / hunted in AR
    public static string ReturnScene;    // Scene name to return to after AR

    // ===========================
    // Player State Saving
    // ===========================

    public static Vector3 PlayerPos;                // Saved player world position
    public static Quaternion PlayerRot;             // Saved player world rotation
    public static bool HasSavedPlayerState = false; // Whether a valid saved state exists

    public static string LetterJustLearned;         // Letter completed in AR (for resuming progress)

    // ===========================
    // Popup Suppression (Fallback)
    // ===========================

    public static bool SuppressNextPopupOnce = false;

    // ===========================
    // Global Popup Block Until Player Moves
    // ===========================

    // Blocks popups globally until player moves a minimum distance after returning
    private static bool blockPopupsUntilMoved = false;
    private static Vector3 blockOrigin;        // Player position at the moment of return
    private static float blockDistance = 1.5f; // Required distance to unlock popups (can be overridden)

    // ===========================
    // Player State APIs
    // ===========================

    /// <summary>
    /// Saves the player's transform (position + rotation)
    /// so we can restore it after AR return.
    /// </summary>
    public static void SavePlayerState(Transform player)
    {
        if (player == null) return;
        PlayerPos = player.position;
        PlayerRot = player.rotation;
        HasSavedPlayerState = true;
    }

    // ===========================
    // AR Launch / Return Flow
    // ===========================

    /// <summary>
    /// Launches the AR scene:
    /// - Store current letter
    /// - Store current scene name for return
    /// - Load AR scene
    /// </summary>
    public static void LaunchAR(string letter)
    {
        CurrentLetter = letter;
        ReturnScene = SceneManager.GetActiveScene().name;

        // Load AR scene as single scene (replace current)
        SceneManager.LoadScene("AR_World1", LoadSceneMode.Single);
    }

    /// <summary>
    /// Called when AR activity is completed successfully.
    /// Sets flags and returns to the previous world scene.
    /// </summary>
    public static void CompleteARAndReturn()
    {
        // Mark this letter as just learned for resume logic in World1
        LetterJustLearned = CurrentLetter;

        SceneManager.sceneLoaded += OnWorldLoaded;

        // Return to world scene
        ReturnToWorld();
    }

    /// <summary>
    /// Loads the return world scene.
    /// If ReturnScene is empty, fallback to default "World(1)".
    /// </summary>
    public static void ReturnToWorld()
    {
        var scene = string.IsNullOrEmpty(ReturnScene) ? "World(1)" : ReturnScene;
        SceneManager.LoadScene(scene, LoadSceneMode.Single);
    }

    /// <summary>
    /// SceneLoaded callback after returning from AR.
    /// Restores player state and activates popup blocking.
    /// </summary>
    private static void OnWorldLoaded(Scene scene, LoadSceneMode mode)
    {
        // Unsubscribe immediately to avoid multiple triggers
        SceneManager.sceneLoaded -= OnWorldLoaded;

        // Find World1 manager script (your island/world controller)
        var w = Object.FindObjectOfType<Mankibo.World1>();

        if (w != null && HasSavedPlayerState)
        {
            // Apply saved player position/rotation
            w.ApplySavedStateFromGameSession();

            // Resume logic using the learned letter (e.g., unlock, stop AR triggers)
            w.ResumeAfterReturn(LetterJustLearned);

            // Activate global popup block until player moves slightly
            ActivateGlobalPopupBlock(w.transform, 0.01f);

            // Clear saved flags next frame (after Awake/Start finish)
            w.StartCoroutine(ClearSavedFlagNextFrame());
        }
        else
        {
            // If World1 isn't found or no saved state, clear non-critical flags safely
            _ = TempRunner.Run(ClearNonCriticalNextFrame());
        }
    }

    // ===========================
    // Clear Flags after Return
    // ===========================

    /// <summary>
    /// Clears all return-related flags next frame.
    /// </summary>
    private static IEnumerator ClearSavedFlagNextFrame()
    {
        yield return null;

        HasSavedPlayerState = false;
        CurrentLetter = null;
        LetterJustLearned = null;
        ReturnScene = null;
    }

    /// <summary>
    /// Clears non-critical flags next frame when no saved state restoration happened.
    /// </summary>
    private static IEnumerator ClearNonCriticalNextFrame()
    {
        yield return null;

        CurrentLetter = null;
        LetterJustLearned = null;
        ReturnScene = null;
    }

    // ===========================
    // Global Popup Block Logic
    // ===========================

    /// <summary>
    /// Enables a global popup block right after returning to world.
    /// Popups stay blocked until player moves "minMoveDistance".
    /// </summary>
    public static void ActivateGlobalPopupBlock(Transform player, float minMoveDistance)
    {
        if (player == null) return;

        blockOrigin = player.position;
        blockDistance = Mathf.Max(0.001f, minMoveDistance);
        blockPopupsUntilMoved = true;
    }

    /// <summary>
    /// Checks if popups should remain blocked.
    /// Once player moves >= blockDistance, block is lifted.
    /// </summary>
    public static bool ArePopupsGloballyBlocked(Transform player)
    {
        if (!blockPopupsUntilMoved || player == null) return false;

        if (Vector3.Distance(player.position, blockOrigin) >= blockDistance)
        {
            // Player moved enough => lift global block
            blockPopupsUntilMoved = false;
            return false;
        }
        return true;
    }

    // ===========================
    // One-time Popup Suppression (Fallback)
    // ===========================

    /// <summary>
    /// Returns whether the next popup should be suppressed (one-time).
    /// (حاليًا لن تُفعل إلا إذا استُخدمت من مكان آخر)
    /// </summary>
    public static bool ShouldSuppressPopupNow() => SuppressNextPopupOnce;

    /// <summary>
    /// Consumes the one-time suppression flag after being used.
    /// </summary>
    public static void ConsumePopupSuppression() => SuppressNextPopupOnce = false;

    // ===========================
    // Temp Coroutine Runner
    // ===========================

    /// <summary>
    /// Temporary MonoBehaviour host to run coroutines
    /// when we don't have a valid scene object ready.
    /// </summary>
    private class TempRunner : MonoBehaviour
    {
        public static Coroutine Run(IEnumerator routine)
        {
            var host = new GameObject("[GameSession.TempRunner]").AddComponent<TempRunner>();
            Object.DontDestroyOnLoad(host.gameObject);
            return host.StartCoroutine(host.SelfDestruct(routine));
        }

        /// <summary>
        /// Runs the given coroutine then destroys the host object.
        /// </summary>
        private IEnumerator SelfDestruct(IEnumerator routine)
        {
            yield return StartCoroutine(routine);
            Destroy(gameObject);
        }
    }
}
