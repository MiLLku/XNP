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

        LastOpened = this;
        LastOpenedFrame = Time.frameCount;
        // 건물 정보 창이 떠 있으면 화면 차단막이 깔려 있다 — 그 뒤에 여는 창(작업 배정·아이템 정보 등)은 차단막 위로
        if (BuildingNameBar.IsOpen) transform.SetAsLastSibling();
    }

    /// <summary>가장 최근에 열린 패널과 그 프레임 — 건물 이름 바가 '방금 열린 정보 창'을 찾는 데 씁니다.</summary>
    public static BasePanel LastOpened { get; private set; }
    public static int LastOpenedFrame { get; private set; } = -1;

    /// <summary>
    /// 패널이 닫힐 때 호출됩니다.
    /// </summary>
    public virtual void OnClose()
    {
        gameObject.SetActive(false);
    }
}
