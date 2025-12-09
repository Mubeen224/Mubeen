using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using Mankibo;

public class MathExitManagerW2 : MonoBehaviour
{
    // ===================== UI: Math Canvas =====================
    [Header("Math Canvas")]
    public GameObject mathCanvas;
    public TMP_Text equationText;
    public TMP_InputField answerInput;
    public TMP_Text emptyFieldWarning;
    public Button answerButton;
    public Button wrongOkayButton;
    public Button backButton;
    public GameObject wrongAnswerPanel;

    // ===================== UI: Tracing Canvases =================
    [Header("Tracing Canvases & Scripts")]
    public List<GameObject> tracingCanvases = new List<GameObject>();
    public List<LetterTracingW2> tracingScripts = new List<LetterTracingW2>();

    [Header("Player")]
    public World2 playerScript;

    // ===================== Return Policy =======================
    public enum ReturnDest { Tracing, AR }
    private ReturnDest wrongReturnDest = ReturnDest.Tracing;

    // الرجوع للتتريس (مراجع مباشرة — المفضل)
    private LetterTracingW2 tracingScriptForReturn = null;
    private GameObject tracingCanvasForReturn = null;

    // احتياطي: الرجوع بالفهرس إذا لم تتوفر المراجع
    private int tracingIndexForReturn = -1;

    // الرجوع للـAR
    private GameObject arCanvasForReturn = null;
    private AudioSource arReturnAudioRef = null;

    // ===================== Runtime =============================
    private int correctAnswer = 0;
    private Action onSuccessCallback;

    private void Awake()
    {
        if (answerButton) answerButton.onClick.AddListener(CheckAnswer);
        if (wrongOkayButton) wrongOkayButton.onClick.AddListener(ReturnAfterWrong);
        if (backButton) backButton.onClick.AddListener(RestorePreviousUIAndState);
    }

    private void Start()
    {
        if (playerScript == null) playerScript = FindObjectOfType<World2>();
    }

    // ---------- فتح من التتريس (بالفهرس - للتوافق) ----------
    public void OpenMathFromTracing(Action onCorrect, int tracingIndex)
    {
        onSuccessCallback = onCorrect;
        wrongReturnDest = ReturnDest.Tracing;

        tracingIndexForReturn = tracingIndex;
        tracingScriptForReturn = null;
        tracingCanvasForReturn = null;

        arCanvasForReturn = null;
        arReturnAudioRef = null;

        OpenMathCanvas();
    }

    // ---------- فتح من التتريس (بالمراجع - مفضل) ----------
    public void OpenMathFromTracing(Action onCorrect, LetterTracingW2 scriptRef, GameObject canvasRef)
    {
        onSuccessCallback = onCorrect;
        wrongReturnDest = ReturnDest.Tracing;

        tracingScriptForReturn = scriptRef;
        tracingCanvasForReturn = canvasRef;

        tracingIndexForReturn = -1; // نتجاهل الفهرس
        arCanvasForReturn = null;
        arReturnAudioRef = null;

        OpenMathCanvas();
    }

    // ---------- فتح من الـAR (صيغة قديمة للتوافق) ----------
    public void OpenMathFromAR(Action onCorrect, GameObject arCanvasToReturn)
    {
        OpenMathFromAR(onCorrect, arCanvasToReturn, null);
    }

    // ---------- فتح من الـAR (مع تمرير صوت الـAR) ----------
    public void OpenMathFromAR(Action onCorrect, GameObject arCanvasToReturn, AudioSource arReturnAudio)
    {
        onSuccessCallback = onCorrect;
        wrongReturnDest = ReturnDest.AR;

        arCanvasForReturn = arCanvasToReturn;
        arReturnAudioRef = arReturnAudio;

        tracingScriptForReturn = null;
        tracingCanvasForReturn = null;
        tracingIndexForReturn = -1;

        OpenMathCanvas();
    }

    // ---------- العرض ----------
    private void OpenMathCanvas()
    {
        GenerateRandomEquation();

        if (mathCanvas) mathCanvas.SetActive(true);
        if (wrongAnswerPanel) wrongAnswerPanel.SetActive(false);

        // إخفِ جميع كانفسات التتريس أثناء الحل (لتفادي حجب الإدخال)
        foreach (var c in tracingCanvases) if (c) c.SetActive(false);

        if (playerScript) { playerScript.canMove = false; playerScript.Idle(); }
    }

    private void RestorePreviousUIAndState()
    {
        // رجوع إلى المصدر (زر Back)
        if (wrongAnswerPanel) wrongAnswerPanel.SetActive(false);
        if (mathCanvas) mathCanvas.SetActive(false);

        if (wrongReturnDest == ReturnDest.Tracing)
        {
            // مراجع مباشرة أولاً
            if (tracingCanvasForReturn != null && tracingScriptForReturn != null)
            {
                tracingCanvasForReturn.SetActive(true);

                var s = tracingScriptForReturn;
                s.StartNewAttempt();

                if (s.LetterImageObj) s.LetterImageObj.SetActive(false);
                if (s.TracingPointsGroup) s.TracingPointsGroup.SetActive(false);

                if (s.letterAudio != null)
                {
                    s.letterAudio.Stop();
                    s.letterAudio.Play();
                    s.SetCanTraceAfterAudio(s.letterAudio);
                }
            }
            else
            {
                // احتياطي: الرجوع بالفهرس
                if (tracingIndexForReturn >= 0 &&
                    tracingIndexForReturn < tracingCanvases.Count &&
                    tracingCanvases[tracingIndexForReturn])
                {
                    var canvas = tracingCanvases[tracingIndexForReturn];
                    canvas.SetActive(true);

                    if (tracingIndexForReturn < tracingScripts.Count &&
                        tracingScripts[tracingIndexForReturn])
                    {
                        var s = tracingScripts[tracingIndexForReturn];
                        s.StartNewAttempt();

                        if (s.LetterImageObj) s.LetterImageObj.SetActive(false);
                        if (s.TracingPointsGroup) s.TracingPointsGroup.SetActive(false);

                        if (s.letterAudio != null)
                        {
                            s.letterAudio.Stop();
                            s.letterAudio.Play();
                            s.SetCanTraceAfterAudio(s.letterAudio);
                        }
                    }
                }
            }

            if (playerScript) playerScript.canMove = false; // أثناء التتريس الحركة مقفلة
        }
        else // ReturnDest.AR
        {
            if (arCanvasForReturn) arCanvasForReturn.SetActive(true);

            // تشغيل صوت الـAR بعد التفعيل بإطار واحد
            if (arReturnAudioRef != null) StartCoroutine(PlayAudioNextFrame(arReturnAudioRef));
            else
            {
                var tracing = arCanvasForReturn ? arCanvasForReturn.GetComponentInChildren<LetterTracingW2>(true) : null;
                if (tracing != null && tracing.arPopupAudio != null)
                    StartCoroutine(PlayAudioNextFrame(tracing.arPopupAudio));
            }

            if (playerScript) playerScript.canMove = false; // أثناء AR الحركة مقفلة
        }
    }

    private void CloseMathOnly()
    {
        if (wrongAnswerPanel) wrongAnswerPanel.SetActive(false);
        if (mathCanvas) mathCanvas.SetActive(false);
        // لا نغيّر حالة الحركة هنا — الدوال الأخرى تتكفل بها
    }

    // ---------- المنطق ----------
    private void GenerateRandomEquation()
    {
        int n1 = UnityEngine.Random.Range(1, 10);
        int n2 = UnityEngine.Random.Range(1, 10);
        correctAnswer = n1 * n2;

        if (equationText)
            equationText.text = $"{ToArabic(n1)} × {ToArabic(n2)} = ؟";

        if (answerInput) answerInput.text = "";
        if (emptyFieldWarning) emptyFieldWarning.gameObject.SetActive(false);
    }

    private void CheckAnswer()
    {
        if (!answerInput || string.IsNullOrWhiteSpace(answerInput.text))
        {
            if (emptyFieldWarning)
            {
                emptyFieldWarning.text = "لا يمكن ترك الإجابة فارغة!";
                emptyFieldWarning.gameObject.SetActive(true);
            }
            return;
        }
        if (emptyFieldWarning) emptyFieldWarning.gameObject.SetActive(false);

        if (int.TryParse(answerInput.text, out int playerAns))
        {
            if (playerAns == correctAnswer) CorrectAnswerAction();
            else WrongAnswerAction();
        }
        else
        {
            if (emptyFieldWarning)
            {
                emptyFieldWarning.text = "الرجاء إدخال أرقام فقط.";
                emptyFieldWarning.gameObject.SetActive(true);
            }
        }
    }

    private void CorrectAnswerAction()
    {
        if (wrongAnswerPanel) wrongAnswerPanel.SetActive(false);
        if (mathCanvas) mathCanvas.SetActive(false);

        if (playerScript) playerScript.canMove = true;

        onSuccessCallback?.Invoke();
    }

    private void WrongAnswerAction()
    {
        if (wrongAnswerPanel) wrongAnswerPanel.SetActive(true);
        if (mathCanvas) mathCanvas.SetActive(false);
        if (playerScript) playerScript.canMove = false;
    }

    private void ReturnAfterWrong()
    {
        if (wrongAnswerPanel) wrongAnswerPanel.SetActive(false);

        if (wrongReturnDest == ReturnDest.Tracing)
        {
            // مراجع مباشرة أولاً
            if (tracingCanvasForReturn != null && tracingScriptForReturn != null)
            {
                tracingCanvasForReturn.SetActive(true);

                var s = tracingScriptForReturn;
                s.StartNewAttempt();

                if (s.LetterImageObj) s.LetterImageObj.SetActive(false);
                if (s.TracingPointsGroup) s.TracingPointsGroup.SetActive(false);

                if (s.letterAudio != null)
                {
                    s.letterAudio.Stop();
                    s.letterAudio.Play();
                    s.SetCanTraceAfterAudio(s.letterAudio);
                }
                return; // لا حاجة للفهرس
            }

            // احتياطي: بالفهرس
            if (tracingIndexForReturn >= 0 &&
                tracingIndexForReturn < tracingCanvases.Count &&
                tracingCanvases[tracingIndexForReturn])
            {
                var canvas = tracingCanvases[tracingIndexForReturn];
                canvas.SetActive(true);

                if (tracingIndexForReturn < tracingScripts.Count &&
                    tracingScripts[tracingIndexForReturn])
                {
                    var s = tracingScripts[tracingIndexForReturn];
                    s.StartNewAttempt();

                    if (s.LetterImageObj) s.LetterImageObj.SetActive(false);
                    if (s.TracingPointsGroup) s.TracingPointsGroup.SetActive(false);

                    if (s.letterAudio != null)
                    {
                        s.letterAudio.Stop();
                        s.letterAudio.Play();
                        s.SetCanTraceAfterAudio(s.letterAudio);
                    }
                }
            }
        }
        else // ReturnDest.AR
        {
            if (arCanvasForReturn != null)
            {
                arCanvasForReturn.SetActive(true);

                // إعادة تشغيل صوت الـAR بعد التفعيل
                if (arReturnAudioRef != null) StartCoroutine(PlayAudioNextFrame(arReturnAudioRef));
                else
                {
                    var tracing = arCanvasForReturn.GetComponentInChildren<LetterTracingW2>(true);
                    if (tracing != null && tracing.arPopupAudio != null)
                        StartCoroutine(PlayAudioNextFrame(tracing.arPopupAudio));
                }
            }
        }
    }

    private IEnumerator PlayAudioNextFrame(AudioSource src)
    {
        yield return null; // انتظر إطارًا لضمان تفعيل الكائنات
        if (src == null) yield break;
        src.Stop();
        src.Play();
    }

    // ---------- أدوات ----------
    private static string ToArabic(int num)
    {
        string[] d = { "٠", "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩" };
        var s = num.ToString();
        System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s) sb.Append(char.IsDigit(c) ? d[c - '0'] : c);
        return sb.ToString();
    }
}
