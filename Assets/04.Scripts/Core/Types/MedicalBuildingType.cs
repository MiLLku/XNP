/// <summary>
/// 의료 건물 종류 식별자.
///
/// 정수값 = BuildingData.buildingID = GameIDRegistry.Buildings의 의료 대역(3700~3799)과 일치합니다.
/// (enum↔id 연동 규칙 — WashBuildingType과 같은 패턴)
/// </summary>
public enum MedicalBuildingType
{
    /// <summary>임시 요양소 (2x2, 무전력, 동시 1명) — 정식 의료 건물 전 임시</summary>
    TempRecuperationBed = 3700,
}
