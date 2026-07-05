/// <summary>
/// 경고 평가기. 기준값(Config SO)을 참조해 월드를 스캔하고 출력(AlertReport)을 생성한다.
///
/// 역할 경계:
///   - Config(SO)   : 기준값(임계치)만 보관. 로직 없음.
///   - Evaluator(코드): 월드 스캔 + 라벨·심각도·대상 생성 = 출력 전부.
/// </summary>
public interface IAlertEvaluator
{
    /// <summary>이 경고가 활성화(평가 대상)인지 — 보통 Config.enabled를 위임</summary>
    bool Enabled { get; }

    /// <summary>현재 월드 상태를 평가해 경고 결과를 반환</summary>
    AlertReport Evaluate();
}
