using UnityEngine;

/// <summary>
/// 직원 침식 경고 기준값(SO). 숫자만 보관한다 — 평가·출력은 ErosionAlertEvaluator(코드)가 담당.
/// 분수는 완전 침식(200) 대비 비율. 0.5 = 50% = 침식 100, 0.7 = 70% = 침식 140.
/// </summary>
[CreateAssetMenu(fileName = "ErosionAlertConfig", menuName = "NotificationSystem/Configs/Erosion Alert")]
public class ErosionAlertConfig : ScriptableObject
{
    [Tooltip("이 경고 활성화 여부")]
    public bool enabled = true;

    [Tooltip("주의(주황) 임계 — 완전침식 대비 비율")]
    [Range(0f, 1f)] public float cautionFraction = 0.5f;

    [Tooltip("위험(빨강) 임계 — 완전침식 대비 비율")]
    [Range(0f, 1f)] public float criticalFraction = 0.7f;
}
