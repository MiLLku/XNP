using UnityEngine;

/// <summary>
/// 고정 침식 발원지 컴포넌트 (ITerrainErosionSource 범용 구현체).
///
/// 이 컴포넌트가 붙은 GameObject는 OnEnable/OnDisable 시 TerrainErosionManager에
/// 자동 등록/해제되며, <b>자기가 있는 방의 침식 수치</b>를 올립니다.
/// 실외에 있으면 아무 일도 하지 않습니다 — 바깥은 침식이 고이지 않습니다.
///
/// <b>일반 발원지</b>: saturationLevel을 넘으면 스스로 멈춰 평형을 이룹니다.
///   → 방치해도 방이 일정 수준에서 안정되므로, 급하게 처리하지 않아도 됩니다.
///
/// <b>특수 발원지(시한폭탄)</b>: saturationLevel을 0으로 두면 한계 없이 계속 올립니다.
///   → 오래 두면 방 전체가 죽으므로 빨리 제거해야 합니다.
/// </summary>
public class TerrainErosionEmitter : MonoBehaviour, ITerrainErosionSource
{
    #region 설정

    [Header("방출")]
    [Tooltip("이 발원지가 방에 넣는 초당 침식량")]
    [SerializeField] private float erosionPerSecond = 0.5f;

    [Tooltip("방 침식이 이 값 이상이면 활동을 멈춥니다.\n0 이하 = 한계 없음 (시한폭탄형 특수 발원지)")]
    [SerializeField] private float saturationLevel = 40f;

    [Tooltip("UI·로그 표기용 이름 (비우면 오브젝트 이름 사용)")]
    [SerializeField] private string displayName = "";

    #endregion

    #region ITerrainErosionSource

    /// <summary>발원지의 타일 좌표 (transform 기반 FloorToInt)</summary>
    public Vector2Int TilePosition => new Vector2Int(
        Mathf.FloorToInt(transform.position.x),
        Mathf.FloorToInt(transform.position.y)
    );

    public float ErosionPerSecond => erosionPerSecond;
    public float SaturationLevel  => saturationLevel;
    public bool  IsActive         => isActiveAndEnabled;

    public string SourceDisplayName
        => string.IsNullOrEmpty(displayName) ? gameObject.name : displayName;

    /// <summary>한계 없이 계속 올리는 특수 발원지인지</summary>
    public bool IsUnbounded => saturationLevel <= 0f;

    #endregion

    #region 런타임 조정

    /// <summary>방출량을 바꿉니다 (이벤트·성장 단계 등).</summary>
    public void SetErosionPerSecond(float value) => erosionPerSecond = Mathf.Max(0f, value);

    /// <summary>포화 수치를 바꿉니다. 0 이하로 두면 한계 없는 특수 발원지가 됩니다.</summary>
    public void SetSaturationLevel(float value) => saturationLevel = value;

    #endregion

    #region Unity 라이프사이클

    private void OnEnable()
    {
        TerrainErosionManager.instance?.RegisterSource(this);
    }

    private void OnDisable()
    {
        TerrainErosionManager.instance?.UnregisterSource(this);
    }

    #endregion
}
