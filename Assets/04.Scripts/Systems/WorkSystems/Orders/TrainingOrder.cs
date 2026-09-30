using UnityEngine;

/// <summary>
/// 단련 작업 — 지정된 직원 한 명만 맡을 수 있습니다 (<see cref="WorkTask.CanBeAssignedTo"/>).
/// <b>진행도 누적 방식</b>(IProgressiveWork)이라 도중에 밥·수면·징집으로 빠져도 진행도가 남고, 다시 오면 이어서 합니다.
/// 진행도는 <see cref="LevelUpStation"/>이 들고 있어 세이브에도 남습니다.
/// </summary>
public class TrainingOrder : IWorkTarget, IProgressiveWork
{
    public LevelUpStation station;
    public Employee trainee;

    public Vector3 GetWorkPosition() => station != null ? station.TrainPosition : Vector3.zero;

    public WorkType GetWorkType() => WorkType.Training;

    public float GetWorkTime() => GetWorkAmount();

    public bool IsWorkAvailable() => station != null && trainee != null && station.Trainee == trainee && trainee.State != EmployeeState.Dead;

    public void CompleteWork(Employee worker)
    {
        if (station != null) station.CompleteTraining();
    }

    public void CancelWork(Employee worker) { } // 진행도는 단련장에 남는다

    #region IProgressiveWork — 진행도는 단련장에 누적

    public float GetWorkAmount() => station != null ? station.Duration : 1f;

    public float GetAccumulatedWork() => station != null ? station.Accumulated : 0f;

    public void AddWork(float amount)
    {
        if (station != null) station.Accumulated = Mathf.Min(station.Duration, station.Accumulated + amount);
    }

    public void ReduceWork(float amount)
    {
        if (station != null) station.Accumulated = Mathf.Max(0f, station.Accumulated - amount);
    }

    #endregion
}
