using UnityEngine;

/// <summary>
/// 직원이 지금 있는 방의 온도를 견디는지 판정합니다.
///
/// <b>체온을 따로 추적하지 않습니다.</b> 현재 방 온도를 그대로 읽어 즉시 판정하므로
/// 세이브에 저장할 상태가 없습니다(마이그레이션 불필요). 림월드처럼 개인 체온이
/// 서서히 오르내리는 세분화는 의도적으로 하지 않았습니다.
///
/// <b>견디는 범위</b> = 기본 쾌적 범위를 착용 장비의 방한/방열 레벨만큼 넓힌 것.
/// 레벨 1당 고정폭이라 "방한 2 = 0℃까지"처럼 산술로 자명합니다.
///
/// 범위를 벗어난 정도(초과 도수)로 3단계를 나누고, 정신력은 <b>상태형 모디파이어</b>로 겁니다 —
/// 쾌적한 곳으로 돌아오거나 장비를 갖추면 즉시 사라집니다(굶주림·탈진과 같은 방식).
/// </summary>
[RequireComponent(typeof(Employee))]
public class EmployeeTemperature : MonoBehaviour
{
    #region 필드

    private Employee employee;
    private EmployeeStatsController statsController;
    private EmployeeEquipment equipment;

    private float tickTimer;

    /// <summary>마지막으로 판정한 방 온도 (UI·디버그용)</summary>
    public float LastRoomTemperature { get; private set; }

    /// <summary>마지막으로 계산한 견딤 하한</summary>
    public float LastToleratedMin { get; private set; }

    /// <summary>마지막으로 계산한 견딤 상한</summary>
    public float LastToleratedMax { get; private set; }

    /// <summary>현재 초과 단계 (0 = 쾌적, 1~3)</summary>
    public int CurrentTier { get; private set; }

    #endregion

    #region 초기화

    private void Awake()
    {
        employee = GetComponent<Employee>();
        statsController = GetComponent<EmployeeStatsController>();
        equipment = GetComponent<EmployeeEquipment>();
    }

    #endregion

    #region 판정

    private void Update()
    {
        if (employee == null || employee.State == EmployeeState.Dead) return;

        TemperatureManager manager = TemperatureManager.instance;
        if (manager == null) return;

        float interval = manager.Config != null ? Mathf.Max(0.1f, manager.Config.conditionTickInterval) : 1f;

        tickTimer += Time.deltaTime;
        if (tickTimer < interval) return;

        float delta = tickTimer;
        tickTimer = 0f;
        Evaluate(manager, delta);
    }

    private void Evaluate(TemperatureManager manager, float deltaTime)
    {
        if (statsController == null) return;

        TemperatureConfig config = manager.Config;
        if (config == null) return;

        Vector2Int cell = new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.y));

        float roomTemperature = manager.GetTemperatureAt(cell);
        LastRoomTemperature = roomTemperature;

        // 견디는 범위 = 기본 쾌적 범위 + 장비 레벨 × 레벨당 폭
        int coldLevel = equipment != null ? equipment.GetTotalColdProtection() : 0;
        int heatLevel = equipment != null ? equipment.GetTotalHeatProtection() : 0;

        LastToleratedMin = config.comfortMin - coldLevel * config.degreesPerProtectionLevel;
        LastToleratedMax = config.comfortMax + heatLevel * config.degreesPerProtectionLevel;

        // 얼마나 벗어났는지 (추위/더위 중 하나만 성립한다)
        float coldExcess = LastToleratedMin - roomTemperature;
        float heatExcess = roomTemperature - LastToleratedMax;

        bool isCold = coldExcess > 0f;
        bool isHot = heatExcess > 0f;
        float excess = isCold ? coldExcess : (isHot ? heatExcess : 0f);

        CurrentTier = GetTier(config, excess);

        // 정신력 — 상태형이라 조건이 풀리면 즉시 사라진다
        float penalty = GetMentalPenalty(config, CurrentTier);
        statsController.SetConditionalMental(MentalReason.COLD, "추위", penalty, isCold);
        statsController.SetConditionalMental(MentalReason.HEAT, "더위", penalty, isHot);

        // 체력 — 단계별 초당 감소
        float healthLoss = GetHealthLoss(config, CurrentTier);
        if (healthLoss > 0f)
            statsController.ModifyHealth(-healthLoss * deltaTime);
    }

    /// <summary>초과 도수로 단계를 구합니다. 0이면 쾌적.</summary>
    private int GetTier(TemperatureConfig config, float excess)
    {
        if (excess <= 0f) return 0;
        if (excess <= config.tier1MaxExcess) return 1;
        if (excess <= config.tier2MaxExcess) return 2;
        return 3;
    }

    private float GetMentalPenalty(TemperatureConfig config, int tier)
    {
        switch (tier)
        {
            case 1: return config.tier1MentalPenalty;
            case 2: return config.tier2MentalPenalty;
            case 3: return config.tier3MentalPenalty;
            default: return 0f;
        }
    }

    private float GetHealthLoss(TemperatureConfig config, int tier)
    {
        switch (tier)
        {
            case 1: return Mathf.Max(0f, config.tier1HealthLossPerSecond);
            case 2: return Mathf.Max(0f, config.tier2HealthLossPerSecond);
            case 3: return Mathf.Max(0f, config.tier3HealthLossPerSecond);
            default: return 0f;
        }
    }

    #endregion

    #region 조회

    /// <summary>
    /// 지금 상태를 한 줄로 설명합니다. (직원 관리창·디버그용)
    /// 예: "22.4도 · 견딤 0~25도 (방한 2)"
    /// </summary>
    public string Describe()
    {
        int coldLevel = equipment != null ? equipment.GetTotalColdProtection() : 0;
        int heatLevel = equipment != null ? equipment.GetTotalHeatProtection() : 0;

        string protection = "";
        if (coldLevel > 0) protection += $" 방한{coldLevel}";
        if (heatLevel > 0) protection += $" 방열{heatLevel}";
        if (protection.Length == 0) protection = " 보호 없음";

        string state = CurrentTier == 0 ? "쾌적" : $"{CurrentTier}단계";
        return $"{LastRoomTemperature:F1}도 · 견딤 {LastToleratedMin:F0}~{LastToleratedMax:F0}도 ({protection.Trim()}) · {state}";
    }

    #endregion
}
