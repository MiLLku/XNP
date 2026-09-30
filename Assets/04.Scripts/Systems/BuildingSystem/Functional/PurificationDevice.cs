using UnityEngine;

/// <summary>
/// 완전 정화 장치 — 가동하면 14일 뒤 승리.
///
/// 규칙 (기획 확정):
///   - 전력과 무관하다. 한 번 가동하면 그대로 카운트가 흐른다.
///   - 진행도는 장치 자체가 들고 있다 → 파괴되면 진행도도 사라지고, 다시 지어 처음부터 가동해야 한다.
///
/// 승패 판정·HUD는 <see cref="PurificationManager"/>가 맡고, 이 컴포넌트는 자기 진행도만 관리합니다.
/// 진행도는 Building의 extraData로 저장됩니다.
/// </summary>
[RequireComponent(typeof(Building))]
public class PurificationDevice : MonoBehaviour, IBuildingFunction, IBuildingExtraSerializable
{
    #region 상수

    /// <summary>가동부터 완료까지 걸리는 게임일 수</summary>
    public const int PURIFICATION_DAYS = 14;

    private const float FALLBACK_DAY_LENGTH = 1000f;

    #endregion

    #region 상태

    private Building _building;
    private bool _isRunning;
    private bool _isCompleted;
    private float _elapsedSeconds;

    #endregion

    #region 프로퍼티

    public bool IsRunning => _isRunning;
    public bool IsCompleted => _isCompleted;

    /// <summary>완료까지 필요한 총 시간(초) — 하루 길이가 바뀌어도 '14일'을 유지한다</summary>
    public float DurationSeconds
        => PURIFICATION_DAYS * (DayCycle.instance != null ? DayCycle.instance.DayLengthInSeconds : FALLBACK_DAY_LENGTH);

    /// <summary>진행률 0~1</summary>
    public float Progress => Mathf.Clamp01(_elapsedSeconds / DurationSeconds);

    /// <summary>가동 후 경과한 게임일 (소수)</summary>
    public float ElapsedDays => Progress * PURIFICATION_DAYS;

    #endregion

    #region 생명주기

    void Awake()
    {
        _building = GetComponent<Building>();
    }

    void OnEnable()
    {
        PurificationManager.instance?.RegisterDevice(this);
    }

    void Start()
    {
        // 씬 로드 순서상 OnEnable 시점에 매니저가 아직 없을 수 있다
        PurificationManager.instance?.RegisterDevice(this);
    }

    void OnDestroy()
    {
        // 씬 언로드·종료 중엔 매니저도 함께 사라지는 중이다
        if (PurificationManager.instance == null || !gameObject.scene.isLoaded) return;

        // 체력이 0이 되어 사라진 경우만 '파괴'로 알린다 (철거·로드 정리는 레터 없이 빠진다)
        bool destroyedByDamage = _building != null && _building.CurrentHealth <= 0;
        PurificationManager.instance.UnregisterDevice(this, _isRunning, destroyedByDamage);
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 경과 시간을 흘립니다. <see cref="PurificationManager"/>가 매 프레임 호출합니다.
    /// (기반이 무너지면 Building이 기능 스크립트를 꺼버리므로 자체 Update에 맡기지 않는다 —
    ///  전력·기반과 무관하게 카운트는 흐르고, 파괴만이 진행을 끊는다)
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!_isRunning || _isCompleted) return;

        _elapsedSeconds += deltaTime;
        if (_elapsedSeconds >= DurationSeconds)
        {
            _elapsedSeconds = DurationSeconds;
            _isRunning = false;
            _isCompleted = true;
            PurificationManager.instance?.OnDeviceCompleted(this);
        }
    }

    /// <summary>정화를 시작합니다. 이미 가동 중이거나 완료됐으면 무시합니다.</summary>
    public bool Activate()
    {
        if (_isRunning || _isCompleted) return false;
        if (_building != null && !_building.IsFunctional) return false; // 기반이 무너진 장치는 켤 수 없다

        _isRunning = true;
        _elapsedSeconds = 0f;
        PurificationManager.instance?.OnDeviceActivated(this);
        return true;
    }

    #endregion

    #region IBuildingFunction

    public bool IsOperating => _isRunning;

    // 카운트는 매니저가 Tick으로 흘리므로 기반 붕괴로 스크립트가 꺼져도 멈추지 않는다
    public void OnBuildingDisabled() { }
    public void OnBuildingEnabled() { }

    #endregion

    #region IBuildingExtraSerializable

    [System.Serializable]
    private class SaveState
    {
        public bool running;
        public bool completed;
        public float elapsed;
    }

    public string SerializeExtra()
    {
        if (!_isRunning && !_isCompleted) return string.Empty;
        return JsonUtility.ToJson(new SaveState { running = _isRunning, completed = _isCompleted, elapsed = _elapsedSeconds });
    }

    public void DeserializeExtra(string json)
    {
        var s = JsonUtility.FromJson<SaveState>(json);
        if (s == null) return;
        _isRunning = s.running;
        _isCompleted = s.completed;
        _elapsedSeconds = s.elapsed;
    }

    #endregion
}
