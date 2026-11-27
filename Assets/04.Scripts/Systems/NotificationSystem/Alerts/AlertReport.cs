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

    /// <summary>
    /// 펼침 목록 — 지정되면 배너를 누를 때 카메라 이동 대신 배너 바로 아래로 상자 목록이 세로로 펼쳐진다(다시 누르면 접힘).
    /// 펼친 동안은 폴링마다 다시 불러 갱신한다.
    /// </summary>
    public System.Func<List<AlertDetailRow>> details;

    /// <summary>펼침 상태를 기억할 키 (배너 풀이 재배치돼도 같은 경고는 같은 상태로)</summary>
    public string key;

    /// <summary>비활성 결과 헬퍼</summary>
    public static AlertReport Inactive => new AlertReport { active = false };
}

/// <summary>경고 펼침 목록의 상자 한 칸</summary>
public struct AlertDetailRow
{
    public string label;
    /// <summary>상자를 눌렀을 때 (없으면 null)</summary>
    public System.Action onClick;
}
