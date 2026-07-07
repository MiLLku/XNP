using UnityEngine;

/// <summary>
/// 장비 보관소 건물 컴포넌트. 직원이 장비를 장착/해제하려면 이 건물로 이동해야 한다.
/// 장비 재고 자체는 EquipmentStorageManager의 전역 풀 — 이 건물은 '접근 지점' 역할.
/// </summary>
public class ArmoryStorage : MonoBehaviour, IBuildingFunction
{
    private bool buildingEnabled = true;

    public bool IsOperating => buildingEnabled;

    public void OnBuildingDisabled() => buildingEnabled = false;

    public void OnBuildingEnabled() => buildingEnabled = true;

    private void OnEnable()
    {
        EquipmentStorageManager.instance?.RegisterArmory(this);
    }

    private void Start()
    {
        // OnEnable 시점에 매니저가 아직 없을 수 있으므로 재시도
        EquipmentStorageManager.instance?.RegisterArmory(this);
    }

    private void OnDisable()
    {
        EquipmentStorageManager.instance?.UnregisterArmory(this);
    }
}
