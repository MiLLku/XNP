using UnityEngine;

/// <summary>
/// 식량 부족 경고 기준값(SO). 전역 인벤토리 음식 가용 총량 기준.
/// 평가·출력은 FoodShortageAlertEvaluator(코드)가 담당.
/// </summary>
[CreateAssetMenu(fileName = "FoodShortageAlertConfig", menuName = "NotificationSystem/Configs/Food Shortage Alert")]
public class FoodShortageAlertConfig : ScriptableObject
{
    [Tooltip("이 경고 활성화 여부")]
    public bool enabled = true;

    [Tooltip("이 수량 이하 → 주의(주황)")]
    public int cautionThreshold = 10;

    [Tooltip("이 수량 이하 → 위험(빨강)")]
    public int criticalThreshold = 3;
}
