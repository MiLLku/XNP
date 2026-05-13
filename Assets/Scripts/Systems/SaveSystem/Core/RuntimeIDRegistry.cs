using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 런타임 인스턴스 ID 레지스트리.
/// 저장/로드 시 크로스레퍼런스(Employee↔WorkOrder, ConstructionSite↔BuildOrder 등)를
/// 해결하기 위한 중앙 매핑 시스템입니다.
///
/// 사용법:
///   저장 시: 각 엔티티가 자신의 instanceId를 SaveData에 기록
///   로드 시: Restore()에서 엔티티 생성 후 Register() 호출
///           PostRestore()에서 Resolve()로 참조 복원
/// </summary>
public class RuntimeIDRegistry : DestroySingleton<RuntimeIDRegistry>
{
    #region 필드

    /// <summary>instanceId → UnityEngine.Object 매핑</summary>
    private Dictionary<int, UnityEngine.Object> _registry = new Dictionary<int, UnityEngine.Object>();

    [Header("디버그")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    #region 공개 API

    /// <summary>
    /// 오브젝트를 ID로 등록합니다.
    /// 이미 같은 ID가 등록되어 있으면 덮어씁니다.
    /// </summary>
    /// <param name="id">인스턴스 ID</param>
    /// <param name="obj">등록할 오브젝트</param>
    public void Register(int id, UnityEngine.Object obj)
    {
        if (id <= 0 || obj == null) return;

        _registry[id] = obj;

        if (showDebugLogs)
        {
            Debug.Log($"[RuntimeIDRegistry] 등록: ID={id}, Type={obj.GetType().Name}");
        }
    }

    /// <summary>
    /// ID 등록을 해제합니다.
    /// </summary>
    /// <param name="id">해제할 인스턴스 ID</param>
    public void Unregister(int id)
    {
        if (_registry.Remove(id) && showDebugLogs)
        {
            Debug.Log($"[RuntimeIDRegistry] 해제: ID={id}");
        }
    }

    /// <summary>
    /// ID로 오브젝트를 조회합니다.
    /// </summary>
    /// <typeparam name="T">조회할 타입</typeparam>
    /// <param name="id">인스턴스 ID</param>
    /// <returns>해당 오브젝트 (없으면 null)</returns>
    public T Resolve<T>(int id) where T : UnityEngine.Object
    {
        if (id <= 0) return null;

        if (_registry.TryGetValue(id, out UnityEngine.Object obj))
        {
            if (obj is T typed)
            {
                return typed;
            }

            Debug.LogWarning($"[RuntimeIDRegistry] 타입 불일치: ID={id}, 요청={typeof(T).Name}, 실제={obj.GetType().Name}");
        }

        return null;
    }

    /// <summary>
    /// ID로 오브젝트를 조회합니다 (Component 용).
    /// GameObject가 등록된 경우 해당 컴포넌트를 찾아 반환합니다.
    /// </summary>
    /// <typeparam name="T">조회할 컴포넌트 타입</typeparam>
    /// <param name="id">인스턴스 ID</param>
    /// <returns>해당 컴포넌트 (없으면 null)</returns>
    public T ResolveComponent<T>(int id) where T : Component
    {
        if (id <= 0) return null;

        if (_registry.TryGetValue(id, out UnityEngine.Object obj))
        {
            // 직접 컴포넌트인 경우
            if (obj is T typed)
            {
                return typed;
            }

            // GameObject인 경우 컴포넌트 검색
            if (obj is GameObject go)
            {
                return go.GetComponent<T>();
            }

            // 다른 Component인 경우 같은 GameObject에서 검색
            if (obj is Component comp)
            {
                return comp.GetComponent<T>();
            }
        }

        return null;
    }

    /// <summary>
    /// 특정 ID가 등록되어 있는지 확인합니다.
    /// </summary>
    /// <param name="id">확인할 ID</param>
    /// <returns>등록 여부</returns>
    public bool IsRegistered(int id)
    {
        return id > 0 && _registry.ContainsKey(id);
    }

    /// <summary>
    /// 모든 등록을 해제합니다 (로드 시 초기화용).
    /// </summary>
    public void Clear()
    {
        _registry.Clear();

        if (showDebugLogs)
        {
            Debug.Log("[RuntimeIDRegistry] 전체 초기화");
        }
    }

    /// <summary>
    /// 현재 등록된 엔트리 수를 반환합니다.
    /// </summary>
    public int Count => _registry.Count;

    #endregion
}
