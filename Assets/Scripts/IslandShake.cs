using UnityEngine;

public class IslandFloat : MonoBehaviour
{
    public float floatStrength = 10f; // Height of the floating effect (how far up and down it moves)
    public float speed = 2f;          // Speed of the floating animation (how fast it moves)

    private Vector3 initialPosition; // Stores the original position of the object

    void Start()
    {
        // Save the initial local position of the island
        initialPosition = transform.localPosition;
    }

    void Update()
    {
        // Calculate vertical offset using a sine wave
        float offset = Mathf.Sin(Time.time * speed) * floatStrength;

        // Apply the offset to create a smooth floating motion
        transform.localPosition = initialPosition + new Vector3(0, offset, 0);
    }
}
