using UnityEngine;
using UnityEngine.UI;

public class SimplePieFill : MonoBehaviour
{
    [Header("Assign the Image with Fill Method = Radial360")]
    public Image pieImage;

    [Tooltip("For debugging only. At runtime this is set from other scripts (e.g., LevelCircleController).")]
    [Range(0f, 100f)]
    public float percentage = 0f;

    private void Start()
    {
        UpdatePie();
    }

    private void OnValidate()
    {
        UpdatePie();
    }

    /// <summary>
    /// Call this function to update the pie fill from code.
    /// Example (from LevelCircleController): quizOverallPieFiller.SetFill(75f);
    /// </summary>
    public void SetFill(float percent)
    {
        percentage = Mathf.Clamp(percent, 0f, 100f);
        UpdatePie();
    }

    private void UpdatePie()
    {
        if (pieImage != null)
        {
            // Unity fillAmount works from 0 to 1, so divide by 100
            pieImage.fillAmount = percentage / 100f;
        }
    }
}
