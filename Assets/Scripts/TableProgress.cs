using Firebase.Auth;
using Firebase.Database;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class TableProgress : MonoBehaviour
{
    public Transform contentParent;   // الحاوية التي تحتوي على خلايا الجدول (الخانات)
    public GameObject cellPrefab;     // النموذج المستخدم لإنشاء كل خلية

    // مجموعة الحروف الصعبة الثابتة التي سيتم تتبعها
    private string[] fixedLetters = { "خ", "ذ", "ر", "س", "ش", "ص", "ض", "ظ", "غ" };

    private string parentId;            // معرّف ولي الأمر من Firebase
    private string selectedChildKey;    // معرف الطفل المختار من قاعدة البيانات

    // لون خلفية فاتح (أزرق) لعناوين الصفوف والأعمدة
    private Color lightBlue = new Color(0.83f, 0.90f, 0.98f);

    void Start()
    {
        // نحصل على المستخدم المسجل في Firebase
        FirebaseUser user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user != null)
        {
            parentId = user.UserId;
            StartCoroutine(LoadSelectedChildAndShowTable()); // بدء تحميل جدول الطفل المختار
        }
    }

    // الخطوة الأولى: تحميل الطفل المحدد ثم بناء الجدول
    IEnumerator LoadSelectedChildAndShowTable()
    {
        DatabaseReference dbRef = FirebaseDatabase.DefaultInstance.RootReference
            .Child("parents").Child(parentId).Child("children");

        var dbTask = dbRef.GetValueAsync();
        yield return new WaitUntil(() => dbTask.IsCompleted);

        if (dbTask.Exception != null)
        {
            Debug.LogError("فشل في تحميل بيانات الأطفال: " + dbTask.Exception);
            yield break;
        }

        DataSnapshot snapshot = dbTask.Result;

        // البحث عن الطفل الذي يحتوي على "selected = true"
        foreach (var child in snapshot.Children)
        {
            if (child.HasChild("selected") && child.Child("selected").Value != null
                && child.Child("selected").Value.ToString().ToLower() == "true")
            {
                selectedChildKey = child.Key;
                yield return StartCoroutine(FillTableFromFirebase()); // تحميل البيانات وبناء الجدول
                yield break;
            }
        }

        Debug.LogWarning("لم يتم العثور على طفل محدد.");
    }

    // الخطوة الثانية: تحميل عدد الأخطاء لكل محاولة وعرضها في الجدول
    IEnumerator FillTableFromFirebase()
    {
        string path = $"parents/{parentId}/children/{selectedChildKey}/letters";
        var dbTask = FirebaseDatabase.DefaultInstance.RootReference.Child(path).GetValueAsync();
        yield return new WaitUntil(() => dbTask.IsCompleted);

        Dictionary<string, List<int>> errorsPerAttempt = new Dictionary<string, List<int>>(); // الأخطاء لكل حرف
        int maxAttempts = 0; // أكثر عدد محاولات بين كل الحروف

        DataSnapshot snapshot = dbTask.Result;

        // معالجة كل حرف من الحروف المحددة
        foreach (var letter in fixedLetters)
        {
            List<int> attemptsList = new List<int>();

            if (snapshot.HasChild(letter) && snapshot.Child(letter).HasChild("attempts"))
            {
                var attemptsSnap = snapshot.Child(letter).Child("attempts");

                foreach (var attemptSnap in attemptsSnap.Children)
                {
                    int errors = 0;
                    if (attemptSnap.HasChild("errors"))
                        int.TryParse(attemptSnap.Child("errors").Value.ToString(), out errors);
                    attemptsList.Add(errors); // حفظ عدد الأخطاء لكل محاولة
                }
            }

            errorsPerAttempt[letter] = attemptsList;

            if (attemptsList.Count > maxAttempts)
                maxAttempts = attemptsList.Count; // تحديث الحد الأقصى للمحاولات
        }

        // مسح الخلايا السابقة إن وجدت
        foreach (Transform child in contentParent)
            Destroy(child.gameObject);

        // ضبط تخطيط الشبكة: الأعمدة = عدد المحاولات + 1 (لعمود الحرف)
        var grid = contentParent.GetComponent<UnityEngine.UI.GridLayoutGroup>();
        if (grid != null)
        {
            grid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = maxAttempts + 1;
            grid.startCorner = UnityEngine.UI.GridLayoutGroup.Corner.UpperRight;
            grid.startAxis = UnityEngine.UI.GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperRight;
        }

        // ---------------- سطر العناوين ----------------
        GameObject titleCell = Instantiate(cellPrefab, contentParent);
        var txtTitle = titleCell.GetComponentInChildren<TMP_Text>();
        txtTitle.text = ArabicSupport.ArabicFixer.Fix("الأخطاء / المحاولات"); // العنوان الرئيسي
        txtTitle.fontStyle = FontStyles.Bold;
        txtTitle.fontSize = 34;
        txtTitle.alignment = TextAlignmentOptions.Center;
        titleCell.GetComponent<UnityEngine.UI.Image>().color = lightBlue;

        // إنشاء عناوين الأعمدة: "محاولة ١", "محاولة ٢", ...
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            GameObject tryCell = Instantiate(cellPrefab, contentParent);
            var txtTry = tryCell.GetComponentInChildren<TMP_Text>();
            txtTry.text = ArabicSupport.ArabicFixer.Fix($"محاولة {ToArabicNumbers(attempt)}");
            txtTry.fontStyle = FontStyles.Bold;
            txtTry.fontSize = 30;
            txtTry.alignment = TextAlignmentOptions.Center;
            tryCell.GetComponent<UnityEngine.UI.Image>().color = lightBlue;
        }

        // ---------------- صفوف البيانات ----------------
        foreach (var letter in fixedLetters)
        {
            // العمود الأول: الحرف
            GameObject letterCell = Instantiate(cellPrefab, contentParent);
            var txtL = letterCell.GetComponentInChildren<TMP_Text>();
            txtL.text = ArabicSupport.ArabicFixer.Fix(letter);
            txtL.fontStyle = FontStyles.Bold;
            txtL.fontSize = 32;
            txtL.alignment = TextAlignmentOptions.Center;
            letterCell.GetComponent<UnityEngine.UI.Image>().color = lightBlue;

            // الأعمدة الأخرى: عدد الأخطاء أو "لا يوجد"
            var attempts = errorsPerAttempt[letter];
            for (int attemptIndex = 0; attemptIndex < maxAttempts; attemptIndex++)
            {
                GameObject valCell = Instantiate(cellPrefab, contentParent);
                var txt = valCell.GetComponentInChildren<TMP_Text>();

                if (attemptIndex < attempts.Count)
                    txt.text = ToArabicNumbers(attempts[attemptIndex]); // عرض عدد الأخطاء
                else
                    txt.text = ArabicSupport.ArabicFixer.Fix("لا يوجد"); // لم يقم بمحاولة

                txt.fontStyle = FontStyles.Normal;
                txt.fontSize = 30;
                txt.alignment = TextAlignmentOptions.Center;
                valCell.GetComponent<UnityEngine.UI.Image>().color = Color.white;
            }
        }
    }

    // دالة تحويل الأرقام الإنجليزية إلى أرقام عربية
    string ToArabicNumbers(int number)
    {
        string[] arabicDigits = { "٠", "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩" };
        string numStr = number.ToString();
        string result = "";
        foreach (char c in numStr)
        {
            if (char.IsDigit(c))
                result += arabicDigits[(int)char.GetNumericValue(c)];
            else
                result += c;
        }
        return result;
    }
}
