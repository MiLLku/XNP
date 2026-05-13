using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 이벤트 데이터 (ScriptableObject)
/// 각 이벤트의 정보, 조건, 효과를 정의합니다.
/// </summary>
[CreateAssetMenu(fileName = "NewEvent", menuName = "EventSystem/Event Data")]
public class EventData : ScriptableObject
{
    [Header("기본 정보")]
    [Tooltip("이벤트 고유 ID")]
    public int eventId;

    [Tooltip("이벤트 제목")]
    public string title;

    [Tooltip("이벤트 설명")]
    [TextArea(3, 6)]
    public string description;

    [Tooltip("이벤트 아이콘")]
    public Sprite icon;

    [Header("분류")]
    [Tooltip("이벤트 카테고리")]
    public EventCategory category;

    [Header("발생 확률")]
    [Tooltip("발생 가중치 (높을수록 자주 등장)")]
    [Range(1, 100)]
    public int weight = 10;

    [Tooltip("한 게임에서 최대 발생 횟수 (0 = 무제한)")]
    public int maxOccurrences = 0;

    [Tooltip("같은 이벤트 재발생 쿨다운 (초)")]
    public float cooldown = 0f;

    [Header("지속형 이벤트 설정")]
    [Tooltip("지속형 이벤트인지 여부 (Persistent 카테고리용)")]
    public bool isPersistent = false;

    [Tooltip("최소 지속 시간 (초)")]
    public float minDuration = 60f;

    [Tooltip("최대 지속 시간 (초)")]
    public float maxDuration = 180f;

    [Header("발동 조건")]
    [Tooltip("모든 조건을 만족해야 발동 가능")]
    public List<EventCondition> conditions;

    [Header("발동 효과")]
    [Tooltip("이벤트 발동 시 적용되는 효과")]
    public List<EventEffect> effects;

    [Header("지속 효과 (지속형 이벤트용)")]
    [Tooltip("이벤트가 지속되는 동안 적용되는 효과")]
    public List<EventEffect> persistentEffects;

    [Header("종료 효과 (지속형 이벤트용)")]
    [Tooltip("지속형 이벤트가 끝날 때 적용되는 효과")]
    public List<EventEffect> endEffects;

    [Header("선택지 (선택사항)")]
    [Tooltip("비어있으면 자동 발동, 있으면 플레이어 선택 필요")]
    public List<EventChoice> choices;

    /// <summary>
    /// 이 이벤트가 선택지를 가지는지 여부
    /// </summary>
    public bool HasChoices => choices != null && choices.Count > 0;

    /// <summary>
    /// 랜덤 지속 시간 반환 (지속형 이벤트용)
    /// </summary>
    public float GetRandomDuration()
    {
        return Random.Range(minDuration, maxDuration);
    }

    /// <summary>
    /// 에디터용 검증
    /// </summary>
    private void OnValidate()
    {
        // 카테고리가 Persistent면 isPersistent 자동 활성화
        if (category == EventCategory.Persistent && !isPersistent)
        {
            isPersistent = true;
        }

        // 지속 시간 최소값 보장
        if (minDuration > maxDuration)
        {
            maxDuration = minDuration;
        }
    }
}
