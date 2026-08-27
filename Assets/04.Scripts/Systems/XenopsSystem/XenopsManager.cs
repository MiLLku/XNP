using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제노프스 매니저.
/// DestroySingleton + ISaveModule로 제노프스의 생성, 관리, 저장/로드를 담당합니다.
///
/// SaveOrder: 55 (Employee=50 이후, WorkOrder=60 이전)
/// </summary>
public class XenopsManager : DestroySingleton<XenopsManager>, ISaveModule
{
    #region 필드

    /// <summary>활성 제노프스 목록</summary>
    private List<Xenops> activeXenops = new List<Xenops>();

    #endregion

    #region ISaveModule 구현

    public int SaveOrder => 55;

    /// <summary>
    /// 현재 모든 제노프스 상태를 SaveData에 기록합니다.
    /// </summary>
    public void Capture(SaveData data)
    {
        data.xenopsEntities = new List<XenopsSaveData>();

        foreach (var xenops in activeXenops)
        {
            if (xenops == null) continue;
            data.xenopsEntities.Add(xenops.CreateSaveData());
        }

        Debug.Log($"[XenopsManager] Capture 완료: {data.xenopsEntities.Count}개 제노프스 저장");
    }

    /// <summary>
    /// SaveData에서 제노프스를 복원합니다.
    /// </summary>
    public void Restore(SaveData data)
    {
        // 기존 제노프스 제거
        ClearAll();

        if (data.xenopsEntities == null) return;

        foreach (var xenopsSaveData in data.xenopsEntities)
        {
            RestoreXenops(xenopsSaveData);
        }

        Debug.Log($"[XenopsManager] Restore 완료: {activeXenops.Count}개 제노프스 복원");
    }

    /// <summary>
    /// 크로스레퍼런스를 복원합니다 (Employee, Building 참조).
    /// </summary>
    public void PostRestore(SaveData data)
    {
        foreach (var xenops in activeXenops)
        {
            if (xenops == null) continue;
            xenops.ResolveReferences();
        }

        Debug.Log($"[XenopsManager] PostRestore 완료: 크로스레퍼런스 연결");
    }

    #endregion

    #region 생성/제거

    /// <summary>
    /// 새 제노프스를 스폰합니다.
    /// </summary>
    /// <param name="xenopsDataId">XenopsData ID (GameIDRegistry.Xenops 범위)</param>
    /// <param name="position">스폰 위치</param>
    /// <returns>생성된 Xenops (실패 시 null)</returns>
    public Xenops SpawnXenops(int xenopsDataId, Vector3 position)
    {
        if (GameDatabase.Instance == null)
        {
            Debug.LogError("[XenopsManager] GameDatabase가 초기화되지 않았습니다.");
            return null;
        }

        var xenopsData = GameDatabase.Instance.GetXenopsData(xenopsDataId);
        if (xenopsData == null)
        {
            Debug.LogError($"[XenopsManager] XenopsData 없음: ID {xenopsDataId}");
            return null;
        }

        return SpawnXenops(xenopsData, position);
    }

    /// <summary>
    /// 새 제노프스를 스폰합니다 (데이터 직접 전달).
    /// </summary>
    public Xenops SpawnXenops(XenopsData xenopsData, Vector3 position)
    {
        if (xenopsData == null || xenopsData.prefab == null)
        {
            Debug.LogError("[XenopsManager] XenopsData 또는 프리팹이 null입니다.");
            return null;
        }

        // 디버그: 제놉스 등장 차단 — 이벤트·레이드·직접 스폰이 모두 이 지점을 거친다
        if (DebugManager.IsBlocked(DebugFlag.XenopsSpawn)) return null;

        var go = Instantiate(xenopsData.prefab, position, Quaternion.identity);
        var xenops = go.GetComponent<Xenops>();

        if (xenops == null)
        {
            Debug.LogError($"[XenopsManager] 프리팹에 Xenops 컴포넌트가 없습니다: {xenopsData.xenopsName}");
            Destroy(go);
            return null;
        }

        xenops.Initialize(xenopsData);

        // Hostile 타입은 스폰 즉시 Active 상태로 전환 → Behavior.OnActivated() 호출
        // (Start()가 Update() 보다 먼저 실행되므로 캐시/탐지존 초기화 후 AI 작동)
        if (xenopsData.xenopsType == XenopsType.Hostile)
            xenops.SetState(XenopsState.Active);

        RegisterXenops(xenops);

        Debug.Log($"[XenopsManager] {xenopsData.xenopsName} 스폰 완료 (ID:{xenops.InstanceId}, 위치:{position})");

        return xenops;
    }

    /// <summary>
    /// 제노프스를 제거합니다.
    /// </summary>
    public void RemoveXenops(Xenops xenops)
    {
        if (xenops == null) return;

        UnregisterXenops(xenops);
        Destroy(xenops.gameObject);

        Debug.Log($"[XenopsManager] {xenops.DisplayName} 제거됨");
    }

    /// <summary>
    /// 모든 제노프스를 제거합니다.
    /// </summary>
    public void ClearAll()
    {
        for (int i = activeXenops.Count - 1; i >= 0; i--)
        {
            if (activeXenops[i] != null)
            {
                Destroy(activeXenops[i].gameObject);
            }
        }
        activeXenops.Clear();
    }

    #endregion

    #region 등록/해제

    /// <summary>
    /// 제노프스를 매니저에 등록합니다.
    /// </summary>
    public void RegisterXenops(Xenops xenops)
    {
        if (xenops != null && !activeXenops.Contains(xenops))
        {
            activeXenops.Add(xenops);
        }
    }

    /// <summary>
    /// 제노프스를 매니저에서 해제합니다.
    /// </summary>
    public void UnregisterXenops(Xenops xenops)
    {
        activeXenops.Remove(xenops);
    }

    #endregion

    #region 조회

    /// <summary>
    /// 모든 활성 제노프스를 반환합니다.
    /// </summary>
    public List<Xenops> GetAllXenops() => new List<Xenops>(activeXenops);

    /// <summary>
    /// 특정 타입의 제노프스 목록을 반환합니다.
    /// </summary>
    public List<Xenops> GetXenopsByType(XenopsType type)
    {
        var result = new List<Xenops>();
        foreach (var xenops in activeXenops)
        {
            if (xenops != null && xenops.Type == type)
            {
                result.Add(xenops);
            }
        }
        return result;
    }

    /// <summary>
    /// 인스턴스 ID로 제노프스를 찾습니다.
    /// </summary>
    public Xenops GetXenopsByInstanceId(int instanceId)
    {
        foreach (var xenops in activeXenops)
        {
            if (xenops != null && xenops.InstanceId == instanceId)
            {
                return xenops;
            }
        }
        return null;
    }

    /// <summary>
    /// 현재 활성 제노프스 수를 반환합니다.
    /// </summary>
    public int Count => activeXenops.Count;

    #endregion

    #region 내부 — 복원

    /// <summary>
    /// 저장 데이터로부터 개별 제노프스를 복원합니다.
    /// </summary>
    private void RestoreXenops(XenopsSaveData saveData)
    {
        if (GameDatabase.Instance == null) return;

        var xenopsData = GameDatabase.Instance.GetXenopsData(saveData.templateId);
        if (xenopsData == null)
        {
            Debug.LogWarning($"[XenopsManager] 복원 실패 — XenopsData 없음: ID {saveData.templateId}");
            return;
        }

        if (xenopsData.prefab == null)
        {
            Debug.LogWarning($"[XenopsManager] 복원 실패 — 프리팹 없음: {xenopsData.xenopsName}");
            return;
        }

        var go = Instantiate(xenopsData.prefab, new Vector3(saveData.posX, saveData.posY, 0f), Quaternion.identity);
        var xenops = go.GetComponent<Xenops>();

        if (xenops == null)
        {
            Debug.LogWarning($"[XenopsManager] 복원 실패 — Xenops 컴포넌트 없음: {xenopsData.xenopsName}");
            Destroy(go);
            return;
        }

        xenops.RestoreFromSaveData(saveData);
        RegisterXenops(xenops);
    }

    #endregion
}
