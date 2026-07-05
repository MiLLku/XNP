using UnityEngine;

/// <summary>
/// 직원 재미(사기) 저하 경고 기준값(SO). 숫자만 보관 — 평가·출력은 FunAlertEvaluator(코드)가 담당.
/// </summary>
[CreateAssetMenu(fileName = "FunAlertConfig", menuName = "NotificationSystem/Configs/Fun Alert")]
public class FunAlertConfig : ScriptableObject
{
    [Tooltip("이 경고 활성화 여부")]
    public bool enabled = true;

    [Tooltip("재미가 이 수치 미만 → 주의(주황). FunConfig.vulnerableThreshold와 맞추는 것을 권장")]
    [Range(0f, 100f)] public float cautionThreshold = 30f;

    [Tooltip("재미가 이 수치 미만 → 위험(빨강). FunConfig.severeVulnerableThreshold와 맞추는 것을 권장")]
    [Range(0f, 100f)] public float criticalThreshold = 10f;
}
