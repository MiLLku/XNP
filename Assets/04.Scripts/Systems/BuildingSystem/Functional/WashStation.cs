using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 세척 시설 컴포넌트. 간이 세척대·세척실·정화 세척실 프리팹에 부착합니다.
///
/// 동작:
///   - Awake에서 Unity Tag를 FacilityTag.WashStation으로 설정 → 기존 AI 시설 탐색에 자동 편입
///   - 직원이 슬롯을 예약하고 그 자리로 이동 → 도착하면 EmployeeAI가 초당 침식을 깎음
///   - 씻긴 침식량만큼 침식 결정체를 산출해 전역 인벤토리에 넣음
///   - PowerConsumer가 붙어 있으면 정전 시 사용 불가 (IsOperating=false)
///
/// 티어 차등은 프리팹별 인스펙터 값으로 표현합니다 (오락 시설의 DartBoard/ArcadeMachine과 같은 방식).
///   4x3 = capacity 1 / 6x3 = capacity 2 / 8x3 = capacity 4
///
/// 슬롯 점유는 반드시 반납되어야 합니다. AI가 명시적으로 ReleaseSlot을 부르지만,
/// 어떤 경로로든 새는 경우에 대비해 Update의 자가 청소 스윕이 최종 안전망 역할을 합니다.
/// </summary>
public class WashStation : MonoBehaviour, IBuildingFunction
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

    [Tooltip("씻긴 침식 1당 산출되는 결정체 개수")]
    [SerializeField] private float crystalPerErosion = 1f;

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

    private float sweepTimer;

    #endregion

    #region 프로퍼티

    public int Capacity => capacity;
    public int Priority => priority;
    public float ErosionClearPerSecond => erosionClearPerSecond;

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

    public bool IsOperating => buildingEnabled && (powerConsumer == null || powerConsumer.IsPowered);

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

    private void OnEnable() => EmployeeAI.InvalidateTagCache(FacilityTag.WashStation);

    private void OnDisable() => ReleaseAllSlots();

    private void OnDestroy() => EmployeeAI.InvalidateTagCache(FacilityTag.WashStation);

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

    #region 부산물

    /// <summary>
    /// 씻긴 침식량을 보고받아 침식 결정체를 산출합니다.
    /// 매 프레임 소수점 단위로 들어오므로 정수가 될 때까지 모았다가 넣습니다.
    /// </summary>
    public void ReportWashed(float erosionRemoved)
    {
        if (erosionRemoved <= 0f || crystalItem == null) return;
        if (InventoryManager.instance == null) return;

        crystalCarry += erosionRemoved * crystalPerErosion;
        if (crystalCarry < 1f) return;

        int amount = Mathf.FloorToInt(crystalCarry);
        crystalCarry -= amount;
        InventoryManager.instance.AddItem(crystalItem, amount);
    }

    #endregion
}
