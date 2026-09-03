using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 속도 제어 매니저.
/// 배속(1x/2x/3x), 일시정지, 이벤트 연동 자동 속도 조절을 담당합니다.
///
/// 조작:
///   Alpha1 → 1배속, Alpha2 → 2배속, Alpha3 → 3배속
///   Space  → 일시정지 토글
///
/// 이벤트 연동:
///   위험 카테고리(Inspector 설정) → 자동 일시정지
///   일반 이벤트 → 1배속 전환
/// </summary>
public class TimeManager : DestroySingleton<TimeManager>
{
    [Header("속도 설정")]
    [Tooltip("현재 게임 배속 (1~3)")]
    [SerializeField] private int currentSpeed = 1;

    [Header("이벤트 연동")]
    [Tooltip("자동 일시정지를 발동하는 위험 이벤트 카테고리")]
    [SerializeField] private List<EventCategory> dangerousCategories = new List<EventCategory>
    {
        EventCategory.Invasion,
        EventCategory.Disaster
    };

    [Header("디버그")]
    [SerializeField] private bool showDebugLogs = true;

    // 내부 상태
    private bool _isPaused;
    private int _previousSpeed = 1;

    // 공개 프로퍼티
    public int CurrentSpeed => currentSpeed;
    public bool IsPaused => _isPaused;

    #region Unity 생명주기

    protected override void Awake()
    {
        base.Awake();
        ApplyTimeScale();
    }

    private void Update()
    {
        HandleSpeedInput();
    }

    private void OnDestroy()
    {
        // 씬 전환 시 timeScale 복원
        Time.timeScale = 1f;
    }

    #endregion

    #region 입력 처리

    private void HandleSpeedInput()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            SetSpeed(1);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            SetSpeed(2);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            SetSpeed(3);
        }
        else if (Input.GetKeyDown(KeyCode.Space))
        {
            TogglePause();
        }
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 게임 배속 설정 (1~3). 정지 상태면 해제 후 적용.
    /// </summary>
    public void SetSpeed(int speed)
    {
        speed = Mathf.Clamp(speed, 1, 3);

        if (_isPaused)
        {
            _isPaused = false;
            GameMessageBus.Publish(new GamePauseStateChangedMessage(false));
        }

        if (currentSpeed == speed && !_isPaused) return;

        currentSpeed = speed;
        _previousSpeed = speed;
        ApplyTimeScale();

        GameMessageBus.Publish(new GameSpeedChangedMessage(currentSpeed));

        if (showDebugLogs)
        {
            Debug.Log($"[TimeManager] 배속 변경: {currentSpeed}x");
        }
    }

    /// <summary>
    /// 일시정지
    /// </summary>
    public void Pause()
    {
        if (_isPaused) return;

        _previousSpeed = currentSpeed;
        _isPaused = true;
        Time.timeScale = 0f;

        GameMessageBus.Publish(new GamePauseStateChangedMessage(true));

        if (showDebugLogs)
        {
            Debug.Log("[TimeManager] 일시정지");
        }
    }

    /// <summary>
    /// 일시정지 해제 (이전 배속으로 복원)
    /// </summary>
    public void Resume()
    {
        if (!_isPaused) return;

        _isPaused = false;
        currentSpeed = _previousSpeed;
        ApplyTimeScale();

        GameMessageBus.Publish(new GamePauseStateChangedMessage(false));

        if (showDebugLogs)
        {
            Debug.Log($"[TimeManager] 재개: {currentSpeed}x");
        }
    }

    /// <summary>
    /// 일시정지 토글
    /// </summary>
    public void TogglePause()
    {
        if (_isPaused)
            Resume();
        else
            Pause();
    }

    /// <summary>
    /// 위험 이벤트용 강제 일시정지.
    /// 플레이어가 수동으로 해제해야 합니다.
    /// </summary>
    public void ForcePause()
    {
        _previousSpeed = currentSpeed;
        _isPaused = true;
        Time.timeScale = 0f;

        GameMessageBus.Publish(new GamePauseStateChangedMessage(true));

        if (showDebugLogs)
        {
            Debug.Log("[TimeManager] 위험 이벤트 → 강제 일시정지");
        }
    }

    /// <summary>
    /// 일반 이벤트용 1배속 전환.
    /// 정지 상태가 아닐 때만 적용됩니다.
    /// </summary>
    public void SetSpeedToNormal()
    {
        if (_isPaused) return;

        currentSpeed = 1;
        _previousSpeed = 1;
        ApplyTimeScale();

        GameMessageBus.Publish(new GameSpeedChangedMessage(currentSpeed));

        if (showDebugLogs)
        {
            Debug.Log("[TimeManager] 이벤트 발생 → 1배속 전환");
        }
    }

    /// <summary>
    /// 해당 카테고리가 위험 카테고리인지 확인
    /// </summary>
    public bool IsDangerousCategory(EventCategory category)
    {
        return dangerousCategories.Contains(category);
    }

    #endregion

    #region 내부 로직

    private void ApplyTimeScale()
    {
        Time.timeScale = currentSpeed;
    }

    #endregion

    #region 에디터 테스트

    [ContextMenu("Test Pause")]
    private void TestPause() => Pause();

    [ContextMenu("Test Resume")]
    private void TestResume() => Resume();

    [ContextMenu("Test Speed 3x")]
    private void TestSpeed3() => SetSpeed(3);

    #endregion
}
