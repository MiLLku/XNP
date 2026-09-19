using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 요양 시설 (임시 건물). 체력이 낮은 직원이 들어와 가만히 서 있는 동안 체력이 회복됩니다.
///
/// 동작:
///   - 직원이 슬롯을 예약하고 그 자리로 이동 → 도착하면 EmployeeAI가 초당 체력을 채움
///   - 언제 요양하러 오는지는 작업 우선순위의 '요양'(WorkType.Recuperation)이 결정합니다
///   - PowerConsumer가 붙어 있으면 정전 시 사용 불가
///
/// 세척 시설과 달리 Unity Tag가 아니라 정적 목록(All)으로 찾습니다 — 새 태그를 TagManager에 등록할 필요가 없습니다.
/// </summary>
public class RecuperationBed : MonoBehaviour, IBuildingFunction
{
    #region 인스펙터

    [Header("요양 성능")]
    [Tooltip("동시에 요양할 수 있는 인원")]
    [SerializeField, Min(1)] private int capacity = 1;

    [Tooltip("초당 회복되는 체력")]
    [SerializeField] private float healPerSecond = 2f;

    [Tooltip("높을수록 직원이 선호합니다 (세척·오락 시설과 같은 규약)")]
    [SerializeField] private int priority = 0;

    #endregion

    /// <summary>자가 청소 스윕 주기 (초).</summary>
    private const float SWEEP_INTERVAL = 1f;

    /// <summary>맵에 존재하는 요양 시설 전체.</summary>
    public static readonly List<RecuperationBed> All = new List<RecuperationBed>();

    private FacilitySlots slots;
    private bool buildingEnabled = true;
    private PowerConsumer powerConsumer;
    private Building building;
    private float sweepTimer;

    public int Priority => priority;
    public float HealPerSecond => healPerSecond;

    #region IBuildingFunction

    public bool IsOperating => buildingEnabled && (powerConsumer == null || powerConsumer.IsPowered);

    public void OnBuildingDisabled()
    {
        buildingEnabled = false;
        slots?.ReleaseAll();
    }

    public void OnBuildingEnabled() => buildingEnabled = true;

    #endregion

    #region 생명주기

    private void Awake()
    {
        capacity      = Mathf.Max(1, capacity);
        slots         = new FacilitySlots(capacity);
        powerConsumer = GetComponent<PowerConsumer>();
        building      = GetComponent<Building>();
    }

    private void OnEnable() => All.Add(this);

    private void OnDisable()
    {
        All.Remove(this);
        slots?.ReleaseAll();
    }

    /// <summary>슬롯 누수 최종 안전망 (FacilitySlots.Sweep 참고).</summary>
    private void Update()
    {
        sweepTimer -= Time.deltaTime;
        if (sweepTimer > 0f) return;
        sweepTimer = SWEEP_INTERVAL;

        slots.Sweep(GetSlotPosition);
    }

    #endregion

    #region 슬롯

    /// <summary>지금 이 직원이 쓸 수 있는지. 이미 슬롯을 잡고 있으면 만석이어도 true입니다.</summary>
    public bool CanUse(Employee employee) =>
        IsOperating && (slots.HasFreeSlot || slots.IndexOf(employee) >= 0);

    /// <summary>슬롯을 예약합니다. 성공하면 슬롯 인덱스, 실패(만석·정전)하면 -1.</summary>
    public int TryReserveSlot(Employee employee) => IsOperating ? slots.TryReserve(employee) : -1;

    public void MarkArrived(Employee employee) => slots.MarkArrived(employee);

    public void ReleaseSlot(Employee employee) => slots.Release(employee);

    /// <summary>i번째 슬롯의 월드 좌표. 건물 폭을 인원수로 나눠 계산합니다 (원점 = 좌하단).</summary>
    public Vector3 GetSlotPosition(int slotIndex)
    {
        float width = building != null && building.buildingData != null
            ? building.buildingData.size.x
            : capacity;

        float step = width / capacity;
        return transform.position + new Vector3((slotIndex + 0.5f) * step, 0.5f, 0f);
    }

    #endregion
}
