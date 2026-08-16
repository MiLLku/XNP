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
public class ContaminationSphereBehavior : MonoBehaviour, IXenopsBehavior, IHarvestable
{
    #region 상수

    /// <summary>좌우 영향 범위 (타일 수)</summary>
    private const float HORIZONTAL_RANGE = 5f;

    /// <summary>직원에게 부여할 침식 수치</summary>
    private const float EROSION_AMOUNT = 2f;

    /// <summary>작업 속도 보너스 (1.0 = +100%, 0.05 = +5%)</summary>
    public const float WORK_SPEED_BONUS = 0.05f;

    /// <summary>범위 체크 간격 (초)</summary>
    private const float CHECK_INTERVAL = 0.5f;

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
    private readonly HashSet<Employee> _affectedEmployees = new HashSet<Employee>();

    private float _checkTimer = 0f;

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
        _checkTimer = 0f;
        Debug.Log($"[오염 구체] {xenops?.DisplayName} 활성화 — 침식 범위 ON");
    }

    public void OnDeactivated()
    {
        isActive = false;
        _activeInstances.Remove(this);
        RemoveAllErosion();
        Debug.Log($"[오염 구체] {xenops?.DisplayName} 비활성화 — 침식 제거");
    }

    public void UpdateBehavior()
    {
        if (!isActive) return;

        _checkTimer -= Time.deltaTime;
        if (_checkTimer <= 0f)
        {
            _checkTimer = CHECK_INTERVAL;
            UpdateErosionEffects();
        }
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
        RemoveAllErosion();
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

    /// <summary>
    /// 매 CHECK_INTERVAL마다 범위 내 직원을 갱신하여 침식 부여/회복을 처리합니다.
    /// </summary>
    private void UpdateErosionEffects()
    {
        if (EmployeeManager.instance == null) return;

        float myX = transform.position.x;
        var currentInRange = new HashSet<Employee>();

        // 범위 내 직원 수집
        foreach (var emp in EmployeeManager.instance.AllEmployees)
        {
            if (emp == null || emp.State == EmployeeState.Dead) continue;
            if (Mathf.Abs(emp.transform.position.x - myX) <= HORIZONTAL_RANGE)
            {
                currentInRange.Add(emp);
            }
        }

        // 새로 범위에 들어온 직원 → 침식 부여
        foreach (var emp in currentInRange)
        {
            if (!_affectedEmployees.Contains(emp))
            {
                emp.ErosionController?.AddErosion(
                    EROSION_AMOUNT, ErosionSource.CONTAMINATION, "오염 구체");
                Debug.Log($"[오염 구체] {emp.DisplayName} 침식 부여: {EROSION_AMOUNT}");
            }
        }

        // 범위를 벗어난 직원 → 이 구체가 준 침식만 거둬간다 (다른 출처의 침식은 유지)
        foreach (var emp in _affectedEmployees)
        {
            if (emp == null) continue;
            if (!currentInRange.Contains(emp))
            {
                emp.ErosionController?.RemoveErosionBySource(ErosionSource.CONTAMINATION);
                Debug.Log($"[오염 구체] {emp.DisplayName} 침식 회복");
            }
        }

        // 목록 갱신
        _affectedEmployees.Clear();
        foreach (var emp in currentInRange)
            _affectedEmployees.Add(emp);
    }

    /// <summary>비활성화/파괴 시 모든 영향받은 직원의 침식 수치를 제거합니다.</summary>
    private void RemoveAllErosion()
    {
        foreach (var emp in _affectedEmployees)
        {
            if (emp != null)
            {
                emp.ErosionController?.RemoveErosionBySource(ErosionSource.CONTAMINATION);
            }
        }
        _affectedEmployees.Clear();
    }

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
