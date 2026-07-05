/// <summary>
/// 식량 부족 경고 평가기. 전역 인벤토리의 음식 가용 총량을 기준값과 비교한다.
/// 출력은 이 코드에서 생성하고, 기준값만 Config(SO)에서 읽는다.
/// </summary>
public class FoodShortageAlertEvaluator : IAlertEvaluator
{
    private readonly FoodShortageAlertConfig cfg;

    public FoodShortageAlertEvaluator(FoodShortageAlertConfig cfg)
    {
        this.cfg = cfg;
    }

    public bool Enabled => cfg != null && cfg.enabled;

    public AlertReport Evaluate()
    {
        var inv = InventoryManager.instance;
        if (inv == null) return AlertReport.Inactive;

        int food = inv.GetTotalFoodCount();

        if (food <= cfg.criticalThreshold)
            return new AlertReport
            {
                active = true,
                severity = AlertSeverity.Critical,
                label = $"식량 부족: {food}",
                culprits = null
            };

        if (food <= cfg.cautionThreshold)
            return new AlertReport
            {
                active = true,
                severity = AlertSeverity.Caution,
                label = $"식량 주의: {food}",
                culprits = null
            };

        return AlertReport.Inactive;
    }
}
