using System;
using UnityEngine;

/// <summary>
/// 메시지 로그에 표시되는 단일 레터(편지).
/// 이벤트 발생 등 "이미 일어난 일"을 1회성으로 알린다. 클릭 확인 시 소멸한다.
/// 런타임 인스턴스이며 SO가 아니다(이벤트마다 새로 생성).
/// </summary>
public class Letter
{
    /// <summary>NotificationManager가 부여하는 고유 ID</summary>
    public int id;

    public string title;
    public string body;
    public LetterType type = LetterType.Neutral;
    public Sprite icon;

    /// <summary>생성 시점(Time.time)</summary>
    public float createdGameTime;

    /// <summary>클릭 시 부가 동작(카메라 이동 등). null 허용.</summary>
    public Action onClick;

    /// <summary>이벤트 출신이면 채워짐(상세 표시·선택지 확장용). null 허용.</summary>
    public EventData sourceEvent;

    /// <summary>확인 전까지 일시정지를 유지할지 여부(위협 레터).</summary>
    public bool pauseUntilRead;
}
