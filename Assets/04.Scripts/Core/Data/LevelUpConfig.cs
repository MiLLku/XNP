using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 직원 레벨업 기준값(SO). 레벨은 작업이 아니라 <b>습격 전리품</b>으로만 오르고, 1레벨 = 스킬 포인트 1.
///
/// 흐름: 습격 적 처치 → 확률로 전리품 드롭 → 창고 운반 → 연구로 해금한 <b>단련장</b>을 지으면
///       단련장 창에서 직원을 골라 단련 예약 → 그 직원 전용 '단련' 작업(우선순위 설정 가능)으로 단련장에서 단련
///       → 전리품 소모, 레벨 +1. 진행도는 멈춰도 남음.
///       늘어난 스킬 포인트는 직원 관리창의 스킬 트리에서 찍습니다.
/// 참조 경로: EmployeeManager.LevelUpConfig.
/// </summary>
[CreateAssetMenu(fileName = "LevelUpConfig", menuName = "XNP/Level Up Config")]
public class LevelUpConfig : ScriptableObject
{
    [Header("전리품")]
    [Tooltip("레벨업에 쓰는 아이템 (습격 적이 떨굼)")]
    public ItemData growthItem;

    [Tooltip("습격으로 온 적 1마리가 죽을 때 전리품을 떨굴 확률 (0~1)")]
    [Range(0f, 1f)] public float raidDropChance = 0.35f;

    [Header("비용")]
    [Tooltip("레벨업 비용 표 — [0] = Lv1→2, [1] = Lv2→3 … 표보다 높은 레벨은 마지막 값을 씀")]
    public List<int> costByLevel = new List<int> { 1, 1, 2, 2, 3, 3, 4, 4, 5 };

    [Header("단련량 (작업 속도 1 기준 게임 초 — 기본 하루 1000초, 1시간 ≈ 42초)")]
    [Tooltip("Lv1→2 단련 시간")]
    [Min(1f)] public float trainingSecondsBase = 80f;

    [Tooltip("레벨이 1 오를 때마다 늘어나는 단련 시간")]
    [Min(0f)] public float trainingSecondsPerLevel = 20f;

    [Header("레벨업 보상 (스킬 포인트 1은 항상)")]
    [Tooltip("레벨마다 최대 체력 증가")]
    [Min(0)] public int healthPerLevel = 8;

    [Tooltip("이 레벨 간격마다 운반 용량 +1 (예: 3이면 Lv4·7·10…)")]
    [Min(1)] public int carryEveryLevels = 3;

    /// <summary>현재 레벨에서 다음 레벨로 가는 단련 시간(초)</summary>
    public float TrainingSecondsFor(int currentLevel) => trainingSecondsBase + trainingSecondsPerLevel * Mathf.Max(0, currentLevel - 1);

    /// <summary>현재 레벨에서 다음 레벨로 가는 비용</summary>
    public int CostFor(int currentLevel)
    {
        if (costByLevel == null || costByLevel.Count == 0) return 1;
        int i = Mathf.Clamp(currentLevel - 1, 0, costByLevel.Count - 1);
        return Mathf.Max(0, costByLevel[i]);
    }
}
