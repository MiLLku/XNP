using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 시설 오브젝트의 Unity Tag 상수.
/// 마법 문자열 대신 이 클래스의 상수를 사용하세요.
///
/// 사용 예:
///   FacilityTag.FindAll(FacilityTag.Bed)
///   gameObject.CompareTag(FacilityTag.WashStation)
///
/// 태그 추가/변경 시 이 파일과 ProjectSettings/TagManager.asset을 함께 수정하세요.
/// (여기 상수만 늘리고 프로젝트 태그를 빠뜨리면 조회 시 UnityException이 납니다)
/// </summary>
public static class FacilityTag
{
    /// <summary>침대 (수면 시설)</summary>
    public const string Bed = "Bed";

    /// <summary>오락 시설</summary>
    public const string Recreation = "Recreation";

    /// <summary>세척 시설</summary>
    public const string WashStation = "WashStation";

    /// <summary>음식 저장소</summary>
    public const string FoodStorage = "FoodStorage";

    /// <summary>정의된 시설 태그 전체. 프로젝트 태그 목록과 대조할 때 사용합니다.</summary>
    public static readonly string[] All = { Bed, Recreation, WashStation, FoodStorage };

    #region 안전한 태그 조회

    private static readonly GameObject[] Empty = Array.Empty<GameObject>();

    /// <summary>프로젝트에 정의되지 않아 조회가 실패한 태그. 예외 반복을 막습니다.</summary>
    private static readonly HashSet<string> _undefinedTags = new HashSet<string>();

    /// <summary>
    /// 해당 태그의 오브젝트를 모두 찾습니다.
    ///
    /// GameObject.FindGameObjectsWithTag는 프로젝트에 정의되지 않은 태그를 받으면
    /// UnityException을 던집니다. 시설 태그는 상수로만 존재하고 TagManager에는
    /// 아직 없을 수 있으므로(미구현 시설), 여기서 삼켜 빈 배열로 되돌립니다.
    /// 그렇지 않으면 호출부(예: EmployeeAI.ExecuteFreeTime) 중간에서 흐름이 끊깁니다.
    /// </summary>
    public static GameObject[] FindAll(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return Empty;
        if (_undefinedTags.Contains(tag)) return Empty;

        try
        {
            return GameObject.FindGameObjectsWithTag(tag) ?? Empty;
        }
        catch (UnityException)
        {
            _undefinedTags.Add(tag);
            Debug.LogWarning(
                $"[FacilityTag] 태그 '{tag}'가 프로젝트에 정의되어 있지 않습니다. " +
                $"시설이 없는 것으로 간주합니다. " +
                $"ProjectSettings/TagManager.asset에 추가하세요.");
            return Empty;
        }
    }

    /// <summary>해당 태그의 오브젝트가 하나라도 있는지 확인합니다.</summary>
    public static bool AnyExists(string tag)
    {
        var objs = FindAll(tag);
        for (int i = 0; i < objs.Length; i++)
        {
            if (objs[i] != null) return true;
        }
        return false;
    }

    /// <summary>
    /// 미정의 태그 기록을 지웁니다. 도메인 리로드가 꺼져 있어도
    /// 플레이 모드 진입 시 태그 추가가 즉시 반영되도록 합니다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetUndefinedTags() => _undefinedTags.Clear();

    #endregion
}
