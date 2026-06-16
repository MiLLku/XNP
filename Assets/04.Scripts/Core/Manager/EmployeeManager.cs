using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// 직원 중앙 관리 매니저.
/// 직원 생성, 스폰, 검색, 통계를 담당합니다.
///
/// 주요 기능:
///   - 초기 직원 스폰 (SpawnInitialEmployees)
///   - 런타임 직원 스폰/제거 (SpawnEmployee/RemoveEmployee)
///   - 직원 검색 (ID, 이름, 작업 타입, 상태별)
///   - 직원 통계 (GetStatistics)
/// </summary>
public class EmployeeManager : DestroySingleton<EmployeeManager>, ISaveModule
{
    #region 필드 및 설정

    [Header("직원 프리팹")]
    [Tooltip("사용할 직원 프리팹")]
    [SerializeField] private GameObject employeePrefab;

    [Header("초기 직원 설정")]
    [Tooltip("게임 시작 시 자동으로 생성할 직원들")]
    [SerializeField] private List<EmployeeSpawnData> initialEmployees = new List<EmployeeSpawnData>();

    [Header("직원 관리")]
    [Tooltip("현재 게임에 존재하는 모든 직원")]
    [SerializeField] private List<Employee> allEmployees = new List<Employee>();

    [Header("스폰 설정")]
    [Tooltip("직원들이 스폰될 위치 (자동 설정됨)")]
    [SerializeField] private Vector3 spawnPoint = new Vector3(105, 142, 0);

    [Tooltip("여러 직원 스폰 시 간격")]
    [SerializeField] private float spawnSpacing = 2f;

        [Header("디버그")]
    [SerializeField] private bool showDebugInfo = true;

    [Header("직원 생성 설정")]
    [Tooltip("무작위 직원 생성 규칙 (이름·헤어·특성 풀, 기본 스탯 등)")]
    [SerializeField] private EmployeeGenerationConfig generationConfig;

    #endregion

    #region 이벤트

    public delegate void EmployeeDelegate(Employee employee);
    public event EmployeeDelegate OnEmployeeSpawned;
    public event EmployeeDelegate OnEmployeeRemoved;

    #endregion

    #region 프로퍼티

    /// <summary>모든 직원 목록</summary>
    public List<Employee> AllEmployees => allEmployees;

    /// <summary>직원 수</summary>
    public int EmployeeCount => allEmployees.Count;

        /// <summary>스폰 지점</summary>
    public Vector3 SpawnPoint => spawnPoint;

    /// <summary>무작위 직원 생성 설정 (HiringOffice 등에서 참조)</summary>
    public EmployeeGenerationConfig GenerationConfig => generationConfig;

    #endregion

    #region 초기화

    protected override void Awake()
    {
        base.Awake();
    }

    /// <summary>
    /// 스폰 지점을 설정합니다 (MapGenerator에서 호출).
    /// </summary>
    /// <param name="point">스폰 월드 좌표</param>
    public void SetSpawnPoint(Vector3 point)
    {
        spawnPoint = point;
        if (showDebugInfo)
        {
            Debug.Log($"[EmployeeManager] 스폰 지점 설정: {point}");
        }
    }

    #endregion

    #region 직원 스폰

    /// <summary>
    /// 초기 직원들을 스폰합니다.
    /// Inspector에서 설정된 initialEmployees 목록을 사용합니다.
    /// </summary>
    public void SpawnInitialEmployees()
    {
        if (employeePrefab == null)
        {
            Debug.LogError("[EmployeeManager] Employee 프리팹이 설정되지 않았습니다!");
            return;
        }

        if (initialEmployees.Count == 0)
        {
            Debug.LogWarning("[EmployeeManager] 초기 직원 목록이 비어있습니다.");
            return;
        }

        for (int i = 0; i < initialEmployees.Count; i++)
        {
            var spawnData = initialEmployees[i];
            if (spawnData.employeeData == null)
            {
                Debug.LogWarning($"[EmployeeManager] 초기 직원 {i}의 EmployeeData가 null입니다.");
                continue;
            }

            Vector3 offset = new Vector3(i * spawnSpacing, 0, 0);
            Vector3 finalSpawnPos = spawnPoint + offset;

            SpawnEmployee(spawnData.employeeData, finalSpawnPos);
        }

        if (showDebugInfo)
        {
            Debug.Log($"[EmployeeManager] 초기 직원 {allEmployees.Count}명 스폰 완료");
        }
    }

    /// <summary>
    /// 특정 위치에 직원을 스폰합니다.
    /// </summary>
    /// <param name="employeeData">직원 템플릿 데이터</param>
    /// <param name="position">스폰 월드 좌표</param>
    /// <returns>생성된 직원 (실패 시 null)</returns>
    public Employee SpawnEmployee(EmployeeData employeeData, Vector3 position)
    {
        if (employeePrefab == null)
        {
            Debug.LogError("[EmployeeManager] Employee 프리팹이 설정되지 않았습니다!");
            return null;
        }

        if (employeeData == null)
        {
            Debug.LogError("[EmployeeManager] EmployeeData가 null입니다!");
            return null;
        }

        GameObject employeeObj = Instantiate(employeePrefab, position, Quaternion.identity);
        employeeObj.name = $"Employee_{employeeData.employeeName}_{allEmployees.Count}";

        Employee employee = employeeObj.GetComponent<Employee>();
        if (employee == null)
        {
            Debug.LogError("[EmployeeManager] 프리팹에 Employee 컴포넌트가 없습니다!");
            Destroy(employeeObj);
            return null;
        }

        employee.Initialize(employeeData);
        allEmployees.Add(employee);

        // 신규 직원에게 시작 식량 1개 지급 (인벤토리에 음식이 있으면 차감해 소지로 이전).
        // 채용 직후 굶주림 전까지 버틸 안전망 — 로드 복원 직원은 이 경로를 타지 않고
        // RestoreFromSaveData가 소지 식량을 복원한다.
        GrantInitialFood(employee);

        OnEmployeeSpawned?.Invoke(employee);

        if (showDebugInfo)
        {
            Debug.Log($"[EmployeeManager] '{employeeData.employeeName}' 스폰 완료: {position}");
        }

        return employee;
    }

    /// <summary>신규 직원에게 인벤토리의 음식 1개를 지급합니다 (있을 때만).</summary>
    private void GrantInitialFood(Employee employee)
    {
        if (InventoryManager.instance == null || employee == null) return;

        var work = employee.GetComponent<EmployeeWork>();
        if (work == null) return;

        ItemData food = InventoryManager.instance.TakeAnyFood(1);
        if (food != null && work.StoreFood(food, 1) && showDebugInfo)
            Debug.Log($"[EmployeeManager] '{employee.Data?.employeeName}'에게 시작 식량 지급: {food.itemName}");
    }

    /// <summary>
    /// 스폰 지점에 직원을 스폰합니다 (위치 자동 계산).
    /// </summary>
    /// <param name="employeeData">직원 템플릿 데이터</param>
    /// <returns>생성된 직원 (실패 시 null)</returns>
    public Employee SpawnEmployee(EmployeeData employeeData)
    {
        Vector3 offset = new Vector3(allEmployees.Count * spawnSpacing, 0, 0);
        return SpawnEmployee(employeeData, spawnPoint + offset);
    }

    #endregion

    #region 직원 제거

    /// <summary>
    /// 직원을 제거합니다.
    /// </summary>
    /// <param name="employee">제거할 직원</param>
    public void RemoveEmployee(Employee employee)
    {
        if (employee == null) return;

        if (allEmployees.Contains(employee))
        {
            allEmployees.Remove(employee);
            OnEmployeeRemoved?.Invoke(employee);

            if (showDebugInfo)
            {
                Debug.Log($"[EmployeeManager] '{employee.Data.employeeName}' 제거됨");
            }
        }

        Destroy(employee.gameObject);
    }

    /// <summary>
    /// 모든 직원을 제거합니다.
    /// </summary>
    public void RemoveAllEmployees()
    {
        foreach (var employee in allEmployees.ToList())
        {
            RemoveEmployee(employee);
        }
    }

    #endregion

    #region 직원 검색

    /// <summary>
    /// ID로 직원을 찾습니다.
    /// </summary>
    /// <param name="employeeId">검색할 직원 ID</param>
    /// <returns>일치하는 직원 (없으면 null)</returns>
    public Employee GetEmployeeById(int employeeId)
    {
        return allEmployees.FirstOrDefault(e => e.Data != null && e.Data.employeeID == employeeId);
    }

    /// <summary>
    /// 이름으로 직원을 찾습니다.
    /// </summary>
    /// <param name="name">검색할 직원 이름</param>
    /// <returns>일치하는 직원 (없으면 null)</returns>
    public Employee GetEmployeeByName(string name)
    {
        return allEmployees.FirstOrDefault(e => e.Data != null && e.Data.employeeName == name);
    }

    /// <summary>
    /// 특정 작업을 수행할 수 있는 직원 목록을 반환합니다.
    /// </summary>
    /// <param name="workType">작업 타입</param>
    /// <returns>수행 가능한 직원 목록</returns>
    public List<Employee> GetEmployeesCapableOf(WorkType workType)
    {
        return allEmployees.Where(e => e != null && e.CanPerformWork(workType)).ToList();
    }

    /// <summary>
    /// 유휴 상태인 직원 목록을 반환합니다.
    /// </summary>
    /// <returns>Idle 상태 직원 목록</returns>
    public List<Employee> GetIdleEmployees()
    {
        return allEmployees.Where(e => e != null && e.State == EmployeeState.Idle).ToList();
    }

    /// <summary>
    /// 작업 중인 직원 목록을 반환합니다.
    /// </summary>
    /// <returns>Working 상태 직원 목록</returns>
    public List<Employee> GetWorkingEmployees()
    {
        return allEmployees.Where(e => e != null && e.State == EmployeeState.Working).ToList();
    }

    #endregion

    #region 통계

    /// <summary>
    /// 직원 통계를 반환합니다.
    /// </summary>
    /// <returns>전체 직원 통계 데이터</returns>
    public EmployeeStatistics GetStatistics()
    {
        var stats = new EmployeeStatistics
        {
            totalEmployees = allEmployees.Count,
            idleEmployees = GetIdleEmployees().Count,
            workingEmployees = GetWorkingEmployees().Count,
        };

        if (allEmployees.Count > 0)
        {
            stats.averageHealth = allEmployees.Average(e => e.Stats.health);
            stats.averageMental = allEmployees.Average(e => e.Stats.mental);
            stats.averageHunger = allEmployees.Average(e => e.Needs.hunger);
            stats.averageFatigue = allEmployees.Average(e => e.Needs.fatigue);
        }

        return stats;
    }

    #endregion

    #region ISaveModule 구현

    public int SaveOrder => 50;

    public void Capture(SaveData data)
    {
        data.employees.Clear();
        foreach (var employee in allEmployees)
        {
            if (employee != null)
            {
                data.employees.Add(employee.CreateSaveData());
            }
        }
    }

    public void Restore(SaveData data)
    {
        if (data.employees == null) return;

        var db = GameDatabase.Instance;
        if (db == null)
        {
            Debug.LogError("[EmployeeManager] GameDatabase가 없어 직원 복원 불가");
            return;
        }

        allEmployees.Clear();

        foreach (var esd in data.employees)
        {
            EmployeeData empData = db.GetEmployeeData(esd.templateId);

            // 무작위 생성 직원: GameDatabase에 없음 → 저장된 스냅샷으로 런타임 재구성
            if (empData == null && esd.generated != null && esd.generated.isGenerated)
            {
                empData = RandomEmployeeGenerator.Rebuild(
                    esd.generated, esd.templateId, esd.abilities, generationConfig);

                if (empData != null && generationConfig == null &&
                    (esd.generated.traitNames.Count > 0 || !string.IsNullOrEmpty(esd.generated.hairSpriteName)))
                {
                    Debug.LogWarning("[EmployeeManager] GenerationConfig 미할당 — 무작위 직원의 특성/헤어 복원이 생략됩니다.");
                }
            }

            if (empData == null)
            {
                Debug.LogWarning($"[EmployeeManager] 직원 데이터 없음: ID {esd.templateId}");
                continue;
            }

            if (employeePrefab == null)
            {
                Debug.LogError("[EmployeeManager] Employee 프리팹이 설정되지 않았습니다!");
                continue;
            }

            Vector3 pos = new Vector3(esd.posX, esd.posY, 0);
            GameObject employeeObj = Instantiate(employeePrefab, pos, Quaternion.identity);

            Employee employee = employeeObj.GetComponent<Employee>();
            if (employee != null)
            {
                employee.Initialize(empData, esd.instanceId);
                employee.RestoreFromSaveData(esd);
                allEmployees.Add(employee);
            }
        }

        if (showDebugInfo)
        {
            Debug.Log($"[EmployeeManager] {allEmployees.Count}명 직원 복원 완료");
        }
    }

    public void PostRestore(SaveData data) { }

    #endregion

    #region 디버그

    /// <summary>
    /// 모든 직원의 상태를 콘솔에 출력합니다.
    /// </summary>
    [ContextMenu("Print All Employees")]
    public void PrintAllEmployees()
    {
        Debug.Log($"=== 직원 목록 ({allEmployees.Count}명) ===");
        foreach (var employee in allEmployees)
        {
            if (employee == null || employee.Data == null) continue;

            Debug.Log($"- {employee.Data.employeeName}: " +
                     $"상태={employee.State}, " +
                     $"체력={employee.Stats.health}/{employee.Stats.maxHealth}, " +
                     $"정신={employee.Stats.mental}/{employee.Stats.maxMental}");
        }
    }

    #endregion
}

/// <summary>
/// 초기 직원 스폰 데이터.
/// Inspector에서 게임 시작 시 생성할 직원을 설정합니다.
/// </summary>
[System.Serializable]
public class EmployeeSpawnData
{
    [Tooltip("스폰할 직원의 데이터")]
    public EmployeeData employeeData;

    [Tooltip("이 직원이 활성화되어 있는지 (체크 해제 시 스폰 안 됨)")]
    public bool isEnabled = true;
}

/// <summary>
/// 직원 통계 데이터.
/// 전체 직원의 상태 요약 정보를 담습니다.
/// </summary>
public class EmployeeStatistics
{
    /// <summary>전체 직원 수</summary>
    public int totalEmployees;

    /// <summary>유휴 직원 수</summary>
    public int idleEmployees;

    /// <summary>작업 중 직원 수</summary>
    public int workingEmployees;

    /// <summary>평균 체력</summary>
    public float averageHealth;

    /// <summary>평균 정신력</summary>
    public float averageMental;

    /// <summary>평균 배고픔</summary>
    public float averageHunger;

    /// <summary>평균 피로</summary>
    public float averageFatigue;
}
