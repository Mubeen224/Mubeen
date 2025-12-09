using System.Collections;
using UnityEngine;

public class W4IntroCardsManager : MonoBehaviour
{
    [Header("Voice Over")]
    public AudioSource narrationSource;
    public AudioClip introClip;

    [Header("Objects To Hide During Narration")]
    public GameObject[] objectsToHide;

    private void OnEnable()
    {
        StartCoroutine(PlayIntro());
    }

    private IEnumerator PlayIntro()
    {
        foreach (var obj in objectsToHide)
            if (obj != null) obj.SetActive(false);

        if (narrationSource != null && introClip != null)
        {
            narrationSource.clip = introClip;
            narrationSource.Play();
            yield return new WaitForSeconds(introClip.length);
        }

        foreach (var obj in objectsToHide)
            if (obj != null) obj.SetActive(true);
    }
}