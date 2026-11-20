using UnityEngine;

/// <summary>
/// 침식 호위병 — 근접 개체(GrapplerBehavior)에 붙여 원거리 침식체(침식 사수) 곁을 지키게 한다.
///
/// 이동·공격은 GrapplerBehavior + EnemyNavigator 그대로 쓰고, 이 컴포넌트는 지키기 자리(EnemyNavigator.GuardAnchor)만 옮긴다:
///   1. 가장 가까운 침식 사수를 호위 대상으로 삼는다 (chargeSearchRadius 안)
///   2. 지키기 자리 = 사수 곁, 사수가 노리는 쪽으로 followDistance만큼 → 그 자리 guardRadius 안에 들어온 직원만 막아선다
///   3. 호위 대상을 잃으면(전멸) 지키기를 풀어 가장 가까운 직원에게 돌진한다
/// 거점 습격 경비·미끼 유인처럼 다른 쪽이 지키기 자리를 정해 두었으면 손대지 않는다.
/// 벽을 부수지 않는 것은 데이터(canBreachBuildings·canDigTerrain = false)로 정한다.
/// </summary>
public class EscortGuard : MonoBehaviour
{
    [Tooltip("호위할 침식 사수를 찾는 반경 (칸)")]
    [Min(1f)] [SerializeField] private float chargeSearchRadius = 25f;

    [Tooltip("호위 대상과 벌려 두는 가로 거리 (칸) — 사수가 노리는 쪽에 선다")]
    [Min(0f)] [SerializeField] private float followDistance = 1.5f;

    [Tooltip("지키기 자리에서 이 반경 안으로 들어온 직원만 막아선다 (칸)")]
    [Min(0.5f)] [SerializeField] private float guardRadius = 4f;

    private const float RECHARGE_INTERVAL = 0.5f;

    private EnemyNavigator _nav;
    private ErosionShooterBehavior _charge;
    private float _timer;
    /// <summary>이 컴포넌트가 마지막으로 정한 자리 (null = 정한 적 없음/풀어 둠)</summary>
    private Vector2? _myAnchor;

    private void Update()
    {
        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = RECHARGE_INTERVAL;

        // 길찾기는 GrapplerBehavior가 Awake에서 붙인다
        if (_nav == null && (_nav = GetComponent<EnemyNavigator>()) == null) return;
        if (_nav.IsLured) return;
        // 다른 쪽(거점 경비 등)이 정한 지키기면 그대로 둔다
        if (_nav.GuardAnchor.HasValue && _nav.GuardAnchor != _myAnchor) { _myAnchor = null; return; }

        if (_charge == null || !_charge.IsActive) _charge = FindCharge();
        if (_charge == null)
        {
            // 호위 대상을 잃음 — 돌진
            if (_myAnchor.HasValue) _nav.GuardAnchor = null;
            _myAnchor = null;
            return;
        }

        Vector2 chargePos = _charge.transform.position;
        var chargeNav = _charge.GetComponent<EnemyNavigator>();
        Component aim = chargeNav != null ? (Component)chargeNav.TargetEmployee ?? chargeNav.TargetBuilding : null;
        float side = aim != null ? Mathf.Sign(aim.transform.position.x - chargePos.x) : Mathf.Sign(transform.position.x - chargePos.x);
        if (Mathf.Approximately(side, 0f)) side = 1f;

        _myAnchor = chargePos + new Vector2(side * followDistance, 0f);
        _nav.GuardAnchor = _myAnchor;
        _nav.GuardRadius = guardRadius;
    }

    /// <summary>가장 가까운 활성 침식 사수</summary>
    private ErosionShooterBehavior FindCharge()
    {
        ErosionShooterBehavior best = null;
        float bestDist = chargeSearchRadius;
        Vector2 myPos = transform.position;
        foreach (var s in FindObjectsByType<ErosionShooterBehavior>())
        {
            if (s == null || !s.IsActive) continue;
            float d = Vector2.Distance(myPos, s.transform.position);
            if (d <= bestDist) { bestDist = d; best = s; }
        }
        return best;
    }
}
