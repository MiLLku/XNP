using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 경고 평가 결과. Evaluator가 매 폴링마다 반환하는 순수 출력 데이터.
/// </summary>
public struct AlertReport
{
    /// <summary>현재 경고가 활성 상태인지</summary>
    public bool active;

    /// <summary>심각도(배너 색 결정)</summary>
    public AlertSeverity severity;

    /// <summary>배너에 표시할 텍스트</summary>
    public string label;

    /// <summary>클릭 시 카메라가 이동할 대상 목록(없으면 null)</summary>
    public IReadOnlyList<Employee> culprits;

    /// <summary>
    /// 클릭 시 카메라가 이동할 좌표. 직원이 아니라 <b>장소</b>가 문제인 경고에 사용합니다
    /// (예: 침식이 심한 방). 지정되면 culprits보다 우선합니다.
    /// </summary>
    public Vector3? focusPosition;

    /// <summary>비활성 결과 헬퍼</summary>
    public static AlertReport Inactive => new AlertReport { active = false };
}
