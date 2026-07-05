using System.Collections.Generic;

/// <summary>
/// 직원 재미(사기) 저하 경고 평가기. 재미가 낮은 직원은 침식에 취약해지므로
/// 플레이어에게 오락 시설·약물 확보를 유도합니다.
/// 출력(라벨/심각도/대상)은 이 코드에서 생성, 기준값만 Config(SO)에서 읽습니다.
/// </summary>
public class FunAlertEvaluator : IAlertEvaluator
{
    private readonly FunAlertConfig cfg;

    public FunAlertEvaluator(FunAlertConfig cfg)
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

            float fun = e.Needs.fun;
            if (fun < cfg.criticalThreshold)
            {
                if (critical == null) critical = new List<Employee>();
                critical.Add(e);
            }
            else if (fun < cfg.cautionThreshold)
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
                label = $"사기 저하 심각: {critical.Count}명",
                culprits = critical
            };

        if (caution != null)
            return new AlertReport
            {
                active = true,
                severity = AlertSeverity.Caution,
                label = $"사기 저하: {caution.Count}명",
                culprits = caution
            };

        return AlertReport.Inactive;
    }
}
