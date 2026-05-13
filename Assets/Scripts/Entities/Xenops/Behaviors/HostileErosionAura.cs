using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 개체형(Hostile) 제놉스 공통 범위 침식 컴포넌트.
/// 모든 개체형 프리팹에 독립적으로 부착합니다.
///
/// - 0.5초 간격으로 aoeErosionRange 이내 직원을 탐색
/// - 범위 내 직원의 침식 수치에 aoeErosionPerSecond * CHECK_INTERVAL 을 누적
/// - 범위를 벗어나도 침식 수치는 자동 회복되지 않음 (누적형)
///   → 침식 자연 감소는 별도 시스템에서 처리 예정
/// </summary>
[RequireComponent(typeof(Xenops))]
public class HostileErosionAura : MonoBehaviour
{
    private const float CHECK_INTERVAL = 0.5f;

    private Xenops _xenops;
    private float  _aoeErosionPerSecond;
    private float  _aoeErosionRange;
    private float  _checkTimer;

    // ─────────────────────────────────────────
    #region 초기화

    private void Awake()
    {
        _xenops = GetComponent<Xenops>();
    }

    private void Start()
    {
        if (_xenops?.Data?.hostileStats == null) return;
        _aoeErosionPerSecond = _xenops.Data.hostileStats.aoeErosionPerSecond;
        _aoeErosionRange     = _xenops.Data.hostileStats.aoeErosionRange;
        _checkTimer          = CHECK_INTERVAL;
    }

    #endregion

    // ─────────────────────────────────────────
    #region 업데이트

    private void Update()
    {
        if (_aoeErosionPerSecond <= 0f || _aoeErosionRange <= 0f) return;

        _checkTimer -= Time.deltaTime;
        if (_checkTimer > 0f) return;
        _checkTimer = CHECK_INTERVAL;

        ApplyAuraToEmployeesInRange();
    }

    private void ApplyAuraToEmployeesInRange()
    {
        if (EmployeeManager.instance == null) return;

        float erosionThisTick = _aoeErosionPerSecond * CHECK_INTERVAL;
        Vector2 myPos = transform.position;

        foreach (var emp in EmployeeManager.instance.AllEmployees)
        {
            if (emp == null || emp.State == EmployeeState.Dead) continue;

            float dist = Vector2.Distance(myPos, emp.transform.position);
            if (dist > _aoeErosionRange) continue;

            // 방어구/특성 침식 무시: 합산값 >= 이 개체의 오라값이면 차단
            float armorIgnore = (emp.Equipment?.GetTotalErosionIgnore() ?? 0f)
                              + (emp.StatsController?.CachedErosionIgnoreBonus ?? 0f);
            if (armorIgnore >= _aoeErosionPerSecond) continue;

            // 침식 수치 누적 (SetErosion은 절댓값 세팅이므로 현재값을 읽어 더함)
            float newErosion = emp.ErosionLevel + erosionThisTick;
            emp.SetErosion(newErosion);

            // 회복 타이머 리셋: 이 프레임에 오라 노출됨을 알림
            emp.ErosionController?.MarkAuraExposure();
        }
    }

    #endregion
}
