using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제노프스 데이터 템플릿 (ScriptableObject).
/// Unity 에디터에서 Create > StampSystem > Xenops Data 로 생성합니다.
///
/// 모든 제노프스는 최소 1개의 이로운 효과(benefits)와
/// 최소 1개의 해로운 효과(drawbacks)를 가져야 합니다.
/// </summary>
[CreateAssetMenu(fileName = "NewXenops", menuName = "StampSystem/Xenops Data")]
public class XenopsData : ScriptableObject
{
    #region 기본 정보

    [Header("기본 정보")]
    [Tooltip("제노프스 ID (GameIDRegistry.Xenops 범위: 6000~6999)")]
    public int xenopsID;

    [Tooltip("제노프스 이름")]
    public string xenopsName;

    [TextArea(2, 4)]
    [Tooltip("제노프스 설명")]
    public string description;

    [Tooltip("아이콘")]
    public Sprite icon;

    [Tooltip("제노프스 타입")]
    public XenopsType xenopsType;

    [Tooltip("난이도 등급 (F~S)")]
    public XenopsGrade grade = XenopsGrade.D;

    [Tooltip("프리팹 (타입별 Behavior 컴포넌트가 부착된 상태)")]
    public GameObject prefab;

    #endregion

    #region 효과

    [Header("효과")]
    [Tooltip("이로운 효과 목록 (최소 1개)")]
    public List<XenopsEffect> benefits = new List<XenopsEffect>();

    [Tooltip("해로운 효과 목록 (최소 1개)")]
    public List<XenopsEffect> drawbacks = new List<XenopsEffect>();

    #endregion

    #region 해석 시스템

    [Header("해석 시스템")]
    [Tooltip("최대 해석도 레벨")]
    [Min(1)]
    public int maxInterpretationLevel = 10;

    [Tooltip("레벨 1→2에 필요한 기본 경험치")]
    [Min(1)]
    public int baseExpRequired = 50;

    [Tooltip("상호작용 1회당 획득 경험치")]
    [Min(1)]
    public int expPerInteraction = 10;

    #endregion

    #region 장비형 설정

    [Header("장비형 설정 (Equipment 전용)")]
    [Tooltip("장비 슬롯 (장비형 전용)")]
    public EquipmentSlot equipmentSlot;

    #endregion

    #region 환경/잠입체 설정

    [Header("환경/잠입체 설정")]
    [Tooltip("효과 영향 범위 (환경형)")]
    [Min(0)]
    public float effectRadius = 3f;

    [Tooltip("자원 소모/생산 간격 — 초 (잠입체)")]
    [Min(0.1f)]
    public float infiltrateInterval = 30f;

    #endregion

    #region 개체형 전투 스탯

    [Header("개체형 전투 스탯 (Hostile 전용)")]
    [Tooltip("이동속도·체력·방어력·공격 스탯 및 범위 침식 설정.\nxenopsType이 Hostile일 때만 사용됩니다.")]
    public HostileCombatStats hostileStats = new HostileCombatStats();

    #endregion

    #region 에디터 검증

    private void OnValidate()
    {
        if (!GameIDRegistry.Xenops.IsValid(xenopsID) && xenopsID != 0)
        {
            Debug.LogWarning(
                $"[XenopsData] {xenopsName}: ID {xenopsID}는 Xenops 범위(6000~6999)에 속하지 않습니다!");
        }

        // Hostile 타입(전투 개체)은 직원 장착/연구 대상이 아니므로 benefits/drawbacks 불필요
        if (xenopsType != XenopsType.Hostile)
        {
            if (benefits.Count == 0)
                Debug.LogWarning($"[XenopsData] {xenopsName}: 이로운 효과가 없습니다! 최소 1개 필요.");

            if (drawbacks.Count == 0)
                Debug.LogWarning($"[XenopsData] {xenopsName}: 해로운 효과가 없습니다! 최소 1개 필요.");
        }
    }

    #endregion
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 개체형(Hostile) 제놉스 전투 스탯.
/// XenopsData.hostileStats 필드로 사용됩니다.
/// </summary>
[System.Serializable]
public class HostileCombatStats
{
    [Header("이동")]
    [Tooltip("이동 속도 (units/second)")]
    [Min(0)] public float moveSpeed = 2f;

    [Header("체력 / 방어")]
    [Tooltip("최대 체력")]
    [Min(1)] public float maxHealth = 100f;

    [Tooltip("방어력 (0~1, 받는 피해 감소율. 0.3 = 30% 감소)")]
    [Range(0f, 1f)] public float defense = 0f;

    [Header("공격")]
    [Tooltip("공격 속도 (회/초)")]
    [Min(0.01f)] public float attackSpeed = 1f;

    [Tooltip("공격력 (1회 체력 피해량)")]
    [Min(0)] public float attackDamage = 10f;

    [Tooltip("공격 사거리 (블럭 수)")]
    [Min(0)] public float attackRange = 1f;

    [Tooltip("크기 (블럭 수)")]
    [Min(1)] public int size = 1;

    [Header("범위 침식 (모든 개체형 공통)")]
    [Tooltip("주변 직원에게 초당 적용되는 침식량")]
    [Min(0)] public float aoeErosionPerSecond = 2f;

    [Tooltip("범위 침식 반경 (블럭 수)")]
    [Min(0)] public float aoeErosionRange = 3f;

    [Header("드랍")]
    [Tooltip("제압/사망 시 드랍 아이템 ID (0 = 없음)")]
    public int subdueDropItemId = 0;

    [Tooltip("드랍 수량")]
    [Min(0)] public int subdueDropAmount = 1;
}
