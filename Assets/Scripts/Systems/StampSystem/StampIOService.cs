using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 스탬프 파일 입출력 서비스.
/// 스탬프 데이터를 JSON 파일로 저장/로드합니다.
/// </summary>
public static class StampIOService
{
    /// <summary>스탬프 JSON 파일 저장 경로</summary>
    public static string StampSavePath => $"{Application.dataPath}/GameData/Stamps";

    /// <summary>
    /// 스탬프 데이터를 JSON 파일로 저장합니다.
    /// </summary>
    /// <param name="data">저장할 스탬프 데이터</param>
    public static void SaveStamp(StampData data)
    {
        if (data == null || string.IsNullOrEmpty(data.key)) return;

        Directory.CreateDirectory(StampSavePath);
        string filePath = Path.Combine(StampSavePath, $"{data.key}.json");
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(filePath, json);
        Debug.Log($"[StampIOService] 스탬프 저장: {filePath}");
    }

    /// <summary>
    /// 스탬프 폴더의 모든 JSON 파일을 불러옵니다.
    /// </summary>
    /// <returns>로드된 스탬프 데이터 리스트</returns>
    public static List<StampData> LoadAllStamps()
    {
        List<StampData> stamps = new List<StampData>();
        if (!Directory.Exists(StampSavePath))
        {
            Debug.LogWarning($"[StampIOService] 스탬프 폴더를 찾을 수 없습니다: {StampSavePath}");
            return stamps;
        }

        string[] files = Directory.GetFiles(StampSavePath, "*.json");
        foreach (string filePath in files)
        {
            string json = File.ReadAllText(filePath);
            StampData data = JsonUtility.FromJson<StampData>(json);
            if (data != null)
            {
                stamps.Add(data);
            }
        }
        Debug.Log($"[StampIOService] 스탬프 {stamps.Count}개 로드 완료.");
        return stamps;
    }
}
