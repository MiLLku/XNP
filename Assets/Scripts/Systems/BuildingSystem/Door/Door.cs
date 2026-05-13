using UnityEngine;

/// <summary>
/// 문 컴포넌트 — Building 컴포넌트와 함께 같은 GameObject에 추가합니다 (IBuildingFunction 구현).
///
/// 동작 요약:
///   - 직원이 문 타일로 이동하기 전 RequestOpen() 호출 → 열리는 시간(openTime) 후 IsPassable = true
///   - 통과 완료 후 ReleaseOpen() 호출 → 마지막 요청 해제 후 closeDelay초 뒤 자동으로 닫힘
///   - interactionAllowed = false  → 직원도 통과 불가 (잠긴 문)
///   - 파손(IsFunctional = false)  → 강제 개방 상태 고정 (차단 기능 비활성)
///
/// 상태 머신:
///   Closed ─[RequestOpen]→ Opening ─[openTime 경과]→ Open
///   Open   ─[closeDelay 경과]→ Closing ─[closeTime 경과]→ Closed
///   Closing ─[RequestOpen 재요청]→ Opening (역방향 전환)
///   파손 시: 모든 상태 무시 → 항상 IsPassable = true
/// </summary>
[RequireComponent(typeof(Building))]
public class Door : MonoBehaviour, IBuildingFunction
{
    #region 상수

    /// <summary>열린 상태 스프라이트가 없을 때 폴백으로 사용하는 알파 (반투명)</summary>
    private const float OPEN_ALPHA   = 0.25f;

    /// <summary>닫힌 상태 알파 (불투명)</summary>
    private const float CLOSED_ALPHA = 1.00f;

    #endregion

    #region 열거형

    private enum DoorState { Closed, Opening, Open, Closing }

    #endregion

    #region 필드

    [Header("문 설정")]
    [Tooltip("true: 직원이 통과 가능 / false: 모든 생명체 차단 (잠금 상태)")]
    [SerializeField] private bool interactionAllowed = true;

    private DoorData       _doorData;
    private Building       _building;
    private SpriteRenderer _spriteRenderer;
    private Collider2D     _collider;

    private DoorState _state          = DoorState.Closed;
    private float     _stateTimer     = 0f;
    private float     _closeDelayTimer = 0f;

    /// <summary>
    /// 현재 통과를 요청 중인 직원 수.
    /// 0이 되어야 closeDelay 카운트다운이 시작됩니다.
    /// </summary>
    private int _pendingRequests = 0;

    private Vector2Int _bottomTile;
    private bool       _registered = false;

    #endregion

    #region 프로퍼티

    /// <summary>직원/비적대 생명체가 통과를 요청할 수 있는지 여부</summary>
    public bool InteractionAllowed => interactionAllowed;

    /// <summary>현재 완전히 열려있거나 파손되어 통과 가능한 상태인지 여부</summary>
    public bool IsPassable => _state == DoorState.Open || IsBroken;

    /// <summary>완전히 닫힌 상태</summary>
    public bool IsClosed => _state == DoorState.Closed;

    /// <summary>파손(기능 비활성) 여부 — Building.IsFunctional을 통해 확인</summary>
    public bool IsBroken => _building != null && !_building.IsFunctional;

    // IBuildingFunction ──────────────────────────────────────────────────────────
    public bool IsOperating => !IsBroken;

    #endregion

    #region IBuildingFunction

    /// <summary>파손 시: 문을 강제 개방 상태로 고정합니다 (차단 기능 상실).</summary>
    public void OnBuildingDisabled()
    {
        ForceOpen();
        Debug.Log($"[Door] {gameObject.name} 파손 — 문이 강제 개방 상태로 고정됩니다.");
    }

    /// <summary>복구 시: 다시 닫힌 상태로 전환합니다.</summary>
    public void OnBuildingEnabled()
    {
        _pendingRequests = 0;
        SetState(DoorState.Closed);
        Debug.Log($"[Door] {gameObject.name} 복구 — 문이 닫힌 상태로 복귀합니다.");
    }

    #endregion

    #region 초기화

    void Awake()
    {
        _building       = GetComponent<Building>();
        _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        _collider       = GetComponent<Collider2D>();
    }

    void Start()
    {
        // BuildingData를 DoorData로 캐스팅
        if (_building?.buildingData is DoorData doorData)
        {
            _doorData = doorData;
        }
        else
        {
            Debug.LogError($"[Door] {gameObject.name}: buildingData가 DoorData 타입이 아닙니다. " +
                           "Door 컴포넌트를 비활성화합니다.");
            enabled = false;
            return;
        }

        // 하단 타일 좌표 계산
        _bottomTile = new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.y)
        );

        // DoorManager에 등록 (타일 기반 조회를 위해)
        DoorManager.instance?.Register(_bottomTile, this);
        _registered = true;

        // 닫힌 상태로 초기화
        SetState(DoorState.Closed);
    }

    void OnDestroy()
    {
        if (_registered)
            DoorManager.instance?.Unregister(_bottomTile);
    }

    #endregion

    #region 업데이트

    void Update()
    {
        // 파손된 문은 상태 머신 비활성 (항상 IsPassable = true)
        if (_doorData == null || IsBroken) return;

        switch (_state)
        {
            case DoorState.Opening:
                _stateTimer += Time.deltaTime;
                if (_stateTimer >= _doorData.openTime)
                    SetState(DoorState.Open);
                break;

            case DoorState.Open:
                if (_pendingRequests <= 0)
                {
                    // 통과 중인 직원이 없으면 닫기 딜레이 카운트다운
                    _closeDelayTimer += Time.deltaTime;
                    if (_closeDelayTimer >= _doorData.closeDelay)
                        SetState(DoorState.Closing);
                }
                else
                {
                    // 아직 통과 중인 직원이 있으면 타이머 리셋
                    _closeDelayTimer = 0f;
                }
                break;

            case DoorState.Closing:
                _stateTimer += Time.deltaTime;
                if (_stateTimer >= _doorData.closeTime)
                {
                    SetState(DoorState.Closed);
                }
                else if (_pendingRequests > 0)
                {
                    // 닫히는 중에 새 요청 → 다시 열기
                    SetState(DoorState.Opening);
                }
                break;

            case DoorState.Closed:
                break; // 대기 중 — 외부에서 RequestOpen()이 호출되기를 기다림
        }
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 직원/비적대 생명체가 문 통과를 요청합니다.
    ///
    /// 반환값:
    ///   true  → 요청 수락 (문이 열릴 예정이거나 이미 개방/파손)
    ///   false → 거부 (interactionAllowed = false인 잠긴 문)
    /// </summary>
    public bool RequestOpen()
    {
        if (IsBroken)            return true;  // 파손 → 이미 통과 가능
        if (!interactionAllowed) return false; // 잠금 → 거부

        _pendingRequests++;

        if (_state == DoorState.Closed || _state == DoorState.Closing)
            SetState(DoorState.Opening);

        return true;
    }

    /// <summary>
    /// 통과 완료 후 호출합니다. 요청 카운터를 1 감소시킵니다.
    /// 카운터가 0이 되면 closeDelay 후 자동으로 닫힙니다.
    /// </summary>
    public void ReleaseOpen()
    {
        _pendingRequests = Mathf.Max(0, _pendingRequests - 1);
    }

    /// <summary>
    /// 상호작용 허용/차단을 동적으로 변경합니다.
    /// 차단으로 전환 시 진행 중인 요청을 모두 취소하고 즉시 닫기 시작합니다.
    /// </summary>
    public void SetInteractionAllowed(bool allowed)
    {
        interactionAllowed = allowed;

        if (!allowed)
        {
            _pendingRequests = 0;
            if (_state != DoorState.Closed && _state != DoorState.Closing)
                SetState(DoorState.Closing);
        }

        Debug.Log($"[Door] {gameObject.name} 상호작용 → {(allowed ? "허용" : "차단")}");
    }

    #endregion

    #region 내부 상태 관리

    private void SetState(DoorState newState)
    {
        _state      = newState;
        _stateTimer = 0f;

        switch (newState)
        {
            case DoorState.Closed:
                _closeDelayTimer = 0f;
                ApplyVisual(isOpen: false);
                break;

            case DoorState.Opening:
                // 열리는 중 — 아직 닫힌 비주얼 유지
                ApplyVisual(isOpen: false);
                break;

            case DoorState.Open:
                _closeDelayTimer = 0f;
                ApplyVisual(isOpen: true);
                break;

            case DoorState.Closing:
                // 닫히는 중 — 아직 열린 비주얼 유지
                ApplyVisual(isOpen: true);
                break;
        }
    }

    /// <summary>문을 강제로 열린 상태에 고정합니다 (파손 시 호출).</summary>
    private void ForceOpen()
    {
        _pendingRequests  = 0;
        _closeDelayTimer  = 0f;
        _state            = DoorState.Open;
        _stateTimer       = 0f;
        ApplyVisual(isOpen: true);
    }

    /// <summary>
    /// 개폐 상태에 맞게 스프라이트와 알파를 적용합니다.
    /// DoorData에 스프라이트가 설정된 경우 스프라이트를 교체하고,
    /// 없는 경우 알파값으로 폴백합니다.
    /// </summary>
    private void ApplyVisual(bool isOpen)
    {
        if (_spriteRenderer == null) return;

        // 스프라이트 교체 (설정된 경우)
        if (isOpen  && _doorData?.openSprite   != null)
            _spriteRenderer.sprite = _doorData.openSprite;
        else if (!isOpen && _doorData?.closedSprite != null)
            _spriteRenderer.sprite = _doorData.closedSprite;

        // 알파값으로 상태 표현
        Color c = _spriteRenderer.color;
        c.a = isOpen ? OPEN_ALPHA : CLOSED_ALPHA;
        _spriteRenderer.color = c;
    }

    #endregion
}
