using UnityEngine;

/// <summary>
/// 세척 시설 부재 경고 평가기.
///
/// 침식 회복 경로는 자연 회복(하한까지)·세척 시설·무작위 이벤트뿐이다.
/// 세척 시설이 없으면 <b>침식을 하한 아래로 지울 방법이 사라지므로</b>, 직원의 침식 상태와
/// 무관하게 시설 부재 자체를 알린다.
///
/// 배너는 조건이 유지되는 동안 계속 표시되고, 레터(팝업)는 중복을 막아 1회만 띄운다.
/// </summary>
public class WashStationAlertEvaluator : IAlertEvaluator
{
    private readonly WashStationAlertConfig cfg;

    /// <summary>레터를 마지막으로 띄운 시각 (Time.time). 음수면 아직 안 띄움.</summary>
    private float lastLetterTime = -1f;

    public WashStationAlertEvaluator(WashStationAlertConfig cfg)
    {
        this.cfg = cfg;
    }

    public bool Enabled => cfg != null && cfg.enabled;

    public AlertReport Evaluate()
    {
        if (HasAnyWashStation())
        {
            // 시설이 다시 생기면 레터를 재발행할 수 있도록 초기화
            lastLetterTime = -1f;
            return AlertReport.Inactive;
        }

        TryPushLetter();

        return new AlertReport
        {
            active = true,
            severity = AlertSeverity.Caution,
            label = "세척 시설 없음 — 침식을 완전히 제거할 수 없습니다"
        };
    }

    /// <summary>
    /// 세척 시설이 하나라도 존재하는지 확인합니다 (활성 상태면 존재로 간주).
    /// </summary>
    private static bool HasAnyWashStation()
    {
        GameObject[] stations;
        try
        {
            stations = GameObject.FindGameObjectsWithTag(FacilityTag.WashStation);
        }
        catch (UnityException)
        {
            // 태그가 프로젝트에 정의되지 않은 경우 — 시설도 없다고 본다
            return false;
        }

        if (stations == null) return false;

        foreach (var go in stations)
        {
            if (go != null && go.activeInHierarchy) return true;
        }

        return false;
    }

    /// <summary>중복을 막아 레터를 띄웁니다.</summary>
    private void TryPushLetter()
    {
        if (!cfg.pushLetter) return;
        if (NotificationManager.instance == null) return;

        bool firstTime = lastLetterTime < 0f;
        bool intervalPassed = cfg.letterRepeatInterval > 0f &&
                              Time.time - lastLetterTime >= cfg.letterRepeatInterval;

        if (!firstTime && !intervalPassed) return;

        lastLetterTime = Time.time;

        NotificationManager.instance.PushLetter(new Letter
        {
            title = "세척 시설이 없습니다",
            body = "침식은 자연 회복만으로는 일정 수치 아래로 내려가지 않습니다.\n" +
                   "세척 시설을 건설해야 직원의 침식을 완전히 제거할 수 있습니다.",
            type = LetterType.Threat,
            pauseUntilRead = cfg.pauseUntilRead
        });
    }
}
