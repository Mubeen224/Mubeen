using System.Collections;
using UnityEngine;

public class IntroCardsManagerW1 : MonoBehaviour
{
    [Header("Only For Letter")]
    public string onlyForLetter = "ذ";

    [Header("Voice Over")]
    public AudioSource narrationSource;
    public AudioClip introClip;

    [Header("Objects To Hide During Narration")]
    public GameObject[] objectsToHide;

    private void OnEnable()
    {
        if (!string.IsNullOrEmpty(onlyForLetter) && GameSession.CurrentLetter != onlyForLetter)
        {
            gameObject.SetActive(false);
            return;
        }
    }

    private IEnumerator Start()
    {
        foreach (var obj in objectsToHide) if (obj) obj.SetActive(false);

        if (narrationSource && introClip)
        {
            narrationSource.clip = introClip;
            narrationSource.Play();
            yield return new WaitForSeconds(introClip.length);
        }

        foreach (var obj in objectsToHide) if (obj) obj.SetActive(true);
    }
}
