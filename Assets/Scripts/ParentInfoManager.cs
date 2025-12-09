using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase;
using Firebase.Auth;
using UnityEngine.SceneManagement;

/// <summary>
/// هذا السكربت مسؤول عن إدارة معلومات حساب ولي الأمر في واجهة المستخدم، 
/// ويشمل: عرض البريد الإلكتروني، تغيير كلمة المرور، حذف الحساب، تسجيل الخروج، وعرض الرسائل التنبيهية.
/// </summary>
public class ParentInfoManager : MonoBehaviour
{
    // العناصر المرتبطة بواجهة المستخدم
    [Header("UI Elements")]
    public TMP_Text emailText; // لعرض بريد ولي الأمر الحالي
    public TMP_InputField oldPasswordInput; // حقل إدخال كلمة المرور القديمة
    public TMP_InputField newPasswordInput; // حقل إدخال كلمة المرور الجديدة
    public TMP_InputField confirmNewPasswordInput; // حقل تأكيد كلمة المرور الجديدة
    public Button changePasswordButton; // زر لتأكيد تغيير كلمة المرور
    public Button signOutButton; // زر تسجيل الخروج
    public Button deleteAccountOpenButton; // زر لفتح نافذة تأكيد حذف الحساب
    public Button deleteAccountConfirmButton; // زر تأكيد حذف الحساب نهائيًا
    public TextMeshProUGUI statusText; // لعرض رسائل النجاح أو الخطأ
    public GameObject changePasswordPopup; // نافذة منبثقة لتغيير كلمة المرور
    public GameObject deleteAccountPopup; // نافذة منبثقة لتأكيد الحذف
    public TMP_InputField deletePasswordInput; // حقل إدخال كلمة المرور عند حذف الحساب
    public GameObject changePasswordWindow; // نافذة مخصصة لتغيير كلمة المرور
    public Button homeButton; // زر للعودة إلى الصفحة الرئيسية

    // تنبيهات مرئية للمستخدم
    [Header("Alerts")]
    public GameObject AlertDelete; // تنبيه يظهر بعد حذف الحساب
    public GameObject AlertLogOut; // تنبيه يظهر بعد تسجيل الخروج
    public GameObject AlertChangePass; // تنبيه يظهر بعد تغيير كلمة المرور
    public Button confirmPasswordChangeButton; // زر إغلاق تنبيه تغيير كلمة المرور

    // متغيرات لإدارة إظهار/إخفاء كلمات المرور
    [Header("Password Visibility Toggle")]
    public Button oldPasswordToggleBtn;
    public Image oldPasswordToggleImage;
    public Button newPasswordToggleBtn;
    public Image newPasswordToggleImage;
    public Button confirmPasswordToggleBtn;
    public Image confirmPasswordToggleImage;
    public Button deletePasswordToggleBtn;
    public Image deletePasswordToggleImage;
    public Sprite showSprite; // أيقونة إظهار كلمة المرور
    public Sprite hideSprite; // أيقونة إخفاء كلمة المرور

    // مراجع Firebase
    private FirebaseAuth auth; // لتسجيل الدخول/الخروج وتعديل الحساب
    private FirebaseUser user; // المستخدم الحالي المسجل دخوله

    // متغيرات لتتبع حالة ظهور كلمات المرور
    private bool isOldPasswordVisible = false;
    private bool isNewPasswordVisible = false;
    private bool isConfirmPasswordVisible = false;
    private bool isDeletePasswordVisible = false;

    // تُنفذ عند بدء المشهد
    void Start()
    {
        // التأكد من جاهزية Firebase
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task =>
        {
            if (task.Result == DependencyStatus.Available)
            {
                // تهيئة Firebase وتحديد المستخدم الحالي
                auth = FirebaseAuth.DefaultInstance;
                user = auth.CurrentUser;

                // عرض البريد الإلكتروني للمستخدم الحالي أو رسالة خطأ إن لم يكن مسجلًا
                emailText.text = user != null ? user.Email : "!ﻞﺠﺴﻣ ﺮﻴﻏ";
            }
            else
            {
                Debug.LogError("Firebase dependencies not available: " + task.Result);
            }
        });

        // تهيئة الأحداث المرتبطة بالأزرار
        statusText.gameObject.SetActive(false);
        changePasswordButton.onClick.AddListener(ChangePassword);
        signOutButton.onClick.AddListener(SignOut);
        deleteAccountOpenButton.onClick.AddListener(ShowDeleteAccountPopup);
        deleteAccountConfirmButton.onClick.AddListener(DeleteAccount);
        homeButton.onClick.AddListener(ReturnToHomePage);

        // تفعيل أزرار إظهار/إخفاء كلمة المرور
        oldPasswordToggleBtn.onClick.AddListener(() => TogglePasswordVisibility(ref isOldPasswordVisible, oldPasswordInput, oldPasswordToggleImage));
        newPasswordToggleBtn.onClick.AddListener(() => TogglePasswordVisibility(ref isNewPasswordVisible, newPasswordInput, newPasswordToggleImage));
        confirmPasswordToggleBtn.onClick.AddListener(() => TogglePasswordVisibility(ref isConfirmPasswordVisible, confirmNewPasswordInput, confirmPasswordToggleImage));
        deletePasswordToggleBtn.onClick.AddListener(() => TogglePasswordVisibility(ref isDeletePasswordVisible, deletePasswordInput, deletePasswordToggleImage));

        if (confirmPasswordChangeButton != null)
            confirmPasswordChangeButton.onClick.AddListener(ClosePasswordChangeAlert);
    }

    // إغلاق نافذة تنبيه تغيير كلمة المرور
    public void ClosePasswordChangeAlert()
    {
        AlertChangePass?.SetActive(false);
        changePasswordWindow?.SetActive(false);
    }

    // عرض نافذة تأكيد حذف الحساب
    public void ShowDeleteAccountPopup() => deleteAccountPopup.SetActive(true);

    // إخفاء نافذة حذف الحساب وإعادة تعيين كلمة المرور المدخلة
    public void HideDeleteAccountPopup()
    {
        deleteAccountPopup.SetActive(false);
        deletePasswordInput.text = "";
    }

    // البدء بتغيير كلمة المرور بعد التحقق
    public void ChangePassword()
    {
        if (user == null)
        {
            ShowStatusMessage("!ﻻﻭﺃ ﻝﻮﺧﺪﻟﺍ ﻞﻴﺠﺴﺗ ﺐﺠﻳ", Color.red);
            return;
        }

        string oldPassword = oldPasswordInput.text;
        string newPassword = newPasswordInput.text;
        string confirmNewPassword = confirmNewPasswordInput.text;

        // التحقق من إدخال جميع الحقول
        if (string.IsNullOrEmpty(oldPassword) || string.IsNullOrEmpty(newPassword) || string.IsNullOrEmpty(confirmNewPassword))
        {
            ShowStatusMessage("!ﻝﻮﻘﺤﻟﺍ ﻊﻴﻤﺟ ﺀﻞﻣ ﻰﺟﺮﻳ", Color.red);
            return;
        }

        // التحقق من تطابق كلمتي المرور
        if (newPassword != confirmNewPassword)
        {
            ShowStatusMessage("!ﺔﻘﺑﺎﻄﺘﻣ ﺮﻴﻏ ﺭﻭﺮﻤﻟﺍ ﺕﺎﻤﻠﻛ", Color.red);
            return;
        }

        // التحقق من قوة كلمة المرور
        if (!IsValidPassword(newPassword))
        {
            ShowStatusMessage("!ﺔﺑﻮﻠﻄﻤﻟﺍ ﻁﻭﺮﺸﻟﺍ ﻲﻓﻮﺘﺴﺗ ﻻ ﺭﻭﺮﻤﻟﺍ ﺔﻤﻠﻛ", Color.red);
            return;
        }

        // إعادة التحقق باستخدام كلمة المرور القديمة
        Credential credential = EmailAuthProvider.GetCredential(user.Email, oldPassword);
        StartCoroutine(ReauthenticateAndChangePassword(credential, newPassword));
    }

    // Coroutine لإعادة التحقق وتغيير كلمة المرور
    private IEnumerator ReauthenticateAndChangePassword(Credential credential, string newPassword)
    {
        var reauthTask = user.ReauthenticateAsync(credential);
        yield return new WaitUntil(() => reauthTask.IsCompleted);

        if (reauthTask.IsFaulted || reauthTask.IsCanceled)
        {
            ShowStatusMessage("!ﺔﺤﻴﺤﺻ ﺮﻴﻏ ﺔﻴﻟﺎﺤﻟﺍ ﺭﻭﺮﻤﻟﺍ ﺔﻤﻠﻛ", Color.red);
            yield break;
        }

        var changePasswordTask = user.UpdatePasswordAsync(newPassword);
        yield return new WaitUntil(() => changePasswordTask.IsCompleted);

        if (changePasswordTask.IsFaulted || changePasswordTask.IsCanceled)
        {
            ShowStatusMessage("!ﺭﻭﺮﻤﻟﺍ ﺔﻤﻠﻛ ﺮﻴﻴﻐﺗ ﻞﺸﻓ", Color.red);
        }
        else
        {
            oldPasswordInput.text = "";
            newPasswordInput.text = "";
            confirmNewPasswordInput.text = "";
            AlertChangePass?.SetActive(true);
        }
    }

    // البدء بعملية حذف الحساب
    public void DeleteAccount()
    {
        if (user == null)
        {
            ShowStatusMessage("!ﻝﻮﺧﺪﻟﺍ ﻞﺠﺴﻣ ﻡﺪﺨﺘﺴﻣ ﺪﺟﻮﻳ ﻻ", Color.red);
            return;
        }

        string password = deletePasswordInput.text;
        if (string.IsNullOrEmpty(password))
        {
            ShowStatusMessage("!ﺭﻭﺮﻤﻟﺍ ﺔﻤﻠﻛ ﻝﺎﺧﺩﺇ ﻰﺟﺮﻳ", Color.red);
            return;
        }

        Credential credential = EmailAuthProvider.GetCredential(user.Email, password);
        StartCoroutine(ReauthenticateAndDelete(credential));
    }

    // Coroutine لإعادة التحقق وحذف الحساب من Firebase Auth و Realtime Database
    private IEnumerator ReauthenticateAndDelete(Credential credential)
    {
        var reauthTask = user.ReauthenticateAsync(credential);
        yield return new WaitUntil(() => reauthTask.IsCompleted);

        if (reauthTask.IsFaulted || reauthTask.IsCanceled)
        {
            ShowStatusMessage("ﺎﻬﺘﺤﺻ ﻦﻣ ﺪﻛﺄﺗ !ﺔﺤﻴﺤﺻ ﺮﻴﻏ ﺭﻭﺮﻤﻟﺍ ﺔﻤﻠﻛ", Color.red);
            yield break;
        }

        string parentId = user.UserId;
        var dbReference = Firebase.Database.FirebaseDatabase.DefaultInstance.RootReference;

        // حذف بيانات المستخدم من قاعدة البيانات
        var deleteDataTask = dbReference.Child("parents").Child(parentId).RemoveValueAsync();
        yield return new WaitUntil(() => deleteDataTask.IsCompleted);

        if (deleteDataTask.IsFaulted || deleteDataTask.IsCanceled)
        {
            ShowStatusMessage("!ﺏﺎﺴﺤﻟﺍ ﻑﺬﺣ ﺀﺎﻨﺛﺃ ﺄﻄﺧ ﺙﺪﺣ", Color.red);
            yield break;
        }

        // حذف حساب Firebase نفسه
        var deleteTask = user.DeleteAsync();
        yield return new WaitUntil(() => deleteTask.IsCompleted);

        if (deleteTask.IsFaulted || deleteTask.IsCanceled)
        {
            ShowStatusMessage("ﻯﺮﺧﺃ ﺓﺮﻣ ﻝﻮﺧﺪﻟﺍ ﻞﻴﺠﺴﺗ ﻝﻭﺎﺣ !ﺏﺎﺴﺤﻟﺍ ﻑﺬﺣ ﻞﺸﻓ", Color.red);
        }
        else
        {
            ShowStatusMessage("!ﺡﺎﺠﻨﺑ ﺏﺎﺴﺤﻟﺍ ﻑﺬﺣ ﻢﺗ", Color.green);
            AlertDelete?.SetActive(true);
            yield return new WaitForSeconds(2);
            SceneManager.LoadScene("Signup-Login");
        }
    }

    // تسجيل خروج المستخدم
    public void SignOut()
    {
        auth.SignOut();
        AlertLogOut.SetActive(true);
        StartCoroutine(DelayedLogout(2f));
    }

    // عرض رسالة في الواجهة لوقت مؤقت
    private void ShowStatusMessage(string message, Color color)
    {
        statusText.gameObject.SetActive(true);
        statusText.text = message;
        statusText.color = color;
        StartCoroutine(HideStatusMessageAfterDelay(10));
    }

    // إخفاء رسالة الحالة بعد فترة
    private IEnumerator HideStatusMessageAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        statusText.gameObject.SetActive(false);
    }

    // التحقق من قوة كلمة المرور باستخدام regex
    private bool IsValidPassword(string password)
    {
        string pattern = @"^(?=.*[A-Z])(?=.*[a-z])(?=.*\d)(?=.*[+=\-&%$#@?!]).{8,}$";
        return Regex.IsMatch(password, pattern);
    }

    // تبديل حالة ظهور/إخفاء حقل كلمة المرور
    private void TogglePasswordVisibility(ref bool isVisible, TMP_InputField inputField, Image toggleImage)
    {
        isVisible = !isVisible;
        inputField.contentType = isVisible ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
        toggleImage.sprite = isVisible ? hideSprite : showSprite;
        inputField.ForceLabelUpdate();
    }

    // تسجيل الخروج مع تأخير بسيط
    private IEnumerator DelayedLogout(float delay)
    {
        yield return new WaitForSeconds(delay);
        auth.SignOut();
        SceneManager.LoadScene("Signup-Login");
    }

    // الانتقال للصفحة الرئيسية
    public void ReturnToHomePage()
    {
        SceneManager.LoadScene("HomePage");
    }
}
