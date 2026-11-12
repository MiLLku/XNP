using System.Collections.Generic;
using UnityEngine;

/// <summary>조건이 오래 어긋났을 때 이 종에게 일어날 수 있는 사건</summary>
public enum NeutralEventType
{
    /// <summary>한동안 생산을 멈춘다</summary>
    ProductionHalt,
    /// <summary>주변 직원·방에 침식을 뿜는다</summary>
    ErosionBurst,
    /// <summary>방 밖으로 나가 한동안 떠돈다 (그동안 생산 없음)</summary>
    Wander,
    /// <summary>먹이를 한꺼번에 더 먹는다</summary>
    Overeat,
    /// <summary>둥지를 떠나 사라진다</summary>
    Escape,
    /// <summary>적으로 돌변한다 (hostileXenopsId로 바뀐다)</summary>
    TurnHostile,
    /// <summary>죽으면서 큰 침식을 뿜고 근처 직원 정신을 크게 깎는다</summary>
    DeathBurst,
}

/// <summary>
/// 중립 침식체 한 종 — 불길한 알에서 깨어나 격리된 방(둥지)에 산다.
/// 조건(온도·침식·먹이·밝기·밀집·소음)을 모두 맞추면 주기마다 생산물을 떨구고,
/// 하나라도 어긋난 시간이 길어질수록 <see cref="events"/> 중 하나가 일어날 확률이 오른다.
/// </summary>
[CreateAssetMenu(fileName = "NeutralSpecies", menuName = "XNP/Husbandry/Neutral Species")]
public class NeutralSpeciesData : ScriptableObject
{
    [Header("기본")]
    public int speciesId;
    public string displayName;
    [TextArea] public string description;
    [Tooltip("1 쉬움 · 2 중간 · 3 어려움 — 알을 떨군 적의 티어 풀에서 뽑힌다")]
    [Range(1, 3)] public int tier = 1;
    public Sprite sprite;
    public Color tint = Color.white;
    [Tooltip("부화하면 나오는 운반용 유체 아이템 (먹이통이 둥지 방으로 받아 풀어 준다)")]
    public ItemData hatchlingItem;

    [Header("부화")]
    [Tooltip("부화에 걸리는 시간(초) — 부화기 방이 이 종의 온도·침식 범위일 때만 진행")]
    [Min(1f)] public float incubateSeconds = 600f;

    [Header("조건")]
    public Vector2 temperatureRange = new Vector2(0f, 30f);
    public Vector2 erosionRange = new Vector2(0f, 60f);
    [Tooltip("먹이 (침식 사료·시체 등). 먹이통에서 하루 feedPerDay개를 먹는다")]
    public ItemData food;
    [Min(0f)] public float feedPerDay = 1f;
    [Tooltip("먹이 1개가 며칠분인지 (시체는 3일분)")]
    [Min(0.1f)] public float daysPerFood = 1f;
    [Tooltip("어두운 방(작업 조명 없음)이어야 한다")]
    public bool needsDark;
    [Tooltip("같은 방에 이 종이 몇 마리까지 괜찮은지")]
    [Min(1)] public int maxPerRoom = 3;
    [Tooltip("근처(noiseRadius 칸)에 제련소·용광로가 있으면 불안해한다")]
    public bool hatesNoise;

    [Header("생산")]
    public ItemData product;
    [Min(1)] public int productAmount = 1;
    [Tooltip("조건을 모두 맞춘 상태로 이만큼(초) 지나면 생산물 1회")]
    [Min(1f)] public float produceSeconds = 500f;

    [Header("사건 (조건 불충족이 길어질수록 확률↑)")]
    public List<NeutralEventType> events = new List<NeutralEventType>();
    [Tooltip("TurnHostile 때 바뀌는 적 제놉스 ID")]
    public int hostileXenopsId = 6101;
    [Tooltip("ErosionBurst·DeathBurst 침식량")]
    [Min(0f)] public float burstErosion = 15f;
}
