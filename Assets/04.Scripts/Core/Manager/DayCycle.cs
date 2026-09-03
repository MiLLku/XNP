using UnityEngine;

/// <summary>
/// 게임 내 24시간 주기를 관리합니다.
/// TimeManager의 timeScale 영향을 받아 시간이 흐릅니다.
///
/// 시간 단위:
///   1일 = dayLengthInSeconds (현실 초, 기본 1000초 = 16분 40초 — 림월드 60,000틱 기준)
///   1시간 = dayLengthInSeconds / 24
///   시각 0 = 자정, 12 = 정오
///
/// 사용 예:
///   DayCycle.instance.CurrentHour  → 현재 시각 (0~23)
///   GameMessageBus.Subscribe<HourChangedMessage>(m => { ... })  → 시각 변경 구독
/// </summary>
public class DayCycle : DestroySingleton<DayCycle>, ISaveModule
{
    #region 설정

    [Header("시간 설정")]
    [Tooltip("현실 시간(초) 기준 하루 길이. 600 = 현실 10분이 게임 1일")]
    [SerializeField] private float dayLengthInSeconds = 1000f;

    [Tooltip("게임 시작 시 초기 시각 (0~23). 기본 6 = 오전 6시")]
    [SerializeField] private int startHour = 6;

    [Header("디버그")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    #region 상태

    /// <summary>하루 내 경과 비율 (0.0 = 자정, 0.5 = 정오, 1.0 = 다음 자정)</summary>
    private float timeNormalized;

    /// <summary>이전 프레임의 시각 (시간 변경 감지용)</summary>
    private int previousHour;

    /// <summary>경과 일수 (1일차부터 시작)</summary>
    private int day = 1;

    #endregion

    #region 프로퍼티

    /// <summary>현재 시각 (0~23 정수)</summary>
    public int CurrentHour => Mathf.FloorToInt(timeNormalized * 24f) % 24;

    /// <summary>하루 내 경과 비율 (0.0~1.0)</summary>
    public float TimeNormalized => timeNormalized;

    /// <summary>현재 날짜 (1부터 시작)</summary>
    public int Day => day;

    /// <summary>하루 길이 (현실 초)</summary>
    public float DayLengthInSeconds => dayLengthInSeconds;

    /// <summary>현재 시각의 분 (0~59)</summary>
    public int CurrentMinute => Mathf.FloorToInt(timeNormalized * 24f * 60f) % 60;

    /// <summary>밤 시간 여부 (22시~5시)</summary>
    public bool IsNight => CurrentHour >= 22 || CurrentHour < 6;

    #endregion

    #region 생명주기

    protected override void Awake()
    {
        base.Awake();
        timeNormalized = startHour / 24f;
        previousHour = CurrentHour;
    }

    void Update()
    {
        AdvanceTime();
    }

    #endregion

    #region 시간 진행

    private void AdvanceTime()
    {
        float delta = Time.deltaTime / dayLengthInSeconds;
        timeNormalized += delta;

        // 날짜 전환
        if (timeNormalized >= 1f)
        {
            timeNormalized -= 1f;
            day++;
            GameMessageBus.Publish(new NewDayMessage(day));

            if (showDebugLogs)
                Debug.Log($"[DayCycle] === {day}일차 시작 ===");
        }

        // 시간 변경 감지
        int currentHour = CurrentHour;
        if (currentHour != previousHour)
        {
            previousHour = currentHour;
            GameMessageBus.Publish(new HourChangedMessage(currentHour));

            if (showDebugLogs)
                Debug.Log($"[DayCycle] {day}일차 {currentHour}시");
        }
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 시각을 직접 설정합니다 (치트/디버그용).
    /// </summary>
    /// <param name="hour">설정할 시각 (0~23)</param>
    public void SetHour(int hour)
    {
        hour = Mathf.Clamp(hour, 0, 23);
        timeNormalized = hour / 24f;
        previousHour = hour;
        GameMessageBus.Publish(new HourChangedMessage(hour));
    }

    /// <summary>
    /// 날짜와 시각을 직접 설정합니다 (저장 로드용).
    /// </summary>
    public void SetDayAndTime(int newDay, float normalizedTime)
    {
        day = Mathf.Max(1, newDay);
        timeNormalized = Mathf.Clamp01(normalizedTime);
        previousHour = CurrentHour;
    }

    /// <summary>
    /// 하루 길이를 변경합니다 (설정 메뉴용).
    /// </summary>
    public void SetDayLength(float seconds)
    {
        dayLengthInSeconds = Mathf.Max(60f, seconds);
    }

    #endregion

    #region ISaveModule

    /// <summary>맵보다 먼저 로드 (시간 기준이 필요하므로)</summary>
    public int SaveOrder => 5;

    public void Capture(SaveData data)
    {
        data.dayCycle = new DayCycleSaveData
        {
            day           = day,
            timeNormalized = timeNormalized
        };
    }

    public void Restore(SaveData data)
    {
        if (data.dayCycle == null) return;

        SetDayAndTime(data.dayCycle.day, data.dayCycle.timeNormalized);
    }

    public void PostRestore(SaveData data) { }

    #endregion

    #region 에디터 테스트

    [ContextMenu("Skip to Next Hour")]
    private void DebugSkipHour()
    {
        int nextHour = (CurrentHour + 1) % 24;
        SetHour(nextHour);
    }

    [ContextMenu("Skip to Next Day")]
    private void DebugSkipDay()
    {
        timeNormalized = 0f;
        day++;
        previousHour = 0;
        GameMessageBus.Publish(new NewDayMessage(day));
        GameMessageBus.Publish(new HourChangedMessage(0));
    }

    #endregion
}
