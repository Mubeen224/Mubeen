using System;
using System.Collections;
using Firebase.Auth;
using Firebase.Database;
using UnityEngine;

namespace Mankibo
{
    public class World2BGScroll : MonoBehaviour
    {
        [Header("References")]
        public Transform character;     // Assigned at runtime to the selected character
        public Transform[] layers;      // Assign Layer_0..Layer_4 in Inspector
        public float[] layerSpeeds;     // Same length as layers (or shorter; missing -> 0)

        private Vector3[] startPositions;

        private void Start()
        {
            if (layers == null || layers.Length == 0)
            {
                Debug.LogError("World2BG: No background layers assigned!");
                return;
            }

            startPositions = new Vector3[layers.Length];
            for (int i = 0; i < layers.Length; i++)
                startPositions[i] = layers[i].position;

            // Resolve selected character and bind its Transform
            StartCoroutine(ResolveSelectedCharacterName(selectedName =>
            {
                var go = GameObject.Find(selectedName);
                if (go == null)
                {
                    Debug.LogError($"World2BG: Character GameObject '{selectedName}' not found.");
                    return;
                }
                character = go.transform;
            }));
        }

        private void Update()
        {
            if (character == null) return;

            for (int i = 0; i < layers.Length; i++)
            {
                float speed = (layerSpeeds != null && i < layerSpeeds.Length) ? layerSpeeds[i] : 0f;
                float newX = startPositions[i].x + (character.position.x * speed);
                layers[i].position = new Vector3(newX, startPositions[i].y, startPositions[i].z);
            }
        }

        // === Firebase resolve: which character is selected for current child? ===
        private IEnumerator ResolveSelectedCharacterName(Action<string> onResolved)
        {
            string result = "Archer"; // safe default

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
