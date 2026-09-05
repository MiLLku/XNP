using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 세척 시설 컴포넌트. 간이 세척대·세척실·정화 세척실 프리팹에 부착합니다.
///
/// 동작:
///   - Awake에서 Unity Tag를 FacilityTag.WashStation으로 설정 → 기존 AI 시설 탐색에 자동 편입
///   - 직원이 슬롯을 예약하고 그 자리로 이동 → 도착하면 EmployeeAI가 초당 침식을 깎음
///   - 씻긴 침식량만큼 침식 결정체를 <b>건물 안에 보관</b> (전역 인벤토리로 바로 가지 않음)
///   - 보관분이 수용 상한에 차면 세척 불가(IsOperating=false) → 직원이 운반해 비워야 재가동
///   - PowerConsumer가 붙어 있으면 정전 시 사용 불가 (IsOperating=false)
///
/// 티어 차등은 프리팹별 인스펙터 값으로 표현합니다 (오락 시설의 DartBoard/ArcadeMachine과 같은 방식).
///   4x3 = capacity 1 / 6x3 = capacity 2 / 8x3 = capacity 4
///
/// 슬롯 점유는 반드시 반납되어야 합니다. AI가 명시적으로 ReleaseSlot을 부르지만,
/// 어떤 경로로든 새는 경우에 대비해 Update의 자가 청소 스윕이 최종 안전망 역할을 합니다.
/// </summary>
public class WashStation : MonoBehaviour, IBuildingFunction, IBuildingOutput, IBuildingExtraSerializable
{
    #region 인스펙터

    [Header("세척 성능")]
    [Tooltip("동시에 세척할 수 있는 인원")]
    [SerializeField, Min(1)] private int capacity = 1;

    [Tooltip("초당 제거되는 침식량")]
    [SerializeField] private float erosionClearPerSecond = 6f;

    [Tooltip("높을수록 직원이 선호합니다 (오락 시설과 같은 규약)")]
    [SerializeField] private int priority = 0;

    [Header("부산물")]
    [Tooltip("세척으로 산출되는 아이템 (침식 결정체)")]
    [SerializeField] private ItemData crystalItem;

    [Tooltip("씻긴 침식 1당 산출되는 결정체 개수." + "\n" +
             "음수면 WashConfig(ErosionManager)의 전역 값을 씁니다 — 티어별로 다르게 할 때만 0 이상을 넣으세요.")]
    [SerializeField] private float crystalPerErosionOverride = -1f;

    [Tooltip("건물이 보관할 수 있는 결정체 최대 개수. 가득 차면 세척이 멈춥니다." + "\n" +
             "권장: 4x3=50 / 6x3=100 / 8x3=200")]
    [SerializeField, Min(1)] private int crystalCapacity = 50;

    [Tooltip("직원이 결정체를 받아갈 위치 오프셋 (건물 좌측 하단 기준)")]
    [SerializeField] private Vector2 pickupOffset = new Vector2(0.5f, 0f);

    [Header("슬롯 위치 (선택)")]
    [Tooltip("비워두면 건물 폭을 capacity로 나눠 자동 계산합니다")]
    [SerializeField] private List<Transform> slotAnchors;

    #endregion

    #region 상수

    /// <summary>예약만 하고 이 시간 안에 도착하지 않으면 슬롯을 회수합니다 (초).</summary>
    private const float RESERVE_TIMEOUT = 90f;

    /// <summary>도착한 직원이 이 거리 밖으로 나가면 반납으로 간주합니다 (타일).</summary>
    private const float ABANDON_DISTANCE = 3f;

    /// <summary>자가 청소 스윕 주기 (초).</summary>
    private const float SWEEP_INTERVAL = 1f;

    #endregion

    #region 필드

    private Employee[] occupants;
    private float[] reservedAt;
    private bool[] arrived;

    private bool buildingEnabled = true;
    private PowerConsumer powerConsumer;
    private SpriteRenderer spriteRenderer;
    private Building building;

    /// <summary>정수 단위가 찰 때까지 결정체 산출분을 모아두는 소수 누적기.</summary>
    private float crystalCarry;

    /// <summary>건물 안에 보관 중인 결정체 수. 직원이 창고로 운반해야 비워집니다.</summary>
    private int storedCrystals;

    private float sweepTimer;

    /// <summary>산출물 레지스트리 등록 여부 (중복 해제 방지)</summary>
    private bool outputRegistered;

    #endregion

    #region 프로퍼티

    public int Capacity => capacity;
    public int Priority => priority;
    public float ErosionClearPerSecond => erosionClearPerSecond;

    /// <summary>보관 중인 결정체 수.</summary>
    public int StoredCrystals => storedCrystals;

    /// <summary>결정체 수용 상한.</summary>
    public int CrystalCapacity => crystalCapacity;

    /// <summary>보관함이 가득 찼는지. 가득 차면 세척을 받지 않습니다.</summary>
    public bool IsCrystalFull => storedCrystals >= crystalCapacity;

    /// <summary>산출되는 결정체 아이템 (운반·출고에서 참조).</summary>
    public ItemData CrystalItem => crystalItem;

    /// <summary>
    /// 실제 적용되는 산출 비율. 프리팹 오버라이드가 0 이상이면 그것을, 아니면 전역 Config 값을 씁니다.
    /// </summary>
    public float EffectiveCrystalPerErosion
    {
        get
        {
            if (crystalPerErosionOverride >= 0f) return crystalPerErosionOverride;
            return ErosionManager.instance != null
                ? ErosionManager.instance.CrystalPerErosion
                : WashConfig.DEFAULT_CRYSTAL_PER_EROSION;
        }
    }

    /// <summary>현재 슬롯을 잡고 있는 직원 수.</summary>
    public int OccupiedCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < occupants.Length; i++)
                if (occupants[i] != null) count++;
            return count;
        }
    }

    public bool HasFreeSlot
    {
        get
        {
            for (int i = 0; i < occupants.Length; i++)
                if (occupants[i] == null) return true;
            return false;
        }
    }

    #endregion

    #region IBuildingFunction

    /// <summary>
    /// 세척을 받을 수 있는 상태인지.
    /// 보관함이 가득 차면 더 씻어도 산출물을 둘 곳이 없으므로 정전과 동일하게 사용 불가로 만듭니다.
    /// (AI의 시설 선택·세척 루프가 모두 이 값을 보므로 자연스럽게 다른 시설로 흩어집니다)
    /// </summary>
    public bool IsOperating =>
        buildingEnabled && (powerConsumer == null || powerConsumer.IsPowered) && !IsCrystalFull;

    public void OnBuildingDisabled()
    {
        buildingEnabled = false;
        ReleaseAllSlots();
    }

    public void OnBuildingEnabled() => buildingEnabled = true;

    #endregion

    #region 생명주기

    private void Awake()
    {
        capacity = Mathf.Max(1, capacity);
        occupants  = new Employee[capacity];
        reservedAt = new float[capacity];
        arrived    = new bool[capacity];

        powerConsumer  = GetComponent<PowerConsumer>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        building       = GetComponent<Building>();

        if (!gameObject.CompareTag(FacilityTag.WashStation))
            gameObject.tag = FacilityTag.WashStation;
    }

    private void Start()
    {
        BuildingOutputRegistry.instance?.Register(this);
        outputRegistered = true;
    }

    private void OnEnable() => EmployeeAI.InvalidateTagCache(FacilityTag.WashStation);

    private void OnDisable() => ReleaseAllSlots();

    private void OnDestroy()
    {
        EmployeeAI.InvalidateTagCache(FacilityTag.WashStation);
        if (outputRegistered) BuildingOutputRegistry.instance?.Unregister(this);
    }

    /// <summary>
    /// 슬롯 누수 최종 안전망. AI가 반납하지 못한 경우(직원 파괴·사망·이탈·예약 후 미도착)를
    /// 주기적으로 훑어 회수합니다. 이게 없으면 시설이 영구히 만석으로 굳습니다.
    /// </summary>
    private void Update()
    {
        sweepTimer -= Time.deltaTime;
        if (sweepTimer > 0f) return;
        sweepTimer = SWEEP_INTERVAL;

        float now = Time.time;
        for (int i = 0; i < occupants.Length; i++)
        {
            Employee emp = occupants[i];
            if (emp == null) { ClearSlot(i); continue; }          // 파괴된 직원 (Unity null 비교)

            if (emp.State == EmployeeState.Dead) { ClearSlot(i); continue; }

            if (!arrived[i])
            {
                if (now - reservedAt[i] > RESERVE_TIMEOUT) ClearSlot(i);
                continue;
            }

            float dist = Vector2.Distance(emp.transform.position, GetSlotPosition(i));
            if (dist > ABANDON_DISTANCE) ClearSlot(i);
        }
    }

    #endregion

    #region 슬롯

    /// <summary>
    /// 지금 이 직원이 쓸 수 있는 시설인지. 이미 슬롯을 잡고 있으면 만석이어도 true입니다.
    /// </summary>
    public bool CanUse(Employee employee)
    {
        if (!IsOperating) return false;
        return HasFreeSlot || IndexOf(employee) >= 0;
    }

    /// <summary>
    /// 슬롯을 예약합니다. 성공하면 슬롯 인덱스, 실패(만석·정전)하면 -1.
    /// 이미 잡고 있으면 그 인덱스를 그대로 돌려줍니다 (멱등).
    /// </summary>
    public int TryReserveSlot(Employee employee)
    {
        if (employee == null || !IsOperating) return -1;

        int existing = IndexOf(employee);
        if (existing >= 0) return existing;

        for (int i = 0; i < occupants.Length; i++)
        {
            if (occupants[i] != null) continue;

            occupants[i]  = employee;
            reservedAt[i] = Time.time;
            arrived[i]    = false;
            return i;
        }

        return -1;
    }

    /// <summary>슬롯 위치에 도착했음을 표시합니다 (예약 타임아웃 해제).</summary>
    public void MarkArrived(Employee employee)
    {
        int idx = IndexOf(employee);
        if (idx >= 0) arrived[idx] = true;
    }

    /// <summary>슬롯을 반납합니다. 잡고 있지 않아도 안전합니다 (멱등).</summary>
    public void ReleaseSlot(Employee employee)
    {
        int idx = IndexOf(employee);
        if (idx >= 0) ClearSlot(idx);
    }

    /// <summary>i번째 슬롯의 월드 좌표. 앵커가 지정돼 있으면 그것을, 없으면 폭을 나눠 계산합니다.</summary>
    public Vector3 GetSlotPosition(int slotIndex)
    {
        if (slotAnchors != null && slotIndex < slotAnchors.Count && slotAnchors[slotIndex] != null)
            return slotAnchors[slotIndex].position;

        // 건물 원점은 좌하단이고 회전을 지원하지 않으므로 단순 분할로 충분하다.
        // 폭은 BuildingData가 정답이지만 런타임 주입이라, 아직 없으면 스프라이트로 폴백한다.
        float width = capacity;
        if (building != null && building.buildingData != null)
            width = building.buildingData.size.x;
        else if (spriteRenderer != null && spriteRenderer.size.x > 0f)
            width = spriteRenderer.size.x;

        float step = width / capacity;
        return transform.position + new Vector3((slotIndex + 0.5f) * step, 0.5f, 0f);
    }

    private int IndexOf(Employee employee)
    {
        if (employee == null) return -1;
        for (int i = 0; i < occupants.Length; i++)
            if (occupants[i] == employee) return i;
        return -1;
    }

    private void ClearSlot(int i)
    {
        occupants[i]  = null;
        arrived[i]    = false;
        reservedAt[i] = 0f;
    }

    private void ReleaseAllSlots()
    {
        if (occupants == null) return;
        for (int i = 0; i < occupants.Length; i++) ClearSlot(i);
    }

    #endregion

    #region 부산물 (건물 내 보관)

    /// <summary>
    /// 씻긴 침식량을 보고받아 침식 결정체를 <b>건물 안에</b> 쌓습니다.
    /// 매 프레임 소수점 단위로 들어오므로 정수가 될 때까지 모았다가 적립합니다.
    ///
    /// 전역 인벤토리로 바로 넣지 않습니다 — 직원이 창고로 운반해야 합니다.
    /// 상한을 넘기는 몫은 버리지만, 상한에 닿는 순간 IsOperating이 false가 되어
    /// 세척 자체가 멈추므로 실제로 버려지는 양은 한 프레임분뿐입니다.
    /// </summary>
    public void ReportWashed(float erosionRemoved)
    {
        if (erosionRemoved <= 0f || crystalItem == null) return;

        crystalCarry += erosionRemoved * EffectiveCrystalPerErosion;
        if (crystalCarry < 1f) return;

        int amount = Mathf.FloorToInt(crystalCarry);
        crystalCarry -= amount;

        int room = crystalCapacity - storedCrystals;
        if (room <= 0) return;

        storedCrystals += Mathf.Min(amount, room);

        // 운반 작업이 아직 없으면 만들어 달라고 알린다
        BuildingOutputRegistry.instance?.NotifyOutputChanged(this);
    }

    /// <summary>
    /// 보관 중인 결정체를 꺼냅니다 (직원 운반·제작 출고 공용).
    /// 요청량보다 적게 남았으면 남은 만큼만 주고, 실제로 꺼낸 수량을 반환합니다.
    /// </summary>
    public int TakeCrystals(int amount)
    {
        if (amount <= 0 || storedCrystals <= 0) return 0;

        int taken = Mathf.Min(amount, storedCrystals);
        storedCrystals -= taken;

        // 가득 차서 멈춰 있었다면 이제 다시 쓸 수 있다 → 시설 탐색 캐시를 즉시 무효화
        EmployeeAI.InvalidateTagCache(FacilityTag.WashStation);
        return taken;
    }

    #endregion

    #region IBuildingOutput

    public bool HasPendingOutput => storedCrystals > 0 && crystalItem != null;

    public bool IsOutputAccessible => building == null || building.IsFunctional;

    public Vector3 GetPickupPosition() =>
        transform.position + new Vector3(pickupOffset.x, pickupOffset.y, 0f);

    public void GetPendingOutputs(List<ResourceCost> into)
    {
        if (into == null) return;
        into.Clear();

        if (crystalItem == null || storedCrystals <= 0) return;
        into.Add(new ResourceCost { item = crystalItem, amount = storedCrystals });
    }

    public int TakeOutput(ItemData item, int amount)
    {
        if (item != crystalItem) return 0;
        return TakeCrystals(amount);
    }

    #endregion

    #region 저장 (IBuildingExtraSerializable)

    [System.Serializable]
    private class WashStationExtra
    {
        public int storedCrystals;
        public float crystalCarry;
    }

    public string SerializeExtra()
    {
        if (storedCrystals <= 0 && crystalCarry <= 0f) return string.Empty;

        return JsonUtility.ToJson(new WashStationExtra
        {
            storedCrystals = storedCrystals,
            crystalCarry   = crystalCarry
        });
    }

    public void DeserializeExtra(string json)
    {
        if (string.IsNullOrEmpty(json)) return;

        var extra = JsonUtility.FromJson<WashStationExtra>(json);
        if (extra == null) return;

        storedCrystals = Mathf.Clamp(extra.storedCrystals, 0, crystalCapacity);
        crystalCarry   = Mathf.Max(0f, extra.crystalCarry);
    }

    #endregion
}
