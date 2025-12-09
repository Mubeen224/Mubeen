using System.Collections;
using System.Collections.Generic;
using Firebase.Auth;
using Firebase.Database;
using UnityEngine;
using UnityEngine.UI;

public class StarRatingPreview : MonoBehaviour
{
    [Header("Assign the three parent holders (each has 5 Star_* with child *_Full)")]
    public Transform srStarsHolder;      // activities/SR/Quiz_attempt
    public Transform arStarsHolder;      // activities/ar/Quiz_attempt OR activities/cards/Quiz_attempt
    public Transform tracingStarsHolder; // activities/tracing/Quiz_attempt

    [Header("Behavior")]
    public bool liveFirebaseUpdates = true;

    // Firebase
    private FirebaseAuth auth;
    private FirebaseDatabase db;
    private string parentId;
    private string selectedChildKey;
    private DatabaseReference childrenRef;
    private DatabaseReference lettersRef;

    // UI cache
    private readonly List<Image> srFillImages = new List<Image>(5);
    private readonly List<Image> arFillImages = new List<Image>(5);
    private readonly List<Image> trFillImages = new List<Image>(5);

    private const int STAR_COUNT = 5;
    private const int MAX_ERRORS = 10;

    void Awake()
    {
        CollectStarFills(srStarsHolder, srFillImages);
        CollectStarFills(arStarsHolder, arFillImages);
        CollectStarFills(tracingStarsHolder, trFillImages);

        ForceImagesToFilledLeft(srFillImages);
        ForceImagesToFilledLeft(arFillImages);
        ForceImagesToFilledLeft(trFillImages);
    }

    void OnEnable()
    {
        auth = FirebaseAuth.DefaultInstance;
        db = FirebaseDatabase.DefaultInstance;

        if (auth.CurrentUser == null)
        {
            Debug.LogError("[StarRatingPreview] No logged-in user (OnEnable).");
            ApplyFractionToStars(srFillImages, 0f);
            ApplyFractionToStars(arFillImages, 0f);
            ApplyFractionToStars(trFillImages, 0f);
            return;
        }

        parentId = auth.CurrentUser.UserId;
        childrenRef = db.RootReference.Child("parents").Child(parentId).Child("children");

        StartCoroutine(RefreshStarsForSelectedChild());
    }

    void OnDisable()
    {
        if (lettersRef != null && liveFirebaseUpdates)
        {
            lettersRef.ValueChanged -= OnLettersValueChanged;
            lettersRef = null;
        }
    }

    void OnValidate()
    {
        CollectStarFills(srStarsHolder, srFillImages);
        CollectStarFills(arStarsHolder, arFillImages);
        CollectStarFills(tracingStarsHolder, trFillImages);

        ForceImagesToFilledLeft(srFillImages);
        ForceImagesToFilledLeft(arFillImages);
        ForceImagesToFilledLeft(trFillImages);
    }

    // -------------------- Firebase: load stars for current child --------------------

    IEnumerator RefreshStarsForSelectedChild()
    {
        Debug.Log("[StarRatingPreview] RefreshStarsForSelectedChild started.");

        var getChildrenTask = childrenRef.GetValueAsync();
        yield return new WaitUntil(() => getChildrenTask.IsCompleted);

        if (getChildrenTask.Exception != null)
        {
            Debug.LogError("[StarRatingPreview] Load children failed: " + getChildrenTask.Exception);
            ApplyFractionToStars(srFillImages, 0f);
            ApplyFractionToStars(arFillImages, 0f);
            ApplyFractionToStars(trFillImages, 0f);
            yield break;
        }

        var snap = getChildrenTask.Result;
        if (snap == null || !snap.Exists)
        {
            Debug.LogWarning("[StarRatingPreview] No children for this parent.");
            ApplyFractionToStars(srFillImages, 0f);
            ApplyFractionToStars(arFillImages, 0f);
            ApplyFractionToStars(trFillImages, 0f);
            yield break;
        }

        DataSnapshot selectedChildSnap = null;
        int selectedCount = 0;

        foreach (var child in snap.Children)
        {
            bool isSel = false;
            if (child.HasChild("selected") && child.Child("selected").Value != null)
                bool.TryParse(child.Child("selected").Value.ToString(), out isSel);

            if (isSel)
            {
                selectedCount++;
                if (selectedChildSnap == null)
                {
                    selectedChildSnap = child;
                    selectedChildKey = child.Key;
                }
            }
        }

        if (selectedChildSnap == null)
        {
            Debug.LogWarning("[StarRatingPreview] No child marked as selected.");
            ApplyFractionToStars(srFillImages, 0f);
            ApplyFractionToStars(arFillImages, 0f);
            ApplyFractionToStars(trFillImages, 0f);
            yield break;
        }

        Debug.Log($"[StarRatingPreview] Using selected child key = {selectedChildKey}, selectedCount={selectedCount}");

        DataSnapshot lettersSnap = selectedChildSnap.HasChild("letters")
            ? selectedChildSnap.Child("letters")
            : null;

        ApplyFromLettersSnapshot(lettersSnap);

        if (liveFirebaseUpdates)
        {
            if (lettersRef != null)
                lettersRef.ValueChanged -= OnLettersValueChanged;

            lettersRef = childrenRef.Child(selectedChildKey).Child("letters");
            lettersRef.ValueChanged += OnLettersValueChanged;

            Debug.Log($"[StarRatingPreview] Listening to ValueChanged on /parents/{parentId}/children/{selectedChildKey}/letters");
        }
    }

    void OnLettersValueChanged(object sender, ValueChangedEventArgs e)
    {
        if (e.DatabaseError != null)
        {
            Debug.LogError("[StarRatingPreview] Firebase error: " + e.DatabaseError.Message);
            return;
        }

        Debug.Log("[StarRatingPreview] Letters ValueChanged received.");
        ApplyFromLettersSnapshot(e.Snapshot);
    }

    void ApplyFromLettersSnapshot(DataSnapshot lettersSnap)
    {
        if (lettersSnap == null || !lettersSnap.Exists)
        {
            Debug.Log("[StarRatingPreview] No letters node for this child → 0 stars.");
            ApplyFractionToStars(srFillImages, 0f);
            ApplyFractionToStars(arFillImages, 0f);
            ApplyFractionToStars(trFillImages, 0f);
            return;
        }

        float sr = ComputeActivityFraction(lettersSnap, "SR");
        float tr = ComputeActivityFraction(lettersSnap, "tracing");
        float arOrCards = ComputeArOrCardsFraction(lettersSnap);

        ApplyFractionToStars(srFillImages, sr);
        ApplyFractionToStars(trFillImages, tr);
        ApplyFractionToStars(arFillImages, arOrCards);
    }

    // -------------------- Core compute --------------------

    float ComputeActivityFraction(DataSnapshot lettersSnap, string activityKey)
    {
        if (lettersSnap == null || !lettersSnap.Exists) return 0f;

        foreach (var letter in lettersSnap.Children)
        {
            var activityNode = letter.Child("activities").Child(activityKey);
            if (!activityNode.Exists) continue;

            var qa = activityNode.Child("Quiz_attempt");
            if (!qa.Exists) continue;

            // Case A: single object
            if (qa.HasChild("errors"))
            {
                int errs = SafeToInt(qa.Child("errors").Value, MAX_ERRORS);
                return ToFraction(errs);
            }

            // Case B: list
            int bestIdx = -1, bestErrs = MAX_ERRORS;
            foreach (var attempt in qa.Children)
            {
                if (!attempt.HasChild("errors")) continue;
                int idx;
                int.TryParse(attempt.Key, out idx);
                int errs = SafeToInt(attempt.Child("errors").Value, MAX_ERRORS);
                if (idx > bestIdx)
                {
                    bestIdx = idx;
                    bestErrs = errs;
                }
            }
            return ToFraction(bestErrs);
        }
        return 0f;
    }

    // AR or Cards (only one type exists at a time)
    float ComputeArOrCardsFraction(DataSnapshot lettersSnap)
    {
        bool arExists = ActivityExists(lettersSnap, "ar");
        if (arExists)
            return ComputeActivityFraction(lettersSnap, "ar");

        bool cardsExists = ActivityExists(lettersSnap, "cards");
        if (cardsExists)
            return ComputeActivityFraction(lettersSnap, "cards");

        return 0f;
    }

    bool ActivityExists(DataSnapshot lettersSnap, string activityKey)
    {
        if (lettersSnap == null || !lettersSnap.Exists) return false;
        foreach (var letter in lettersSnap.Children)
        {
            var qa = letter.Child("activities").Child(activityKey).Child("Quiz_attempt");
            if (qa.Exists) return true;
        }
        return false;
    }

    float ToFraction(int errors)
    {
        errors = Mathf.Clamp(errors, 0, MAX_ERRORS);
        return Mathf.Clamp01((MAX_ERRORS - errors) / (float)MAX_ERRORS);
    }

    int SafeToInt(object val, int fallback)
    {
        try { return System.Convert.ToInt32(val); }
        catch { return fallback; }
    }

    // -------------------- Star UI helpers --------------------

    void ApplyFractionToStars(List<Image> fills, float fraction01)
    {
        if (fills.Count == 0) return;

        float total = fraction01 * STAR_COUNT;

        for (int i = 0; i < fills.Count; i++)
        {
            var img = fills[i];
            if (!img) continue;

            ForceImageToFilledLeft(img);
            if (img.transform.parent) img.transform.parent.gameObject.SetActive(true);
            img.gameObject.SetActive(true);

            img.fillAmount = Mathf.Clamp01(total - i);
        }
    }

    void CollectStarFills(Transform holder, List<Image> outList)
    {
        outList.Clear();
        if (!holder) return;

        var images = holder.GetComponentsInChildren<Image>(true);
        foreach (var img in images)
            if (img.gameObject.name.ToLower().Contains("full"))
                outList.Add(img);

        outList.Sort((a, b) => a.name.CompareTo(b.name));
        if (outList.Count > STAR_COUNT)
            outList.RemoveRange(STAR_COUNT, outList.Count - STAR_COUNT);
    }

    void ForceImagesToFilledLeft(List<Image> imgs)
    {
        foreach (var img in imgs) if (img) ForceImageToFilledLeft(img);
    }

    void ForceImageToFilledLeft(Image img)
    {
        if (img.type != Image.Type.Filled) img.type = Image.Type.Filled;
        if (img.fillMethod != Image.FillMethod.Horizontal) img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillClockwise = true;
    }
}
