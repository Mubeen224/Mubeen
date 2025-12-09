using UnityEngine;
using UnityEngine.UI;
using ArabicSupport;

public class arabicInput : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private Text placeholder;     // UI Text used as the input field's placeholder
    [SerializeField] private Text previewText;     // UI Text used to display the real-time preview of Arabic input

    private InputField inputField; // Reference to the InputField component

    void Start()
    {
        // Fix the placeholder text to properly display Arabic letters
        if (placeholder != null)
        {
            placeholder.text = ArabicFixer.Fix(placeholder.text);
        }

        // Get the InputField component from this GameObject
        inputField = GetComponent<InputField>();
        if (inputField == null)
        {
            Debug.LogError("InputField component not found on this GameObject!");
            return;
        }

        // Add a listener to update the preview text whenever input changes
        inputField.onValueChanged.AddListener(UpdatePreview);
    }

    // Called every time the input text changes
    void UpdatePreview(string rawInput)
    {
        if (previewText == null) return;

        if (!string.IsNullOrWhiteSpace(rawInput))
        {
            // Fix the text for correct Arabic rendering and display it in the preview
            previewText.text = ArabicFixer.Fix(rawInput);
        }
        else
        {
            // If input is empty, clear the preview
            previewText.text = "";
        }
    }
}
