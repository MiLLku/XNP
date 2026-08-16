/// <summary>
/// 연구로 얻는 전역 스탯 보너스 종류.
/// 값은 비율(0.1 = +10%)이며 ResearchTreeManager.GetStatBonus로 조회합니다.
/// 새 타입을 추가하면 반드시 소비 지점(작업 속도·스탯 계산 등)도 함께 배선하세요.
/// </summary>
public enum ResearchStatType
{
    ResearchSpeedBonus,
    ConstructionSpeedBonus,
    CraftingSpeedBonus,
    HarvestYieldBonus,
    EmployeeMaxHealthBonus,
    EmployeeAttackPowerBonus,
    ErosionResistanceBonus,

    /// <summary>채광 속도 — 깊은 층의 높은 경도를 상쇄하는 주 수단</summary>
    MiningSpeedBonus,

    /// <summary>수확·벌목 속도</summary>
    HarvestSpeedBonus,

    /// <summary>
    /// 침식 자연 회복 하한 감소 (비율이 아니라 <b>침식 수치 그대로</b>).
    /// 기본 하한 50에서 이만큼 빼서 유효 하한을 구한다 — 게임 진행에 따라 자립도가 올라가는 축.
    /// 소비 지점: ErosionManager.EffectiveRecoveryFloor
    /// </summary>
    ErosionRecoveryFloorReduction,
}
