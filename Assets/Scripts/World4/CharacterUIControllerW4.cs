using UnityEngine;

namespace Mankibo
{
    public class CharacterUIControllerW4 : MonoBehaviour
    {
        [Header("Player References (assign scene objects)")]
        public World4 archer;
        public World4 knight;
        public World4 rogue;
        public World4 wizard;

        // Cached current character (the one actually moving)
        private World4 current;

        // set by another script after binding
        public void ForceSetCurrent(World4 ch) => current = ch;

        // ---- Public API used by EventTrigger / UI Buttons ----
        public void MoveLeft() => GetCurrent()?.Move(new Vector2(-1f, 0f));
        public void MoveRight() => GetCurrent()?.Move(new Vector2(1f, 0f));
        public void StopMoving() => GetCurrent()?.ReleaseMove(Vector2.zero);

        // ---- Helper: choose active/usable character ----
        private World4 GetCurrent()
        {
            if (IsUsable(current)) return current;

            if (IsUsable(archer)) return current = archer;
            if (IsUsable(knight)) return current = knight;
            if (IsUsable(rogue)) return current = rogue;
            if (IsUsable(wizard)) return current = wizard;

            if (IsActive(archer)) return current = archer;
            if (IsActive(knight)) return current = knight;
            if (IsActive(rogue)) return current = rogue;
            if (IsActive(wizard)) return current = wizard;

            return current = FindObjectOfType<World4>();
        }

        private static bool IsUsable(World4 w)
            => w && w.gameObject.activeInHierarchy && w.canMove;

        private static bool IsActive(World4 w)
            => w && w.gameObject.activeInHierarchy;
    }
}
