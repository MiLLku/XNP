using UnityEngine;

/// <summary>
/// 제작 장비의 품질. 값 0이 평범함이라 품질이 없던 세이브·시작 장비·채용 장비는 그대로 평범함이 된다.
/// 순서(낮음→높음): 끔찍함 - 평범함 - 완벽함 - 뛰어남 - 전설적
/// </summary>
public enum EquipmentQuality
{
    Awful = -1,
    Normal = 0,
    Perfect = 1,
    Excellent = 2,
    Legendary = 3,
}

/// <summary>품질 판정·배율·표시 — 수치는 Resources/EquipmentQualityConfig</summary>
public static class EquipmentQualities
{
    private static EquipmentQualityConfig _config;
    public static EquipmentQualityConfig Config => _config != null ? _config : (_config = Resources.Load<EquipmentQualityConfig>("EquipmentQualityConfig"));

    public static readonly EquipmentQuality[] Ordered =
        { EquipmentQuality.Awful, EquipmentQuality.Normal, EquipmentQuality.Perfect, EquipmentQuality.Excellent, EquipmentQuality.Legendary };

    private static int Index(EquipmentQuality q) => (int)q + 1;

    public static string Label(EquipmentQuality q)
    {
        switch (q)
        {
            case EquipmentQuality.Awful: return "끔찍함";
            case EquipmentQuality.Perfect: return "완벽함";
            case EquipmentQuality.Excellent: return "뛰어남";
            case EquipmentQuality.Legendary: return "전설적";
            default: return "평범함";
        }
    }

    /// <summary>성능·내구도 배율 (금속 단계 배율과 곱한다)</summary>
    public static float Multiplier(EquipmentQuality q)
    {
        var m = Config != null ? Config.multipliers : null;
        int i = Index(q);
        return m != null && i < m.Length ? m[i] : 1f;
    }

    /// <summary>표시 이름 — 평범함은 품질을 붙이지 않는다</summary>
    public static string Name(EquipmentQuality q, string baseName) => q == EquipmentQuality.Normal ? baseName : $"[{Label(q)}] {baseName}";

    /// <summary>
    /// 제작 점수 P = 제작 적성 레벨 + 제작 건물 단계 보너스 + 같은 방 부속 건물 종류당 보너스(상한 있음).
    /// </summary>
    public static float Score(Employee crafter, Building station)
    {
        var cfg = Config;
        if (cfg == null) return 0f;
        float p = crafter != null && crafter.Growth != null ? crafter.Growth.GetAptitudeLevel(WorkType.Crafting) : 0f;
        if (station != null && station.buildingData != null)
        {
            p += cfg.TierBonusOf(station.buildingData.buildingID);
            p += Mathf.Min(cfg.maxAddonBonus, BuildingAddonUtil.CountAddonKinds(station) * cfg.addonBonusPerKind);
        }
        return Mathf.Clamp(p, 0f, cfg.maxScore);
    }

    /// <summary>점수 P에서 확률을 선형 보간해 품질을 뽑는다</summary>
    public static EquipmentQuality Roll(float score)
    {
        var cfg = Config;
        if (cfg == null) return EquipmentQuality.Normal;
        float t = cfg.maxScore > 0f ? Mathf.Clamp01(score / cfg.maxScore) : 0f;
        float total = 0f;
        for (int i = 0; i < Ordered.Length; i++) total += Weight(cfg, i, t);
        float r = Random.value * total;
        for (int i = 0; i < Ordered.Length; i++)
        {
            r -= Weight(cfg, i, t);
            if (r < 0f) return Ordered[i];
        }
        return EquipmentQuality.Normal;
    }

    private static float Weight(EquipmentQualityConfig cfg, int i, float t)
        => Mathf.Max(0f, Mathf.Lerp(cfg.chanceAtMinScore[i], cfg.chanceAtMaxScore[i], t));
}
