using UnityEngine;

/// <summary>
/// 분출구 위에 올려 자원을 뽑아내는 시설.
///
/// 전력을 받는 동안 일정 시간마다 산출물을 자기 보관함에 적립합니다.
/// <b>인벤토리로 순간이동시키지 않습니다</b> — 만든 자리에 쌓이고 직원이 창고로 옮겨야 재고가 됩니다.
/// (수경 재배기는 아직 옛 방식으로 인벤토리에 직접 넣습니다. 그 경로를 따르지 마세요.)
///
/// 정전이나 기반 파괴 시 진행도 누적만 멈추고 리셋하지 않습니다 — 복구되면 이어서 채웁니다.
/// 보관함이 가득 차면 멈춥니다. 직원이 비워 가야 다시 돕니다.
/// </summary>
[RequireComponent(typeof(Building), typeof(BuildingOutputBuffer))]
public class VentExtractor : MonoBehaviour, IBuildingFunction
{
    [Header("추출 설정")]
    [Tooltip("뽑아낼 산출물")]
    [SerializeField] private ItemData product;

    [Tooltip("1주기 산출량")]
    [Min(1)] [SerializeField] private int amountPerCycle = 1;

    [Tooltip("1주기에 걸리는 시간(초)")]
    [Min(0.1f)] [SerializeField] private float cycleSeconds = 30f;

    private Building _building;
    private PowerConsumer _power;
    private BuildingOutputBuffer _buffer;
    private float _progress;

    /// <summary>현재 추출 진행도(0~1)</summary>
    public float Progress => _progress;

    /// <summary>지금 실제로 돌고 있는지 (전력·기반·보관함 여유가 모두 있어야 한다)</summary>
    public bool IsOperating =>
        (_building == null || _building.IsFunctional) &&
        (_power == null || _power.IsPowered) &&
        (_buffer == null || !_buffer.IsFull);

    void Awake()
    {
        _building = GetComponent<Building>();
        _power = GetComponent<PowerConsumer>();
        _buffer = GetComponent<BuildingOutputBuffer>();
    }

    void Update()
    {
        if (product == null || !IsOperating) return;

        _progress += Time.deltaTime / cycleSeconds;
        if (_progress < 1f) return;

        _progress -= 1f;

        int stored = _buffer.TryStore(product, amountPerCycle);
        if (stored < amountPerCycle)
        {
            // 보관함이 넘쳐 일부만 들어갔다 — 남은 진행도는 버린다(다음 주기부터 다시)
            _progress = 0f;
        }
    }

    // ── IBuildingFunction ──────────────────────────────────────
    // 기반 파괴·복구 시 Building이 이 컴포넌트의 enabled를 토글하므로 별도 처리는 없습니다.
    public void OnBuildingDisabled() { }
    public void OnBuildingEnabled() { }
}
