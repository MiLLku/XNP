using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 직원별 구역 할당 컴포넌트.
/// 플레이어가 UI에서 스케줄 활동별 구역을 직원에게 직접 할당합니다.
///
/// 동작 규칙:
///   - 활동별로 구역을 할당하면 AI가 해당 구역 내 시설을 우선 탐색
///   - 할당된 구역이 없으면 맵 전체에서 가장 가까운 시설 탐색 (기존 동작)
///   - Work 구역이 할당되면 길찾기 시 해당 구역 밖은 높은 비용 부여
///   - Restricted 구역은 통과 불가
///
/// UI 연동:
///   - AssignZone(type, zoneId) : 구역 할당
///   - ClearZone(type) : 구역 해제
///   - GetAssignedZone(type) : 현재 할당된 구역 조회
/// </summary>
public class EmployeeZoneAssignment : MonoBehaviour
{
    #region 필드

    /// <summary>활동 타입별 할당된 구역 ID (-1 = 미할당)</summary>
    [SerializeField] private int sleepZoneId = -1;
    [SerializeField] private int recreationZoneId = -1;
    [SerializeField] private int washZoneId = -1;
    [SerializeField] private int workZoneId = -1;

    private Employee employee;

    #endregion

    #region 이벤트

    /// <summary>구역 할당 변경 시 발생 (UI 갱신용)</summary>
    public event Action OnZoneAssignmentChanged;

    #endregion

    #region 초기화

    void Awake()
    {
        employee = GetComponent<Employee>();
    }

    #endregion

    #region 공개 API — 할당 (UI에서 호출)

    /// <summary>
    /// 특정 구역 타입에 구역을 할당합니다.
    /// </summary>
    public void AssignZone(ZoneType type, int zoneId)
    {
        SetZoneId(type, zoneId);
        OnZoneAssignmentChanged?.Invoke();
    }

    /// <summary>
    /// 특정 구역 타입의 할당을 해제합니다.
    /// </summary>
    public void ClearZone(ZoneType type)
    {
        SetZoneId(type, -1);
        OnZoneAssignmentChanged?.Invoke();
    }

    /// <summary>
    /// 모든 구역 할당을 해제합니다.
    /// </summary>
    public void ClearAllZones()
    {
        sleepZoneId = -1;
        recreationZoneId = -1;
        washZoneId = -1;
        workZoneId = -1;
        OnZoneAssignmentChanged?.Invoke();
    }

    #endregion

    #region 공개 API — 조회

    /// <summary>
    /// 특정 타입에 할당된 구역 ID를 반환합니다 (없으면 -1).
    /// </summary>
    public int GetAssignedZoneId(ZoneType type)
    {
        return type switch
        {
            ZoneType.Sleep      => sleepZoneId,
            ZoneType.Recreation => recreationZoneId,
            ZoneType.Wash       => washZoneId,
            ZoneType.Work       => workZoneId,
            _                   => -1
        };
    }

    /// <summary>
    /// 특정 타입에 할당된 구역을 반환합니다.
    /// ZoneManager에서 구역이 삭제됐으면 자동으로 할당 해제하고 null 반환.
    /// </summary>
    public Zone GetAssignedZone(ZoneType type)
    {
        if (ZoneManager.instance == null) return null;

        int id = GetAssignedZoneId(type);
        if (id < 0) return null;

        var zone = ZoneManager.instance.GetZone(id);

        // 구역이 삭제됐으면 자동 해제
        if (zone == null)
        {
            SetZoneId(type, -1);
            return null;
        }

        return zone;
    }

    /// <summary>
    /// 특정 타입에 구역이 할당되어 있는지 확인합니다.
    /// </summary>
    public bool HasZoneAssigned(ZoneType type) => GetAssignedZoneId(type) >= 0;

    /// <summary>
    /// 할당된 모든 구역 ID의 HashSet을 반환합니다 (경로 탐색용).
    /// </summary>
    public HashSet<int> GetAllAssignedZoneIds()
    {
        var result = new HashSet<int>();
        if (sleepZoneId >= 0)      result.Add(sleepZoneId);
        if (recreationZoneId >= 0) result.Add(recreationZoneId);
        if (washZoneId >= 0)       result.Add(washZoneId);
        if (workZoneId >= 0)       result.Add(workZoneId);
        return result;
    }

    /// <summary>
    /// 스케줄 활동에 대응하는 ZoneType을 반환합니다.
    /// </summary>
    public static ZoneType GetZoneTypeForActivity(ScheduleActivity activity)
    {
        return activity switch
        {
            ScheduleActivity.Sleep      => ZoneType.Sleep,
            ScheduleActivity.Recreation => ZoneType.Recreation,
            ScheduleActivity.Wash       => ZoneType.Wash,
            ScheduleActivity.Work       => ZoneType.Work,
            _                           => ZoneType.Work
        };
    }

    /// <summary>
    /// 활동 수행을 위한 구역 내 가장 가까운 시설을 찾습니다.
    /// 구역이 할당됐으면 구역 내에서만 탐색, 없으면 전체 탐색.
    /// </summary>
    /// <param name="activity">수행할 활동</param>
    /// <param name="facilityTag">시설의 Unity 태그</param>
    /// <param name="myPosition">직원 현재 위치</param>
    /// <returns>가장 가까운 시설 오브젝트 (없으면 null)</returns>
    public GameObject FindNearestFacility(ScheduleActivity activity, string facilityTag, Vector3 myPosition)
    {
        ZoneType zoneType = GetZoneTypeForActivity(activity);
        Zone assignedZone = GetAssignedZone(zoneType);

        var allFacilities = GameObject.FindGameObjectsWithTag(facilityTag)
            .Where(f => f != null)
            .ToArray();

        if (allFacilities.Length == 0) return null;

        // 구역이 할당됐으면 구역 내 시설만 우선 탐색
        if (assignedZone != null && ZoneManager.instance != null)
        {
            var inZone = allFacilities.Where(f =>
            {
                var tile = new Vector2Int(
                    Mathf.FloorToInt(f.transform.position.x),
                    Mathf.FloorToInt(f.transform.position.y)
                );
                return assignedZone.ContainsTile(tile);
            }).ToArray();

            if (inZone.Length > 0)
            {
                return inZone
                    .OrderBy(f => Vector2.Distance(myPosition, f.transform.position))
                    .First();
            }
            // 구역 내 시설 없음 → 전체에서 탐색 (fallback)
        }

        // 전체에서 가장 가까운 시설
        return allFacilities
            .OrderBy(f => Vector2.Distance(myPosition, f.transform.position))
            .First();
    }

    #endregion

    #region 내부

    private void SetZoneId(ZoneType type, int id)
    {
        switch (type)
        {
            case ZoneType.Sleep:      sleepZoneId = id;      break;
            case ZoneType.Recreation: recreationZoneId = id; break;
            case ZoneType.Wash:       washZoneId = id;       break;
            case ZoneType.Work:       workZoneId = id;       break;
        }
    }

    #endregion

    #region 저장/로드

    /// <summary>EmployeeSaveData에 구역 할당 데이터를 기록합니다.</summary>
    public void PopulateSaveData(EmployeeSaveData data)
    {
        data.sleepZoneId      = sleepZoneId;
        data.recreationZoneId = recreationZoneId;
        data.washZoneId       = washZoneId;
        data.workZoneId       = workZoneId;
    }

    /// <summary>EmployeeSaveData에서 구역 할당을 복원합니다.</summary>
    public void RestoreFromSaveData(EmployeeSaveData data)
    {
        sleepZoneId      = data.sleepZoneId;
        recreationZoneId = data.recreationZoneId;
        washZoneId       = data.washZoneId;
        workZoneId       = data.workZoneId;
    }

    #endregion
}
