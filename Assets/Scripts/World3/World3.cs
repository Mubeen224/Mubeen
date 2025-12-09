using UnityEngine;

namespace Mankibo
{
    [RequireComponent(typeof(CharacterController))]
    public class World3 : CharacterAction, IMoveable
    {
        [Header("Character Settings")]
        public bool canMove = true;

        [Header("Audio")]
        public AudioSource footstepAudio;

        protected virtual void Awake()
        {
            animator = GetComponent<Animator>();
            controller = GetComponent<CharacterController>();

            // Initial spawn
            transform.position = new Vector3(-11.99f, -6.75f, transform.position.z);
        }

        protected virtual void Update()
        {
            if (!canMove)
            {
                if (footstepAudio && footstepAudio.isPlaying)
                    footstepAudio.Stop();
                return;
            }

            float dt = Time.deltaTime;
            isGrounded = controller.isGrounded;
            charPosition = transform.localPosition;

            velocity.x = 0f;

            if (charMovement.x != 0f)
            {
                velocity.x = moveSpeed * Mathf.Sign(charMovement.x);

                if (footstepAudio && !footstepAudio.isPlaying)
                    footstepAudio.Play();
            }
            else
            {
                if (footstepAudio && footstepAudio.isPlaying)
                    footstepAudio.Stop();
            }

            controller.Move(velocity * dt);
        }

        // Stop any looping audio if this character gets deactivated
        private void OnDisable()
        {
            if (footstepAudio && footstepAudio.isPlaying)
                footstepAudio.Stop();
        }

        #region BASIC ACTION CHARACTER
        public virtual void Idle()
        {
            charMovement = Vector2.zero;
            ChangeSpeed(0f);
        }

        public virtual void Move(Vector2 position)
        {
            if (!canMove) return;

            charMovement = new Vector2(position.x, 0f);

            if (charMovement.x != 0f)
            {
                if ((charMovement.x < 0f && facingRight) || (charMovement.x > 0f && !facingRight))
                    Flip();
            }

            ChangeSpeed(moveSpeed);
        }

        public virtual void ReleaseMove(Vector2 position)
        {
            if (!canMove) return;

            charMovement = Vector2.zero;
            Idle();
        }
        #endregion

        public virtual void ChangeSpeed(float m_speed)
        {
            currentSpeed = m_speed;
            animator?.SetFloat("Move", m_speed);
        }

        protected void Flip()
        {
            facingRight = !facingRight;
            Vector3 theScale = transform.localScale;
            theScale.x *= -1f;
            transform.localScale = theScale;
        }

        // Placeholders
        public void GetDamage(float damageValue) { }
        public void Dead() { }
    }
}
