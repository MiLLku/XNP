using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using MessagePipe;

/// <summary>런 기록 저장 데이터 (구 세이브에는 없음 — null이면 처음부터)</summary>
[Serializable]
public class RunStatsSaveData
{
    public int deepestDepth;
    public int raidsSurvived;
    public int staffHired;
    public int staffDead;
    public bool ended;
}

/// <summary>한 런의 결과 — 결과 화면과 (나중의) 메타 진행 보상에 넘긴다</summary>
public struct RunResult
{
    public int daysSurvived;
    public int deepestDepth;
    public int raidsSurvived;
    public int staffHired;
    public int staffDead;
}

/// <summary>
/// 런 기록과 런 종료 — 직원이 모두 죽으면 게임을 멈추고 결과 화면(버틴 날·최대 깊이·막은 습격·채용·사망)을 띄운다.
///
/// 기록:
///   • 최대 깊이 = 시작 스폰 높이보다 직원이 내려간 가장 깊은 칸 수 (2초마다 확인)
///   • 막은 습격 = 직원이 살아 있는 채로 끝난 습격 수
///   • 채용 = EmployeeManager.RecordHire, 사망 = 사망 상태로 제거된 직원
/// 메타 진행(회차 보상)은 아직 없다 — <see cref="RunEnded"/>에 붙이면 된다.
///
/// 씬 배선 없이 스스로 생긴다 (씬 로드 후 없으면 생성).
/// </summary>
public class RunStatsManager : DestroySingleton<RunStatsManager>, ISaveModule
{
    private const float DEPTH_CHECK_INTERVAL = 2f;
    private const float END_CHECK_INTERVAL = 1f;

    /// <summary>런이 끝났을 때 (메타 진행 훅)</summary>
    public static event Action<RunResult> RunEnded;

    private RunStatsSaveData stats = new RunStatsSaveData();
    private float depthTimer, endTimer;
    private bool sawStaff;
    private IDisposable subscriptions;
    private GameObject resultScreen;

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
        if (FindAnyObjectByType<RunStatsManager>() == null)
            new GameObject("RunStatsManager").AddComponent<RunStatsManager>();
    }

    private void Start()
    {
        subscriptions = DisposableBag.Create(
            GameMessageBus.Subscribe<RaidCompletedMessage>(_ => { if (AliveStaff() > 0) stats.raidsSurvived++; }),
            GameMessageBus.Subscribe<EmployeeRemovedMessage>(m =>
            {
                if (m.employee != null && m.employee.State == EmployeeState.Dead) stats.staffDead++;
            }));
    }

    private void OnDestroy() => subscriptions?.Dispose();

    /// <summary>채용 기록 (EmployeeManager.RecordHire가 부름)</summary>
    public void OnHired() => stats.staffHired++;

    private static int AliveStaff()
    {
        var em = EmployeeManager.instance;
        if (em == null) return 0;
        int n = 0;
        foreach (var e in em.AllEmployees) if (e != null && e.State != EmployeeState.Dead) n++;
        return n;
    }

    private void Update()
    {
        var em = EmployeeManager.instance;
        if (em == null || stats.ended) return;
        if (SaveManager.instance != null && SaveManager.instance.IsLoading) return;

        depthTimer -= Time.unscaledDeltaTime;
        if (depthTimer <= 0f)
        {
            depthTimer = DEPTH_CHECK_INTERVAL;
            int startY = Mathf.FloorToInt(em.SpawnPoint.y);
            foreach (var e in em.AllEmployees)
                if (e != null) stats.deepestDepth = Mathf.Max(stats.deepestDepth, startY - Mathf.FloorToInt(e.transform.position.y));
        }

        endTimer -= Time.unscaledDeltaTime;
        if (endTimer > 0f) return;
        endTimer = END_CHECK_INTERVAL;

        int alive = AliveStaff();
        if (alive > 0) { sawStaff = true; return; }
        if (sawStaff) EndRun(); // 시작 직원이 생기기 전(첫 프레임)·불러오는 중에는 끝내지 않는다
    }

    /// <summary>런 종료 — 멈추고 결과 화면을 띄운다</summary>
    public void EndRun()
    {
        if (stats.ended) return;
        stats.ended = true;
        TimeManager.instance?.ForcePause();

        var result = new RunResult
        {
            daysSurvived  = DayCycle.instance != null ? DayCycle.instance.Day : 0,
            deepestDepth  = stats.deepestDepth,
            raidsSurvived = stats.raidsSurvived,
            staffHired    = stats.staffHired,
            staffDead     = stats.staffDead,
        };
        RunEnded?.Invoke(result);
        ShowResult(result);
    }

    private void ShowResult(RunResult r)
    {
        // 전용 캔버스를 맨 위에 — 기존 오버레이 캔버스는 반투명하게 그려져 어두운 막이 안 보였다
        resultScreen = new GameObject("RunResultScreen", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = resultScreen.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = resultScreen.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        dim.transform.SetParent(resultScreen.transform, false);
        var rt = (RectTransform)dim.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f); // 뒤를 막는 어두운 막

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(resultScreen.transform, false);
        var prt = (RectTransform)panel.transform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        RuntimeUI.SetupPanel(prt, 420f);

        var title = RuntimeUI.Text(panel.transform, "기지가 무너졌습니다", 28);
        title.alignment = TextAlignmentOptions.Center;
        RuntimeUI.Text(panel.transform, $"버틴 날: {r.daysSurvived}일", 20);
        RuntimeUI.Text(panel.transform, $"최대 깊이: {r.deepestDepth}칸", 20);
        RuntimeUI.Text(panel.transform, $"막아 낸 습격: {r.raidsSurvived}회", 20);
        RuntimeUI.Text(panel.transform, $"채용한 직원: {r.staffHired}명 · 잃은 직원: {r.staffDead}명", 20);

        var row = RuntimeUI.Row(panel.transform, 12);
        RuntimeUI.Btn(row, "메인 메뉴", 190, MainMenuController.GoToMenu, RuntimeUI.BTN);
        RuntimeUI.Btn(row, "새 게임", 190, NewGame, RuntimeUI.BTN_ON);
    }

    private static void NewGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    #region ISaveModule

    /// <summary>제놉스 도감(86) 다음</summary>
    public int SaveOrder => 87;

    public void Capture(SaveData data) => data.runStats = JsonUtility.FromJson<RunStatsSaveData>(JsonUtility.ToJson(stats));

    public void Restore(SaveData data)
    {
        stats = data.runStats ?? new RunStatsSaveData();
        stats.ended = false; // 끝난 런을 저장해 두었어도 불러오면 다시 판정
        sawStaff = false;
        if (resultScreen != null) Destroy(resultScreen);
    }

    public void PostRestore(SaveData data) { }

    #endregion
}
