using System.Collections.Generic;

/// <summary>
/// 직원 침식 경고 평가기. 모든 직원을 스캔해 가장 심각한 단계로 배너를 만든다.
/// 출력(라벨/심각도/대상)은 전부 이 코드에서 생성한다. 기준값만 Config(SO)에서 읽는다.
/// </summary>
public class ErosionAlertEvaluator : IAlertEvaluator
{
    /// <summary>완전 침식 수치 — EmployeeErosionController.FULL_EROSION_THRESHOLD와 동일.</summary>
    private const float FULL_EROSION = 200f;

    private readonly ErosionAlertConfig cfg;

    public ErosionAlertEvaluator(ErosionAlertConfig cfg)
    {
        this.cfg = cfg;
    }

    public bool Enabled => cfg != null && cfg.enabled;

    public AlertReport Evaluate()
    {
        var em = EmployeeManager.instance;
        if (em == null) return AlertReport.Inactive;

        List<Employee> caution = null;
        List<Employee> critical = null;

        foreach (var e in em.AllEmployees)
        {
            if (e == null || e.State == EmployeeState.Dead) continue;

            float frac = e.ErosionLevel / FULL_EROSION;
            if (frac >= cfg.criticalFraction)
            {
                if (critical == null) critical = new List<Employee>();
                critical.Add(e);
            }
            else if (frac >= cfg.cautionFraction)
            {
                if (caution == null) caution = new List<Employee>();
                caution.Add(e);
            }
        }

        if (critical != null)
            return new AlertReport
            {
                active = true,
                severity = AlertSeverity.Critical,
                label = $"침식 위험: {critical.Count}명",
                culprits = critical
            };

        if (caution != null)
            return new AlertReport
            {
                active = true,
                severity = AlertSeverity.Caution,
                label = $"침식 주의: {caution.Count}명",
                culprits = caution
            };

        return AlertReport.Inactive;
    }
}
