using UnityEngine;

/// <summary>
/// 침식 배양기 — 방을 인공 침식 환경으로 유지합니다 (침식 작물 재배용).
///
/// 일정 시간마다, 방 침식이 목표치 아래면 침식 결정체 1개를 써서 방 침식을 <see cref="erosionPerCrystal"/>만큼 올립니다.
/// 목표치에 닿으면 멈추고, 떨어지면(환기·세척) 다시 채웁니다. 전력이 끊기면 멈춥니다.
///
/// <b>밀폐된 방에서만 의미가 있습니다</b> — 침식은 방 단위로 고이고 실외는 희석되므로 실외에 두면 아무 일도 하지 않습니다.
/// 결정체는 <see cref="ItemSupply"/>가 창고에서 자동 보급합니다.
/// </summary>
[RequireComponent(typeof(Building))]
public class ErosionIncubator : MonoBehaviour, IBuildingExtraSerializable, IBuildingFunction
{
    public const float TARGET_MIN = 0f;
    public const float TARGET_MAX = 100f;

    [Header("배양")]
    [Tooltip("목표 방 침식 (플레이어가 창에서 조절)")]
    [SerializeField, Range(TARGET_MIN, TARGET_MAX)] private float targetErosion = 50f;

    [Tooltip("결정체 1개로 올리는 방 침식량")]
    [SerializeField, Min(0.1f)] private float erosionPerCrystal = 2f;

    [Tooltip("배양 주기(초) — 이 주기마다 최대 결정체 1개를 씁니다")]
    [SerializeField, Min(0.5f)] private float interval = 10f;

    [Header("침식 결정체")]
    [SerializeField] private ItemSupply crystalSupply = new ItemSupply();

    private Building building;
    private PowerConsumer power;
    private float timer;

    public float TargetErosion => targetErosion;
    public ItemSupply CrystalSupply => crystalSupply;

    public bool IsPowered => (building == null || building.IsFunctional) && (power == null || power.IsPowered);

    /// <summary>이 기기가 선 방 (실외·벽 속이면 null)</summary>
    public Room CurrentRoom => FindRoom();

    public void SetTargetErosion(float value) => targetErosion = Mathf.Clamp(value, TARGET_MIN, TARGET_MAX);

    private void Awake()
    {
        building = GetComponent<Building>();
        power = GetComponent<PowerConsumer>();
        crystalSupply.Bind(this);
    }

    private void Update()
    {
        crystalSupply.Tick();

        timer += Time.deltaTime;
        if (timer < interval) return;
        timer = 0f;

        if (!IsPowered) return;
        var room = FindRoom();
        if (room == null || room.Erosion >= targetErosion) return;
        if (!crystalSupply.TryConsume(1)) return;

        room.Erosion = Mathf.Min(targetErosion, room.Erosion + erosionPerCrystal);
    }

    /// <summary>자기 칸이 벽 취급이면 방에 속하지 않으므로 위·좌·우 칸의 방을 봅니다.</summary>
    private Room FindRoom()
    {
        if (RoomManager.instance == null) return null;
        var c = Vector2Int.FloorToInt(transform.position);
        foreach (var d in new[] { Vector2Int.zero, Vector2Int.up, Vector2Int.left, Vector2Int.right })
        {
            var room = RoomManager.instance.GetRoom(c + d);
            if (room != null) return room;
        }
        return null;
    }

    /// <summary>UI 상태 문구</summary>
    public string DescribeStatus()
    {
        if (!IsPowered) return "전력 없음 — 멈춤";
        var room = FindRoom();
        if (room == null) return "밀폐된 방이 아님 — 실외에서는 침식이 고이지 않습니다";
        if (crystalSupply.Stock <= 0) return "침식 결정체 없음 — 보급 대기";
        return room.Erosion >= targetErosion ? "목표 유지 중" : "배양 중";
    }

    private void OnDestroy()
    {
        if (SaveManager.instance != null && SaveManager.instance.IsLoading) return;
        crystalSupply.Cancel();
    }

    #region 클릭

    private void OnMouseDown()
    {
        if (UIManager.PointerOverUI || UIManager.instance == null) return;
        if (InteractionManager.instance != null &&
            InteractionManager.instance.GetCurrentMode() != InteractionManager.InteractMode.Normal) return;

        var panel = UIManager.instance.GetPanel<ErosionIncubatorUI>(UIPanelType.ErosionIncubatorUI);
        if (panel == null) { Debug.LogError("[ErosionIncubator] ErosionIncubatorUI 패널이 등록되지 않았습니다."); return; }

        panel.Setup(this);
        UIManager.instance.ShowPanel(UIPanelType.ErosionIncubatorUI);
    }

    #endregion

    #region IBuildingFunction

    public void OnBuildingDisabled() { }
    public void OnBuildingEnabled() { }
    public bool IsOperating => IsPowered && crystalSupply.Stock > 0 && FindRoom() != null;

    #endregion

    #region 세이브

    [System.Serializable]
    private class SaveState
    {
        public float target;
        public ItemSupply.SaveState crystal;
    }

    public string SerializeExtra() => JsonUtility.ToJson(new SaveState { target = targetErosion, crystal = crystalSupply.Capture() });

    public void DeserializeExtra(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        var s = JsonUtility.FromJson<SaveState>(json);
        if (s == null) return;
        SetTargetErosion(s.target);
        crystalSupply.Restore(s.crystal);
    }

    #endregion
}
