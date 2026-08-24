using UnityEngine;

/// <summary>
/// 이상 행동 — 작업 중단.
///
/// 하던 일을 <b>망가뜨리고</b> 멈춘다. 단순히 손을 놓는 게 아니라
/// 작업 대상에 쌓여 있던 누적 진행도를 깎아버린 뒤 잠시 배정을 거부한다.
///
/// 설계 의도:
///   '작업 거부'(일반 계열)와 '명령 무시'(침식 계열)는 이미 "일을 안 한다 = 시간 손실"을
///   담당하고 있다. 이 행동까지 같은 성격이면 지속 시간만 다른 열화판이 되므로,
///   <b>성과 손실</b>이라는 다른 축을 준다 — 다른 직원이 이어받아도 손해가 남고,
///   침식이 높은 직원에게 중요한 건설을 맡기는 것 자체가 리스크가 된다.
///
///   진행도가 대상에 누적되지 않는 작업(채광·벌목·제작)은 원래 중단 시 진행분이
///   사라지므로, 취소하는 것만으로 이미 손실이 발생한다.
/// </summary>
public class AbnormalBehaviorWorkStop : AbnormalBehaviorBase
{
    #region 수치 (밸런스 조정 지점)

    /// <summary>지속 시간 (초). 이 동안 새 작업이 배정되지 않는다.</summary>
    private const float DURATION = 5f;

    /// <summary>깎아내는 누적 진행도 비율 (총 작업량 기준)</summary>
    private const float PROGRESS_LOSS_RATIO = 0.3f;

    #endregion

    public override AbnormalBehaviorType BehaviorType => AbnormalBehaviorType.WorkStop;

    /// <summary>
    /// 실제로 작업 중일 때만 후보가 된다.
    /// 발생 판정에는 "작업 중"이라는 조건이 없어서, 자거나 쉬는 직원이 뽑히면
    /// 중단할 작업이 없어 아무 일도 일어나지 않은 채 쿨다운만 소모된다.
    /// </summary>
    public override bool CanExecute(Employee employee)
    {
        if (!base.CanExecute(employee)) return false;

        var work = employee.GetComponent<EmployeeWork>();
        return work != null && work.CurrentWork != WorkType.None;
    }

    public override float Execute(Employee employee)
    {
        var work = employee.GetComponent<EmployeeWork>();

        // 진행도가 대상에 누적되는 작업(건설·철거)이면 쌓아둔 것을 깎는다.
        // 취소보다 먼저 해야 한다 — CancelWork가 대상 참조를 비운다.
        if (work != null && work.CurrentWorkTarget is IProgressiveWork progressive)
        {
            float before = progressive.GetAccumulatedWork();
            progressive.ReduceWork(progressive.GetWorkAmount() * PROGRESS_LOSS_RATIO);
            float after = progressive.GetAccumulatedWork();

            Debug.Log($"[AbnormalBehavior] {employee.DisplayName}: 작업 중단 — 진행도 {before:F1} → {after:F1} ({DURATION:F0}초)");
        }
        else
        {
            Debug.Log($"[AbnormalBehavior] {employee.DisplayName}: 작업 중단 ({DURATION:F0}초)");
        }

        employee.CancelWork();
        return DURATION;
    }
}
