using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GameSessionworld2
{
    public static string CurrentLetter;
    public static string ReturnScene;

    public static Vector3 PlayerPos;
    public static Quaternion PlayerRot;
    public static bool HasSavedPlayerState = false;

    public static string LetterJustLearned;

    public static bool SuppressNextPopupOnce = false;

    private static bool blockPopupsUntilMoved = false;
    private static Vector3 blockOrigin;
    private static float blockDistance = 1.5f;

    public static void SavePlayerState(Transform player)
    {
        if (player == null) return;
        PlayerPos = player.position;
        PlayerRot = player.rotation;
        HasSavedPlayerState = true;
    }

    public static void LaunchAR(string letter)
    {
        CurrentLetter = letter;
        ReturnScene = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene("ARWORLD2", LoadSceneMode.Single);
    }

    public static void CompleteARAndReturn()
    {
        LetterJustLearned = CurrentLetter;

        SceneManager.sceneLoaded += OnWorldLoaded;
        ReturnToWorld();
    }

    public static void ReturnToWorld()
    {

        var scene = string.IsNullOrEmpty(ReturnScene) ? "World(2)" : ReturnScene;
        SceneManager.LoadScene(scene, LoadSceneMode.Single);
    }

    private static void OnWorldLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnWorldLoaded;

        var w = Object.FindObjectOfType<Mankibo.World2>();
        if (w != null && HasSavedPlayerState)
        {
            w.ApplySavedStateFromGameSession();
            w.ResumeAfterReturn(LetterJustLearned);

            ActivateGlobalPopupBlock(w.transform, 0.01f);

            w.StartCoroutine(ClearSavedFlagNextFrame());
        }
        else
        {
            _ = TempRunner.Run(ClearNonCriticalNextFrame());
        }
    }

    private static IEnumerator ClearSavedFlagNextFrame()
    {
        yield return null;
        HasSavedPlayerState = false;
        CurrentLetter = null;
        LetterJustLearned = null;
        ReturnScene = null;
    }

    private static IEnumerator ClearNonCriticalNextFrame()
    {
        yield return null;
        CurrentLetter = null;
        LetterJustLearned = null;
        ReturnScene = null;
    }

    public static void ActivateGlobalPopupBlock(Transform player, float minMoveDistance)
    {
        if (player == null) return;
        blockOrigin = player.position;
        blockDistance = Mathf.Max(0.001f, minMoveDistance);
        blockPopupsUntilMoved = true;
    }

    public static bool ArePopupsGloballyBlocked(Transform player)
    {
        if (!blockPopupsUntilMoved || player == null) return false;

        if (Vector3.Distance(player.position, blockOrigin) >= blockDistance)
        {
            // اللاعب ابتعد كفاية — فك الحظر
            blockPopupsUntilMoved = false;
            return false;
        }
        return true;
    }
    public static bool ShouldSuppressPopupNow() => SuppressNextPopupOnce;
    public static void ConsumePopupSuppression() => SuppressNextPopupOnce = false;

    private class TempRunner : MonoBehaviour
    {
        public static Coroutine Run(IEnumerator routine)
        {
            var host = new GameObject("[GameSessionworld2.TempRunner]").AddComponent<TempRunner>();
            DontDestroyOnLoad(host.gameObject);
            return host.StartCoroutine(host.SelfDestruct(routine));
        }

        private IEnumerator SelfDestruct(IEnumerator routine)
        {
            yield return StartCoroutine(routine);
            Destroy(gameObject);
        }
    }
}
