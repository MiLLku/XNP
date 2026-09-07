using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// DefinitionDatabase 인스펙터.
/// 목록 아래에 검증 결과와 enum 재생성 버튼을 붙여, 정의를 고친 뒤
/// 프로젝트 창을 떠나지 않고 바로 반영할 수 있게 합니다.
/// </summary>
[CustomEditor(typeof(DefinitionDatabase))]
public class DefinitionDatabaseEditor : Editor
{
    private List<string> _problems;
    private bool _validatedOnce;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var db = (DefinitionDatabase)target;

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("정의 → 코드", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "정의 에셋이 식별자의 원천입니다.\n" +
            "목록을 고친 뒤 [enum 재생성]을 눌러야 TileType / EntityType / FloorTileType에 반영됩니다.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("정의 검증", GUILayout.Height(24)))
            {
                _problems = DefinitionEnumGenerator.ValidateAll(db);
                _validatedOnce = true;
            }

            if (GUILayout.Button("enum 재생성", GUILayout.Height(24)))
            {
                _problems = DefinitionEnumGenerator.ValidateAll(db);
                _validatedOnce = true;

                if (_problems.Count == 0)
                {
                    if (DefinitionEnumGenerator.Regenerate(db, out string report))
                        Debug.Log($"[DefinitionDatabase] {report}");
                    else
                        Debug.LogError($"[DefinitionDatabase] {report}");
                }
            }
        }

        if (GUILayout.Button("다음 사용 가능한 ID 보기"))
        {
            Debug.Log($"[DefinitionDatabase] 다음 빈 ID — " +
                      $"타일: {NextFreeId(db.tiles, d => d.id, GameIDRegistry.Tiles.RAW_MIN, GameIDRegistry.Tiles.RAW_MAX)}, " +
                      $"개체: {NextFreeId(db.entities, d => d.id, GameIDRegistry.Entities.RECOMMENDED_MIN, GameIDRegistry.Entities.RECOMMENDED_MAX)}, " +
                      $"바닥 타일: {NextFreeId(db.floorTiles, d => d.id, GameIDRegistry.FloorTiles.MIN, GameIDRegistry.FloorTiles.MAX)}");
        }

        if (_validatedOnce)
        {
            EditorGUILayout.Space(6);
            if (_problems.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    $"문제 없음 — 타일 {db.tiles.Count}종, 개체 {db.entities.Count}종, " +
                    $"바닥 타일 {db.floorTiles.Count}종.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"문제 {_problems.Count}건:\n• " + string.Join("\n• ", _problems),
                    MessageType.Error);
            }
        }
    }

    /// <summary>지정 대역에서 아직 쓰이지 않은 가장 작은 ID를 찾습니다.</summary>
    private static int NextFreeId<T>(List<T> source, System.Func<T, int> idOf, int min, int max)
        where T : UnityEngine.Object
    {
        var used = new HashSet<int>();
        if (source != null)
        {
            foreach (var item in source)
            {
                if (item != null) used.Add(idOf(item));
            }
        }

        for (int id = min; id <= max; id++)
        {
            if (!used.Contains(id)) return id;
        }
        return -1;
    }
}
