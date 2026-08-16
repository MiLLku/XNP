using System;
using UnityEngine;

/// <summary>
/// 세척 시설 부재 경고 기준값(SO 내부 클래스로 NotificationManager에 주입).
///
/// 침식은 자연 회복만으로는 하한 아래로 내려가지 않는다. 즉 <b>세척 시설이 없으면
/// 침식을 완전히 지울 방법이 없다</b> — 플레이어가 이 사실을 모르고 방치하지 않도록 알린다.
/// </summary>
[Serializable]
public class WashStationAlertConfig
{
    [Tooltip("이 경고 활성화 여부")]
    public bool enabled = true;

    [Tooltip("세척 시설이 없을 때 팝업(레터)을 띄울지 여부. 배너는 항상 표시됩니다.")]
    public bool pushLetter = true;

    [Tooltip("레터를 다시 띄우기까지의 최소 간격(초). 0이면 게임당 1회만 띄웁니다.")]
    [Min(0f)] public float letterRepeatInterval = 0f;

    [Tooltip("레터 확인 전까지 게임을 일시정지할지 여부")]
    public bool pauseUntilRead = false;
}
