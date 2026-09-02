using System;
using UnityEngine;

/// <summary>
/// 소집(Draft) 시스템.
/// 소집 중에는 스케줄, AI 자율 행동이 모두 중지되고
/// 플레이어의 직접 이동/공격 명령만 수행합니다.
///
/// 우선순위: Dead > MentalBreak > Drafted > 스케줄/AI
///   - Dead, MentalBreak 상태에서는 소집 불가
///   - 소집 해제 시 Idle로 복귀 → AI가 스케줄에 따라 행동 재개
/// </summary>
public class EmployeeDraft : MonoBehaviour
{
    #region 필드

    [Header("상태")]
    [SerializeField] private bool isDrafted = false;

    // 컴포넌트 참조
    private Employee employee;
    private EmployeeWork work;
    private EmployeeMovement movement;

    #endregion

    #region 이벤트

    /// <summary>소집 상태 변경 시 발생 (새 상태 전달)</summary>
    public event Action<bool> OnDraftChanged;

    #endregion

    #region 프로퍼티

    /// <summary>현재 소집 상태 여부</summary>
    public bool IsDrafted => isDrafted;

    #endregion

    #region 초기화

    void Awake()
    {
        employee = GetComponent<Employee>();
        work = GetComponent<EmployeeWork>();
        movement = GetComponent<EmployeeMovement>();
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 소집 상태를 토글합니다 (단축키에서 호출).
    /// </summary>
    public void ToggleDraft()
    {
        SetDrafted(!isDrafted);
    }

    /// <summary>
    /// 소집 상태를 설정합니다.
    /// </summary>
    public void SetDrafted(bool drafted)
    {
        // Dead, MentalBreak 상태에서는 소집 불가
        if (drafted && employee != null &&
            (employee.State == EmployeeState.Dead ||
             employee.State == EmployeeState.MentalBreak))
        {
            Debug.LogWarning($"[Draft] {employee.DisplayName}: {employee.State} 상태에서는 소집 불가");
            return;
        }

        if (isDrafted == drafted) return;
        isDrafted = drafted;

        if (drafted)
        {
            EnterDraft();
        }
        else
        {
            ExitDraft();
        }

        OnDraftChanged?.Invoke(drafted);
    }

    /// <summary>
    /// 플레이어가 지정한 위치로 직접 이동합니다.
    /// 소집 중에만 동작합니다.
    /// </summary>
    public void CommandMoveTo(Vector3 worldPosition)
    {
        if (!isDrafted || employee == null) return;

        // 전투 기준점을 목적지로 이동 + 현재 교전 해제 (이동 명령 우선)
        GetComponent<EmployeeCombat>()?.OnPlayerMoveCommand(worldPosition);

        // Drafted 상태 유지하면서 이동
        if (movement != null)
        {
            movement.StopMoving();
            // 플레이어의 명시적 지시는 배정 구역보다 우선한다.
            movement.MoveTo(worldPosition,
                onComplete: () =>
                {
                    // 이동 완료 후에도 Drafted 상태 유지
                    if (isDrafted)
                        employee.SetState(EmployeeState.Drafted);
                },
                onFailed: () =>
                {
                    if (isDrafted)
                        employee.SetState(EmployeeState.Drafted);
                }
            );
        }
    }

    #endregion

    #region 내부 로직

    private void EnterDraft()
    {
        // 진행 중 작업 즉시 취소
        work?.CancelWork();

        // 이동 즉시 중지
        movement?.StopMoving();

        // 소집 상태로 전환
        employee?.SetState(EmployeeState.Drafted);

        Debug.Log($"[Draft] {employee?.DisplayName} 소집됨");
    }

    private void ExitDraft()
    {
        // 이동 중지
        movement?.StopMoving();

        // Idle로 복귀 → EmployeeAI가 스케줄에 따라 행동 재개
        employee?.SetState(EmployeeState.Idle);

        Debug.Log($"[Draft] {employee?.DisplayName} 소집 해제");
    }

    #endregion

    #region 저장/로드

    /// <summary>EmployeeSaveData에 소집 상태를 기록합니다.</summary>
    public void PopulateSaveData(EmployeeSaveData data)
    {
        data.isDrafted = isDrafted;
    }

    /// <summary>EmployeeSaveData에서 소집 상태를 복원합니다.</summary>
    public void RestoreFromSaveData(EmployeeSaveData data)
    {
        // 로드 시 소집 상태는 항상 해제 (게임 재시작 시 수동으로 다시 소집)
        isDrafted = false;
    }

    #endregion
}
