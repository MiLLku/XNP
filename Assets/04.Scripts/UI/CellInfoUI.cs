using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// 칸 정보 창 (좌상단, 안내 패널 바로 아래).
/// 마우스 포인터가 가리키는 칸의 이름·침식·온도를 보여 주고, 그 칸이 방 안이면 방의 크기·온도·기능을 덧붙입니다.
///
/// 안내 패널이 길어지거나 접히면 그 아래 끝을 따라 오르내립니다. 안내를 꺼 두면 안내 패널 자리로 올라옵니다.
/// 포인터가 UI 위에 있거나 맵 밖이면 숨습니다. 이 창의 그래픽은 raycastTarget을 꺼 두어 월드 클릭을 막지 않습니다.
/// </summary>
public class CellInfoUI : MonoBehaviour
{
    [SerializeField] private RectTransform panel;
    [SerializeField] private TMP_Text text;
    [Tooltip("안내(가이드라인) 패널 — 이 창은 그 바로 아래에 붙습니다")]
    [SerializeField] private RectTransform guidelinePanel;
    [Tooltip("안내 패널과의 간격 (픽셀)")]
    [SerializeField] private float gap = 6f;

    private const float REFRESH_INTERVAL = 0.2f;
    private static readonly Vector2Int NO_CELL = new Vector2Int(int.MinValue, int.MinValue);

    private readonly Vector3[] corners = new Vector3[4];
    private readonly StringBuilder builder = new StringBuilder();
    private Vector2Int lastCell = NO_CELL;
    private float timer;

    private void LateUpdate()
    {
        bool show = TryGetPointerCell(out Vector2Int cell);
        if (panel.gameObject.activeSelf != show) panel.gameObject.SetActive(show);
        if (!show)
        {
            lastCell = NO_CELL;
            return;
        }

        FollowGuideline();

        // 칸이 바뀌면 바로, 같은 칸이면 수치만 주기적으로 갱신
        timer += Time.unscaledDeltaTime;
        if (cell == lastCell && timer < REFRESH_INTERVAL) return;
        timer = 0f;
        lastCell = cell;
        text.text = Describe(cell);
    }

    private static bool TryGetPointerCell(out Vector2Int cell)
    {
        cell = NO_CELL;
        if (UIManager.PointerOverUI || CameraController.Instance == null) return false;

        Vector3 world = CameraController.Instance.GetMouseWorldPosition();
        cell = new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));
        return cell.x >= 0 && cell.x < GameMap.MAP_WIDTH && cell.y >= 0 && cell.y < GameMap.MAP_HEIGHT;
    }

    /// <summary>안내 패널의 아래 끝에 붙습니다. 안내가 꺼져 있으면 그 자리(위 끝)로 올라옵니다.</summary>
    private void FollowGuideline()
    {
        if (guidelinePanel == null) return;

        guidelinePanel.GetWorldCorners(corners); // 0 = 왼쪽 아래, 1 = 왼쪽 위
        panel.position = guidelinePanel.gameObject.activeInHierarchy
            ? corners[0] + Vector3.down * (gap * panel.lossyScale.y)
            : corners[1];
    }

    private string Describe(Vector2Int cell)
    {
        // 가 보지 않은 곳은 아무것도 알려 주지 않는다
        if (FogOfWarManager.instance != null && !FogOfWarManager.instance.IsExplored(cell.x, cell.y))
            return "<b>미탐색 지역</b>";

        float erosion = TerrainErosionManager.instance != null ? TerrainErosionManager.instance.GetRoomErosionAt(cell.x, cell.y) : 0f;
        float temperature = TemperatureManager.instance != null ? TemperatureManager.instance.GetTemperatureAt(cell.x, cell.y) : 0f;
        Room room = RoomManager.instance != null ? RoomManager.instance.GetRoom(cell.x, cell.y) : null;

        builder.Clear();
        builder.Append("<b>").Append(GetCellName(cell)).Append("</b>");
        builder.Append("\n침식 ").Append(erosion.ToString("0.#"));

        // 방 안이면 온도는 방의 것이다 — 방 정보 쪽에 적는다
        if (room == null)
        {
            builder.Append("\n온도 ").Append(temperature.ToString("0.#")).Append("도");
        }
        else
        {
            builder.Append("\n\n<b>방</b>  <color=#9AA0AA>").Append(room.CellCount).Append("칸</color>");
            builder.Append("\n온도 ").Append(temperature.ToString("0.#")).Append("도");
            builder.Append("\n기능 ").Append(GetRoomFunction(room));
        }
        return builder.ToString();
    }

    /// <summary>그 칸에 있는 것의 이름 — 지형 타일, 없으면 건설한 바닥 타일·건물, 그것도 없으면 빈 공간.</summary>
    private static string GetCellName(Vector2Int cell)
    {
        GameMap map = MapGenerator.instance != null ? MapGenerator.instance.GameMapInstance : null;
        int tileId = map != null ? map.TileGrid[cell.x, cell.y] : (int)TileType.Air;
        if (tileId != (int)TileType.Air)
        {
            TileDefinition tile = TileDefinitionLookup.Find(tileId);
            if (tile != null) return tile.Label;
        }

        // 스탬프로 깔린 바닥 타일은 건물 데이터가 없어서 바닥 타일 정의에서 이름을 읽는다
        FloorTile floor = FloorTile.GetFloorTileAt(cell);
        if (floor != null)
        {
            FloorTileDefinition definition = DefinitionDatabase.Instance != null ? DefinitionDatabase.Instance.GetFloor(floor.TileType) : null;
            if (definition != null) return definition.Label;
        }

        Building building = Building.GetBuildingAt(cell);
        if (building != null) return building.DisplayName;

        TileDefinition air = TileDefinitionLookup.Find(TileType.Air);
        return air != null ? air.Label : "빈 공간";
    }

    // ponytail: 방 기능(연구소·작업소·침실·세척실·조리실 …) 판정은 아직 없다 — 생기면 여기서 그 이름을 돌려줄 것
    private static string GetRoomFunction(Room room) => "없음";
}
