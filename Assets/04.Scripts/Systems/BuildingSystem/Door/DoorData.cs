using UnityEngine;

/// <summary>
/// 문 건물 데이터 (ScriptableObject).
/// BuildingData를 확장하여 문 고유 동작 설정을 추가합니다.
///
/// 재질별 권장 수치:
///   흙문  : maxHealth= 30, openTime=0.8, closeTime=0.8
///   나무문 : maxHealth= 80, openTime=0.5, closeTime=0.5
///   돌문  : maxHealth=200, openTime=1.2, closeTime=1.0
///
/// 공통 설정:
///   size            = (1, 2)         — 가로 1, 세로 2 타일
///   blocksMovement  = false          — 경로탐색이 문 타일을 통과 가능으로 인식
///   requiresFloorSupport = true      — 문 아래에 바닥 타일 필요
/// </summary>
[CreateAssetMenu(fileName = "NewDoorData", menuName = "StampSystem/Door Data")]
public class DoorData : BuildingData
{
    [Header("문 동작 시간")]
    [Tooltip("문이 완전히 열리는 데 걸리는 시간(초)")]
    public float openTime = 0.5f;

    [Tooltip("문이 완전히 닫히는 데 걸리는 시간(초)")]
    public float closeTime = 0.5f;

    [Tooltip("마지막 통과 요청 이후 자동으로 닫히기까지의 대기 시간(초)")]
    public float closeDelay = 2f;

    [Header("온도")]
    [Tooltip("문을 한 번 여닫을 때 좌우 공간의 공기가 섞이는 비율(0~1). 0이면 TemperatureConfig의 기본값을 씁니다. 작은 방일수록 크게 흔들립니다.")]
    [Range(0f, 1f)]
    public float heatExchangeRate = 0f;

    [Header("문 스프라이트 (선택)")]
    [Tooltip("열린 상태 스프라이트. 없으면 알파값으로 폴백 처리됩니다.")]
    public Sprite openSprite;

    [Tooltip("닫힌 상태 스프라이트. 없으면 기본 스프라이트를 사용합니다.")]
    public Sprite closedSprite;
}
