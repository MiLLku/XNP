/// <summary>
/// 시설 오브젝트의 Unity Tag 상수.
/// 마법 문자열 대신 이 클래스의 상수를 사용하세요.
///
/// 사용 예:
///   GameObject.FindGameObjectsWithTag(FacilityTag.Bed)
///   gameObject.CompareTag(FacilityTag.WashStation)
///
/// 태그 추가/변경 시 이 파일만 수정하면 됩니다.
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
}
