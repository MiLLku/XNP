using UnityEngine;

/// <summary>
/// 24시간 스케줄 템플릿 (ScriptableObject).
/// 미리 정의된 프리셋(기본형, 야간형 등)을 만들어 직원에게 일괄 적용할 수 있습니다.
///
/// 사용법:
///   1. Assets > Create > Employee > Schedule Template 로 생성
///   2. Inspector에서 24시간 각 시간대의 활동을 설정
///   3. EmployeeSchedule 컴포넌트에 템플릿 할당 또는 ApplyTemplate() 호출
/// </summary>
[CreateAssetMenu(fileName = "NewSchedule", menuName = "Employee/Schedule Template")]
public class ScheduleTemplate : ScriptableObject
{
    [Tooltip("템플릿 이름 (UI 표시용)")]
    public string templateName = "기본 스케줄";

    [Tooltip("24칸 배열. 인덱스 = 시간 (0시~23시)")]
    public ScheduleActivity[] hourlySchedule = new ScheduleActivity[24];

    /// <summary>
    /// 기본 스케줄로 초기화합니다.
    /// 6시 기상 → 작업 → 12시 오락 → 작업 → 18시 오락 → 22시 세척 → 23시 취침
    /// </summary>
    private void Reset()
    {
        templateName = "기본 스케줄";
        hourlySchedule = new ScheduleActivity[24];

        for (int h = 0; h < 24; h++)
        {
            hourlySchedule[h] = h switch
            {
                >= 0 and < 6   => ScheduleActivity.Sleep,       // 0~5시: 취침
                6              => ScheduleActivity.Anything,     // 6시: 자유
                >= 7 and < 12  => ScheduleActivity.Work,         // 7~11시: 작업
                12             => ScheduleActivity.Recreation,   // 12시: 오락
                >= 13 and < 18 => ScheduleActivity.Work,         // 13~17시: 작업
                18             => ScheduleActivity.Recreation,   // 18시: 오락
                >= 19 and < 22 => ScheduleActivity.Anything,     // 19~21시: 자유
                22             => ScheduleActivity.Wash,          // 22시: 세척
                23             => ScheduleActivity.Sleep,         // 23시: 취침
                _              => ScheduleActivity.Anything
            };
        }
    }
}
