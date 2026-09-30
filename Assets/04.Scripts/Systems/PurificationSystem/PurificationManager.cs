using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 한 판의 승패를 판정하는 매니저.
///
/// 승리: 완전 정화 장치(<see cref="PurificationDevice"/>)가 14일 가동을 마침.
/// 패배: 멀쩡한 직원이 0명 — 사망(State=Dead)했거나 완전 침식으로 변이(목록에서 빠짐)한 직원은 치지 않는다.
///
/// 장치의 경과 시간도 여기서 흘립니다 (기반 붕괴로 장치 스크립트가 꺼져도 카운트는 멈추지 않도록).
/// HUD와 결과 화면은 코드로 만들어 씬 배선이 필요 없습니다.
/// </summary>
public class PurificationManager : DestroySingleton<PurificationManager>
{
    #region 상수

    /// <summary>패배 판정 주기(초, 게임 시간)</summary>
    private const float DEFEAT_CHECK_INTERVAL = 1f;

    #endregion

    #region 설정

    [SerializeField] private bool showDebugLogs = true;

    #endregion

    #region 상태

    private readonly List<PurificationDevice> _devices = new List<PurificationDevice>();
    private PurificationHUD _hud;
    private GameResultScreen _resultScreen;

    private bool _gameOver;
    /// <summary>멀쩡한 직원이 있었던 적이 있는지 — 직원 스폰 전(시작 직후·로드 중) 패배 오판 방지</summary>
    private bool _hadSurvivor;
    private float _nextDefeatCheck;
    private bool _wasLoading;

    #endregion

    #region 프로퍼티

    public bool IsGameOver => _gameOver;

    /// <summary>가동 중인 장치 (여럿이면 가장 많이 진행된 것). 없으면 null.</summary>
    public PurificationDevice ActiveDevice
    {
        get
        {
            PurificationDevice best = null;
            foreach (var d in _devices)
                if (d != null && d.IsRunning && (best == null || d.Progress > best.Progress)) best = d;
            return best;
        }
    }

    /// <summary>정화가 진행 중인지 — 가동 중 위협 가중 등이 참조합니다.</summary>
    public bool IsPurifying => ActiveDevice != null;

    /// <summary>HUD에 보일 대표 장치 — 가동 중인 것 우선, 없으면 아무 장치. 없으면 null.</summary>
    public PurificationDevice DisplayDevice
    {
        get
        {
            var active = ActiveDevice;
            if (active != null) return active;
            foreach (var d in _devices) if (d != null) return d;
            return null;
        }
    }

    #endregion

    #region 생명주기

    void Start()
    {
        _hud = PurificationHUD.Create(this);
        _resultScreen = GameResultScreen.Create();

        // 매니저보다 먼저 깨어난 장치(씬 배치·로드 순서) 수거
        foreach (var d in FindObjectsByType<PurificationDevice>()) RegisterDevice(d);
    }

    void Update()
    {
        // 로드 중엔 직원·건물이 전부 지워졌다 다시 생기므로 판정하지 않는다
        bool loading = SaveManager.instance != null && SaveManager.instance.IsLoading;
        if (loading) { _wasLoading = true; return; }
        if (_wasLoading) { _wasLoading = false; ResetAfterLoad(); }

        if (_gameOver) return;

        float dt = Time.deltaTime; // 배속·일시정지는 timeScale로 반영된다
        for (int i = _devices.Count - 1; i >= 0; i--)
        {
            if (_devices[i] == null) { _devices.RemoveAt(i); continue; }
            _devices[i].Tick(dt);
            if (_gameOver) return; // 방금 정화 완료
        }

        if (Time.time >= _nextDefeatCheck)
        {
            _nextDefeatCheck = Time.time + DEFEAT_CHECK_INTERVAL;
            CheckDefeat();
        }
    }

    #endregion

    #region 장치 등록 — PurificationDevice가 호출

    public void RegisterDevice(PurificationDevice device)
    {
        if (device != null && !_devices.Contains(device)) _devices.Add(device);
    }

    /// <summary>장치가 사라짐 (파괴·철거·로드 정리).</summary>
    /// <param name="wasRunning">가동 중이던 장치인지</param>
    /// <param name="destroyedByDamage">체력이 0이 되어 파괴됐는지 (철거·정리는 false)</param>
    public void UnregisterDevice(PurificationDevice device, bool wasRunning, bool destroyedByDamage)
    {
        _devices.Remove(device);
        if (_gameOver || !wasRunning) return;
        if (SaveManager.instance != null && SaveManager.instance.IsLoading) return;

        if (destroyedByDamage)
        {
            NotificationManager.instance?.PushLetter(new Letter
            {
                title = "정화 장치 파괴",
                body = "완전 정화 장치가 파괴되어 정화 진행이 모두 사라졌습니다.\n장치를 다시 지어 처음부터 가동해야 합니다.",
                type = LetterType.Threat,
                pauseUntilRead = true
            });
        }

        if (showDebugLogs)
            Debug.Log($"[Purification] 가동 중인 장치 소실 ({(destroyedByDamage ? "파괴" : "철거")}) — 진행 초기화");

        if (!IsPurifying) GameMessageBus.Publish(new PurificationStateChangedMessage(false));
    }

    public void OnDeviceActivated(PurificationDevice device)
    {
        NotificationManager.instance?.PushLetter(new Letter
        {
            title = "정화 개시",
            body = $"완전 정화 장치가 가동을 시작했습니다.\n{PurificationDevice.PURIFICATION_DAYS}일 동안 장치와 직원을 지켜내십시오. " +
                   "장치가 파괴되면 진행이 사라집니다.",
            type = LetterType.Threat,
            pauseUntilRead = true
        });

        if (showDebugLogs) Debug.Log("[Purification] 정화 가동 시작");
        GameMessageBus.Publish(new PurificationStateChangedMessage(true));
    }

    public void OnDeviceCompleted(PurificationDevice device)
    {
        if (showDebugLogs) Debug.Log("[Purification] 정화 완료 — 승리");
        EndGame(true);
    }

    #endregion

    #region 승패

    private void CheckDefeat()
    {
        int survivors = CountSurvivors();
        if (survivors > 0) { _hadSurvivor = true; return; }
        if (!_hadSurvivor) return;

        if (showDebugLogs) Debug.Log("[Purification] 멀쩡한 직원 0명 — 패배");
        EndGame(false);
    }

    /// <summary>사망하지도, 침식 변이하지도 않은 직원 수 (변이한 직원은 목록에서 이미 빠져 있다)</summary>
    private static int CountSurvivors()
    {
        if (EmployeeManager.instance == null) return 0;

        int count = 0;
        foreach (var e in EmployeeManager.instance.AllEmployees)
            if (e != null && e.State != EmployeeState.Dead) count++;
        return count;
    }

    private void EndGame(bool victory)
    {
        if (_gameOver) return;
        _gameOver = true;

        TimeManager.instance?.ForcePause();

        int day = DayCycle.instance != null ? DayCycle.instance.Day : 0;
        string body = victory
            ? $"완전 정화 장치가 {PurificationDevice.PURIFICATION_DAYS}일간의 정화를 마쳤습니다.\n{day}일째 · 생존 직원 {CountSurvivors()}명"
            : $"남은 직원이 모두 쓰러지거나 침식되었습니다.\n{day}일째";

        _resultScreen?.Show(victory, body);
        GameMessageBus.Publish(new GameOverMessage(victory));
    }

    /// <summary>로드가 끝나면 판정 상태를 새 판 기준으로 되돌립니다.</summary>
    private void ResetAfterLoad()
    {
        _gameOver = false;
        _hadSurvivor = false;
        _resultScreen?.Hide();
        _devices.RemoveAll(d => d == null);

        // 가동 중인 장치를 불러왔으면 구독자(위협 가중 등)에게 다시 알린다
        GameMessageBus.Publish(new PurificationStateChangedMessage(IsPurifying));
    }

    #endregion
}
