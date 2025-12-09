using UnityEngine;

public class RepeatingBackground : MonoBehaviour
{
    private float width;           // Width of the background sprite
    private Vector3 startPos;      // Initial position of the background

    void Start()
    {
        startPos = transform.position; // Store the starting position of the background
        width = GetComponent<SpriteRenderer>().bounds.size.x; // Calculate the width of the sprite
    }

    void Update()
    {
        // Check if the background has moved completely to the left out of view
        if (transform.position.x < startPos.x - width)
        {
            RepositionBackground(); // Reposition it to the right
        }
    }

    private void RepositionBackground()
    {
        // Move the background to the right by twice its width to create a seamless loop
        transform.position += new Vector3(width * 2f, 0, 0);
    }
}
