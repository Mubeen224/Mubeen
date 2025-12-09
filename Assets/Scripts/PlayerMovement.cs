using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float speed = 5f;                        // Movement speed of the player
    private Rigidbody2D rb;                         // Reference to the Rigidbody2D component
    private Vector2 movement;                       // Stores the input direction

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();           // Get and store the Rigidbody2D component
    }

    void Update()
    {
        movement.x = Input.GetAxis("Horizontal");   // Capture horizontal input (A/D or Left/Right arrows)
    }

    void FixedUpdate()
    {
        rb.linearVelocity = new Vector2(movement.x * speed, rb.linearVelocity.y); // Apply horizontal movement to Rigidbody
        
    }
}

