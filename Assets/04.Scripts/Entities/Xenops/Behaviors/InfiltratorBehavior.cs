using UnityEngine;

/// <summary>
/// 잠입체 제노프스 행동.
/// 특정 건물에 잠입하여 생산에 간섭합니다.
///
/// 예시:
///   - 생산 속도 향상 (이점)
///   - 주기적으로 자원 소모 (해악)
///
/// 해석도를 올리려면 잠입 상태에서 플레이어가 "분석" 상호작용해야 합니다.
/// (Xenops.Interact() 호출)
/// </summary>
public class InfiltratorBehavior : MonoBehaviour, IXenopsBehavior
{
    #region 필드

    private Xenops xenops;
    private XenopsData data;
    private bool isActive = false;

    /// <summary>자원 소모/생산 타이머</summary>
    private float infiltrateTimer;

    #endregion

    #region IXenopsBehavior 구현

    public XenopsType BehaviorType => XenopsType.Infiltrator;
    public bool IsActive => isActive;

    public void OnActivated()
    {
        isActive = true;
        infiltrateTimer = 0f;

        Debug.Log($"[InfiltratorBehavior] {xenops?.DisplayName} 잠입 활성화");
    }

    public void OnDeactivated()
    {
        isActive = false;

        Debug.Log($"[InfiltratorBehavior] {xenops?.DisplayName} 잠입 비활성화");
    }

    public void UpdateBehavior()
    {
        if (!isActive) return;
        if (xenops == null || xenops.InfiltratedBuilding == null) return;

        infiltrateTimer -= Time.deltaTime;
        if (infiltrateTimer <= 0f)
        {
            infiltrateTimer = data != null ? data.infiltrateInterval : 30f;
            ProcessInfiltration();
        }
    }

    #endregion

    #region 초기화

    void Awake()
    {
        xenops = GetComponent<Xenops>();
    }

    void Start()
    {
        if (xenops != null)
        {
            data = xenops.Data;
        }
    }

    #endregion

    #region 잠입 처리

    void Update()
    {
        UpdateBehavior();
    }

    /// <summary>
    /// 잠입 간섭을 처리합니다.
    /// 자원 소모/생산 효과는 XenopsEffectController에서 처리하고,
    /// 여기서는 잠입 특화 로직을 처리합니다.
    /// </summary>
    private void ProcessInfiltration()
    {
        if (data == null || xenops == null) return;

        var building = xenops.InfiltratedBuilding;
        if (building == null)
        {
            // 건물이 파괴된 경우 잠입 해제
            xenops.Expel();
            return;
        }

        // ConsumeResource / ProduceResource 효과는
        // XenopsEffectController에서 해석도 기반으로 처리.
        // 여기서는 추가적인 잠입 특화 로직만 처리.

        Debug.Log($"[InfiltratorBehavior] {xenops.DisplayName} → {building.name} 잠입 간섭 tick");
    }

    #endregion
}
