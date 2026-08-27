using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 디버그 패널의 차단 스위치 한 줄.
/// 프리팹 안의 템플릿을 복제해 사용하며, 오브젝트를 코드로 만들지 않습니다.
/// </summary>
public class DebugToggleRow : MonoBehaviour
{
    [Header("UI 요소")]
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private Toggle toggle;
    [SerializeField] private Image background;

    [Header("색상")]
    [SerializeField] private Color offColor = new Color(0.16f, 0.17f, 0.21f, 1f);
    [SerializeField] private Color onColor  = new Color(0.30f, 0.18f, 0.18f, 1f);

    private DebugFlag flag;

    /// <summary>이 줄이 담당하는 플래그로 초기화합니다.</summary>
    public void Setup(DebugFlag targetFlag, bool isOn)
    {
        flag = targetFlag;

        if (labelText != null) labelText.text = DebugFlagInfo.GetLabel(flag);
        if (descriptionText != null) descriptionText.text = DebugFlagInfo.GetDescription(flag);

        if (toggle != null)
        {
            toggle.onValueChanged.RemoveAllListeners();
            toggle.SetIsOnWithoutNotify(isOn);
            toggle.onValueChanged.AddListener(OnToggleChanged);
        }

        ApplyColor(isOn);
    }

    private void OnToggleChanged(bool isOn)
    {
        DebugManager.instance?.SetFlag(flag, isOn);
        ApplyColor(isOn);
    }

    private void ApplyColor(bool isOn)
    {
        if (background != null) background.color = isOn ? onColor : offColor;
    }
}
