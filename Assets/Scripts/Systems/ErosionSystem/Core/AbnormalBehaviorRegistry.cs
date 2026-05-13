using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 이상 행동 구현체 레지스트리.
/// AbnormalBehaviorType → IAbnormalBehavior 인스턴스를 관리합니다.
///
/// 사용법:
///   1. 새 이상 행동 클래스 작성 (AbnormalBehaviorBase 상속)
///   2. Initialize() 내에 Register(new YourBehavior()) 추가
///   3. AbnormalBehaviorType 열거형에 대응하는 값 추가
/// </summary>
public static class AbnormalBehaviorRegistry
{
    private static Dictionary<AbnormalBehaviorType, IAbnormalBehavior> _behaviors;
    private static bool _initialized;

    /// <summary>
    /// 레지스트리를 초기화하고 기본 이상 행동들을 등록합니다.
    /// ErosionManager.Awake() 에서 호출됩니다.
    /// </summary>
    public static void Initialize()
    {
        _behaviors = new Dictionary<AbnormalBehaviorType, IAbnormalBehavior>();
        _initialized = true;

        // ── 기본 제공 이상 행동 등록 ─────────────────────────────
        Register(new AbnormalBehaviorIgnoreCommand());
        Register(new AbnormalBehaviorRandomMove());

        // TODO: 아래에 새 이상 행동을 등록하세요
        // Register(new AbnormalBehaviorWorkStop());
        // Register(new AbnormalBehaviorFriendlyAttack());
        // Register(new AbnormalBehaviorIgnoreCommandEnhanced());
        // Register(new AbnormalBehaviorMoveTowardEnemy());
        // Register(new AbnormalBehaviorFriendlyAttackEnhanced());
        // Register(new AbnormalBehaviorFlee());
        // Register(new AbnormalBehaviorErosionTrailExplosion());

        Debug.Log($"[AbnormalBehaviorRegistry] 초기화 완료. 등록된 행동 수: {_behaviors.Count}");
    }

    /// <summary>
    /// 이상 행동 구현체를 등록합니다.
    /// 동일 타입이 이미 등록되어 있으면 덮어씁니다.
    /// </summary>
    public static void Register(IAbnormalBehavior behavior)
    {
        if (behavior == null) return;

        if (!_initialized)
        {
            _behaviors = new Dictionary<AbnormalBehaviorType, IAbnormalBehavior>();
            _initialized = true;
        }

        _behaviors[behavior.BehaviorType] = behavior;
    }

    /// <summary>
    /// 타입에 해당하는 이상 행동 구현체를 반환합니다.
    /// 등록되지 않은 타입이면 null을 반환합니다.
    /// </summary>
    public static IAbnormalBehavior Get(AbnormalBehaviorType type)
    {
        if (!_initialized || _behaviors == null) return null;
        _behaviors.TryGetValue(type, out var behavior);
        return behavior;
    }

    /// <summary>
    /// 등록된 타입 목록에서 실제로 등록된 타입만 필터링하여 반환합니다.
    /// EmployeeErosionController에서 사용 가능한 행동 목록을 구성할 때 사용합니다.
    /// </summary>
    public static List<AbnormalBehaviorType> FilterRegistered(List<AbnormalBehaviorType> types)
    {
        var result = new List<AbnormalBehaviorType>();
        if (types == null || !_initialized) return result;

        foreach (var type in types)
        {
            if (_behaviors.ContainsKey(type))
                result.Add(type);
        }
        return result;
    }
}
