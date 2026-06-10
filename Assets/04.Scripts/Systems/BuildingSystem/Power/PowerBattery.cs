using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 축전기. 전력망의 잉여 전력을 저장하고, 부족할 때 방전하여 보충합니다.
/// 저장 용량(capacityJoules)은 제작 재료에 따라 프리팹마다 다르게 설정합니다.
///
/// 현재 충전량은 IBuildingExtraSerializable로 세이브에 보존됩니다.
/// </summary>
[RequireComponent(typeof(Building))]
public class PowerBattery : MonoBehaviour, IPowerNode, IBuildingExtraSerializable
{
    [Header("축전")]
    [Tooltip("저장 용량(Joule). 제작 재료에 따라 프리팹마다 다르게 설정합니다.")]
    [SerializeField] private float capacityJoules = 10000f;
    [Tooltip("현재 충전량(Joule).")]
    [SerializeField] private float currentCharge = 0f;
    [Tooltip("최대 충전 속도(W).")]
    [SerializeField] private float maxChargeRateWatts = 500f;
    [Tooltip("최대 방전 속도(W).")]
    [SerializeField] private float maxDischargeRateWatts = 500f;

    private Building _building;

    public bool IsOnline => _building == null || _building.IsFunctional;
    public float Capacity => capacityJoules;
    public float CurrentCharge => currentCharge;
    public float ChargeRatio => capacityJoules > 0f ? Mathf.Clamp01(currentCharge / capacityJoules) : 0f;

    // ── IPowerNode ──────────────────────────────────────────────
    public IEnumerable<Vector2Int> OccupiedCells => PowerUtil.FootprintCells(transform, _building);
    public PowerNodeKind Kind => PowerNodeKind.Battery;

    void Awake()
    {
        _building = GetComponent<Building>();
        currentCharge = Mathf.Clamp(currentCharge, 0f, capacityJoules);
    }

    void OnEnable()
    {
        var pm = PowerManager.instance;
        if (pm != null) pm.RegisterBattery(this);
    }

    void OnDisable()
    {
        var pm = PowerManager.instance;
        if (pm != null) pm.UnregisterBattery(this);
    }

    /// <summary>dt초 동안 방전 가능한 최대 전력(W).</summary>
    public float AvailableDischargeW(float dt)
    {
        if (!IsOnline || dt <= 0f) return 0f;
        return Mathf.Min(maxDischargeRateWatts, currentCharge / dt);
    }

    /// <summary>joules만큼 충전을 시도하고, 레이트·용량 한도 내에서 실제 충전한 양(Joule)을 반환합니다.</summary>
    public float Charge(float joules, float dt)
    {
        if (!IsOnline || joules <= 0f) return 0f;
        float rateLimit = maxChargeRateWatts * dt;
        float space = capacityJoules - currentCharge;
        float accepted = Mathf.Min(joules, Mathf.Min(rateLimit, space));
        if (accepted < 0f) accepted = 0f;
        currentCharge += accepted;
        return accepted;
    }

    /// <summary>joules만큼 방전을 시도하고, 레이트·잔량 한도 내에서 실제 공급한 양(Joule)을 반환합니다.</summary>
    public float Discharge(float joules, float dt)
    {
        if (!IsOnline || joules <= 0f) return 0f;
        float rateLimit = maxDischargeRateWatts * dt;
        float available = Mathf.Min(currentCharge, rateLimit);
        float supplied = Mathf.Min(joules, available);
        if (supplied < 0f) supplied = 0f;
        currentCharge -= supplied;
        return supplied;
    }

    // ── IBuildingExtraSerializable (충전량 저장) ───────────────────
    [System.Serializable]
    private class ExtraData { public float currentCharge; }

    public string SerializeExtra() => JsonUtility.ToJson(new ExtraData { currentCharge = currentCharge });

    public void DeserializeExtra(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var d = JsonUtility.FromJson<ExtraData>(json);
            if (d != null) currentCharge = Mathf.Clamp(d.currentCharge, 0f, capacityJoules);
        }
        catch { }
    }
}
