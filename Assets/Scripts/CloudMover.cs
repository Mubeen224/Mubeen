using UnityEngine;

public class CloudMover : MonoBehaviour
{
    public float speed = 30f; // Speed at which the cloud moves (can be adjusted in the Inspector)
    public float resetPositionX = -1200f; // The x-position at which the cloud resets to the other side
    public float startPositionX = 1200f; // The starting x-position from the right side

    private RectTransform rectTransform; // Reference to the UI RectTransform

    void Start()
    {
        rectTransform = GetComponent<RectTransform>(); // Get RectTransform component attached to the cloud
    }

    void Update()
    {
        // Move the cloud to the left every frame
        rectTransform.anchoredPosition += Vector2.left * speed * Time.deltaTime;

        // If the cloud moves past the reset point, send it back to the start position on the right
        if (rectTransform.anchoredPosition.x < resetPositionX)
        {
            rectTransform.anchoredPosition = new Vector2(startPositionX, rectTransform.anchoredPosition.y);
        }
    }
}
