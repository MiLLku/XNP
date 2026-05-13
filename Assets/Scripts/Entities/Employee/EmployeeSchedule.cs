using System;
using UnityEngine;

/// <summary>
/// 직원별 24시간 스케줄.
/// 플레이어가 인게임 UI에서 시간대별로 직접 활동을 지정합니다.
///
/// UI 연동:
///   - schedule[hour] 로 직접 읽기/쓰기 (0=자정 ~ 23=밤 11시)
///   - SetActivity(hour, activity)로 한 칸 변경
///   - ApplyPreset(template)으로 프리셋 일괄 복사
///   - OnScheduleChanged 이벤트로 UI 갱신
///
/// AI 연동:
///   - EmployeeAI가 GetCurrentActivity()로 현재 시간대 활동 조회
/// </summary>
public class EmployeeSchedule : MonoBehaviour
{
    #region 상수

    public const int HOURS_PER_DAY = 24;

    #endregion

    #region 필드

    [Header("스케줄 (24시간)")]
    [Tooltip("인덱스 = 시간 (0시~23시). 플레이어가 직접 편집합니다.")]
    [SerializeField] private ScheduleActivity[] schedule;

    #endregion

    #region 이벤트

    /// <summary>스케줄이 변경될 때 발생 (UI 갱신용)</summary>
    public event Action OnScheduleChanged;

    #endregion

    #region 초기화

    void Awake()
    {
        if (schedule == null || schedule.Length != HOURS_PER_DAY)
        {
            schedule = new ScheduleActivity[HOURS_PER_DAY];
            ApplyDefaultSchedule();
        }
    }

    /// <summary>
    /// 기본 스케줄을 적용합니다.
    /// 0~5 Sleep, 6 Anything, 7~11 Work, 12 Recreation,
    /// 13~17 Work, 18 Recreation, 19~21 Anything, 22 Wash, 23 Sleep
    /// </summary>
    private void ApplyDefaultSchedule()
    {
        for (int h = 0; h < HOURS_PER_DAY; h++)
        {
            schedule[h] = h switch
            {
                >= 0 and < 6   => ScheduleActivity.Sleep,
                6              => ScheduleActivity.Anything,
                >= 7 and < 12  => ScheduleActivity.Work,
                12             => ScheduleActivity.Recreation,
                >= 13 and < 18 => ScheduleActivity.Work,
                18             => ScheduleActivity.Recreation,
                >= 19 and < 22 => ScheduleActivity.Anything,
                22             => ScheduleActivity.Wash,
                23             => ScheduleActivity.Sleep,
                _              => ScheduleActivity.Anything
            };
        }
    }

    #endregion

    #region 조회

    /// <summary>
    /// 현재 시간대의 스케줄 활동을 반환합니다.
    /// </summary>
    public ScheduleActivity GetCurrentActivity()
    {
        if (DayCycle.instance == null)
            return ScheduleActivity.Anything;

        return schedule[DayCycle.instance.CurrentHour];
    }

    /// <summary>
    /// 특정 시간의 스케줄 활동을 반환합니다.
    /// </summary>
    public ScheduleActivity GetActivityAt(int hour)
    {
        hour = Mathf.Clamp(hour, 0, HOURS_PER_DAY - 1);
        return schedule[hour];
    }

    /// <summary>
    /// 전체 24시간 스케줄 배열을 반환합니다 (UI 표시용, 복사본).
    /// </summary>
    public ScheduleActivity[] GetFullSchedule()
    {
        var copy = new ScheduleActivity[HOURS_PER_DAY];
        Array.Copy(schedule, copy, HOURS_PER_DAY);
        return copy;
    }

    /// <summary>
    /// 다음 활동 전환이 일어나는 시각을 반환합니다.
    /// </summary>
    public int GetNextActivityChangeHour()
    {
        if (DayCycle.instance == null) return -1;

        int currentHour = DayCycle.instance.CurrentHour;
        ScheduleActivity currentActivity = schedule[currentHour];

        for (int offset = 1; offset < HOURS_PER_DAY; offset++)
        {
            int checkHour = (currentHour + offset) % HOURS_PER_DAY;
            if (schedule[checkHour] != currentActivity)
                return checkHour;
        }

        return -1;
    }

    #endregion

    #region 설정 (UI에서 호출)

    /// <summary>
    /// 특정 시간의 활동을 설정합니다.
    /// UI에서 한 칸을 클릭/변경할 때 호출합니다.
    /// </summary>
    public void SetActivity(int hour, ScheduleActivity activity)
    {
        hour = Mathf.Clamp(hour, 0, HOURS_PER_DAY - 1);
        if (schedule[hour] == activity) return;

        schedule[hour] = activity;
        OnScheduleChanged?.Invoke();
    }

    /// <summary>
    /// 시간 범위에 같은 활동을 일괄 설정합니다.
    /// UI에서 드래그로 여러 칸을 선택할 때 호출합니다.
    /// 자정을 넘는 범위도 지원합니다 (예: 22시~5시).
    /// </summary>
    public void SetRange(int fromHour, int toHour, ScheduleActivity activity)
    {
        fromHour = Mathf.Clamp(fromHour, 0, HOURS_PER_DAY - 1);
        toHour = Mathf.Clamp(toHour, 0, HOURS_PER_DAY - 1);

        if (fromHour <= toHour)
        {
            for (int h = fromHour; h <= toHour; h++)
                schedule[h] = activity;
        }
        else
        {
            for (int h = fromHour; h < HOURS_PER_DAY; h++)
                schedule[h] = activity;
            for (int h = 0; h <= toHour; h++)
                schedule[h] = activity;
        }

        OnScheduleChanged?.Invoke();
    }

    /// <summary>
    /// 프리셋 템플릿의 스케줄을 복사합니다.
    /// UI의 "프리셋 적용" 버튼에서 호출합니다.
    /// </summary>
    public void ApplyPreset(ScheduleTemplate template)
    {
        if (template == null || template.hourlySchedule == null) return;

        int count = Mathf.Min(template.hourlySchedule.Length, HOURS_PER_DAY);
        Array.Copy(template.hourlySchedule, schedule, count);
        OnScheduleChanged?.Invoke();
    }

    /// <summary>
    /// 모든 시간을 하나의 활동으로 채웁니다.
    /// UI의 "전체 채우기" 버튼에서 호출합니다.
    /// </summary>
    public void FillAll(ScheduleActivity activity)
    {
        for (int h = 0; h < HOURS_PER_DAY; h++)
            schedule[h] = activity;
        OnScheduleChanged?.Invoke();
    }

    /// <summary>
    /// 기본 스케줄로 초기화합니다.
    /// UI의 "초기화" 버튼에서 호출합니다.
    /// </summary>
    public void ResetToDefault()
    {
        ApplyDefaultSchedule();
        OnScheduleChanged?.Invoke();
    }

    #endregion

    #region 저장/로드

    /// <summary>EmployeeSaveData에 스케줄 데이터를 기록합니다.</summary>
    public void PopulateSaveData(EmployeeSaveData data)
    {
        data.scheduleActivities = new int[HOURS_PER_DAY];
        for (int h = 0; h < HOURS_PER_DAY; h++)
            data.scheduleActivities[h] = (int)schedule[h];
    }

    /// <summary>EmployeeSaveData에서 스케줄을 복원합니다.</summary>
    public void RestoreFromSaveData(EmployeeSaveData data)
    {
        if (data.scheduleActivities == null || data.scheduleActivities.Length != HOURS_PER_DAY)
            return;

        for (int h = 0; h < HOURS_PER_DAY; h++)
            schedule[h] = (ScheduleActivity)data.scheduleActivities[h];
    }

    #endregion
}
