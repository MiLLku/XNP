using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 직원별 구역 배정 컴포넌트.
/// 플레이어가 직원 관리창에서 구역 하나를 배정합니다.
///
/// 동작 규칙:
///   - 배정된 구역이 있으면 <b>모든 활동</b>(작업·취침·오락·세척)을 그 구역 안에서 해결
///   - 구역 안에 필요한 시설이 없으면 전체 탐색으로 폴백 (직원이 멈추지 않도록)
///   - 미배정(-1)은 '일반' 상태 — 맵 전체를 자유롭게 씀 (기본값, 해제 불가능한 기본 선택지)
///
/// 여러 직원에게 같은 구역을 배정해 함께 묶을 수 있습니다.
/// </summary>
public class EmployeeZoneAssignment : MonoBehaviour
{
    #region 상수

    /// <summary>미배정 = '일반'(맵 전체)을 뜻하는 구역 ID.</summary>
    public const int GENERAL_ZONE_ID = -1;

    /// <summary>'일반' 선택지의 표시 이름.</summary>
    public const string GENERAL_ZONE_NAME = "일반 (맵 전체)";

    #endregion

    #region 필드

    /// <summary>배정된 구역 ID (-1 = 일반/맵 전체)</summary>
    [SerializeField] private int assignedZoneId = GENERAL_ZONE_ID;

    private Employee employee;

    #endregion

    #region 이벤트

    /// <summary>구역 배정 변경 시 발생 (UI 갱신용)</summary>
    public event Action OnZoneAssignmentChanged;

    #endregion

    #region 초기화

    void Awake()
    {
        employee = GetComponent<Employee>();
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 배정된 구역 ID. -1이면 '일반'(맵 전체).
    /// 구역이 삭제됐으면 자동으로 '일반'으로 되돌아갑니다.
    /// </summary>
    public int AssignedZoneId
    {
        get
        {
            if (assignedZoneId >= 0 && ZoneManager.instance != null &&
                ZoneManager.instance.GetZone(assignedZoneId) == null)
            {
                assignedZoneId = GENERAL_ZONE_ID; // 삭제된 구역 → 일반으로 자동 복귀
            }
            return assignedZoneId;
        }
    }

    /// <summary>구역이 배정되어 있는지 (false = 일반/맵 전체).</summary>
    public bool HasZoneAssigned => AssignedZoneId >= 0;

    /// <summary>배정된 구역 객체 (일반이거나 삭제됐으면 null).</summary>
    public Zone AssignedZone
    {
        get
        {
            int id = AssignedZoneId;
            if (id < 0 || ZoneManager.instance == null) return null;
            return ZoneManager.instance.GetZone(id);
        }
    }

    /// <summary>구역을 배정합니다. -1을 넘기면 '일반'(맵 전체)으로 되돌립니다.</summary>
    public void AssignZone(int zoneId)
    {
        int newId = zoneId < 0 ? GENERAL_ZONE_ID : zoneId;
        if (assignedZoneId == newId) return;

        assignedZoneId = newId;
        OnZoneAssignmentChanged?.Invoke();
    }

    /// <summary>배정을 해제해 '일반'(맵 전체)으로 되돌립니다.</summary>
    public void ClearZone() => AssignZone(GENERAL_ZONE_ID);

    /// <summary>
    /// 배정 구역에 맞는 길찾기 옵션. 일반이면 제한 없음(Default).
    /// </summary>
    public PathOptions GetPathOptions()
    {
        int id = AssignedZoneId;
        return id >= 0 ? PathOptions.ForZone(id) : PathOptions.Default;
    }

    /// <summary>
    /// 활동 수행을 위한 시설을 찾습니다.
    /// 구역이 배정됐으면 구역 안에서 먼저 찾고, 없으면 전체에서 가장 가까운 것을 씁니다.
    /// </summary>
    /// <param name="facilityTag">시설의 Unity 태그</param>
    /// <param name="myPosition">직원 현재 위치</param>
    /// <returns>가장 가까운 시설 오브젝트 (없으면 null)</returns>
    public GameObject FindNearestFacility(string facilityTag, Vector3 myPosition)
    {
        var allFacilities = FacilityTag.FindAll(facilityTag)
            .Where(f => f != null)
            .ToArray();

        if (allFacilities.Length == 0) return null;

        Zone zone = AssignedZone;
        if (zone != null)
        {
            var inZone = allFacilities.Where(f =>
            {
                var tile = new Vector2Int(
                    Mathf.FloorToInt(f.transform.position.x),
                    Mathf.FloorToInt(f.transform.position.y)
                );
                return zone.ContainsTile(tile);
            }).ToArray();

            if (inZone.Length > 0)
            {
                return inZone
                    .OrderBy(f => Vector2.Distance(myPosition, f.transform.position))
                    .First();
            }
            // 구역 안에 시설 없음 → 전체에서 탐색 (직원이 멈추지 않도록 폴백)
        }

        return allFacilities
            .OrderBy(f => Vector2.Distance(myPosition, f.transform.position))
            .First();
    }

    /// <summary>
    /// 해당 시설 태그의 시설이 이 직원에게 하나라도 있는지 확인합니다.
    /// 구역이 배정됐어도 폴백이 있으므로, 맵에 하나라도 있으면 true입니다.
    /// </summary>
    public bool HasAnyFacility(string facilityTag)
    {
        return FacilityTag.AnyExists(facilityTag);
    }

    #endregion

    #region 저장/로드

    /// <summary>EmployeeSaveData에 구역 배정을 기록합니다.</summary>
    public void PopulateSaveData(EmployeeSaveData data)
    {
        data.assignedZoneId = assignedZoneId;
    }

    /// <summary>EmployeeSaveData에서 구역 배정을 복원합니다.</summary>
    public void RestoreFromSaveData(EmployeeSaveData data)
    {
        assignedZoneId = data.assignedZoneId;
    }

    #endregion
}
