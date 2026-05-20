using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 직원 채용 건물 컴포넌트.
/// 게임 시작 시 전체 EmployeeData 목록에서 무작위 3명을 후보로 선정합니다.
/// 건물을 클릭하면 HiringPanel을 열어 후보 3명을 보여주며,
/// 1명을 선택하면 맵에 스폰되고 건물이 상호작용 비활성화됩니다 (1회 한정).
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

    // 런타임 상태
    private bool _isUsed = false;
    private List<EmployeeData> _candidates;
    private Building _building;

    #endregion

    #region 프로퍼티

    /// <summary>이미 채용이 완료되어 상호작용 비활성화 여부</summary>
    public bool IsUsed => _isUsed;

    /// <summary>IBuildingFunction: 현재 동작 중 여부</summary>
    public bool IsOperating => false;

    #endregion

    #region 초기화

    private void Awake()
    {
        _building = GetComponent<Building>();
    }

    private void Start()
    {
        GenerateCandidates();
        FitColliderToSprite();
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
        _candidates = new List<EmployeeData>();

        EmployeeGenerationConfig config = EmployeeManager.instance?.GenerationConfig;
        if (config == null)
        {
            Debug.LogError("[HiringOffice] EmployeeManager에 GenerationConfig가 설정되지 않았습니다. EmployeeManager Inspector에서 할당해주세요.");
            return;
        }

        const int CANDIDATE_COUNT = 3;
        for (int i = 0; i < CANDIDATE_COUNT; i++)
        {
            EmployeeData data = RandomEmployeeGenerator.Generate(config);
            if (data != null)
                _candidates.Add(data);
        }

        Debug.Log($"[HiringOffice] 무작위 채용 후보 {_candidates.Count}명 생성 완료.");
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 선정된 채용 후보 목록을 반환합니다. (HiringPanel에서 사용)
    /// </summary>
    public List<EmployeeData> GetCandidates()
    {
        return _candidates ?? new List<EmployeeData>();
    }

    /// <summary>
    /// 채용된 직원의 스폰 위치를 반환합니다.
    /// </summary>
    public Vector3 GetSpawnPoint()
    {
        return transform.position + spawnOffset;
    }

    /// <summary>
    /// 채용을 완료하고 건물을 상호작용 비활성화 상태로 만듭니다.
    /// HiringPanel에서 직원 채용 후 호출됩니다.
    /// </summary>
    /// <param name="selectedData">채용된 직원 데이터. 나머지 미선택 런타임 인스턴스를 해제합니다.</param>
    public void MarkUsed(EmployeeData selectedData = null)
    {
        if (_isUsed) return;

        _isUsed = true;

        // 미선택 런타임 생성 인스턴스 메모리 해제
        if (_candidates != null)
        {
            foreach (EmployeeData candidate in _candidates)
            {
                if (candidate != null && candidate != selectedData)
                    Destroy(candidate);
            }
        }

        // 콜라이더 비활성화 → 이후 클릭 이벤트 차단
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        // 건물 반투명 처리로 비활성화 시각 표시
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            Color c = sr.color;
            c.a = 0.5f;
            sr.color = c;
        }

        Debug.Log("[HiringOffice] 채용 완료. 이 건물은 더 이상 상호작용할 수 없습니다.");
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
