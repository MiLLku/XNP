using UnityEngine;

/// <summary>
/// 개체형(Hostile) 제놉스 체력 컴포넌트.
/// 프리팹에 붙여두면 Start()에서 XenopsData를 읽어 자동 초기화합니다.
///
/// 피해 처리: TakeDamage(rawDamage) → 방어력 적용 → HP 차감 → 0 이하면 Die()
/// 사망 처리: XenopsManager.instance.RemoveXenops(xenops)
/// </summary>
[RequireComponent(typeof(Xenops))]
public class XenopsHealth : MonoBehaviour
{
    #region 직렬화 필드 (인스펙터 확인용)

    [Header("현재 상태 (읽기 전용)")]
    [SerializeField] private float currentHealth;
    [SerializeField] private float maxHealth;
    [SerializeField] private float defense;

    #endregion

    #region 내부 참조

    private Xenops _xenops;
    private bool _isDead = false;

    #endregion

    #region 프로퍼티

    public float CurrentHealth => currentHealth;
    public float MaxHealth     => maxHealth;
    /// <summary>체력 비율 (0~1)</summary>
    public float HealthRatio   => maxHealth > 0f ? currentHealth / maxHealth : 0f;
    public bool  IsDead        => _isDead;

    #endregion

    #region 초기화

    private void Awake()
    {
        _xenops = GetComponent<Xenops>();
    }

    private void Start()
    {
        // XenopsData가 초기화된 뒤 Start에서 스탯 읽기
        if (_xenops?.Data != null && _xenops.Data.xenopsType == XenopsType.Hostile)
        {
            var stats = _xenops.Data.hostileStats;
            Initialize(stats.maxHealth, stats.defense);
        }
        else
        {
            // Hostile이 아닌데 컴포넌트가 붙어 있을 경우 경고
            Debug.LogWarning(
                $"[XenopsHealth] {gameObject.name}: Hostile 타입이 아닌데 XenopsHealth가 부착되어 있습니다.");
        }
    }

    /// <summary>외부(Xenops.Initialize)에서도 직접 호출 가능.</summary>
    public void Initialize(float hp, float def)
    {
        maxHealth     = hp;
        currentHealth = hp;
        defense       = Mathf.Clamp01(def);
        _isDead       = false;
    }

    #endregion

    #region 피해 처리

    /// <summary>
    /// 피해를 적용합니다.
    /// rawDamage → 방어력 적용(감소) → HP 차감 → 0 이하 시 사망.
    /// </summary>
    public void TakeDamage(float rawDamage)
    {
        if (_isDead) return;

        float actual = rawDamage * (1f - defense);
        currentHealth -= actual;

        if (currentHealth <= 0f)
        {
            currentHealth = 0f;
            Die();
        }
    }

    /// <summary>체력을 직접 회복합니다 (양수만 허용).</summary>
    public void Heal(float amount)
    {
        if (_isDead) return;
        currentHealth = Mathf.Min(currentHealth + Mathf.Abs(amount), maxHealth);
    }

    #endregion

    #region 사망

    private void Die()
    {
        if (_isDead) return;
        _isDead = true;

        // 드랍 처리
        TryDrop();

        // XenopsManager에 제거 위임
        if (XenopsManager.instance != null)
            XenopsManager.instance.RemoveXenops(_xenops);
        else
            Destroy(gameObject);
    }

    private void TryDrop()
    {
        if (_xenops?.Data == null) return;
        var stats = _xenops.Data.hostileStats;
        if (stats.subdueDropItemId == 0) return;

        if (InventoryManager.instance != null && GameDatabase.Instance != null)
        {
            var item = GameDatabase.Instance.GetItemData(stats.subdueDropItemId);
            if (item != null)
                InventoryManager.instance.AddItem(item, stats.subdueDropAmount);
        }
    }

    #endregion
}
