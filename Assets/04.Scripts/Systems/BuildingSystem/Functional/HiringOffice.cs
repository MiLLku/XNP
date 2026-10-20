using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 직원 채용 건물 컴포넌트 — 몇 번이고 쓸 수 있습니다.
/// 건물을 클릭하면 HiringPanel을 열어 오늘의 무작위 후보 3명을 보여주고, 1명을 고르면 맵에 스폰됩니다.
/// 채용은 게임 하루 1명(EmployeeManager.CanHireToday — 사무소 수와 무관한 전역 제한)이고,
/// 후보는 날이 바뀌면 새로 뽑힙니다. 채용 직원은 기본 옷을 보관소에서 꺼내 입으므로 옷을 미리 만들어 둬야 합니다.
///
/// 저장 위치: Assets/Scripts/Systems/BuildingSystem/Functional/HiringOffice.cs
/// </summary>
[RequireComponent(typeof(Building))]
public class HiringOffice : MonoBehaviour, IBuildingFunction
{
    #region 필드 및 설정

    [Header("스폰 설정")]
    [Tooltip("건물 위치 기준 채용된 직원의 스폰 위치 오프셋")]
    [SerializeField] private Vector3 spawnOffset = new Vector3(3f, 0f, 0f);

    [Header("부속 건물 (같은 방 안에 있어야 인정 — 실외면 효과 없음)")]
    [Tooltip("작업 수치 최대치를 올리는 부속 건물 ID — 서로 다른 건물이어야 하고, 종류마다 1개만 인정")]
    [SerializeField] private int[] statCapAddonIds = { 4003, 4004, 4005 };
    [Tooltip("부속 건물 1종당 작업 수치 최대치 증가")]
    [SerializeField] private int statCapPerAddon = 10;
    [Tooltip("다시 뽑기 부속 건물 ID — 같은 건물이 여러 개면 개수만큼 하루 다시 뽑기 +1")]
    [SerializeField] private int rerollAddonId = 4006;
    [SerializeField] private int maxRerollAddons = 3;

    // 런타임 상태
    private List<EmployeeData> _candidates;
    private int _candidatesDay = -1;
    private Building _building;
    private SpriteRenderer _sprite;

    #endregion

    #region 프로퍼티

    /// <summary>오늘은 더 채용할 수 없는지 (하루 채용 한도를 채움)</summary>
    public bool IsUsed => EmployeeManager.instance != null && !EmployeeManager.instance.CanHireToday;

    /// <summary>IBuildingFunction: 현재 동작 중 여부</summary>
    public bool IsOperating => false;

    #endregion

    #region 초기화

    private void Awake()
    {
        _building = GetComponent<Building>();
        _sprite = GetComponent<SpriteRenderer>();
    }

    private void Start()
    {
        FitColliderToSprite();
    }

    /// <summary>오늘 채용을 마쳤으면 반투명으로 보여 준다 (날이 바뀌면 다시 불투명)</summary>
    private void Update()
    {
        if (_sprite == null) return;
        float alpha = IsUsed ? 0.5f : 1f;
        if (!Mathf.Approximately(_sprite.color.a, alpha))
        {
            Color c = _sprite.color;
            c.a = alpha;
            _sprite.color = c;
        }
    }

    /// <summary>
    /// SpriteRenderer의 스프라이트 경계에 맞게 BoxCollider2D를 자동으로 조정합니다.
    /// 스프라이트 피벗이 (0,0) 기준이므로 offset = bounds.center로 보정합니다.
    /// </summary>
    private void FitColliderToSprite()
    {
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        BoxCollider2D col = GetComponent<BoxCollider2D>();

        if (sr == null || col == null || sr.sprite == null)
        {
            Debug.LogWarning("[HiringOffice] 스프라이트 또는 콜라이더가 없어 자동 맞춤을 건너뜁니다.");
            return;
        }

        // sprite.bounds는 로컬 공간 기준 (Transform scale 적용 전)
        // → BoxCollider2D의 size/offset도 로컬 공간이므로 직접 대입하면 됨
        col.size   = sr.sprite.bounds.size;
        col.offset = sr.sprite.bounds.center;

        Debug.Log($"[HiringOffice] 콜라이더 자동 맞춤: size={col.size}, offset={col.offset}");
    }

    /// <summary>
    /// RandomEmployeeGenerator를 통해 무작위 직원 후보 3명을 런타임 생성합니다.
    /// 생성된 EmployeeData 인스턴스는 MarkUsed() 호출 시 미선택 분이 자동 해제됩니다.
    /// </summary>
    private void GenerateCandidates()
    {
        ReleaseCandidates(null);
        _candidates = new List<EmployeeData>();
        _candidatesDay = DayCycle.instance != null ? DayCycle.instance.Day : 0;

        EmployeeGenerationConfig config = EmployeeManager.instance?.GenerationConfig;
        if (config == null)
        {
            Debug.LogError("[HiringOffice] EmployeeManager에 GenerationConfig가 설정되지 않았습니다. EmployeeManager Inspector에서 할당해주세요.");
            return;
        }

        const int CANDIDATE_COUNT = 3;
        for (int i = 0; i < CANDIDATE_COUNT; i++)
        {
            EmployeeData data = RandomEmployeeGenerator.Generate(config, WorkStatCap);
            if (data != null)
                _candidates.Add(data);
        }

        Debug.Log($"[HiringOffice] 무작위 채용 후보 {_candidates.Count}명 생성 완료.");
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 오늘의 채용 후보 목록을 반환합니다. (HiringPanel에서 사용) 날이 바뀌었으면 새로 뽑습니다.
    /// </summary>
    public List<EmployeeData> GetCandidates()
    {
        int today = DayCycle.instance != null ? DayCycle.instance.Day : 0;
        if (_candidates == null || _candidates.Count == 0 || _candidatesDay != today)
            GenerateCandidates();
        return _candidates ?? new List<EmployeeData>();
    }

    /// <summary>채용되지 않은 런타임 후보 인스턴스를 해제합니다.</summary>
    private void ReleaseCandidates(EmployeeData keep)
    {
        if (_candidates == null) return;
        foreach (EmployeeData candidate in _candidates)
            if (candidate != null && candidate != keep) Destroy(candidate);
        _candidates = null;
    }

    /// <summary>작업 수치 최대치 — 기본(설정) + 같은 방의 서로 다른 부속 건물 1종당 +statCapPerAddon</summary>
    public int WorkStatCap
    {
        get
        {
            var config = EmployeeManager.instance != null ? EmployeeManager.instance.GenerationConfig : null;
            int cap = config != null ? config.baseWorkStatCap : 30;
            if (statCapAddonIds != null)
                foreach (int id in statCapAddonIds)
                    if (BuildingAddonUtil.CountInSameRoom(_building, id) > 0) cap += statCapPerAddon;
            return cap;
        }
    }

    /// <summary>하루 다시 뽑기 횟수 — 같은 방의 다시 뽑기 부속 건물 수 (최대 maxRerollAddons)</summary>
    public int RerollsPerDay => Mathf.Min(maxRerollAddons, BuildingAddonUtil.CountInSameRoom(_building, rerollAddonId));

    /// <summary>오늘 남은 다시 뽑기 횟수</summary>
    public int RerollsLeft => Mathf.Max(0, RerollsPerDay - (EmployeeManager.instance != null ? EmployeeManager.instance.RerollsUsedToday : 0));

    /// <summary>후보 3명을 새로 뽑는다 (하루 횟수 제한). 성공하면 새 후보 목록</summary>
    public List<EmployeeData> Reroll()
    {
        if (RerollsLeft <= 0 || IsUsed) return null;
        EmployeeManager.instance?.RecordReroll();
        GenerateCandidates();
        return _candidates;
    }

    /// <summary>
    /// 채용된 직원의 스폰 위치를 반환합니다.
    /// </summary>
    public Vector3 GetSpawnPoint()
    {
        return transform.position + spawnOffset;
    }

    /// <summary>
    /// 채용을 기록하고(하루 한도) 오늘의 후보를 비웁니다. 다음 날 다시 후보가 뽑힙니다.
    /// HiringPanel에서 직원 채용 후 호출됩니다.
    /// </summary>
    /// <param name="selectedData">채용된 직원 데이터. 나머지 미선택 런타임 인스턴스를 해제합니다.</param>
    public void MarkUsed(EmployeeData selectedData = null)
    {
        EmployeeManager.instance?.RecordHire();
        ReleaseCandidates(selectedData);
        Debug.Log("[HiringOffice] 채용 완료. 오늘은 더 채용할 수 없습니다(내일 새 후보).");
    }

    #endregion

    #region IBuildingFunction 구현

    public void OnBuildingDisabled()
    {
        Debug.Log("[HiringOffice] 건물이 비활성화되었습니다.");
    }

    public void OnBuildingEnabled()
    {
        Debug.Log("[HiringOffice] 건물이 다시 활성화되었습니다.");
    }

    #endregion
}
