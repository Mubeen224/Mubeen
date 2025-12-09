using System;
using System.Collections;
using Firebase.Auth;
using Firebase.Database;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mankibo
{
    public class World2Controller : MonoBehaviour
    {
        [Header("Character Reference")]
        public string characterName = "Archer"; // Fallback; will be overwritten by Firebase
        private World2 character;               // Movement script on the selected character

        private PlayerInput inputActions;       // Your generated Input System actions (e.g., "PlayerInput")

        [Header("Camera Follow Settings")]
        public Transform mainCamera;
        public Vector3 cameraOffset = new Vector3(5f, 2f, -10f);
        public float cameraSmoothSpeed = 5f;

        // Known character GameObject names in the scene (match your Store names)
        private static readonly string[] KnownCharacterNames = { "Archer", "Knight", "Rogue", "Wizard" };

        private void Awake()
        {
            inputActions = new PlayerInput();
            inputActions.Player.Enable();

            inputActions.Player.Move.performed += ctx => OnKeyboardMove(ctx.ReadValue<Vector2>());
            inputActions.Player.Move.canceled += ctx => OnKeyboardRelease();

            if (mainCamera == null)
                mainCamera = Camera.main?.transform;
        }

        private void Start()
        {
            StartCoroutine(ResolveSelectedCharacterName(selectedName =>
            {
                if (!string.IsNullOrEmpty(selectedName))
                    characterName = selectedName;

                var go = GameObject.Find(characterName);
                if (go == null)
                {
                    Debug.LogError($"World2Controller: Character GameObject '{characterName}' not found in the scene.");
                    return;
                }

                character = go.GetComponent<World2>();
                if (character == null)
                {
                    Debug.LogError($"World2Controller: 'World2' component not found on '{characterName}'.");
                    return;
                }

                // Ensure only the selected character is active/movable
                ActivateSelectedOnly(go);
            }));
        }

        private void LateUpdate()
        {
            if (character != null && mainCamera != null)
            {
                Vector3 targetPosition = new Vector3(
                    character.transform.position.x + cameraOffset.x,
                    mainCamera.position.y,
                    mainCamera.position.z);

                mainCamera.position = Vector3.Lerp(
                    mainCamera.position,
                    targetPosition,
                    cameraSmoothSpeed * Time.deltaTime);
            }
        }

        // Enable only the chosen character; disable others and stop their audio
        private void ActivateSelectedOnly(GameObject selectedGO)
        {
            foreach (var n in KnownCharacterNames)
            {
                var go = GameObject.Find(n);
                if (!go) continue;

                bool isSelected = (go == selectedGO);
                go.SetActive(isSelected);

                var w = go.GetComponent<World2>();
                if (w)
                {
                    w.canMove = isSelected;
                    if (!isSelected && w.footstepAudio && w.footstepAudio.isPlaying)
                        w.footstepAudio.Stop();
                }
            }
        }

        // === Input passthrough ===
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

        // === Firebase resolve: which character is selected for current child? ===
        private IEnumerator ResolveSelectedCharacterName(Action<string> onResolved)
        {
            string result = characterName; // safe default

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

            // Prefer selected==true; else displayed==true; else default
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
