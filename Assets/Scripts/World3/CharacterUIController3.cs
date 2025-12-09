using UnityEngine;

namespace Mankibo
{
    // Controls the character’s movement via UI buttons (e.g. touch or on-screen controls)
    public class CharacterUIController3 : MonoBehaviour
    {
        [Header("Player References (assign scene objects)")]
        public World3 archer;
        public World3 knight;
        public World3 rogue;
        public World3 wizard;

        // Cached current character (the one actually moving)
        private World3 current;

        // If you want to set it from another script (e.g., after resolving selection)
        public void ForceSetCurrent(World3 ch) => current = ch;

        // ---- Public API used by EventTrigger / UI Buttons ----
        public void MoveLeft() => GetCurrent()?.Move(new Vector2(-1f, 0f));
        public void MoveRight() => GetCurrent()?.Move(new Vector2(1f, 0f));
        public void StopMoving() => GetCurrent()?.ReleaseMove(Vector2.zero);

        // ---- Helper: decide which character to drive ----
        private World3 GetCurrent()
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

            // Last resort: find any World3 in the scene
            return current = FindObjectOfType<World3>();
        }

        private static bool IsUsable(World3 w)
            => w && w.gameObject.activeInHierarchy && w.canMove;

        private static bool IsActive(World3 w)
            => w && w.gameObject.activeInHierarchy;
    }
}
