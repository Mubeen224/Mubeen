using UnityEngine;
using TMPro;

public class AndroidKeyboardShift : MonoBehaviour
{
    [Header("Panel to move")]
    public RectTransform panel;          // «·»«‰·/«·»Ê» √» «··Ì ›ÌÂ «·ÕﬁÊ·

    [Header("Watched Inputs")]
    public TMP_InputField[] inputs;      // ÕﬁÊ· «·≈œŒ«· «· Ì ‰—Ìœ   »⁄Â«

    [Header("Settings")]
    public float shiftAmount = 350f;     // ﬂ„ ‰—›⁄ «·»«‰· ⁄‰œ ŸÂÊ— «·ﬂÌ»Ê—œ

    private Vector2 originalPos;
    private bool isShifted = false;

    void Start()
    {
        if (panel == null)
            panel = GetComponent<RectTransform>();

        originalPos = panel.anchoredPosition;
    }

    void Update()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        bool anyFocused = false;

        // Â· √Ì Õﬁ· „‰ «·ÕﬁÊ· „—ﬂ¯“ ⁄·ÌÂ «·¬‰ø
        foreach (var input in inputs)
        {
            if (input != null && input.isFocused)
            {
                anyFocused = true;
                break;
            }
        }

        if (anyFocused && TouchScreenKeyboard.visible)
        {
            if (!isShifted)
            {
                panel.anchoredPosition = originalPos + new Vector2(0, shiftAmount);
                isShifted = true;
            }
        }
        else
        {
            if (isShifted)
            {
                panel.anchoredPosition = originalPos;
                isShifted = false;
            }
        }
#endif
    }
}
