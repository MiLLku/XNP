using UnityEngine;

/// <summary>
/// 게임 디버그 테스터.
/// 개발 중 기능키(F1~F12)로 각종 디버그 기능을 사용할 수 있습니다.
///
/// 단축키:
///   F1=직원 스폰, F2=자원 지급, F3=이벤트 발생,
///   F5=빠른 저장, F9=빠른 로드,
///   F10=직원 상태 출력, F11=인벤토리 출력, F12=활성 이벤트 출력
/// </summary>
public class GameDebugTester : MonoBehaviour
{
    #region 필드

    [Header("테스트 데이터")]
    public EmployeeData testEmployeeData;
    public BuildingData testBuildingData;
    public ItemData testItemData;

    [Header("설정")]
    public Vector3 spawnPosition = new Vector3(50, 50, 0);
    public int initialResourceAmount = 100;

    #endregion

    #region 입력 처리

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) SpawnTestEmployee();
        if (Input.GetKeyDown(KeyCode.F2)) GiveResources();
        if (Input.GetKeyDown(KeyCode.F3)) TriggerEvent();
        if (Input.GetKeyDown(KeyCode.F5)) QuickSave();
        if (Input.GetKeyDown(KeyCode.F9)) QuickLoad();
        if (Input.GetKeyDown(KeyCode.F10)) PrintAllEmployeeStatus();
        if (Input.GetKeyDown(KeyCode.F11)) PrintInventory();
        if (Input.GetKeyDown(KeyCode.F12)) PrintActiveEvents();
    }

    #endregion

    #region 디버그 기능

    /// <summary>
    /// 테스트 직원을 스폰합니다 (F1).
    /// </summary>
    void SpawnTestEmployee()
    {
        if (EmployeeManager.instance == null || testEmployeeData == null)
        {
            Debug.LogError("[Debug] EmployeeManager 또는 testEmployeeData 없음");
            return;
        }

        var employee = EmployeeManager.instance.SpawnEmployee(testEmployeeData, spawnPosition);
        Debug.Log($"[Debug] 직원 스폰: {employee?.Data?.employeeName}");
    }

    /// <summary>
    /// 테스트 자원을 인벤토리에 추가합니다 (F2).
    /// </summary>
    void GiveResources()
    {
        if (InventoryManager.instance == null || testItemData == null)
        {
            Debug.LogError("[Debug] InventoryManager 또는 testItemData 없음");
            return;
        }

        InventoryManager.instance.AddItem(testItemData, initialResourceAmount);
        Debug.Log($"[Debug] 자원 지급: {testItemData.itemName} x{initialResourceAmount}");
    }

    /// <summary>
    /// 랜덤 이벤트를 강제 발생시킵니다 (F3).
    /// </summary>
    void TriggerEvent()
    {
        if (EventManager.instance == null)
        {
            Debug.LogError("[Debug] EventManager 없음");
            return;
        }

        EventManager.instance.TriggerRandomEvent();
    }

    /// <summary>
    /// 빠른 저장을 수행합니다 (F5).
    /// </summary>
    void QuickSave()
    {
        if (SaveManager.instance == null)
        {
            Debug.LogError("[Debug] SaveManager 없음");
            return;
        }

        bool success = SaveManager.instance.Save("QuickSave");
        Debug.Log($"[Debug] 빠른 저장: {(success ? "성공" : "실패")}");
    }

    /// <summary>
    /// 빠른 로드를 수행합니다 (F9).
    /// </summary>
    void QuickLoad()
    {
        if (SaveManager.instance == null)
        {
            Debug.LogError("[Debug] SaveManager 없음");
            return;
        }

        bool success = SaveManager.instance.Load("QuickSave");
        Debug.Log($"[Debug] 빠른 로드: {(success ? "성공" : "실패")}");
    }

    /// <summary>
    /// 모든 직원의 상태를 콘솔에 출력합니다 (F10).
    /// </summary>
    void PrintAllEmployeeStatus()
    {
        if (EmployeeManager.instance == null) return;

        Debug.Log("===== 직원 상태 =====");
        foreach (var emp in EmployeeManager.instance.AllEmployees)
        {
            Debug.Log($"[{emp.Data?.employeeName}] " +
                $"상태:{emp.State} " +
                $"HP:{emp.Stats.health}/{emp.Stats.maxHealth} " +
                $"멘탈:{emp.Stats.mental}/{emp.Stats.maxMental} " +
                $"배고픔:{emp.Needs.hunger:F1} " +
                $"피로:{emp.Needs.fatigue:F1}");
        }
    }

    /// <summary>
    /// 인벤토리 내용을 콘솔에 출력합니다 (F11).
    /// </summary>
    void PrintInventory()
    {
        if (InventoryManager.instance == null) return;

        Debug.Log("===== 인벤토리 =====");
        foreach (var kvp in InventoryManager.instance.globalInventory)
        {
            Debug.Log($"[{kvp.Key?.itemName}] x{kvp.Value}");
        }
    }

    /// <summary>
    /// 활성 이벤트 목록을 콘솔에 출력합니다 (F12).
    /// </summary>
    void PrintActiveEvents()
    {
        if (EventManager.instance == null) return;

        Debug.Log("===== 활성 이벤트 =====");
        var activeEvents = EventManager.instance.GetActivePersistentEvents();
        foreach (var evt in activeEvents)
        {
            Debug.Log($"[{evt.title}] 카테고리:{evt.category}");
        }
        Debug.Log($"다음 이벤트까지: {EventManager.instance.GetTimeUntilNextEvent():F0}초");
    }

    #endregion
}
