using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 디버그 패널의 즉시 실행 버튼 한 줄.
/// </summary>
public class DebugActionRow : MonoBehaviour
{
    [Header("UI 요소")]
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private Button button;

    /// <summary>버튼 이름과 동작을 설정합니다.</summary>
    public void Setup(string label, string description, Action onClick)
    {
        if (labelText != null) labelText.text = label;
        if (descriptionText != null) descriptionText.text = description;

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            if (onClick != null) button.onClick.AddListener(() => onClick());
        }
    }
}
