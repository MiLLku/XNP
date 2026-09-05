using UnityEngine;

/// <summary>
/// 세척 시설 보관함 가득 참 경고 평가기.
///
/// 세척 시설은 산출한 침식 결정체를 건물 안에 쌓아두고, 상한에 닿으면 세척을 받지 않는다
/// (IsOperating = false). 운반이 밀려 모든 시설이 막히면 침식 제거가 통째로 멈추므로,
/// 원인을 배너로 바로 보여준다.
///
/// 배너만 쓴다 — 운반이 따라잡으면 저절로 풀리는 상태라 레터로 흐름을 끊을 일이 아니다.
/// 여러 곳이 동시에 막히면 라벨 끝에 " x3"처럼 개수를 붙인다.
/// </summary>
public class WashStationFullAlertEvaluator : IAlertEvaluator
{
    private readonly WashStationFullAlertConfig cfg;

    public WashStationFullAlertEvaluator(WashStationFullAlertConfig cfg)
    {
        this.cfg = cfg;
    }

    public bool Enabled => cfg != null && cfg.enabled;

    public AlertReport Evaluate()
    {
        int fullCount = 0;
        Vector3? firstFull = null;

        // 태그 미정의(UnityException)는 FacilityTag.FindAll이 빈 배열로 삼킨다
        foreach (var go in FacilityTag.FindAll(FacilityTag.WashStation))
        {
            if (go == null || !go.activeInHierarchy) continue;

            var station = go.GetComponent<WashStation>();
            if (station == null || !station.IsCrystalFull) continue;

            fullCount++;
            if (firstFull == null) firstFull = go.transform.position;
        }

        if (fullCount == 0) return AlertReport.Inactive;

        string label = "세척 시설 보관함이 가득 찼습니다 — 침식 결정체를 운반해야 다시 씻을 수 있습니다";
        if (fullCount >= 2) label += $" x{fullCount}";

        return new AlertReport
        {
            active = true,
            severity = AlertSeverity.Caution,
            label = label,
            focusPosition = firstFull
        };
    }
}
