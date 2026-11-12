using UnityEngine;

/// <summary>
/// 둥지 방에 사는 중립 침식체 한 마리. 제놉스가 아니라서 습격·전투 대상이 아니다 (적대화하면 적 제놉스로 바뀐다).
/// 판정·생산·사건은 <see cref="HusbandryManager"/>가 돌리고, 이 컴포넌트는 상태와 방 안 배회만 맡는다.
/// </summary>
public class NeutralCreature : MonoBehaviour
{
    private const float MOVE_SPEED = 0.6f;

    public NeutralSpeciesData Species { get; private set; }
    /// <summary>조건이 어긋난 채 지난 시간(초) — 사건 확률이 여기에 비례</summary>
    public float UnmetSeconds;
    /// <summary>생산 진행(초)</summary>
    public float ProduceSeconds;
    /// <summary>먹이 소비 누적(개) — 1이 되면 먹이통에서 하나 먹는다</summary>
    public float FeedAccumulator;
    /// <summary>마지막 판정에서 먹이를 먹었는지</summary>
    public bool Fed = true;
    public float HaltUntil;
    public float WanderUntil;
    /// <summary>마지막 판정에서 어긋난 조건 (상태 표시용)</summary>
    public string LastUnmet = "";

    private SpriteRenderer sr;
    private Vector3 target;
    private float retargetAt;

    public void Setup(NeutralSpeciesData species)
    {
        Species = species;
        name = "Neutral_" + species.displayName;
        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = species.sprite;
        sr.color = species.tint;
        sr.sortingOrder = 6;
        transform.localScale = Vector3.one * 0.7f;
        target = transform.position;
    }

    private void Update()
    {
        if (Time.time >= retargetAt)
        {
            retargetAt = Time.time + Random.Range(3f, 7f);
            var room = HusbandryManager.RoomAt(transform.position);
            target = PickTarget(room);
        }
        transform.position = Vector3.MoveTowards(transform.position, target, MOVE_SPEED * Time.deltaTime);
        if (sr != null && Mathf.Abs(target.x - transform.position.x) > 0.01f) sr.flipX = target.x < transform.position.x;
    }

    /// <summary>방 안 바닥 칸 하나 — 배회 중이면 방 밖 근처</summary>
    private Vector3 PickTarget(Room room)
    {
        if (Time.time < WanderUntil) return transform.position + new Vector3(Random.Range(-6f, 6f), 0f, 0f);
        if (room == null || room.Cells.Count == 0) return transform.position;
        var floorY = Mathf.FloorToInt(transform.position.y);
        for (int i = 0; i < 6; i++)
        {
            var c = room.Cells[Random.Range(0, room.Cells.Count)];
            if (c.y == floorY) return new Vector3(c.x + 0.5f, transform.position.y, 0f);
        }
        return transform.position;
    }
}
