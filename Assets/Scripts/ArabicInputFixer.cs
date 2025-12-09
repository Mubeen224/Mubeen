using UnityEngine;
using TMPro;
using ArabicSupport;

public class ArabicInputFixer : MonoBehaviour
{
    public TMP_InputField inputField;

    void Start()
    {
        if (inputField == null)
            inputField = GetComponent<TMP_InputField>();

        // äÊÃßÏ Ãä TextMeshPro ãÇ íÚßÓ ÇáäÕ ãÑÉ ËÇäíÉ
        if (inputField.textComponent != null)
            inputField.textComponent.isRightToLeftText = false;

        // ßá ãÇ ÇáãÓÊÎÏã íÛíøÑ ÇáäÕ¡ äÍÏøË *ÇáÚÑÖ İŞØ*
        inputField.onValueChanged.AddListener(OnValueChangedArabic);
    }

    private void OnDestroy()
    {
        inputField.onValueChanged.RemoveListener(OnValueChangedArabic);
    }

    void OnValueChangedArabic(string value)
    {
        // äÚÏá İŞØ ÇáäÕ ÇáãÚÑæÖ İí ÇáÜ textComponent
        string fixedText = ArabicFixer.Fix(value, showTashkeel: false, useHinduNumbers: true);
        inputField.textComponent.text = fixedText;
        // áÇ äáãÓ inputField.text ÃÈÏÇğ åäÇ
    }
}
