using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 발전기. 전력을 생산하여 전력망에 공급합니다.
///
/// 두 가지 종류를 데이터로 지원합니다:
/// • 무한 가동형 (requiresFuel=false): 항상 outputWatts 생산 (풍력·지열 등).
/// • 연료 소비형 (requiresFuel=true): 내부 연료 버퍼가 남아있을 때만 생산하며,
///   가동 중 매 틱 연료를 소비합니다 (화력·바이오 등).
///
/// 연료 잔량은 IBuildingExtraSerializable로 세이브에 보존됩니다.
/// </summary>
[RequireComponent(typeof(Building))]
public class PowerProducer : MonoBehaviour, IPowerNode, IBuildingExtraSerializable
{
    [Header("발전")]
    [Tooltip("정상 가동 시 생산 전력(W).")]
    [SerializeField] private int outputWatts = 1000;

    [Header("연료 (선택)")]
    [Tooltip("연료가 필요한 발전기인지 여부. false면 무한 가동.")]
    [SerializeField] private bool requiresFuel = false;
    [Tooltip("소비할 연료 아이템 (연료 보급 연동용).")]
    [SerializeField] private ItemData fuelItem;
    [Tooltip("가동 중 초당 연료 소비량.")]
    [SerializeField] private float fuelUnitsPerSecond = 0.1f;
    [Tooltip("현재 내부 연료 잔량.")]
    [SerializeField] private float fuelBuffer = 0f;
    [Tooltip("내부 연료 버퍼 최대치.")]
    [SerializeField] private float maxFuelBuffer = 100f;

    private Building _building;

    public bool IsOnline => _building == null || _building.IsFunctional;
    public bool RequiresFuel => requiresFuel;
    public ItemData FuelItem => fuelItem;
    public float FuelBuffer => fuelBuffer;
    public float MaxFuelBuffer => maxFuelBuffer;

    /// <summary>현재 실제 출력(W). 기반 파괴/연료 고갈 시 0.</summary>
    public int CurrentOutput
    {
        get
        {
            if (!IsOnline) return 0;
            if (requiresFuel && fuelBuffer <= 0f) return 0;
            return outputWatts;
        }
    }

    // ── IPowerNode ──────────────────────────────────────────────
    public IEnumerable<Vector2Int> OccupiedCells => PowerUtil.FootprintCells(transform, _building);
    public PowerNodeKind Kind => PowerNodeKind.Producer;

    void Awake()
    {
        _building = GetComponent<Building>();
    }

    void OnEnable()
    {
        var pm = PowerManager.instance;
        if (pm != null) pm.RegisterProducer(this);
    }

    void OnDisable()
    {
        var pm = PowerManager.instance;
        if (pm != null) pm.UnregisterProducer(this);
    }

    /// <summary>가동 중인 발전기의 연료를 seconds초만큼 소비합니다 (PowerManager가 호출).</summary>
    public void ConsumeFuel(float seconds)
    {
        if (!requiresFuel || fuelBuffer <= 0f) return;
        fuelBuffer = Mathf.Max(0f, fuelBuffer - fuelUnitsPerSecond * seconds);
    }

    /// <summary>연료를 보급합니다.</summary>
    public void AddFuel(float units)
    {
        fuelBuffer = Mathf.Clamp(fuelBuffer + units, 0f, maxFuelBuffer);
    }

    // ── IBuildingExtraSerializable (연료 잔량 저장) ────────────────
    [System.Serializable]
    private class ExtraData { public float fuelBuffer; }

    public string SerializeExtra()
    {
        if (!requiresFuel) return "";
        return JsonUtility.ToJson(new ExtraData { fuelBuffer = fuelBuffer });
    }

    public void DeserializeExtra(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var d = JsonUtility.FromJson<ExtraData>(json);
            if (d != null) fuelBuffer = Mathf.Clamp(d.fuelBuffer, 0f, maxFuelBuffer);
        }
        catch { }
    }
}
