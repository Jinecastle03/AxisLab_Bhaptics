using UnityEngine;
using TMPro;

public class YawSpinUIBinder : MonoBehaviour
{
    public YawSpinMotors motors;

    [Header("Omega")]
    public TMP_Text omegaText;

    [Header("Gamma")]
    public TMP_Text gammaFBText;
    public TMP_Text gammaSideText;

    void Update()
    {
        if (motors == null) return;

        if (omegaText)
            omegaText.text = motors.omegaDegPerSec.ToString("0");

        if (gammaFBText)
            gammaFBText.text = motors.gammaFB.ToString("0.00");

        if (gammaSideText)
            gammaSideText.text = motors.gammaSideDepth.ToString("0.00");
    }
}
