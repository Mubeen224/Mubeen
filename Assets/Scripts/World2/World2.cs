using UnityEngine;

namespace Mankibo
{
    /// <summary>
    /// Player controller for World(2) island.
    /// - لا يفرض تموضعًا افتراضيًا عند عدم وجود حالة محفوظة (مطابق لجزيرة 1).
    /// - Safe restore after AR return (CharacterController toggle).
    /// - ResumeAfterReturn hook to continue gameplay.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class World2 : CharacterAction, IMoveable
    {
        [Header("Character Settings")]
        public bool canMove = true;

        [Header("Audio")]
        public AudioSource footstepAudio;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            controller = GetComponent<CharacterController>();

            // ✨ لا نُعيد التموضع للبداية في Awake.
            // إن وُجدت حالة محفوظة سيتم تطبيقها هنا أو في Start كشبكة أمان (مطابق لجزيرة 1).
            if (GameSessionworld2.HasSavedPlayerState)
            {
                ApplySavedStateFromGameSession();
            }
        }

        private void Start()
        {
            // احتياط إضافي لو كان OnWorldLoaded لم يُطبّق الحالة بعد
            if (GameSessionworld2.HasSavedPlayerState)
                ApplySavedStateFromGameSession();
        }

        private void Update()
        {
            if (!canMove)
            {
                if (footstepAudio != null && footstepAudio.isPlaying)
                    footstepAudio.Stop();
                return;
            }

            float dt = Time.deltaTime;
            isGrounded = controller.isGrounded;
            charPosition = transform.localPosition;

            velocity.x = 0;

            if (charMovement.x != 0)
            {
                velocity.x = moveSpeed * Mathf.Sign(charMovement.x);

                if (footstepAudio != null && !footstepAudio.isPlaying)
                    footstepAudio.Play();
            }
            else
            {
                if (footstepAudio != null && footstepAudio.isPlaying)
                    footstepAudio.Stop();
            }

            controller.Move(velocity * dt);
        }

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

            charMovement = new Vector2(position.x, 0);

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
            theScale.x *= -1;
            transform.localScale = theScale;
        }

        public void GetDamage(float damageValue) { }
        public void Dead() { }

        // ===== Safe restore (لا نُصفّر الفلاغ هنا، يصفّره GameSessionworld2 لاحقاً) =====
        public void ApplySavedStateFromGameSession()
        {
            if (!GameSessionworld2.HasSavedPlayerState) return;

            bool wasEnabled = controller && controller.enabled;
            if (wasEnabled) controller.enabled = false;

            transform.SetPositionAndRotation(GameSessionworld2.PlayerPos, GameSessionworld2.PlayerRot);

            if (wasEnabled) controller.enabled = true;
            // لا نُصفّر HasSavedPlayerState هنا. يتم تصفيره لاحقاً بعد فريم داخل GameSessionworld2.
        }

        // ===== Resume logic after returning from AR =====
        public void ResumeAfterReturn(string prevLetter)
        {
            canMove = true;
            Idle();
        }


        /// <summary>
        /// Hook your real waypoint/tag logic here.
        /// </summary>
        public void MoveToNextTargetAfter(string justDoneLetter)
        {
            if (string.IsNullOrEmpty(justDoneLetter)) return;

            switch (justDoneLetter)
            {
                case "س":
                    Move(new Vector2(+1f, 0f));
                    break;
                case "ش":
                    Move(new Vector2(+1f, 0f));
                    break;
                case "ص":
                    Idle();
                    break;
                default:
                    Idle();
                    break;
            }
        }
    }
}
