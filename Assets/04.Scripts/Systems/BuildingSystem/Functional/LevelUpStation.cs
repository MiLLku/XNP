using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 단련장 — 습격 전리품을 써서 직원을 단련(레벨업)하는 건물 ('습격 분석' 연구로 해금).
///
/// 흐름: 단련장 창(<see cref="LevelUpUI"/>)에서 직원 선택 → 전리품 예약 → <b>단련 작업</b>(WorkType.Training, 그 직원 전용) 등록
///       → 직원이 작업 우선순위에 따라 와서 단련 → 완료 시 전리품 소모 + 레벨 +1.
/// 진행도는 여기에 쌓여 도중에 멈춰도 사라지지 않고 세이브에도 남습니다. 한 번에 한 명.
/// 늘어난 스킬 포인트는 직원 관리창의 스킬 트리에서 찍습니다.
/// </summary>
[RequireComponent(typeof(Building))]
public class LevelUpStation : MonoBehaviour, IBuildingExtraSerializable, IBuildingFunction
{
    private const int PRIORITY = 3;

    /// <summary>맵에 있는 단련장 전체</summary>
    public static readonly List<LevelUpStation> All = new List<LevelUpStation>();

    [Tooltip("단련할 때 설 위치 (건물 좌측 하단 기준)")]
    [SerializeField] private Vector2 trainOffset = new Vector2(1f, 0f);

    private Employee trainee;
    private int traineeId;          // 로드 직후엔 ID만 — 직원이 복원되면 잇는다
    private WorkOrder order;
    private int reservationId = -1;
    private bool rebuildOrder;

    /// <summary>필요한 단련량(초 × 작업 속도)</summary>
    public float Duration { get; private set; }
    /// <summary>쌓인 단련량 — 멈춰도 남음</summary>
    public float Accumulated { get; set; }

    public Vector3 TrainPosition => transform.position + (Vector3)trainOffset;
    public bool IsBusy => Trainee != null;
    public float Progress => Duration > 0f ? Mathf.Clamp01(Accumulated / Duration) : 0f;

    public Employee Trainee
    {
        get
        {
            if (trainee == null && traineeId != 0 && EmployeeManager.instance != null)
                trainee = EmployeeManager.instance.AllEmployees.Find(e => e != null && e.InstanceId == traineeId);
            return trainee;
        }
    }

    /// <summary>지금 단련장에 서서 단련 중인지 (진행 표시용)</summary>
    public bool IsTrainingNow
        => Trainee != null && Trainee.TryGetComponent(out EmployeeWork w) && w.CurrentWorkTarget is TrainingOrder t && t.station == this
           && Trainee.State == EmployeeState.Working;

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    private void Update()
    {
        if (SaveManager.instance != null && SaveManager.instance.IsLoading) return;
        if (traineeId == 0) return;

        if (Trainee == null) { if (EmployeeManager.instance != null && EmployeeManager.instance.AllEmployees.Count > 0) CancelTraining("직원 없음"); return; }
        if (Trainee.State == EmployeeState.Dead) { CancelTraining("직원 사망"); return; }
        if (rebuildOrder) { rebuildOrder = false; CreateOrder(); }
    }

    #region 단련

    /// <summary>단련을 시작할 수 없는 이유 (가능하면 null)</summary>
    public string BlockReason(Employee employee)
    {
        if (employee == null || employee.Growth == null) return "-";
        if (IsBusy) return "단련장 사용 중";
        foreach (var s in All)
            if (s != this && s.Trainee == employee) return "다른 단련장에서 단련 중";
        return employee.Growth.TrainingBlockReason();
    }

    /// <summary>단련 예약 — 전리품을 잡아 두고 그 직원 전용 단련 작업을 겁니다.</summary>
    public bool StartTraining(Employee employee)
    {
        if (BlockReason(employee) != null) return false;
        var cfg = EmployeeManager.instance.LevelUpConfig;
        int cost = employee.Growth.NextLevelCost;
        if (cost > 0)
        {
            reservationId = InventoryManager.instance.TryReserve(new List<ResourceCost> { new ResourceCost { item = cfg.growthItem, amount = cost } });
            if (reservationId < 0) return false;
        }

        trainee = employee;
        traineeId = employee.InstanceId;
        Duration = employee.Growth.NextTrainingSeconds;
        Accumulated = 0f;
        CreateOrder();
        return true;
    }

    private void CreateOrder()
    {
        if (WorkSystemManager.instance == null || Trainee == null) return;
        order = WorkSystemManager.instance.CreateWorkOrder($"단련: {Trainee.DisplayName}", WorkType.Training, 1, PRIORITY);
        order.AddTarget(new TrainingOrder { station = this, trainee = Trainee });
    }

    /// <summary>단련 완료 (TrainingOrder가 호출) — 전리품 소모, 레벨 +1</summary>
    public void CompleteTraining()
    {
        var employee = Trainee;
        if (employee == null) return;
        if (reservationId >= 0) InventoryManager.instance.ConsumeReservation(reservationId);
        reservationId = -1;
        order = null; // 완료 처리 중인 작업물 — 큐가 비면 WorkSystemManager가 스스로 정리
        Clear();
        employee.Growth.ApplyLevelUp();
    }

    /// <summary>단련을 취소합니다 — 작업을 지우고 전리품 예약을 풉니다 (쌓인 진행도도 버림).</summary>
    public void CancelTraining(string reason)
    {
        if (traineeId == 0) return;
        Debug.Log($"[단련장] 단련 취소: {reason}");
        if (order != null && WorkSystemManager.instance != null) WorkSystemManager.instance.RemoveWorkOrder(order, isCancellation: true);
        order = null;
        if (reservationId >= 0 && InventoryManager.instance != null) InventoryManager.instance.CancelReservation(reservationId);
        reservationId = -1;
        Clear();
    }

    private void Clear()
    {
        trainee = null;
        traineeId = 0;
        Accumulated = 0f;
        Duration = 0f;
    }

    private void OnDestroy()
    {
        if (SaveManager.instance != null && SaveManager.instance.IsLoading) return;
        CancelTraining("단련장 철거");
    }

    #endregion

    #region 클릭

    private void OnMouseDown()
    {
        if (UIManager.PointerOverUI || UIManager.instance == null) return;
        if (InteractionManager.instance != null &&
            InteractionManager.instance.GetCurrentMode() != InteractionManager.InteractMode.Normal) return;

        LevelUpUI.Open(this);
    }

    #endregion

    #region IBuildingFunction

    public bool IsOperating => IsTrainingNow;
    public void OnBuildingDisabled() { }
    public void OnBuildingEnabled() { }

    #endregion

    #region 세이브 — 작업물은 WorkSystemManager가 저장하지 않으므로 여기서 상태를 들고 로드 후 다시 건다

    [System.Serializable]
    private class SaveState
    {
        public int traineeId;
        public int reservationId = -1;
        public float duration;
        public float accumulated;
    }

    public string SerializeExtra() => JsonUtility.ToJson(new SaveState
    {
        traineeId = traineeId, reservationId = reservationId, duration = Duration, accumulated = Accumulated
    });

    public void DeserializeExtra(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        var s = JsonUtility.FromJson<SaveState>(json);
        if (s == null || s.traineeId == 0) return;
        traineeId = s.traineeId;          // 직원은 건물보다 늦게 복원 — Trainee가 처음 불릴 때 찾는다
        reservationId = s.reservationId;  // 인벤토리가 예약을 ID째 복원한다
        Duration = s.duration;
        Accumulated = s.accumulated;
        rebuildOrder = true;              // Restore 순서상 지금 만들면 WorkSystemManager가 지운다
    }

    #endregion
}
