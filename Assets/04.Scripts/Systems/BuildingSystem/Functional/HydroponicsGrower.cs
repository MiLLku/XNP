using UnityEngine;

/// <summary>
/// 수경 재배기의 자동 재배 기능.
/// 전력을 공급받는 동안 일정 시간마다 작물을 자동 수확합니다.
///
/// 정전(PowerConsumer.IsPowered == false) 시 진행도 누적을 멈춰 재배가 **일시정지**되고,
/// 전력이 복구되면 진행도를 이어서 누적합니다(취소 아님).
/// 기반 파괴(Building.IsFunctional == false) 시에도 정지합니다.
/// </summary>
[RequireComponent(typeof(Building), typeof(PowerConsumer))]
public class HydroponicsGrower : MonoBehaviour, IBuildingFunction
{
    [Header("재배 설정")]
    [Tooltip("수확할 작물 아이템 (비어 있으면 로그만 출력).")]
    [SerializeField] private ItemData cropItem;
    [Tooltip("1회 수확량.")]
    [SerializeField] private int cropAmount = 1;
    [Tooltip("1주기 재배 시간(초).")]
    [SerializeField] private float growTime = 10f;

    private Building _building;
    private PowerConsumer _powerConsumer;
    private float _progress;

    /// <summary>현재 재배 진행도(0~1).</summary>
    public float Progress => _progress;

    /// <summary>전력을 받아 재배가 진행 중인지 여부.</summary>
    public bool IsOperating =>
        (_building == null || _building.IsFunctional) &&
        (_powerConsumer == null || _powerConsumer.IsPowered);

    void Awake()
    {
        _building = GetComponent<Building>();
        _powerConsumer = GetComponent<PowerConsumer>();
    }

    void Update()
    {
        if (growTime <= 0f) return;

        // 기반 파괴 시 정지
        if (_building != null && !_building.IsFunctional) return;

        // 정전 시 진행도 누적을 멈춤 → 재배 일시정지 (전력 복구 시 이어서 진행)
        if (_powerConsumer != null && !_powerConsumer.IsPowered) return;

        _progress += Time.deltaTime / growTime;
        if (_progress >= 1f)
        {
            _progress -= 1f;
            Harvest();
        }
    }

    private void Harvest()
    {
        if (cropItem != null && InventoryManager.instance != null)
        {
            InventoryManager.instance.AddItem(cropItem, cropAmount);
            Debug.Log($"[수경재배기] {cropItem.itemName} x{cropAmount} 수확!");
        }
        else
        {
            Debug.Log("[수경재배기] 작물 수확 (cropItem 미지정 — 인벤토리 추가 생략).");
        }
    }

    // ── IBuildingFunction ──────────────────────────────────────
    // 기반 파괴/복구 시 Building이 이 컴포넌트의 enabled를 토글하므로 별도 처리는 불필요합니다.
    public void OnBuildingDisabled() { }
    public void OnBuildingEnabled() { }
}
