using UnityEngine;

/// <summary>
/// 개체형(Hostile) 제놉스 공통 범위 침식 컴포넌트.
/// 모든 개체형 프리팹에 독립적으로 부착합니다.
///
/// <b>동작 방식이 바뀌었습니다.</b>
/// 예전에는 범위 내 직원을 직접 찾아 침식을 꽂았지만, 이제는
/// <see cref="EntityErosionField"/>에 <b>자기 주변을 오염시켜 두기만</b> 합니다.
/// 직원은 그 위에 서 있는 동안 스스로 침식을 받습니다.
///
/// 이렇게 바꾼 이유:
///   · 오염이 <b>장소</b>에 생기므로 "저 구역은 위험하다"가 성립하고 오버레이로 그릴 수 있다.
///   · 직원 탐색 루프가 사라져 개체가 많아져도 비용이 반경에만 비례한다.
///   · 여러 오라가 겹쳐도 합산되지 않고 가장 강한 것만 적용된다(중첩 폭주 방지).
///
/// 방 침식(고정 발원지)과 달리 <b>고이지 않습니다</b> — 개체가 떠나면 다음 틱에 사라집니다.
/// 장비의 침식 무시와 출처 내역은 직원 쪽(EmployeeErosionController)에서 처리합니다.
/// </summary>
[RequireComponent(typeof(Xenops))]
public class HostileErosionAura : MonoBehaviour, IEntityErosionSource
{
    private Xenops _xenops;
    private float  _erosionAmount;
    private float  _aoeErosionRange;
    private bool   _registered;

    #region IEntityErosionSource

    public Vector2 EmitPosition => transform.position;
    public float EmitRadius => _aoeErosionRange;

    /// <summary>범위 안에 있는 동안 붙는 고정량. 벗어나면 돌아갑니다.</summary>
    public float FixedErosionAmount => _erosionAmount;

    public bool HorizontalOnly => false;

    public bool Covers(Vector2 worldPosition)
        => Vector2.Distance(EmitPosition, worldPosition) <= _aoeErosionRange;

    public bool IsEmitting
        => isActiveAndEnabled && _erosionAmount > 0f && _aoeErosionRange > 0f;

    public string ErosionSourceKey
        => ErosionSource.AuraKey(_xenops != null ? _xenops.InstanceId : GetEntityId().GetHashCode());

    public string ErosionSourceName
        => $"{(_xenops != null ? _xenops.DisplayName : "제놉스")} 오라침식";

    #endregion

    #region 생명주기

    private void Awake()
    {
        _xenops = GetComponent<Xenops>();
    }

    private void Start()
    {
        if (_xenops?.Data?.hostileStats == null) return;

        // 데이터의 aoeErosionPerSecond를 '범위 안에서 붙는 고정량'으로 해석한다.
        // 개체 발원지는 시간에 비례해 쌓이지 않으므로 초당이라는 개념이 없다.
        _erosionAmount   = _xenops.Data.hostileStats.aoeErosionPerSecond;
        _aoeErosionRange = _xenops.Data.hostileStats.aoeErosionRange;

        TryRegister();
    }

    private void OnEnable() => TryRegister();

    private void OnDisable()
    {
        if (!_registered) return;

        EntityErosionField.instance?.UnregisterSource(this);
        _registered = false;
    }

    private void TryRegister()
    {
        if (_registered) return;
        if (_erosionAmount <= 0f || _aoeErosionRange <= 0f) return;
        if (EntityErosionField.instance == null) return;

        EntityErosionField.instance.RegisterSource(this);
        _registered = true;
    }

    #endregion
}
