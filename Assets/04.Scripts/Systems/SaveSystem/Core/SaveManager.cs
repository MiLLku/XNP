using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// 게임 저장/로드 오케스트레이터.
/// ISaveModule을 구현한 매니저들을 자동 발견하여 저장/로드를 위임합니다.
///
/// 아키텍처:
///   SaveManager (오케스트레이터)
///   ├─ ISaveModule 자동 발견 (FindObjectsOfType)
///   ├─ 저장: 각 모듈.Capture() → SaveData → JSON → File
///   ├─ 로드: File → JSON → SaveData → Migrate → Phase별 모듈.Restore() → PostRestore()
///   └─ RuntimeIDRegistry로 크로스레퍼런스 해결
///
/// 새 시스템 추가 시:
///   1. 매니저에 ISaveModule 구현
///   2. SaveOrder 지정 (로드 순서)
///   3. SaveData에 해당 필드 추가
///   → SaveManager 수정 불필요
/// </summary>
public class SaveManager : DontDestroySingleton<SaveManager>
{
    #region 상수

    /// <summary>현재 세이브 파일 버전 (단일 출처: SaveMigration — 버전 올릴 때 한 곳만 수정)</summary>
    public const int CURRENT_VERSION = SaveMigration.CURRENT_VERSION;

    #endregion

    #region 필드 및 설정

    [Header("설정")]
    [SerializeField] private GameDatabase gameDatabase;
    [SerializeField] private string saveFolder = "Saves";
    [SerializeField] private string saveExtension = ".json";

    [Header("자동 저장")]
    [SerializeField] private bool enableAutoSave = true;
    [SerializeField] private float autoSaveInterval = 300f;

    [Header("디버그")]
    [SerializeField] private bool showDebugLogs = true;

    /// <summary>런타임 ID 카운터</summary>
    private int _nextInstanceId = 1;

    /// <summary>로드 진행 중 여부 (이중 로드 방지)</summary>
    private bool _isLoading = false;

    /// <summary>로드 진행 중 여부</summary>
    public bool IsLoading => _isLoading;

    /// <summary>자동 저장 타이머</summary>
    private float _autoSaveTimer;

    /// <summary>로드된 세이브의 누적 플레이 타임</summary>
    private float _loadedPlayTime = 0f;

    /// <summary>로드 시점의 Time.time (경과 시간 계산용)</summary>
    private float _loadTimeReference = 0f;

    /// <summary>저장 경로</summary>
    private string SaveFolderPath => Path.Combine(Application.persistentDataPath, saveFolder);

    /// <summary>GameDatabase 접근용 프로퍼티</summary>
    public GameDatabase Database => gameDatabase;

    #endregion

    #region 생명주기

    private void Start()
    {
        if (gameDatabase != null)
        {
            gameDatabase.Initialize();
        }
        else
        {
            Debug.LogError("[SaveManager] GameDatabase가 설정되지 않았습니다!");
        }

        if (!Directory.Exists(SaveFolderPath))
        {
            Directory.CreateDirectory(SaveFolderPath);
        }

        if (showDebugLogs)
        {
            Debug.Log($"[SaveManager] 초기화 완료. 저장 경로: {SaveFolderPath}");
        }
    }

    private void Update()
    {
        if (enableAutoSave && !_isLoading)
        {
            _autoSaveTimer += Time.unscaledDeltaTime;
            if (_autoSaveTimer >= autoSaveInterval)
            {
                _autoSaveTimer = 0f;
                Save("AutoSave");
            }
        }
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 새 인스턴스 ID를 발급합니다.
    /// </summary>
    public int GenerateInstanceId()
    {
        return _nextInstanceId++;
    }

    /// <summary>
    /// 게임을 저장합니다.
    /// 모든 ISaveModule의 Capture()를 SaveOrder 순으로 호출합니다.
    /// </summary>
    public bool Save(string slotName)
    {
        try
        {
            var modules = GetSaveModules();
            SaveData data = new SaveData();

            // 메타 정보
            data.saveVersion = CURRENT_VERSION;
            data.saveName = slotName;
            data.saveTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            data.playTime = _loadedPlayTime + (Time.time - _loadTimeReference);
            data.nextInstanceId = _nextInstanceId;

            // 각 모듈에 캡처 위임
            foreach (var module in modules)
            {
                try
                {
                    module.Capture(data);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SaveManager] 모듈 캡처 실패 ({module.GetType().Name}): {e.Message}");
                }
            }

            string json = JsonUtility.ToJson(data, true);
            string path = GetSavePath(slotName);

            // 원자적 저장: 임시 파일에 쓴 후 이동 (부분 저장 방지)
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(tempPath, path);

            if (showDebugLogs)
            {
                Debug.Log($"[SaveManager] 저장 완료: {path} ({modules.Count}개 모듈)");
            }

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveManager] 저장 실패: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 게임을 로드합니다.
    /// 버전 마이그레이션 후 모든 ISaveModule의 Restore() → PostRestore()를 호출합니다.
    /// </summary>
    public bool Load(string slotName)
    {
        if (_isLoading)
        {
            Debug.LogWarning("[SaveManager] 이미 로드가 진행 중입니다. 중복 요청 무시.");
            return false;
        }

        string path = GetSavePath(slotName);

        if (!File.Exists(path))
        {
            Debug.LogError($"[SaveManager] 세이브 파일 없음: {path}");
            return false;
        }

        try
        {
            string json = File.ReadAllText(path);
            SaveData data = JsonUtility.FromJson<SaveData>(json);

            // 버전 마이그레이션
            if (data.saveVersion < CURRENT_VERSION)
            {
                data = SaveMigration.Migrate(data);
                if (data == null)
                {
                    Debug.LogError("[SaveManager] 마이그레이션 실패");
                    return false;
                }
            }
            else if (data.saveVersion > CURRENT_VERSION)
            {
                Debug.LogError($"[SaveManager] 지원하지 않는 세이브 버전: {data.saveVersion}");
                return false;
            }

            _isLoading = true;
            StartCoroutine(LoadSequence(data));
            return true;
        }
        catch (Exception e)
        {
            _isLoading = false;
            Debug.LogError($"[SaveManager] 로드 실패: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 세이브 파일을 삭제합니다.
    /// </summary>
    public bool Delete(string slotName)
    {
        string path = GetSavePath(slotName);

        if (!File.Exists(path))
        {
            Debug.LogWarning($"[SaveManager] 삭제할 파일 없음: {path}");
            return false;
        }

        try
        {
            File.Delete(path);
            if (showDebugLogs)
            {
                Debug.Log($"[SaveManager] 삭제 완료: {path}");
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveManager] 삭제 실패: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 세이브 파일이 존재하는지 확인합니다.
    /// </summary>
    public bool SaveExists(string slotName)
    {
        return File.Exists(GetSavePath(slotName));
    }

    /// <summary>
    /// 모든 세이브 슬롯 목록을 반환합니다.
    /// </summary>
    public List<SaveSlotInfo> GetAllSaveSlots()
    {
        var slots = new List<SaveSlotInfo>();

        if (!Directory.Exists(SaveFolderPath))
            return slots;

        var files = Directory.GetFiles(SaveFolderPath, $"*{saveExtension}");

        foreach (var file in files)
        {
            try
            {
                string json = File.ReadAllText(file);
                SaveData data = JsonUtility.FromJson<SaveData>(json);

                slots.Add(new SaveSlotInfo
                {
                    slotName = data.saveName,
                    saveTime = DateTimeOffset.FromUnixTimeSeconds(data.saveTimestamp).LocalDateTime,
                    playTime = data.playTime,
                    filePath = file
                });
            }
            catch
            {
                // 손상된 파일 무시
            }
        }

        return slots.OrderByDescending(s => s.saveTime).ToList();
    }

    #endregion

    #region 로드 시퀀스

    private IEnumerator LoadSequence(SaveData data)
    {
        if (showDebugLogs)
        {
            Debug.Log("[SaveManager] 로드 시퀀스 시작...");
        }

        List<ISaveModule> modules = null;

        try
        {
            modules = GetSaveModules();

            // 1. RuntimeIDRegistry 초기화
            if (RuntimeIDRegistry.instance != null)
            {
                RuntimeIDRegistry.instance.Clear();
            }

            // 2. 기존 상태 정리
            ClearCurrentState();
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveManager] 로드 초기화 실패: {e.Message}\n{e.StackTrace}");
            _isLoading = false;
            yield break;
        }

        yield return null;

        // 3. ID 카운터 복원 + 플레이 타임 복원
        _nextInstanceId = data.nextInstanceId;
        _loadedPlayTime = data.playTime;
        _loadTimeReference = Time.time;

        // 4. 각 모듈의 Restore() 호출 (SaveOrder 순)
        foreach (var module in modules)
        {
            try
            {
                module.Restore(data);

                if (showDebugLogs)
                {
                    Debug.Log($"[SaveManager] 모듈 복원 완료: {module.GetType().Name}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveManager] 모듈 복원 실패 ({module.GetType().Name}): {e.Message}\n{e.StackTrace}");
            }

            yield return null;
        }

        // 5. 모든 모듈의 PostRestore() 호출 (크로스레퍼런스 연결)
        foreach (var module in modules)
        {
            try
            {
                module.PostRestore(data);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveManager] PostRestore 실패 ({module.GetType().Name}): {e.Message}\n{e.StackTrace}");
            }
        }

        _isLoading = false;

        if (showDebugLogs)
        {
            Debug.Log($"[SaveManager] 로드 완료! ({modules.Count}개 모듈)");
        }
    }

    private void ClearCurrentState()
    {
        // 직원 제거
        if (EmployeeManager.instance != null)
        {
            EmployeeManager.instance.RemoveAllEmployees();
        }

        // 건물 제거
        var buildings = FindObjectsByType<Building>();
        foreach (var building in buildings)
        {
            Destroy(building.gameObject);
        }

        // 건설 현장 제거
        var sites = FindObjectsByType<ConstructionSite>();
        foreach (var site in sites)
        {
            Destroy(site.gameObject);
        }

        // 자연물 제거
        var trees = FindObjectsByType<ChoppableTree>();
        foreach (var tree in trees)
        {
            Destroy(tree.gameObject);
        }

        var plants = FindObjectsByType<HarvestablePlant>();
        foreach (var plant in plants)
        {
            Destroy(plant.gameObject);
        }

        // 침식 식물 제거
        var erosionPlants = FindObjectsByType<ErosionPlantEntity>();
        foreach (var ep in erosionPlants)
        {
            Destroy(ep.gameObject);
        }

        // 드롭 아이템 제거
        var droppedItems = FindObjectsByType<ClickableItem>();
        foreach (var item in droppedItems)
        {
            Destroy(item.gameObject);
        }

        // 인벤토리 초기화
        if (InventoryManager.instance != null)
        {
            InventoryManager.instance.ClearInventory();
        }

        // 작업 시스템 초기화
        if (WorkSystemManager.instance != null)
        {
            WorkSystemManager.instance.ClearAllOrders();
        }

        if (showDebugLogs)
        {
            Debug.Log("[SaveManager] 기존 상태 정리 완료");
        }
    }

    #endregion

    #region 모듈 탐색

    /// <summary>
    /// 씬에서 ISaveModule을 구현한 모든 MonoBehaviour를 찾아 SaveOrder 순으로 반환합니다.
    /// </summary>
    private List<ISaveModule> GetSaveModules()
    {
        var modules = FindObjectsByType<MonoBehaviour>()
            .OfType<ISaveModule>()
            .OrderBy(m => m.SaveOrder)
            .ToList();

        if (showDebugLogs)
        {
            var moduleNames = string.Join(", ", modules.Select(m => $"{m.GetType().Name}({m.SaveOrder})"));
            Debug.Log($"[SaveManager] 발견된 모듈: [{moduleNames}]");
        }

        return modules;
    }

    #endregion

    #region 유틸리티

    private string GetSavePath(string slotName)
    {
        return Path.Combine(SaveFolderPath, $"{slotName}{saveExtension}");
    }

    #endregion

    #region 에디터 테스트

    [ContextMenu("Test Save")]
    private void TestSave()
    {
        Save("TestSave");
    }

    [ContextMenu("Test Load")]
    private void TestLoad()
    {
        Load("TestSave");
    }

    #endregion
}

/// <summary>
/// 세이브 슬롯 정보
/// </summary>
[Serializable]
public class SaveSlotInfo
{
    public string slotName;
    public DateTime saveTime;
    public float playTime;
    public string filePath;
}
