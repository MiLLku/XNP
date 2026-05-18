using UnityEngine;

/// <summary>
/// 환경 간섭 제노프스 행동.
/// 주변 타일과 건물에 환경 효과를 적용합니다.
///
/// 예시:
///   - 토양을 비옥하게 하여 식물 성장 촉진 (이점)
///   - 금속 건물을 부식시킴 (해악)
///
/// 해석도를 올리려면 플레이어가 주기적으로 "관찰" 상호작용해야 합니다.
/// (Xenops.Interact() 호출)
/// </summary>
public class EnvironmentalBehavior : MonoBehaviour, IXenopsBehavior
{
    #region 필드

    private Xenops xenops;
    private XenopsData data;
    private bool isActive = false;

    /// <summary>환경 효과 적용 간격 (초)</summary>
    private const float ENVIRONMENT_TICK_INTERVAL = 5f;
    private float environmentTickTimer;

    #endregion

    #region IXenopsBehavior 구현

    public XenopsType BehaviorType => XenopsType.Environmental;
    public bool IsActive => isActive;

    public void OnActivated()
    {
        isActive = true;
        environmentTickTimer = 0f;

        Debug.Log($"[EnvironmentalBehavior] {xenops?.DisplayName} 환경 간섭 활성화");
    }

    public void OnDeactivated()
    {
        isActive = false;

        Debug.Log($"[EnvironmentalBehavior] {xenops?.DisplayName} 환경 간섭 비활성화");
    }

    public void UpdateBehavior()
    {
        if (!isActive) return;

        environmentTickTimer -= Time.deltaTime;
        if (environmentTickTimer <= 0f)
        {
            environmentTickTimer = ENVIRONMENT_TICK_INTERVAL;
            ApplyEnvironmentalEffect();
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

    #region 환경 효과

    void Update()
    {
        UpdateBehavior();
    }

    /// <summary>
    /// 주변 환경에 효과를 적용합니다.
    /// 효과 값은 XenopsEffectController가 해석도 기반으로 계산합니다.
    /// 여기서는 환경 특화 로직(타일 탐색, 범위 시각화 등)을 처리합니다.
    /// </summary>
    private void ApplyEnvironmentalEffect()
    {
        if (data == null) return;

        float radius = data.effectRadius;
        Vector3 center = transform.position;

        // 범위 내 타일 좌표 계산
        int minX = Mathf.FloorToInt(center.x - radius);
        int maxX = Mathf.CeilToInt(center.x + radius);
        int minY = Mathf.FloorToInt(center.y - radius);
        int maxY = Mathf.CeilToInt(center.y + radius);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Vector3 tileCenter = new Vector3(x + 0.5f, y + 0.5f, 0);
                if (Vector3.Distance(center, tileCenter) <= radius)
                {
                    // 타일 효과 적용 — MapGenerator와 연동
                    // TODO: 타일 성장 촉진, 자원 변환 등 구체적 로직
                }
            }
        }
    }

    #endregion
}
