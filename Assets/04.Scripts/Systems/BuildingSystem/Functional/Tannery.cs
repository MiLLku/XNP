using UnityEngine;

/// <summary>
/// 무두질 작업장.
/// 가죽을 가공하는 생산 건물.
/// 맑은 날씨에만 작업 가능합니다 (햇빛 필요).
/// </summary>
public class Tannery : ProductionBuilding
{
    #region 필드

    [Header("무두질 작업장 전용")]
    [SerializeField] private bool requiresSunlight = true;

    #endregion

    #region ProductionBuilding 오버라이드

    protected override string GetBuildingName()
    {
        return "무두질 작업장";
    }

    /// <summary>
    /// 생산 주문 생성 전 날씨를 확인합니다.
    /// 날씨가 적합하지 않으면 예약 없이 즉시 반환합니다.
    /// </summary>
    protected override void OnProductionOrderCreated(CraftingRecipe recipe, int amount, Employee worker)
    {
        if (requiresSunlight && !IsWeatherSuitable())
        {
            Debug.LogWarning("[무두질 작업장] 햇빛이 필요합니다! 비가 오거나 밤에는 작업할 수 없습니다.");
            // TODO [날씨시스템]: UI 메시지 표시
            return;
        }

        // 날씨가 괜찮으면 정상 진행 (부모에서 예약 처리)
        base.OnProductionOrderCreated(recipe, amount, worker);
    }

    /// <summary>
    /// 제작 시작 시 호출됩니다.
    /// </summary>
    protected override void OnProductionStarted()
    {
        base.OnProductionStarted();
        Debug.Log("[무두질 작업장] 가죽을 햇빛에 말리기 시작합니다!");
    }

    /// <summary>
    /// 제작 완료 시 호출됩니다.
    /// </summary>
    protected override void OnProductionCompleted(CraftingRecipe recipe, int amount)
    {
        base.OnProductionCompleted(recipe, amount);
        Debug.Log("[무두질 작업장] 가죽 무두질 완료!");
    }

    #endregion

    #region 날씨 확인

    /// <summary>
    /// 현재 날씨가 작업에 적합한지 확인합니다.
    /// </summary>
    /// <returns>작업 가능 여부</returns>
    private bool IsWeatherSuitable()
    {
        // TODO [날씨시스템]: 실제 날씨 시스템과 연동
        // 예시: return WeatherSystem.instance.IsDay && !WeatherSystem.instance.IsRaining;
        return true;
    }

    #endregion
}
