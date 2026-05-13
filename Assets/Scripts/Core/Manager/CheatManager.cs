using UnityEngine;

/// <summary>
/// 치트 키 관리자.
/// 개발/디버그용 단축키를 처리합니다.
///
/// 치트 키:
///   F5 — 무작위 이벤트 즉시 발생
///   F6 — 제노프스 등장 이벤트 즉시 발생 (등록된 XenopsAppearance 이벤트 or 랜덤 스폰)
/// </summary>
public class CheatManager : MonoBehaviour
{
    [Header("치트 키 설정")]
    [Tooltip("치트 기능 활성화 여부 (릴리즈 빌드에서 false로 설정)")]
    public bool enableCheats = true;

    [Header("제노프스 스폰 설정")]
    [Tooltip("F6 치트로 직접 스폰할 XenopsData ID (0 = GameDatabase에서 랜덤)")]
    public int cheatXenopsDataId = 0;

    void Update()
    {
        if (!enableCheats) return;

        // F5: 무작위 이벤트 발생
        if (Input.GetKeyDown(KeyCode.F5))
        {
            TriggerRandomEvent();
        }

        // F6: 제노프스 등장 이벤트 발생
        if (Input.GetKeyDown(KeyCode.F6))
        {
            TriggerXenopsSpawnEvent();
        }
    }

    /// <summary>
    /// F5 — EventManager를 통해 무작위 이벤트를 즉시 발생시킵니다.
    /// </summary>
    private void TriggerRandomEvent()
    {
        if (EventManager.instance == null)
        {
            Debug.LogWarning("[CheatManager] EventManager가 없습니다.");
            return;
        }

        EventManager.instance.TriggerRandomEvent();
        Debug.Log("[CheatManager] 치트 적용: 무작위 이벤트 발생");
    }

    /// <summary>
    /// F6 — 제노프스 등장 이벤트를 즉시 발생시킵니다.
    /// EventManager에 XenopsAppearance 카테고리 이벤트가 있으면 그것을 사용하고,
    /// 없거나 cheatXenopsDataId가 설정된 경우 직접 스폰합니다.
    /// </summary>
    private void TriggerXenopsSpawnEvent()
    {
        // cheatXenopsDataId가 설정된 경우 직접 스폰
        if (cheatXenopsDataId > 0)
        {
            SpawnXenopsDirectly(cheatXenopsDataId);
            return;
        }

        // EventManager의 XenopsAppearance 이벤트 사용
        if (EventManager.instance != null)
        {
            EventManager.instance.TriggerXenopsSpawnEvent();
            Debug.Log("[CheatManager] 치트 적용: 제노프스 등장 이벤트 발생");
        }
        else
        {
            Debug.LogWarning("[CheatManager] EventManager가 없습니다. 직접 스폰을 시도합니다.");
            SpawnRandomXenops();
        }
    }

    /// <summary>
    /// 특정 ID의 제노프스를 카메라 근처에 직접 스폰합니다.
    /// </summary>
    private void SpawnXenopsDirectly(int xenopsDataId)
    {
        if (XenopsManager.instance == null)
        {
            Debug.LogWarning("[CheatManager] XenopsManager가 없습니다.");
            return;
        }

        Vector3 spawnPos = GetSpawnPosition();
        var xenops = XenopsManager.instance.SpawnXenops(xenopsDataId, spawnPos);
        if (xenops != null)
        {
            xenops.SetState(XenopsState.Active);
            Debug.Log($"[CheatManager] 치트 적용: {xenops.DisplayName} 직접 스폰 at {spawnPos}");
        }
    }

    /// <summary>
    /// GameDatabase에서 랜덤 제노프스를 직접 스폰합니다.
    /// </summary>
    private void SpawnRandomXenops()
    {
        if (XenopsManager.instance == null || GameDatabase.Instance == null) return;

        var allXenopsData = GameDatabase.Instance.allXenopsData;
        if (allXenopsData == null || allXenopsData.Count == 0)
        {
            Debug.LogWarning("[CheatManager] GameDatabase에 등록된 XenopsData가 없습니다.");
            return;
        }

        var data = allXenopsData[Random.Range(0, allXenopsData.Count)];
        Vector3 spawnPos = GetSpawnPosition();
        var xenops = XenopsManager.instance.SpawnXenops(data, spawnPos);
        if (xenops != null)
        {
            xenops.SetState(XenopsState.Active);
            Debug.Log($"[CheatManager] 치트 적용: {xenops.DisplayName} 랜덤 스폰 at {spawnPos}");
        }
    }

    /// <summary>카메라 근처 랜덤 스폰 위치를 반환합니다.</summary>
    private Vector3 GetSpawnPosition()
    {
        if (Camera.main != null)
        {
            Vector3 cam = Camera.main.transform.position;
            return new Vector3(
                cam.x + Random.Range(-8f, 8f),
                cam.y + Random.Range(-3f, 3f),
                0f
            );
        }
        return Vector3.zero;
    }
}
