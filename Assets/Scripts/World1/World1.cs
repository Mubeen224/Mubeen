using UnityEngine;

namespace Mankibo
{
    /// <summary>
    /// Player controller for World(1) island.
    /// - يحفظ موضع اللاعب عند العودة من AR.
    /// - يستأنف الحركة بدون إعادة تشغيل البوب-أب.
    /// - يتيح ربط الحركة بالهدف التالي بعد الحرف المكتمل.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class World1 : CharacterAction, IMoveable
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
            // إن وُجدت حالة محفوظة سيتم تطبيقها هنا أو في Start كشبكة أمان.
            if (GameSession.HasSavedPlayerState)
            {
                ApplySavedStateFromGameSession();
            }
        }

        private void Start()
        {
            // احتياط إضافي لو كان OnWorldLoaded لم يُطبّق الحالة بعد
            if (GameSession.HasSavedPlayerState)
                ApplySavedStateFromGameSession();
        }

        private void Update()
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

            velocity.x = 0;

            if (charMovement.x != 0)
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
                if ((charMovement.x < 0f && facingRight) ||
                    (charMovement.x > 0f && !facingRight))
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

        // ====== تطبيق حالة العودة من GameSession (بدون تصفير الفلاغ هنا) ======
        public void ApplySavedStateFromGameSession()
        {
            if (!GameSession.HasSavedPlayerState) return;

            bool wasEnabled = controller && controller.enabled;
            if (wasEnabled) controller.enabled = false;

            transform.SetPositionAndRotation(GameSession.PlayerPos, GameSession.PlayerRot);

            if (wasEnabled) controller.enabled = true;
            // لا نُصفّر HasSavedPlayerState هنا. يتم تصفيره لاحقاً بعد فريم داخل GameSession.
        }

        // ====== استئناف اللعب بعد العودة من AR ======
        public void ResumeAfterReturn(string justDoneLetter)
        {
            canMove = true;
            Idle();
            MoveToNextTargetAfter(justDoneLetter);
        }

        /// <summary>
        /// منطق توجيه اللاعب للهدف التالي بعد إنهاء حرف.
        /// يمكن ربطه بنظام waypoints أو tags لاحقًا.
        /// </summary>
        public void MoveToNextTargetAfter(string justDoneLetter)
        {
            if (string.IsNullOrEmpty(justDoneLetter)) return;

            switch (justDoneLetter)
            {
                case "خ":
                    Move(Vector2.right);
                    break;
                case "ذ":
                    Move(Vector2.right);
                    break;
                case "ر":
                    Idle(); // مثال: يتوقف هنا
                    break;
                default:
                    Idle();
                    break;
            }
        }
    }
}
