using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 오염 구체 제노프스 행동 (환경형 + 채광 가능).
///
/// 특성:
///   - 블록 1칸 크기의 환경형 제노프스
///   - 좌우 5칸 범위의 직원에게 침식 수치 2 부여 (범위 밖으로 나가면 즉시 회복)
///   - 좌우 5칸 범위의 작업대 / 연구대에 작업 속도 +5% 보너스 부여
///   - 채광 가능: 완료 시 아이템 드랍 + 주변 광역 피해
///
/// 범위 판정 기준: |target.x - xenops.x| <= HORIZONTAL_RANGE
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ContaminationSphereBehavior : MonoBehaviour, IXenopsBehavior, IHarvestable, IEntityErosionSource
{
    #region IEntityErosionSource

    /// <summary>
    /// 오염 구체는 <b>개체 발원지</b>입니다 — 좌우 범위에 들어오면 고정량이 붙고 벗어나면 돌아갑니다.
    /// 이 규칙은 제놉스 오라·침식 폭주와 동일하며, 판정은 EntityErosionField에 등록된 뒤
    /// 직원 쪽에서 일괄 처리됩니다(개체마다 직원을 훑던 코드는 제거되었습니다).
    /// </summary>
    public Vector2 EmitPosition => transform.position;

    public float EmitRadius => HORIZONTAL_RANGE;

    public float FixedErosionAmount => EROSION_AMOUNT;

    /// <summary>좌우 거리만 본다 — 세로로는 제한이 없다.</summary>
    public bool HorizontalOnly => true;

    public bool Covers(Vector2 worldPosition)
        => Mathf.Abs(worldPosition.x - transform.position.x) <= HORIZONTAL_RANGE;

    public bool IsEmitting => isActive && isActiveAndEnabled;

    public string ErosionSourceKey => ErosionSource.CONTAMINATION;

    public string ErosionSourceName => "오염 구체";

    #endregion

    #region 상수

    /// <summary>좌우 영향 범위 (타일 수)</summary>
    private const float HORIZONTAL_RANGE = 5f;

    /// <summary>직원에게 부여할 침식 수치</summary>
    private const float EROSION_AMOUNT = 2f;

    /// <summary>작업 속도 보너스 (1.0 = +100%, 0.05 = +5%)</summary>
    public const float WORK_SPEED_BONUS = 0.05f;

    #endregion

    #region 정적 — 작업 속도 보너스 조회용

    /// <summary>현재 씬에서 활성화된 ContaminationSphereBehavior 목록</summary>
    private static readonly List<ContaminationSphereBehavior> _activeInstances = new List<ContaminationSphereBehavior>();

    /// <summary>
    /// 지정 위치에서 좌우 5칸 이내에 있는 오염 구체의 작업 속도 보너스 합계를 반환합니다.
    /// ResearchWorkbench 등 작업 건물에서 호출합니다.
    /// </summary>
    public static float GetWorkSpeedBonusAt(Vector3 position)
    {
        float total = 0f;
        foreach (var instance in _activeInstances)
        {
            if (instance == null || !instance.isActive) continue;
            if (Mathf.Abs(instance.transform.position.x - position.x) <= HORIZONTAL_RANGE)
            {
                total += WORK_SPEED_BONUS;
            }
        }
        return total;
    }

    #endregion

    #region 채광 드랍 설정

    [Serializable]
    public class ContaminationDrop
    {
        [Tooltip("드랍할 아이템 ID")]
        public int itemId;

        [Tooltip("드랍 수량")]
        public int amount = 1;

        [Tooltip("드랍 확률 (0~1)")]
        [Range(0f, 1f)]
        public float dropChance = 0.5f;
    }

    #endregion

    #region 직렬화 필드

    [Header("채광 설정")]
    [Tooltip("채광에 필요한 시간 (초)")]
    [SerializeField] private float miningTime = 5f;

    [Tooltip("채광 완료 시 드랍할 아이템 목록")]
    [SerializeField] private List<ContaminationDrop> miningDrops = new List<ContaminationDrop>();

    [Header("폭발 피해")]
    [Tooltip("채광 완료 시 광역 피해 반경")]
    [SerializeField] private float explosionRadius = 4f;

    [Tooltip("채광 완료 시 체력 피해량")]
    [SerializeField] private float explosionDamage = 30f;

    [Tooltip("채광 완료 시 정신력 피해량 (0 = 없음)")]
    [SerializeField] private float explosionMentalDamage = 20f;

    #endregion

    #region 필드

    private Xenops xenops;
    private bool isActive = false;

    /// <summary>현재 이 오염 구체가 침식을 부여 중인 직원 목록</summary>
    // 채광 상태
    private bool _isMined = false;
    private bool _hasMiningOrder = false;

    #endregion

    #region IXenopsBehavior 구현

    public XenopsType BehaviorType => XenopsType.Environmental;
    public bool IsActive => isActive;

    public void OnActivated()
    {
        isActive = true;
        _activeInstances.Add(this);
        EntityErosionField.instance?.RegisterSource(this);
        Debug.Log($"[오염 구체] {xenops?.DisplayName} 활성화 — 침식 범위 ON");
    }

    public void OnDeactivated()
    {
        isActive = false;
        _activeInstances.Remove(this);
        EntityErosionField.instance?.UnregisterSource(this);
        Debug.Log($"[오염 구체] {xenops?.DisplayName} 비활성화 — 침식 제거");
    }

    public void UpdateBehavior()
    {
        if (!isActive) return;

        // 침식 부여·회수는 EntityErosionField에 등록된 개체 발원지로서 직원 쪽에서 처리된다.
        // 여기서 직접 직원을 훑지 않는다.
    }

    #endregion

    #region IHarvestable 구현

    public bool CanHarvest() => !_isMined;

    public void Harvest()
    {
        if (_isMined) return;
        _isMined = true;

        RollDrops();
        ApplyExplosionDamage();

        // 제노프스 제거
        if (XenopsManager.instance != null && xenops != null)
        {
            XenopsManager.instance.RemoveXenops(xenops);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public float GetHarvestTime() => miningTime;

    public WorkType GetHarvestType() => WorkType.Mining;

    #endregion

    #region 초기화

    void Awake()
    {
        xenops = GetComponent<Xenops>();
    }

    void OnDestroy()
    {
        _activeInstances.Remove(this);
        EntityErosionField.instance?.UnregisterSource(this);
    }

    void Update()
    {
        UpdateBehavior();
    }

    #endregion

    #region 클릭 — 채광 작업 등록

    private void OnMouseDown()
    {
        if (_isMined || _hasMiningOrder) return;
        if (WorkSystemManager.instance == null) return;

        WorkOrder order = WorkSystemManager.instance.CreateWorkOrder(
            "오염 구체 채광",
            WorkType.Mining,
            maxWorkers: 1,
            priority: 5
        );

        HarvestOrder harvestOrder = new HarvestOrder
        {
            target = this,
            position = transform.position,
            priority = 5
        };

        order.AddTarget(harvestOrder);

        _hasMiningOrder = true;

        order.OnCompleted += _ => _hasMiningOrder = false;
        order.OnCancelled += _ => _hasMiningOrder = false;

        Debug.Log($"[오염 구체] 채광 작업 등록 완료");
    }

    #endregion

    #region 침식 효과 관리

    #endregion

    #region 채광 — 드랍 및 폭발

    private void RollDrops()
    {
        if (miningDrops == null || miningDrops.Count == 0) return;
        if (InventoryManager.instance == null || GameDatabase.Instance == null) return;

        foreach (var drop in miningDrops)
        {
            if (UnityEngine.Random.value <= drop.dropChance)
            {
                ItemData itemData = GameDatabase.Instance.GetItemData(drop.itemId);
                if (itemData != null)
                {
                    InventoryManager.instance.AddItem(itemData, drop.amount);
                    Debug.Log($"[오염 구체] 드랍: {itemData.itemName} x{drop.amount}");
                }
            }
        }
    }

    private void ApplyExplosionDamage()
    {
        if (EmployeeManager.instance == null) return;

        Vector3 myPos = transform.position;

        foreach (var emp in EmployeeManager.instance.AllEmployees)
        {
            if (emp == null || emp.State == EmployeeState.Dead) continue;

            float dist = Vector2.Distance(
                new Vector2(emp.transform.position.x, emp.transform.position.y),
                new Vector2(myPos.x, myPos.y)
            );

            if (dist <= explosionRadius)
            {
                if (explosionDamage > 0f)
                    emp.TakeDamage((int)explosionDamage);

                if (explosionMentalDamage > 0f)
                    emp.ModifyMental(-(int)explosionMentalDamage);

                Debug.Log($"[오염 구체] 폭발 피해 — {emp.DisplayName}: HP -{explosionDamage}, 정신력 -{explosionMentalDamage}");
            }
        }
    }

    #endregion
}
