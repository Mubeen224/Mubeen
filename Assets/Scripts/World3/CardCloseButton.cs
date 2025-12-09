using UnityEngine;

public class CardCloseButton : MonoBehaviour
{
    public CardMathManagerW3 cardMathManager;
    [Tooltip("0: ض ، 1: ظ ، 2: غ")]
    public int cardIndex = 0;

    // اربطي هذه الدالة من Button → OnClick
    public void OnClosePressed()
    {
        if (!cardMathManager)
        {
            Debug.LogWarning("CardCloseButton: CardMathManager غير معيّن.");
            return;
        }
        cardMathManager.OpenMath(cardIndex);
    }
}