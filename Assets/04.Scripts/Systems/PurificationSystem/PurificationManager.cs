using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 완전 정화 장치(<see cref="PurificationDevice"/>)를 모아 흘리고, 14일 가동을 마치면 승리로 런을 끝내는 매니저.
///
/// 승리: 장치가 14일 가동을 마침 → <see cref="RunStatsManager.EndRun(bool)"/>(승리)로 기존 결과 화면을 띄운다.
/// 패배는 기존대로 RunStatsManager가 판정한다 (직원 전멸).
///
/// 장치의 경과 시간도 여기서 흘립니다 (기반 붕괴로 장치 스크립트가 꺼져도 카운트는 멈추지 않도록).
/// 진행도는 장치 건물의 extraData로 저장되므로 따로 세이브 모듈이 없습니다.
/// 씬 배선 없이 스스로 생기고, 상단 HUD도 코드로 만듭니다.
/// </summary>
public class PurificationManager : DestroySingleton<PurificationManager>
{
    [SerializeField] private bool showDebugLogs = true;

    private readonly List<PurificationDevice> _devices = new List<PurificationDevice>();
    private bool _wasLoading;

    #region 자동 생성

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        Ensure();
        SceneManager.sceneLoaded -= OnSceneLoaded; // 도메인 리로드 없이 다시 플레이해도 한 번만
        SceneManager.sceneLoaded += OnSceneLoaded; // '새 게임'으로 씬을 다시 불러와도 생기게
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Ensure();

    private static void Ensure()
    {
        if (FindAnyObjectByType<MapGenerator>() == null) return; // 게임 씬에서만 (메인 메뉴 제외)
        if (FindAnyObjectByType<PurificationManager>() == null)
            new GameObject("PurificationManager").AddComponent<PurificationManager>();
    }

    #endregion

    #region 프로퍼티

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

    /// <summary>정화가 진행 중인지 — 가동 중 위협 강화(PurificationThreat)가 참조합니다.</summary>
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

    private static bool RunEnded => RunStatsManager.instance != null && RunStatsManager.instance.HasEnded;

    #endregion

    #region 생명주기

    private void Start()
    {
        PurificationHUD.Create(this);

        // 매니저보다 먼저 깨어난 장치(씬 배치·로드 순서) 수거
        foreach (var d in FindObjectsByType<PurificationDevice>()) RegisterDevice(d);
    }

    private void Update()
    {
        // 로드 중엔 건물이 전부 지워졌다 다시 생기므로 흘리지 않는다
        bool loading = SaveManager.instance != null && SaveManager.instance.IsLoading;
        if (loading) { _wasLoading = true; return; }
        if (_wasLoading)
        {
            _wasLoading = false;
            _devices.RemoveAll(d => d == null);
            // 가동 중인 장치를 불러왔으면 구독자(이벤트·습격 간격)에게 다시 알린다
            GameMessageBus.Publish(new PurificationStateChangedMessage(IsPurifying));
        }

        if (RunEnded) return;

        float dt = Time.deltaTime; // 배속·일시정지는 timeScale로 반영된다
        for (int i = _devices.Count - 1; i >= 0; i--)
        {
            if (_devices[i] == null) { _devices.RemoveAt(i); continue; }
            _devices[i].Tick(dt);
            if (RunEnded) return; // 방금 정화 완료
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
        if (RunEnded || !wasRunning) return;
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
                   "가동하는 동안 습격이 잦아지고 날이 갈수록 강해집니다. 장치가 파괴되면 진행이 사라집니다.",
            type = LetterType.Threat,
            pauseUntilRead = true
        });

        if (showDebugLogs) Debug.Log("[Purification] 정화 가동 시작");
        GameMessageBus.Publish(new PurificationStateChangedMessage(true));
    }

    public void OnDeviceCompleted(PurificationDevice device)
    {
        if (showDebugLogs) Debug.Log("[Purification] 정화 완료 — 승리");
        GameMessageBus.Publish(new PurificationStateChangedMessage(false));
        RunStatsManager.instance?.EndRun(true);
    }

    #endregion

    #region 디버그 (F1 패널)

    /// <summary>대기 중인 장치를 가동 (없으면 false)</summary>
    public bool DebugActivate()
    {
        var d = DisplayDevice;
        return d != null && d.Activate();
    }

    /// <summary>가동 중인 장치의 진행을 하루 넘긴다 (없으면 false)</summary>
    public bool DebugSkipDay()
    {
        var d = ActiveDevice;
        if (d == null) return false;
        float day = DayCycle.instance != null ? DayCycle.instance.DayLengthInSeconds : 1000f;
        d.Tick(day);
        return true;
    }

    #endregion
}
