using UnityEngine;

/// <summary>
/// 계절. <see cref="DayCycle"/>의 경과 일수에서 파생되며 따로 저장하지 않습니다.
/// </summary>
public enum Season
{
    /// <summary>봄 — 온화</summary>
    Spring,
    /// <summary>여름 — 방열 장비가 필요해지는 시기</summary>
    Summer,
    /// <summary>가을 — 온화</summary>
    Autumn,
    /// <summary>겨울 — 난방과 방한 장비가 필요해지는 시기</summary>
    Winter,
}

/// <summary>
/// 계절 계산과 표시 이름.
///
/// 계절은 상태가 아니라 <b>경과 일수의 함수</b>입니다 — 저장할 것이 없고,
/// 세이브를 불러와도 날짜만 맞으면 자동으로 같은 계절이 됩니다.
/// </summary>
public static class SeasonCalendar
{
    /// <summary>계절 개수</summary>
    public const int SEASON_COUNT = 4;

    /// <summary>
    /// 경과 일수로 계절을 구합니다. 1일차가 봄의 첫날입니다.
    /// </summary>
    public static Season GetSeason(int day, int daysPerSeason)
    {
        if (daysPerSeason <= 0) return Season.Spring;

        int index = ((day - 1) / daysPerSeason) % SEASON_COUNT;
        if (index < 0) index += SEASON_COUNT;
        return (Season)index;
    }

    /// <summary>이번 계절이 며칠째인지 (1부터).</summary>
    public static int GetDayInSeason(int day, int daysPerSeason)
    {
        if (daysPerSeason <= 0) return day;

        int index = (day - 1) % daysPerSeason;
        if (index < 0) index += daysPerSeason;
        return index + 1;
    }

    /// <summary>표시 이름</summary>
    public static string GetDisplayName(Season season)
    {
        switch (season)
        {
            case Season.Spring: return "봄";
            case Season.Summer: return "여름";
            case Season.Autumn: return "가을";
            case Season.Winter: return "겨울";
            default:            return season.ToString();
        }
    }
}
