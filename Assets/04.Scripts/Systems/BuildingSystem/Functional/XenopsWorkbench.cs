/// <summary>
/// 제놉스 장비 제작대 — 제놉스 전용 장비를 만드는 생산 건물.
/// 레시피는 부위별 제작 연구 + 그 제놉스 도감 100%가 모두 채워져야 목록에 나온다 (CraftingRecipe.IsAvailable).
/// </summary>
public class XenopsWorkbench : ProductionBuilding
{
    protected override string GetBuildingName() => "제놉스 장비 제작대";
}
