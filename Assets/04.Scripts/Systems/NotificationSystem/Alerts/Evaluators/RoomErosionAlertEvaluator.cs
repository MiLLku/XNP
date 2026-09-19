using UnityEngine;

/// <summary>
/// 침식이 심한 방 경고 평가기.
///
/// 방 침식은 스스로 줄지 않으므로 <b>조건이 유지되는 동안 배너가 계속 떠 있습니다</b>.
/// 배너를 누르면 가장 심한 방으로 화면이 이동합니다 — 넓은 지하에서 어디가 문제인지
/// 찾아다니지 않아도 되게 하기 위한 것입니다.
///
/// 해결 수단은 셋입니다: 발원지 채광 · 세척 작업 · 환기(벽을 뚫어 실외로).
/// </summary>
public class RoomErosionAlertEvaluator : IAlertEvaluator
{
    private readonly RoomErosionAlertConfig cfg;

    /// <summary>레터를 마지막으로 띄운 시각. 음수면 아직 안 띄움.</summary>
    private float lastLetterTime = -1f;

    public RoomErosionAlertEvaluator(RoomErosionAlertConfig cfg)
    {
        this.cfg = cfg;
    }

    public bool Enabled => cfg != null && cfg.enabled;

    public AlertReport Evaluate()
    {
        RoomManager manager = RoomManager.instance;
        if (manager == null) return AlertReport.Inactive;

        // 가장 심한 방 하나를 대표로 잡는다 — 배너 클릭이 그곳으로 이동한다
        Room worst = null;
        int overThreshold = 0;

        foreach (var pair in manager.Rooms)
        {
            Room room = pair.Value;
            if (room.Erosion < cfg.cautionThreshold) continue;
            if (!IsRevealed(room)) continue;

            overThreshold++;
            if (worst == null || room.Erosion > worst.Erosion) worst = room;
        }

        if (worst == null)
        {
            // 다 정리되면 레터를 다시 띄울 수 있게 초기화
            lastLetterTime = -1f;
            return AlertReport.Inactive;
        }

        bool danger = worst.Erosion >= cfg.dangerThreshold;
        if (danger) TryPushLetter(worst);

        string suffix = overThreshold > 1 ? $" (외 {overThreshold - 1}곳)" : "";
        string label = danger
            ? $"침식 위험: 방 침식 {worst.Erosion:F0}{suffix} — 즉시 조치 필요"
            : $"침식 주의: 방 침식 {worst.Erosion:F0}{suffix}";

        return new AlertReport
        {
            active = true,
            severity = danger ? AlertSeverity.Critical : AlertSeverity.Caution,
            label = label,
            focusPosition = new Vector3(worst.Representative.x + 0.5f, worst.Representative.y + 0.5f, 0f)
        };
    }

    /// <summary>
    /// 한 칸이라도 안개가 걷힌 방만 경고 대상입니다.
    ///
    /// 아직 못 본 지하의 방까지 세면 플레이어가 손쓸 수 없는 경고가 계속 떠 있게 됩니다
    /// (배너를 눌러 이동해도 안개뿐입니다).
    /// </summary>
    private static bool IsRevealed(Room room)
    {
        var fog = FogOfWarManager.instance;
        if (fog == null) return true;   // 안개 시스템이 없으면 전부 보이는 것으로 친다

        // ponytail: 방 칸을 전부 훑는다. 안 보이는 큰 방이 최악이지만 방 개수 × 칸 수 정도라
        // 경고 평가 주기에서 문제된 적 없음. 느려지면 방에 '공개됨' 플래그를 캐시할 것
        foreach (var cell in room.Cells)
            if (fog.IsRevealed(cell.x, cell.y)) return true;

        return false;
    }

    private void TryPushLetter(Room worst)
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
            title = "침식이 심한 공간이 있습니다",
            body = $"어떤 공간의 침식이 {worst.Erosion:F0}까지 올랐습니다. " +
                   "이 안에서 일하는 직원은 머무는 동안 계속 침식됩니다.\n\n" +
                   "해결 방법은 셋입니다.\n" +
                   "· 발원지를 채광으로 캐낸다 — 더 오르지 않지만 이미 고인 것은 남습니다\n" +
                   "· 세척 작업을 건다 — 고인 것을 지우지만 발원지가 남아 있으면 다시 찹니다\n" +
                   "· 벽을 뚫어 환기한다 — 즉시 사라지지만 그 공간의 온도 제어를 포기하게 됩니다",
            type = LetterType.Threat,
            pauseUntilRead = cfg.pauseUntilRead
        });
    }
}
