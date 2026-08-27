using UnityEngine;

/// <summary>
/// 온도 시스템 밸런스 설정.
///
/// 온도는 <b>방마다 값 하나</b>이며, 방은 다음 1차 미분방정식으로 평형에 다가갑니다.
/// <code>
///   평형온도 = 주변온도 + 열원출력 / 누출계수
///   T += (평형온도 - T) × (1 - exp(-누출계수 / 열용량 × Δt))
/// </code>
/// 지수형으로 접근시키므로 틱 간격을 바꿔도 결과가 흔들리지 않고, 평형을 넘어서 튀지도 않습니다.
///
/// 방과 방 사이의 열 이동은 <b>문을 여닫는 순간의 혼합</b>으로만 일어납니다.
/// 벽을 통한 손실은 항상 주변온도(실외/지열) 쪽으로 갑니다.
/// </summary>
[CreateAssetMenu(fileName = "TemperatureConfig", menuName = "XNP/Config/Temperature Config")]
public class TemperatureConfig : ScriptableObject
{
    [Header("주변 온도")]
    [Tooltip("계절을 쓰지 않을 때의 실외 기준 온도(℃). 한파·폭염은 여기에 모디파이어로 더해집니다. 깊이 보정은 쓰지 않고 뜨거운 타일 배치로 대신합니다.")]
    public float outdoorTemperature = 20f;

    [Header("계절")]
    [Tooltip("계절에 따라 실외 온도가 바뀌게 할지. 끄면 outdoorTemperature 고정값을 씁니다.")]
    public bool useSeasons = true;

    [Tooltip("계절 하나의 길이(게임 일수)")]
    [Min(1)] public int daysPerSeason = 15;

    [Tooltip("봄 기준 온도(℃)")]
    public float springTemperature = 15f;

    [Tooltip("여름 기준 온도(℃) — 방열 장비가 필요해지는 구간")]
    public float summerTemperature = 30f;

    [Tooltip("가을 기준 온도(℃)")]
    public float autumnTemperature = 12f;

    [Tooltip("겨울 기준 온도(℃) — 난방과 방한 장비가 필요해지는 구간")]
    public float winterTemperature = -8f;

    [Tooltip("하루 안에서의 기온 진폭(℃). 새벽 3시가 가장 춥고 오후 3시가 가장 덥습니다.")]
    [Min(0f)] public float dailyTemperatureAmplitude = 5f;

    /// <summary>계절의 기준 온도를 반환합니다.</summary>
    public float GetSeasonTemperature(Season season)
    {
        switch (season)
        {
            case Season.Spring: return springTemperature;
            case Season.Summer: return summerTemperature;
            case Season.Autumn: return autumnTemperature;
            case Season.Winter: return winterTemperature;
            default:            return outdoorTemperature;
        }
    }

    [Header("열 계산")]
    [Tooltip("칸 하나가 갖는 열용량. 클수록 방이 천천히 데워지고 천천히 식습니다.")]
    public float heatCapacityPerCell = 1f;

    [Tooltip("접촉면 전도율 합에 곱하는 전역 배율. 이 값 하나로 '벽이 얼마나 잘 막느냐'를 통째로 조절합니다.")]
    public float conductanceScale = 0.05f;

    [Tooltip("온도 갱신 주기(초). 방 개수만큼만 계산하므로 짧아도 부담이 적습니다.")]
    public float tickInterval = 1f;

    [Header("문")]
    [Tooltip("문을 한 번 여닫을 때 섞이는 비율(0~1). DoorData에 개별값이 없을 때 사용합니다.")]
    [Range(0f, 1f)]
    public float defaultDoorExchangeRate = 0.05f;

    [Header("직원 쾌적 범위")]
    [Tooltip("보호 장비가 없을 때 견디는 하한(℃)")]
    public float comfortMin = 10f;

    [Tooltip("보호 장비가 없을 때 견디는 상한(℃)")]
    public float comfortMax = 25f;

    [Tooltip("방한/방열 레벨 1당 넓어지는 폭(℃). 레벨당 견디는 온도가 산술로 자명해집니다.")]
    public float degreesPerProtectionLevel = 5f;

    [Header("초과 구간")]
    [Tooltip("1단계 상한 — 쾌적 범위를 이만큼까지 벗어난 상태")]
    public float tier1MaxExcess = 10f;

    [Tooltip("2단계 상한 — 이 이상 벗어나면 3단계")]
    public float tier2MaxExcess = 20f;

    [Header("초과 구간 — 정신력 페널티")]
    [Tooltip("1단계(~10도 초과) 정신력 변화. 음수로 넣으세요.")]
    public float tier1MentalPenalty = -15f;

    [Tooltip("2단계(10~20도 초과) 정신력 변화")]
    public float tier2MentalPenalty = -25f;

    [Tooltip("3단계(20도 초과) 정신력 변화")]
    public float tier3MentalPenalty = -40f;

    [Header("초과 구간 — 체력 감소 (초당)")]
    [Tooltip("1단계에서는 보통 0으로 둡니다.")]
    public float tier1HealthLossPerSecond = 0f;

    public float tier2HealthLossPerSecond = 0.1f;
    public float tier3HealthLossPerSecond = 0.4f;

    [Header("판정")]
    [Tooltip("직원 온도 컨디션을 다시 판정하는 주기(초)")]
    public float conditionTickInterval = 1f;

    [Header("한계")]
    public float minTemperature = -60f;
    public float maxTemperature = 300f;
}
