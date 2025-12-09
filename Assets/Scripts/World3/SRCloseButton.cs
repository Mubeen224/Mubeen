using UnityEngine;

public class SRCloseButton : MonoBehaviour
{
    public SRMathManagerW3 srMathManager;

    [Tooltip("0: ض ، 1: ظ ، 2: غ")]
    public int srIndex = 0;

    // اربطي هذه الدالة من Button → OnClick داخل كانفس SR المناسب
    public void OnClosePressed()
    {
        if (!srMathManager)
        {
            Debug.LogWarning("SRCloseButton: SRMathManagerW3 غير معيّن.");
            return;
        }
        srMathManager.OpenMathGate(srIndex);
    }
}
