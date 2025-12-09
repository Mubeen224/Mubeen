using System;
using System.Collections;
using Firebase.Auth;
using Firebase.Database;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mankibo
{
    public class World3Controller : MonoBehaviour
    {
        [Header("Character Binding")]
        [Tooltip("Optional: drag the 4 character roots here (Archer, Knight, Rogue, Wizard). If assigned, only the selected one will be set active.")]
        public GameObject[] characterRoots;

        [Tooltip("Fallback name to use if Firebase can't be read.")]
        public string defaultCharacterName = "Archer";

        private World3 character;               // Bound at runtime to the selected character's World3 script
        private PlayerInput inputActions;       // Input System actions

        [Header("Camera Follow Settings")]
        public Transform mainCamera;            // Camera to follow the character
        public Vector3 cameraOffset = new Vector3(5f, 2f, -10f);
        public float cameraSmoothSpeed = 5f;

        // Known character GameObject names (used if characterRoots is not provided)
        private static readonly string[] KnownCharacterNames = { "Archer", "Knight", "Rogue", "Wizard" };

        private void Awake()
        {
            inputActions = new PlayerInput();
            inputActions.Player.Enable();

            // Keyboard input hooks
            inputActions.Player.Move.performed += ctx => OnKeyboardMove(ctx.ReadValue<Vector2>());
            inputActions.Player.Move.canceled += ctx => OnKeyboardRelease();

            if (mainCamera == null)
                mainCamera = Camera.main?.transform;
        }

        private void Start()
        {
            // Resolve the selected character from Firebase, then bind and activate only it.
            StartCoroutine(ResolveSelectedCharacterName(selectedName =>
            {
                BindToCharacterByName(selectedName);
            }));
        }

        private void LateUpdate()
        {
            // Smooth camera follow (x only)
            if (character != null && mainCamera != null)
            {
                Vector3 target = new Vector3(character.transform.position.x + cameraOffset.x,
                                             mainCamera.position.y,
                                             mainCamera.position.z);

                mainCamera.position = Vector3.Lerp(mainCamera.position, target, cameraSmoothSpeed * Time.deltaTime);
            }
        }

        // ========== Public API used by UI buttons / touch ==========
        public void StartMove(Vector2 direction)
        {
            if (character != null && character.canMove)
                character.Move(direction);
        }

        public void StopMove()
        {
            if (character != null && character.canMove)
                character.ReleaseMove(Vector2.zero);
        }

        private void OnKeyboardMove(Vector2 direction) => StartMove(direction);
        private void OnKeyboardRelease() => StopMove();

        public void DisablePlayerInput() => inputActions.Player.Disable();

        // ========== Binding helpers ==========

        private void BindToCharacterByName(string selectedName)
        {
            GameObject chosen = null;

            // Prefer the explicit list if provided (and toggle only the selected active)
            if (characterRoots != null && characterRoots.Length > 0)
            {
                foreach (var root in characterRoots)
                {
                    if (!root) continue;
                    bool isMatch = string.Equals(root.name, selectedName, StringComparison.OrdinalIgnoreCase);
                    root.SetActive(isMatch);
                    if (isMatch) chosen = root;

                    var w = root.GetComponent<World3>();
                    if (w)
                    {
                        w.canMove = isMatch;
                        if (!isMatch && w.footstepAudio && w.footstepAudio.isPlaying)
                            w.footstepAudio.Stop();
                    }
                }

                // Fallback to a scene search by name if not found in the list
                if (chosen == null)
                    chosen = GameObject.Find(selectedName);
            }
            else
            {
                // No array provided: use known names to ensure only selected stays active
                GameObject selectedGO = null;

                foreach (var n in KnownCharacterNames)
                {
                    var go = GameObject.Find(n);
                    if (!go) continue;

                    bool isSelected = string.Equals(n, selectedName, StringComparison.OrdinalIgnoreCase);
                    go.SetActive(isSelected);
                    if (isSelected) selectedGO = go;

                    var w = go.GetComponent<World3>();
                    if (w)
                    {
                        w.canMove = isSelected;
                        if (!isSelected && w.footstepAudio && w.footstepAudio.isPlaying)
                            w.footstepAudio.Stop();
                    }
                }

                chosen = selectedGO ?? GameObject.Find(selectedName);
            }

            // Final fallback if the selected was not found
            if (chosen == null)
            {
                Debug.LogError($"World3Controller: Character GameObject '{selectedName}' not found. Falling back to '{defaultCharacterName}'.");
                chosen = GameObject.Find(defaultCharacterName);
                if (chosen == null)
                {
                    Debug.LogError("World3Controller: Fallback character not found either. Please check scene object names.");
                    return;
                }
            }

            if (!chosen.activeSelf) chosen.SetActive(true);

            // Cache the component
            character = chosen.GetComponent<World3>();
            if (character == null)
            {
                Debug.LogError($"World3Controller: GameObject '{chosen.name}' is missing the 'World3' component.");
                return;
            }
        }

        // ========== Firebase resolve: which character is selected for current child? ==========

        private IEnumerator ResolveSelectedCharacterName(Action<string> onResolved)
        {
            string result = defaultCharacterName; // safe default

            var auth = FirebaseAuth.DefaultInstance;
            if (auth.CurrentUser == null)
            {
                onResolved?.Invoke(result);
                yield break;
            }

            string parentId = auth.CurrentUser.UserId;
            var db = FirebaseDatabase.DefaultInstance;
            var childrenRef = db.RootReference.Child("parents").Child(parentId).Child("children");

            var task = childrenRef.GetValueAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            if (task.Exception != null || task.Result == null || !task.Result.Exists)
            {
                onResolved?.Invoke(result);
                yield break;
            }

            DataSnapshot selectedChild = null;
            foreach (var child in task.Result.Children)
            {
                if (child.HasChild("selected") &&
                    bool.TryParse(child.Child("selected").Value?.ToString(), out bool isSel) && isSel)
                {
                    selectedChild = child;
                    break;
                }
            }

            if (selectedChild == null || !selectedChild.HasChild("characters"))
            {
                onResolved?.Invoke(result);
                yield break;
            }

            // Prefer character with selected==true; else the one with displayed==true
            string chosen = null;
            foreach (var ch in selectedChild.Child("characters").Children)
            {
                if (bool.TryParse(ch.Child("selected")?.Value?.ToString(), out bool sel) && sel)
                {
                    chosen = ch.Child("name")?.Value?.ToString();
                    break;
                }
            }
            if (string.IsNullOrEmpty(chosen))
            {
                foreach (var ch in selectedChild.Child("characters").Children)
                {
                    if (bool.TryParse(ch.Child("displayed")?.Value?.ToString(), out bool disp) && disp)
                    {
                        chosen = ch.Child("name")?.Value?.ToString();
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(chosen))
                result = chosen;

            onResolved?.Invoke(result);
        }
    }
}
