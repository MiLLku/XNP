using UnityEngine;

/// <summary>
/// 맵에 인스턴스화된 개체가 "자기가 어떤 EntityType인지" 들고 다니게 하는 꼬리표.
///
/// MapRenderer가 프리팹을 만들 때 붙여 줍니다. 프리팹을 고칠 필요가 없습니다.
///
/// 이게 없던 시절에는 세이브 캡처가 <c>FindObjectsByType&lt;ChoppableTree&gt;()</c>처럼
/// 컴포넌트 타입마다 분기해야 했고, 그래서 베리 덤불처럼 분기를 빠뜨린 개체는
/// 저장되지 않은 채 로드 때 사라졌습니다. 이제는 꼬리표 하나만 훑으면 되므로
/// 새 식생물을 추가해도 세이브 코드를 건드릴 일이 없습니다.
/// </summary>
[DisallowMultipleComponent]
public class MapEntityIdentity : MonoBehaviour
{
    [Tooltip("EntityType의 raw int 값. MapRenderer가 생성 시 채웁니다.")]
    [SerializeField] private int entityId;

    /// <summary>개체 ID (EntityType의 raw 값).</summary>
    public int EntityId => entityId;

    /// <summary>개체 종류.</summary>
    public EntityType Type => (EntityType)entityId;

    /// <summary>이 개체의 정의 에셋. 없으면 null.</summary>
    public EntityDefinition Definition => DefinitionDatabase.Instance != null
        ? DefinitionDatabase.Instance.GetEntity(entityId)
        : null;

    /// <summary>생성 직후 ID를 새깁니다.</summary>
    public void Assign(int id) => entityId = id;

    /// <summary>대상 GameObject에 꼬리표를 붙이거나, 이미 있으면 ID만 갱신합니다.</summary>
    public static void Attach(GameObject target, int id)
    {
        if (target == null) return;

        var identity = target.GetComponent<MapEntityIdentity>();
        if (identity == null) identity = target.AddComponent<MapEntityIdentity>();
        identity.Assign(id);
    }
}
