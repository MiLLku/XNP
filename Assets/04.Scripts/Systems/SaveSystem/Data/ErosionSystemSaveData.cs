using System;

/// <summary>
/// 침식 시스템 전역 상태 저장 데이터.
/// SaveData.erosionSystem 필드로 사용됩니다.
///
/// v10부터 포스트 레이드 가속 회복이 제거되고(회복 경로는 자연 회복·세척·이벤트 셋뿐),
/// 대신 게임 진행에 따라 낮아지는 자연 회복 하한 감소량을 저장합니다.
/// </summary>
[Serializable]
public class ErosionSystemSaveData
{
    /// <summary>
    /// 자연 회복 하한을 낮추는 런타임 감소량 (v10).
    /// 유효 하한 = max(0, Config 기본하한 - 연구 감소 - 이 값)
    /// </summary>
    public float runtimeFloorReduction;
}
