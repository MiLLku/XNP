using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스포너 습격의 스포너 — "움직이지 않는 적 개체"(Hostile Xenops)라 직원의 기존 전투가 그대로 공격 대상으로 잡는다.
///
/// 흐름: 생길 때 무작위 패턴(길이 PATTERN_MIN~MAX, 예: A A B A A C)을 정하고, SPAWN_INTERVAL마다 다음 칸의 적을
///       곁에 내보낸다(끝나면 처음부터). 내보낸 적이 MAX_ALIVE 살아 있으면 타이머가 멈춘다. 부서지기 전까지 계속.
///       정화 수류탄은 체력 50% 피해. 패턴·위치·타이머는 저장된다(XenopsSaveData).
/// </summary>
[RequireComponent(typeof(Xenops))]
public class SpawnerStructure : MonoBehaviour, IXenopsBehavior, IPurifyTarget
{
    // shortcut: 임시 수치 — 밸런스 확정 시 데이터로 뺄 것
    private const float SPAWN_INTERVAL = 20f;
    private const int MAX_ALIVE = 8;
    private const int PATTERN_MIN = 4;
    private const int PATTERN_MAX = 8;

    private Xenops _xenops;

    /// <summary>내보낼 적 ID 순서 (반복)</summary>
    public List<int> Pattern { get; } = new List<int>();

    /// <summary>다음에 내보낼 패턴 칸</summary>
    public int Index { get; private set; }

    /// <summary>다음 스폰까지 남은 시간(초)</summary>
    public float Timer { get; private set; } = SPAWN_INTERVAL;

    public XenopsType BehaviorType => XenopsType.Hostile;
    public bool IsActive { get; private set; }
    public void OnActivated() => IsActive = true;
    public void OnDeactivated() => IsActive = false;
    public void UpdateBehavior() { }

    // Xenops가 스폰 때 그림을 XenopsData.icon으로, 색을 흰색으로 덮는다 — 프리팹 그림·색을 지킨다
    private SpriteRenderer _renderer;
    private Sprite _ownSprite;
    private Color _ownColor;

    private void Awake()
    {
        _xenops = GetComponent<Xenops>();
        _renderer = GetComponent<SpriteRenderer>();
        if (_renderer != null) { _ownSprite = _renderer.sprite; _ownColor = _renderer.color; }
    }

    /// <summary>후보 적 ID에서 무작위 패턴을 정한다 (생성 시 1회)</summary>
    public void RollPattern(IList<int> candidates)
    {
        Pattern.Clear();
        if (candidates == null || candidates.Count == 0) return;
        int n = Random.Range(PATTERN_MIN, PATTERN_MAX + 1);
        for (int i = 0; i < n; i++) Pattern.Add(candidates[Random.Range(0, candidates.Count)]);
        Index = 0;
        Timer = SPAWN_INTERVAL;
        Debug.Log($"[Spawner] 패턴 {string.Join(",", Pattern)}");
    }

    /// <summary>불러온 패턴·위치·타이머 (스포너가 아닌 세이브는 패턴이 비어 무시)</summary>
    public void RestoreState(List<int> pattern, int index, float timer)
    {
        if (pattern == null || pattern.Count == 0) return;
        Pattern.Clear();
        Pattern.AddRange(pattern);
        Index = Mathf.Clamp(index, 0, Pattern.Count - 1);
        Timer = timer;
    }

    /// <summary>정화 수류탄 — 체력 50% 피해</summary>
    public void HitByPurifyGrenade()
    {
        var h = GetComponent<XenopsHealth>();
        if (h != null) h.TakeDamage(h.MaxHealth * 0.5f, 1f);
        Debug.Log("[Spawner] 정화 수류탄 명중");
    }

    private void Update()
    {
        if (SaveManager.instance != null && SaveManager.instance.IsLoading) return;
        if (_renderer != null) { _renderer.sprite = _ownSprite; _renderer.color = _ownColor; }

        var raid = RaidManager.instance;
        if (raid == null || Pattern.Count == 0 || !raid.IsRaider(_xenops)) return;
        if (raid.SpawnerChildrenAlive >= MAX_ALIVE) return; // 상한 — 타이머도 멈춘다

        Timer -= Time.deltaTime;
        if (Timer > 0f) return;
        Timer = SPAWN_INTERVAL;
        raid.SpawnFromSpawner(Pattern[Index], transform.position + new Vector3(0.5f, 0.3f, 0f));
        Index = (Index + 1) % Pattern.Count;
    }
}
