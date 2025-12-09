using UnityEngine;

public class CardCloseButtonW1 : MonoBehaviour
{
    public CardMathManagerW1 cardMathManager;
    [Tooltip("0: ذ")]
    public int cardIndex = 0;

    // اربطي هذه الدالة من Button → OnClick
    public void OnClosePressed()
    {
        if (!cardMathManager)
        {
            Debug.LogWarning("CardCloseButtonW1: CardMathManager غير معيّن.");
            return;
        }
        cardMathManager.OpenMath(cardIndex);
    }
}
