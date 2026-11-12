using UnityEngine;

/// <summary>
/// 게임 난이도 — 습격 위협 점수의 항목별 배율과 적응 보정 폭.
/// 위협 점수 = 기본 + Σ(항목 값 × 항목 배율) × 적응 보정 (<see cref="ThreatScore"/>).
///
/// 에셋은 Resources/Difficulty 아래에 두고 order 순으로 메인 메뉴에 나온다.
/// </summary>
[CreateAssetMenu(fileName = "Difficulty_", menuName = "XNP/난이도")]
public class ThreatDifficulty : ScriptableObject
{
    [Tooltip("메뉴 표시 이름")]
    public string displayName = "보통";

    [Tooltip("메뉴·세이브 순서 (0 = 초보, 1 = 보통, 2 = 어려움)")]
    public int order = 1;

    [Header("위협 점수 — 항목 배율 (임시 수치)")]
    [Tooltip("항목과 무관한 기본 점수")]
    [Min(0f)] public float baseScore = 6f;

    [Tooltip("경과 일수 1일당")]
    [Min(0f)] public float perDay = 4f;

    [Tooltip("기지 재산 1점당 (건물 건설 비용 + 창고 자원 수 + 장비 수 × 장비 가치)")]
    [Min(0f)] public float perWealth = 0.03f;

    [Tooltip("전투력 1점당 (직원별 무기 피해 + 방어구 피해 감소% + 전투 숙련 × 5)")]
    [Min(0f)] public float perCombat = 0.05f;

    [Tooltip("최대 깊이 단계 1단계당")]
    [Min(0f)] public float perDepthStage = 8f;

    [Tooltip("기지 폭 1칸당 (플레이어 건물의 좌우 끝 사이)")]
    [Min(0f)] public float perWidth = 0.05f;

    [Header("적응 보정 — 다음 습격 점수에 곱한다")]
    [Tooltip("직전 습격에서 직원이 죽었으면 이만큼 내림")]
    [Min(0f)] public float lossAdapt = 0.2f;

    [Tooltip("직전 습격을 사망 없이 막았으면 이만큼 올림")]
    [Min(0f)] public float winAdapt = 0.05f;

    [Range(0.1f, 1f)] public float adaptMin = 0.6f;
    [Min(1f)] public float adaptMax = 1.5f;
}
