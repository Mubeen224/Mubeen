using UnityEngine;

namespace Mankibo
{
    public class CharacterUIController : MonoBehaviour
    {
        [Header("Player References (assign scene objects)")]
        public World1 archer;
        public World1 knight;
        public World1 rogue;
        public World1 wizard;

        // Cached current character (the one actually moving)
        private World1 current;

        // Call this if you want to manually override from another script
        public void ForceSetCurrent(World1 ch) => current = ch;

        // ---- Public API used by EventTrigger ----
        public void MoveLeft() => GetCurrent()?.Move(new Vector2(-1f, 0f));
        public void MoveRight() => GetCurrent()?.Move(new Vector2(1f, 0f));
        public void StopMoving() => GetCurrent()?.ReleaseMove(Vector2.zero);

        // ---- Helper: decide which character to drive ----
        private World1 GetCurrent()
        {
            // If cached still valid, keep using it
            if (IsUsable(current)) return current;

            // Prefer the one that is active AND allowed to move
            if (IsUsable(archer)) return current = archer;
            if (IsUsable(knight)) return current = knight;
            if (IsUsable(rogue)) return current = rogue;
            if (IsUsable(wizard)) return current = wizard;

            // Fallback: any active even if canMove was temporarily false
            if (IsActive(archer)) return current = archer;
            if (IsActive(knight)) return current = knight;
            if (IsActive(rogue)) return current = rogue;
            if (IsActive(wizard)) return current = wizard;

            // Last resort: find any World1 in the scene
            return current = FindObjectOfType<World1>();
        }

        private static bool IsUsable(World1 w)
            => w && w.gameObject.activeInHierarchy && w.canMove;

        private static bool IsActive(World1 w)
            => w && w.gameObject.activeInHierarchy;
    }
}
