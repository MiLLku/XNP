using UnityEngine;

/// <summary>
/// UI 패널의 추상 베이스 클래스.
/// 모든 UI 패널이 공통으로 상속하며, 열기/닫기 동작을 정의합니다.
/// </summary>
public abstract class BasePanel : MonoBehaviour
{
    [Header("Panel Settings")]
    public UIPanelType panelType;

    /// <summary>
    /// 패널이 열릴 때 호출됩니다.
    /// </summary>
    public virtual void OnOpen()
    {
        gameObject.SetActive(true);
    }

    /// <summary>
    /// 패널이 닫힐 때 호출됩니다.
    /// </summary>
    public virtual void OnClose()
    {
        gameObject.SetActive(false);
    }
}
