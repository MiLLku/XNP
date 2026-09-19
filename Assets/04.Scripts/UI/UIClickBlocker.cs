using UnityEngine;

/// <summary>
/// 월드 클릭 차단 마커 — <b>더 이상 필요 없습니다.</b>
///
/// 지금은 UIManager.IsPointerOverUI가 raycastTarget이 켜진 그래픽이면 전부 UI로 치므로
/// 이 마커 없이도 패널 배경이 월드 클릭을 막습니다. 씬에 이미 붙어 있는 것들이 있어
/// (빼면 스크립트 누락이 되므로) 클래스만 남겨둡니다. 새로 붙이지 마세요.
/// </summary>
public class UIClickBlocker : MonoBehaviour
{
}
