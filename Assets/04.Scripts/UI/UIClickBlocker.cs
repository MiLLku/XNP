using UnityEngine;

/// <summary>
/// 월드 클릭 차단 마커.
/// InteractionManager.IsPointerOverInteractiveUI가 Selectable(버튼) 외에
/// 이 컴포넌트가 붙은 UI(자식 포함) 위의 클릭도 UI 클릭으로 취급한다.
///
/// 용도: 버튼 사이 배경을 클릭해도 월드로 새서 선택이 풀리면 안 되는 패널
/// (전투 태세 바 Content 등)에 부착. Image 등 raycastTarget 그래픽이 있어야 잡힌다.
/// </summary>
public class UIClickBlocker : MonoBehaviour
{
}
