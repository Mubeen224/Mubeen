using UnityEngine;

namespace Mankibo
{
    public class CharacterUIControllerW2 : MonoBehaviour
    {
        [Header("Player References (assign scene objects)")]
        public World2 archer;
        public World2 knight;
        public World2 rogue;
        public World2 wizard;

        // Cached current character (the one actually moving)
        private World2 current;

        // If you want to set it from another script (e.g., after resolving selection)
        public void ForceSetCurrent(World2 ch) => current = ch;

        // ---- Public API used by EventTrigger / UI Buttons ----
        public void MoveLeft() => GetCurrent()?.Move(new Vector2(-1f, 0f));
        public void MoveRight() => GetCurrent()?.Move(new Vector2(1f, 0f));
        public void StopMoving() => GetCurrent()?.ReleaseMove(Vector2.zero);

        // ---- Helper: decide which character to drive ----
        private World2 GetCurrent()
        {
            // If cached still valid, keep using it
            if (IsUsable(current)) return current;

            // Prefer the one that is active AND allowed to move
            if (IsUsable(archer)) return current = archer;
            if (IsUsable(knight)) return current = knight;
            if (IsUsable(rogue)) return current = rogue;
            if (IsUsable(wizard)) return current = wizard;

            // Fallback: any active even if canMove is temporarily false
            if (IsActive(archer)) return current = archer;
            if (IsActive(knight)) return current = knight;
            if (IsActive(rogue)) return current = rogue;
            if (IsActive(wizard)) return current = wizard;

            // Last resort: find any World2 in the scene
            return current = FindObjectOfType<World2>();
        }

        private static bool IsUsable(World2 w)
            => w && w.gameObject.activeInHierarchy && w.canMove;

        private static bool IsActive(World2 w)
            => w && w.gameObject.activeInHierarchy;
    }
}
