/// <summary>
/// 풀에서 Spawn/Despawn 시 자동 호출되는 콜백을 제공하는 인터페이스.
///
/// PoolManager가 Spawn할 때 OnSpawn()을, Despawn할 때 OnDespawn()을 호출합니다.
/// 같은 GameObject에 여러 컴포넌트가 IPoolable을 구현하면 모두 호출됩니다.
///
/// 사용 예:
///   - 풀에서 재사용되는 컴포넌트의 상태 리셋 (claim, 타이머, velocity 등)
///   - 풀로 반환되기 전 정리 (이벤트 해제, ref 끊기 등)
///
/// 주의:
///   OnSpawn은 SetActive(true) 직후 호출됩니다.
///   OnDespawn은 SetActive(false) 직전 호출됩니다.
/// </summary>
public interface IPoolable
{
    /// <summary>풀에서 꺼내 활성화된 직후 호출. 상태 리셋·재초기화 용도.</summary>
    void OnSpawn();

    /// <summary>풀로 반환되기 직전 호출. 정리·이벤트 해제 용도.</summary>
    void OnDespawn();
}
