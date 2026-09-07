using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 정의 에셋(TileDefinition / EntityDefinition / FloorTileDefinition)에서
/// TileType · EntityType · FloorTileType enum 소스를 생성하는 에디터 도구.
///
/// <b>이 프로젝트의 식별자 흐름</b>
///   정의 에셋(진실의 원천) → [enum 재생성] → Core/Types/*.cs → 컴파일 → 기존 코드가 그대로 사용
///
/// enum을 유지하는 이유는 인스펙터 드롭다운·switch 문·세이브 호환성을 모두 살리기 위해서입니다.
/// 새 블록이나 식생물을 추가할 때는 에셋만 만들고 이 메뉴를 실행하면 됩니다.
/// </summary>
public static class DefinitionEnumGenerator
{
    #region 상수

    private const string TypesDir = "Assets/04.Scripts/Core/Types";
    private const string TilePath = TypesDir + "/TileType.cs";
    private const string EntityPath = TypesDir + "/EntityType.cs";
    private const string FloorPath = TypesDir + "/FloorTileType.cs";

    private const string MenuRoot = "XNP/정의/";

    #endregion

    #region 메뉴

    [MenuItem(MenuRoot + "enum 재생성 %#g", priority = 0)]
    public static void RegenerateFromMenu()
    {
        var db = FindDatabase();
        if (db == null)
        {
            EditorUtility.DisplayDialog(
                "정의 데이터베이스를 찾을 수 없습니다",
                $"Resources/{DefinitionDatabase.ResourcePath}.asset 이 없습니다.\n\n" +
                "메뉴 [XNP/정의/현재 코드에서 정의 에셋 생성]을 먼저 실행하세요.",
                "확인");
            return;
        }

        if (!Regenerate(db, out string report))
        {
            EditorUtility.DisplayDialog("enum 재생성 실패", report, "확인");
            return;
        }

        Debug.Log($"[DefinitionEnumGenerator] {report}");
        EditorUtility.DisplayDialog("enum 재생성 완료", report, "확인");
    }

    [MenuItem(MenuRoot + "정의 검증", priority = 1)]
    public static void ValidateFromMenu()
    {
        var db = FindDatabase();
        if (db == null)
        {
            Debug.LogError("[DefinitionEnumGenerator] 정의 데이터베이스를 찾을 수 없습니다.");
            return;
        }

        var problems = ValidateAll(db);
        if (problems.Count == 0)
        {
            Debug.Log($"[DefinitionEnumGenerator] 검증 통과 — 타일 {db.tiles.Count}종, " +
                      $"개체 {db.entities.Count}종, 바닥 타일 {db.floorTiles.Count}종.");
            return;
        }

        Debug.LogError($"[DefinitionEnumGenerator] 문제 {problems.Count}건:\n  - " + string.Join("\n  - ", problems));
    }

    [MenuItem(MenuRoot + "정의 데이터베이스 선택", priority = 2)]
    public static void SelectDatabase()
    {
        var db = FindDatabase();
        if (db == null)
        {
            Debug.LogError("[DefinitionEnumGenerator] 정의 데이터베이스를 찾을 수 없습니다.");
            return;
        }
        Selection.activeObject = db;
        EditorGUIUtility.PingObject(db);
    }

    #endregion

    #region 생성

    /// <summary>프로젝트에서 정의 데이터베이스 에셋을 찾습니다.</summary>
    public static DefinitionDatabase FindDatabase()
    {
        var loaded = Resources.Load<DefinitionDatabase>(DefinitionDatabase.ResourcePath);
        if (loaded != null) return loaded;

        // Resources 밖에 있어도 편집은 가능하도록 프로젝트 전체를 뒤진다
        var guids = AssetDatabase.FindAssets("t:DefinitionDatabase");
        if (guids.Length == 0) return null;

        return AssetDatabase.LoadAssetAtPath<DefinitionDatabase>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    /// <summary>
    /// 정의 에셋에서 세 개의 enum 파일을 다시 씁니다.
    /// 검증에 실패하면 아무 파일도 건드리지 않습니다.
    /// </summary>
    /// <param name="db">정의 데이터베이스</param>
    /// <param name="report">사람이 읽을 결과 문장</param>
    /// <returns>생성에 성공했으면 true</returns>
    public static bool Regenerate(DefinitionDatabase db, out string report)
    {
        db.RebuildCaches();

        var problems = ValidateAll(db);
        if (problems.Count > 0)
        {
            report = $"정의에 문제가 {problems.Count}건 있어 생성을 중단했습니다:\n  - "
                     + string.Join("\n  - ", problems);
            return false;
        }

        // 빈 목록으로 enum을 날려버리는 사고 방지
        if (db.tiles.Count == 0 || db.entities.Count == 0 || db.floorTiles.Count == 0)
        {
            report = "정의 목록 중 비어 있는 것이 있습니다. " +
                     "빈 enum이 생성되면 프로젝트 전체가 컴파일되지 않으므로 중단했습니다.";
            return false;
        }

        var rows = new List<Row>();

        // ── TileType ────────────────────────────────────────────────────────
        rows.Clear();
        foreach (var d in db.tiles) rows.Add(new Row(d.codeName, d.id, d.Label, d.description));
        WriteEnum(TilePath, "TileType", rows,
            "블록(지형 타일) 종류 식별자.",
            "정수값 = GameMap.TileGrid의 raw int 값과 같습니다.",
            "TileDefinition");

        // ── EntityType ──────────────────────────────────────────────────────
        rows.Clear();
        foreach (var d in db.entities) rows.Add(new Row(d.codeName, d.id, d.Label, d.description));
        WriteEnum(EntityPath, "EntityType", rows,
            "맵에 배치되는 개체(식생물·건물·바닥 프리팹) 식별자.",
            "정수값 = MapEntity.id / StampElement.id의 raw int 값과 같습니다.",
            "EntityDefinition");

        // ── FloorTileType ───────────────────────────────────────────────────
        rows.Clear();
        foreach (var d in db.floorTiles) rows.Add(new Row(d.codeName, d.id, d.Label, d.description));
        WriteEnum(FloorPath, "FloorTileType", rows,
            "건설되는 바닥 타일 종류 식별자.",
            "정수값 = FloorTile 프리팹에 직렬화된 값과 같습니다.",
            "FloorTileDefinition");

        AssetDatabase.Refresh();

        report = $"enum 3종 재생성 완료 — 타일 {db.tiles.Count}종, " +
                 $"개체 {db.entities.Count}종, 바닥 타일 {db.floorTiles.Count}종.";
        return true;
    }

    /// <summary>
    /// 정의 자체의 검증(DefinitionDatabase.Validate)에 에디터에서만 가능한 검사를 더합니다.
    /// 인스펙터 버튼과 메뉴, enum 생성이 모두 이 창구를 씁니다.
    /// </summary>
    public static List<string> ValidateAll(DefinitionDatabase db)
    {
        var problems = db.Validate();
        problems.AddRange(ValidateStampKeys(db));
        return problems;
    }

    /// <summary>
    /// 스탬프로 배치되는 개체의 stampKey가 StampLibrary에 실제로 있는지 확인합니다.
    ///
    /// 리팩터링 도중 나무의 키를 "TREE_2X3"로 적었는데 라이브러리의 실제 키는 "TREE_2x3"라서
    /// 나무가 조용히 하나도 생기지 않은 적이 있습니다. PlaceStamp는 키가 틀려도 예외를 던지지
    /// 않으므로, 오타를 잡아 주는 곳이 여기밖에 없습니다.
    /// </summary>
    private static List<string> ValidateStampKeys(DefinitionDatabase db)
    {
        var problems = new List<string>();

        var guids = AssetDatabase.FindAssets("t:StampLibrary");
        if (guids.Length == 0)
        {
            problems.Add("StampLibrary 에셋을 찾지 못해 스탬프 키를 검증하지 못했습니다.");
            return problems;
        }

        var library = AssetDatabase.LoadAssetAtPath<StampLibrary>(AssetDatabase.GUIDToAssetPath(guids[0]));
        var known = new List<string>();
        foreach (var stamp in library.GetAllStamps())
        {
            if (stamp != null && !string.IsNullOrEmpty(stamp.key)) known.Add(stamp.key);
        }

        foreach (var e in db.entities)
        {
            if (e == null || !e.spawnOnMapGeneration) continue;
            if (e.placementMode != PlacementMode.Stamp) continue;
            if (known.Contains(e.stampKey)) continue;

            problems.Add($"개체 '{e.codeName}': stampKey '{e.stampKey}'가 StampLibrary에 없습니다 " +
                         $"(대소문자 구분). 사용 가능한 키: {string.Join(", ", known)}");
        }

        return problems;
    }

    private readonly struct Row
    {
        public readonly string Code;
        public readonly int Id;
        public readonly string Label;
        public readonly string Description;

        public Row(string code, int id, string label, string description)
        {
            Code = code;
            Id = id;
            Label = label;
            Description = description;
        }
    }

    private static void WriteEnum(string path, string enumName, List<Row> rows,
        string summary, string idNote, string sourceType)
    {
        rows.Sort((a, b) => a.Id.CompareTo(b.Id));

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("//     이 파일은 DefinitionEnumGenerator가 생성했습니다. 직접 수정하지 마세요.");
        sb.AppendLine($"//     원본: {sourceType} 에셋 (DefinitionDatabase에 등록된 것)");
        sb.AppendLine("//     수정하려면 정의 에셋을 고친 뒤 메뉴 [XNP/정의/enum 재생성]을 실행하세요.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// {summary}");
        sb.AppendLine("///");
        sb.AppendLine($"/// {idNote}");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public enum {enumName}");
        sb.AppendLine("{");

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            string doc = string.IsNullOrWhiteSpace(row.Description)
                ? row.Label
                : $"{row.Label} — {row.Description.Replace("\r", " ").Replace("\n", " ")}";

            sb.AppendLine($"    /// <summary>{Escape(doc)}</summary>");
            sb.Append($"    {row.Code} = {row.Id},");
            sb.AppendLine();
            if (i < rows.Count - 1) sb.AppendLine();
        }

        sb.AppendLine("}");

        System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    #endregion
}
