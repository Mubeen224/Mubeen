using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class PermissionChecker : MonoBehaviour
{
    [Header("UI")]
    public GameObject permissionPanel;    // اللوحة التي تظهر للوالد
    public TMP_Text messageText;          // النص التوضيحي
    public Button requestButton;          // زر طلب الإذن

    private bool camGranted = false;
    private bool micGranted = false;

    private void Start()
    {
        permissionPanel.SetActive(false);
        StartCoroutine(CheckPermissions());
    }

    private IEnumerator CheckPermissions()
    {
        // فحص الكاميرا
        camGranted = Application.HasUserAuthorization(UserAuthorization.WebCam);

        // فحص المايك
        micGranted = Application.HasUserAuthorization(UserAuthorization.Microphone);

        // إذا كلاهما مُفعّلان → نكمل
        if (camGranted && micGranted)
        {
            OnPermissionsGranted();
            yield break;
        }

        // عرض الشاشة للوالد إذا في إذن مفقود
        permissionPanel.SetActive(true);

        messageText.text =
            ".ﺢﻴﺤﺻ ﻞﻜﺸﺑ ﺕﻮﺼﻟﺍ ﻰﻠﻋ ﻑﺮﻌﺘﻟﺍﻭ ﺯﺰﻌﻤﻟﺍ ﻊﻗﺍﻮﻟﺍ ﺔﻄﺸﻧﺃ ﻞﻤﻌﺗ ﻲﻜﻟ ﻥﻮﻓﻭﺮﻜﻴﻤﻟﺍﻭ ﺍﺮﻴﻣﺎﻜﻟﺍ ﻥﺫﺇ ﻰﻟﺇ ﺝﺎﺘﺤﻳ ﻖﻴﺒﻄﺘﻟﺍ";

        requestButton.onClick.RemoveAllListeners();
        requestButton.onClick.AddListener(() =>
        {
            StartCoroutine(RequestPermissions());
        });
    }

    private IEnumerator RequestPermissions()
    {
        // طلب إذن الكاميرا
        if (!camGranted)
        {
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            camGranted = Application.HasUserAuthorization(UserAuthorization.WebCam);
        }

        // طلب إذن المايك
        if (!micGranted)
        {
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
            micGranted = Application.HasUserAuthorization(UserAuthorization.Microphone);
        }

        // إذا قبلوا كلها → نكمل
        if (camGranted && micGranted)
        {
            OnPermissionsGranted();
        }
        else
        {
            messageText.text =
                ".ﺕﺎﻴﺣﻼﺼﻟﺍ ﻩﺬﻬﺑ ﺡﺎﻤﺴﻟﺍ ﻥﻭﺪﺑ ﺕﻮﺼﻟﺍ ﻰﻠﻋ ﻑﺮﻌﺘﻟﺍ ﻭﺃ ﺯﺰﻌﻤﻟﺍ ﻊﻗﺍﻮﻟﺍ ﺔﻄﺸﻧﺃ ﻞﻴﻐﺸﺗ ﻦﻜﻤﻳ ﻻn\\n\\.ﻥﻮﻓﻭﺮﻜﻳﺎﻤﻟﺍ ﻭﺃ ﺍﺮﻴﻣﺎﻜﻟﺍ ﻥﺫﺇ ﺢﻨﻣ ﻢﺘﻳ ﻢﻟ";
        }
    }

    // عندما يتم قبول كل التصاريح
    private void OnPermissionsGranted()
    {
        Debug.Log("All permissions granted.");
        permissionPanel.SetActive(false);

    }
}
