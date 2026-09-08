using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전력 소비 건물에 부착하는 컴포넌트.
/// BuildingData.powerConsumption(W)만큼 전력을 소비하며, PowerManager가 매 틱 IsPowered를 설정합니다.
/// 정전(IsPowered=false) 시 ProductionBuilding/CraftingTable의 작업 진행이 일시정지됩니다.
///
/// 전력 시스템(PowerManager)이 씬에 없으면 등록되지 않고 IsPowered=true를 유지하여
/// 기존 건물 동작과 호환됩니다.
/// </summary>
[RequireComponent(typeof(Building))]
public class PowerConsumer : MonoBehaviour, IPowerNode
{
    [Tooltip("초당 소비 전력(W). 0이면 BuildingData.powerConsumption을 사용합니다.")]
    [SerializeField] private int consumptionOverride = 0;

    private Building _building;

    /// <summary>소비 전력이 변하는 컴포넌트 (냉난방기 등). 없으면 null.</summary>
    private IVariablePowerDraw _variableDraw;

    /// <summary>현재 전력을 공급받고 있는지 (PowerManager가 설정).</summary>
    public bool IsPowered { get; private set; } = true;

    /// <summary>기반이 정상인지 (파괴되면 false).</summary>
    public bool IsOnline => _building == null || _building.IsFunctional;

    /// <summary>
    /// 초당 소비 전력(W).
    /// <see cref="IVariablePowerDraw"/> 컴포넌트가 있으면 그쪽이 최우선입니다 —
    /// 냉난방기처럼 부하에 따라 소비가 달라지는 건물이 여기에 해당합니다.
    /// </summary>
    public int Consumption
    {
        get
        {
            if (_variableDraw != null) return Mathf.Max(0, _variableDraw.GetPowerDraw());
            if (consumptionOverride > 0) return consumptionOverride;
            return (_building != null && _building.buildingData != null)
                ? _building.buildingData.powerConsumption
                : 0;
        }
    }

    // ── IPowerNode ──────────────────────────────────────────────
    public IEnumerable<Vector2Int> OccupiedCells => PowerUtil.FootprintCells(transform, _building);
    public PowerNodeKind Kind => PowerNodeKind.Consumer;

    void Awake()
    {
        _building = GetComponent<Building>();
        _variableDraw = GetComponent<IVariablePowerDraw>();
    }

    void OnEnable()
    {
        var pm = PowerManager.instance;
        if (pm != null) pm.RegisterConsumer(this);
    }

    void OnDisable()
    {
        var pm = PowerManager.instance;
        if (pm != null) pm.UnregisterConsumer(this);
    }

    /// <summary>PowerManager가 매 틱 전력 공급 여부를 설정합니다.</summary>
    public void SetPowered(bool powered)
    {
        IsPowered = powered;
    }
}
