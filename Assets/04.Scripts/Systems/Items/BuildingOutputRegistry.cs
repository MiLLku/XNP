using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 산출물을 안고 있는 건물(<see cref="IBuildingOutput"/>)을 추적하고
/// 운반(Haul) 작업을 자동 생성하는 싱글톤.
///
/// <see cref="DroppedItemManager"/>가 바닥 아이템에 대해 하는 일을,
/// 건물 산출물에 대해 똑같이 합니다:
///   1. 건물이 Register(this) → 산출물이 생기면 NotifyOutputChanged(this)
///   2. 전역 "운반 작업" WorkOrder에 BuildingHaulOrder 태스크 추가
///   3. 직원 AI가 WorkType.Hauling을 자동 픽업 → 건물로 이동해 수령
///   4. EmployeeWork가 가장 가까운 창고에 배달
///
/// 저장/복원:
///   Haul WorkOrder는 저장하지 않습니다.
///   건물의 산출물 수량은 각 건물이 IBuildingExtraSerializable로 저장하고,
///   복원 후 Register()가 다시 불리면서 태스크가 자동 재생성됩니다.
///
/// 중복 방지:
///   건물당 미완료 태스크를 1개만 유지합니다. 직원이 한 번에 운반력만큼 쓸어담으므로
///   같은 건물에 태스크를 여러 개 쌓아봐야 헛걸음만 늘어납니다.
/// </summary>
public class BuildingOutputRegistry : DestroySingleton<BuildingOutputRegistry>
{
    #region 설정

    [Header("Haul 작업물 설정")]
    [Tooltip("건물 산출물 운반에 동시에 붙을 수 있는 최대 직원 수")]
    [SerializeField] private int haulMaxWorkers = 10;

    [Tooltip("산출물 보유 건물을 다시 훑어 누락된 운반 작업을 채우는 주기(초)")]
    [SerializeField] private float rescanInterval = 5f;

    #endregion

    #region 내부 상태

    private readonly List<IBuildingOutput> _sources = new();

    /// <summary>건물별로 아직 살아 있는 태스크 (중복 생성 방지)</summary>
    private readonly Dictionary<IBuildingOutput, BuildingHaulOrder> _pendingTasks = new();

    /// <summary>WorkSystemManager가 관리하는 전역 Haul WorkOrder</summary>
    private WorkOrder _haulOrder;

    private float _rescanTimer;

    #endregion

    #region 등록

    public void Register(IBuildingOutput source)
    {
        if (source == null || _sources.Contains(source)) return;
        _sources.Add(source);
        NotifyOutputChanged(source);
    }

    public void Unregister(IBuildingOutput source)
    {
        if (source == null) return;
        _sources.Remove(source);
        _pendingTasks.Remove(source);
    }

    /// <summary>
    /// 산출물이 늘었을 때 건물이 호출합니다. 아직 운반 작업이 없으면 만듭니다.
    /// </summary>
    public void NotifyOutputChanged(IBuildingOutput source)
    {
        if (source == null || !source.HasPendingOutput) return;
        if (!source.AutoHaulEnabled) return;   // 자동 운반 꺼짐 — 직원을 보내지 않는다

        // 이미 유효한 태스크가 걸려 있으면 더 만들지 않는다
        if (_pendingTasks.TryGetValue(source, out var existing) &&
            existing != null && !existing.completed)
            return;

        CreateHaulTask(source);
    }

    #endregion

    #region 생명주기

    void Update()
    {
        // 태스크가 취소·유실됐거나 WorkOrder가 새로 만들어진 경우를 주기적으로 메꾼다.
        // (이벤트만 믿으면 한 번 놓친 건물이 영영 방치된다)
        _rescanTimer -= Time.deltaTime;
        if (_rescanTimer > 0f) return;
        _rescanTimer = rescanInterval;

        for (int i = _sources.Count - 1; i >= 0; i--)
        {
            var src = _sources[i];
            var obj = src as UnityEngine.Object;
            if (src == null || obj == null) { _sources.RemoveAt(i); continue; }

            if (src.AutoHaulEnabled && src.IsOutputAccessible && src.HasPendingOutput)
                NotifyOutputChanged(src);
        }
    }

    #endregion

    #region 작업 생성

    private WorkOrder GetOrCreateHaulOrder()
    {
        if (_haulOrder != null && _haulOrder.isActive && !_haulOrder.IsCompleted())
            return _haulOrder;

        if (WorkSystemManager.instance == null) return null;

        _haulOrder = WorkSystemManager.instance.CreateWorkOrder(
            "산출물 운반",
            WorkType.Hauling,
            haulMaxWorkers,
            priority: 3
        );

        return _haulOrder;
    }

    private void CreateHaulTask(IBuildingOutput source)
    {
        WorkOrder order = GetOrCreateHaulOrder();
        if (order == null) return;

        var target = new BuildingHaulOrder(source);
        order.taskQueue.Enqueue(new WorkTask(target, taskPriority: 3));
        _pendingTasks[source] = target;
    }

    #endregion
}
