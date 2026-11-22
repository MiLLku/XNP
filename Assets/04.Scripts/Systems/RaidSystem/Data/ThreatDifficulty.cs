using UnityEngine;

/// <summary>
/// 게임 난이도 — 습격 점수의 난이도 배수·플레이 시간 보정 (<see cref="ThreatScore"/>).
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

    [Header("습격 점수 = (직원 + 자원 + 건물 가치) × 난이도 배수 × 시간 보정 (임시 수치)")]
    [Tooltip("총 가치에 곱하는 난이도 배수")]
    [Min(0f)] public float valueMultiplier = 0.08f;

    [Tooltip("플레이 시간 보정 곡선 — (경과 일수, 배수) 점들, 사이는 선형 보간, 마지막 점 이후는 마지막 값")]
    public Vector2[] timeCurve = { new Vector2(0f, 0.5f), new Vector2(5f, 0.8f), new Vector2(12f, 1f) };

    [Header("연구 (임시 수치)")]
    [Tooltip("연구 속도 배율 — 모든 연구대 포인트에 곱합니다. 초보 1.0을 기준으로 (2026-11-22 사용자 결정).\n" +
             "실측: 1.0이면 연구원 1명·간이 연구대로 하루 약 270~500pt (직원 연구 속도에 따라).")]
    [Min(0.01f)] public float researchSpeedMultiplier = 1f;

    /// <summary>경과 일수(소수 포함)에 대한 시간 보정</summary>
    public float TimeMultiplier(float day)
    {
        if (timeCurve == null || timeCurve.Length == 0) return 1f;
        if (day <= timeCurve[0].x) return timeCurve[0].y;
        for (int i = 1; i < timeCurve.Length; i++)
            if (day <= timeCurve[i].x)
                return Mathf.Lerp(timeCurve[i - 1].y, timeCurve[i].y, Mathf.InverseLerp(timeCurve[i - 1].x, timeCurve[i].x, day));
        return timeCurve[timeCurve.Length - 1].y;
    }
}
