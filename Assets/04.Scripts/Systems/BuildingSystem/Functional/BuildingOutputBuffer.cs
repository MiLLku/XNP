using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제작 건물 공용 산출물 보관함.
///
/// 완성품을 전역 인벤토리로 바로 보내지 않고 이 버퍼에 담아두면,
/// <see cref="BuildingOutputRegistry"/>가 운반 작업을 만들어 직원이 창고로 옮깁니다.
///
/// 사용법: 생산 건물 프리팹에 이 컴포넌트를 붙이고, 완성 시
/// <see cref="TryStore"/>를 호출하세요. 컴포넌트가 없으면 호출부가
/// 기존대로 인벤토리에 직접 넣도록 폴백합니다(구 프리팹 호환).
///
/// 저장: 이 컴포넌트가 IBuildingExtraSerializable을 구현합니다.
/// <b>같은 건물에 다른 IBuildingExtraSerializable이 이미 있으면 붙이지 마세요</b> —
/// Building은 건물당 하나만 사용합니다. 그런 건물(CraftingTable 등)은
/// 자기 SerializeExtra 안에서 이 버퍼 상태를 함께 저장하면 됩니다.
/// </summary>
[RequireComponent(typeof(Building))]
public class BuildingOutputBuffer : MonoBehaviour, IBuildingOutput, IMaterialSource, IBuildingExtraSerializable
{
    #region 인스펙터

    [Tooltip("보관할 수 있는 총 개수(아이템 종류 합산). 가득 차면 TryStore가 남는 만큼만 받습니다.")]
    [SerializeField, Min(1)] private int capacity = 100;

    [Tooltip("직원이 산출물을 받아갈 위치 오프셋 (건물 좌측 하단 기준)")]
    [SerializeField] private Vector2 pickupOffset = new Vector2(0.5f, 0f);

    [Tooltip("여기 쌓인 산출물을 자동 물류에 태울지 여부." + "\n" +
             "끄면 직원이 창고로 옮기지도, 제작에 꺼내 쓰지도 않습니다.")]
    [SerializeField] private bool autoHaulEnabled = true;

    #endregion

    #region 상태

    private readonly Dictionary<ItemData, int> _stored = new();
    private Building _building;
    private bool _registered;

    #endregion

    #region 프로퍼티

    public int Capacity => capacity;

    /// <summary>보관 중인 총 개수 (종류 무관 합산).</summary>
    public int TotalStored
    {
        get
        {
            int sum = 0;
            foreach (var kv in _stored) sum += kv.Value;
            return sum;
        }
    }

    public bool IsFull => TotalStored >= capacity;

    #endregion

    #region 생명주기

    void Awake() => _building = GetComponent<Building>();

    void Start()
    {
        BuildingOutputRegistry.instance?.Register(this);
        MaterialSourceRegistry.instance?.Register(this);
        _registered = true;
    }

    void OnDestroy()
    {
        if (!_registered) return;
        BuildingOutputRegistry.instance?.Unregister(this);
        MaterialSourceRegistry.instance?.Unregister(this);
    }

    #endregion

    #region 입고

    /// <summary>
    /// 산출물을 보관함에 넣습니다. 남은 자리가 부족하면 들어간 만큼만 받고
    /// <b>실제로 보관된 수량</b>을 반환합니다.
    /// </summary>
    public int TryStore(ItemData item, int amount)
    {
        if (item == null || amount <= 0) return 0;

        int room = capacity - TotalStored;
        if (room <= 0) return 0;

        int stored = Mathf.Min(amount, room);
        _stored.TryGetValue(item, out int current);
        _stored[item] = current + stored;

        BuildingOutputRegistry.instance?.NotifyOutputChanged(this);
        return stored;
    }

    #endregion

    #region IBuildingOutput

    public bool AutoHaulEnabled
    {
        get => autoHaulEnabled;
        set => autoHaulEnabled = value;
    }

    public bool HasPendingOutput => _stored.Count > 0;

    public bool IsOutputAccessible => _building == null || _building.IsFunctional;

    public Vector3 GetPickupPosition() =>
        transform.position + new Vector3(pickupOffset.x, pickupOffset.y, 0f);

    public void GetPendingOutputs(List<ResourceCost> into)
    {
        if (into == null) return;
        into.Clear();

        foreach (var kv in _stored)
        {
            if (kv.Key == null || kv.Value <= 0) continue;
            into.Add(new ResourceCost { item = kv.Key, amount = kv.Value });
        }
    }

    public int TakeOutput(ItemData item, int amount)
    {
        if (item == null || amount <= 0) return 0;
        if (!_stored.TryGetValue(item, out int have) || have <= 0) return 0;

        int taken = Mathf.Min(amount, have);
        int left  = have - taken;

        if (left > 0) _stored[item] = left;
        else          _stored.Remove(item);

        return taken;
    }

    #endregion

    #region IMaterialSource

    public bool IsSourceAvailable => autoHaulEnabled && IsOutputAccessible;

    public Vector3 GetWithdrawPosition() => GetPickupPosition();

    public int GetStoredAmount(ItemData item)
        => item != null && _stored.TryGetValue(item, out int n) ? n : 0;

    /// <summary>요청량을 전부 댈 수 있을 때만 꺼냅니다 (반쪽 출고 금지).</summary>
    public bool Withdraw(ItemData item, int amount)
    {
        if (item == null || amount <= 0) return false;
        if (GetStoredAmount(item) < amount) return false;
        return TakeOutput(item, amount) == amount;
    }

    #endregion

    #region 저장

    [System.Serializable]
    private class Entry { public int itemId; public int amount; }

    [System.Serializable]
    private class BufferState { public List<Entry> entries = new List<Entry>(); }

    public string SerializeExtra()
    {
        if (_stored.Count == 0) return string.Empty;

        var state = new BufferState();
        foreach (var kv in _stored)
        {
            if (kv.Key == null || kv.Value <= 0) continue;
            state.entries.Add(new Entry { itemId = kv.Key.itemID, amount = kv.Value });
        }

        return state.entries.Count > 0 ? JsonUtility.ToJson(state) : string.Empty;
    }

    public void DeserializeExtra(string json)
    {
        _stored.Clear();
        if (string.IsNullOrEmpty(json)) return;

        var state = JsonUtility.FromJson<BufferState>(json);
        if (state?.entries == null) return;

        foreach (var e in state.entries)
        {
            if (e == null || e.amount <= 0) continue;

            var item = GameDatabase.Instance?.GetItemData(e.itemId);
            if (item == null) continue;   // 삭제된 아이템 정의는 조용히 버린다

            _stored.TryGetValue(item, out int current);
            _stored[item] = current + e.amount;
        }
    }

    #endregion
}
