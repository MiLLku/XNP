using System;
using UnityEngine;

/// <summary>
/// 세척 시설 보관함 가득 참 경고 기준값 (NotificationManager에 주입).
///
/// 세척 시설은 결정체를 건물 안에 쌓아두고, 상한에 닿으면 세척을 받지 않는다.
/// 즉 <b>운반이 밀리면 침식 제거가 통째로 멈춘다</b> — 플레이어가 원인을 모른 채
/// "직원이 안 씻으러 간다"고 오해하지 않도록 알린다.
///
/// 레터(팝업)는 쓰지 않는다: 운반이 따라잡으면 저절로 풀리는 상태라
/// 흐름을 끊을 만한 사건이 아니고, 반복되면 도배가 되기 때문이다.
/// </summary>
[Serializable]
public class WashStationFullAlertConfig
{
    [Tooltip("이 경고 활성화 여부")]
    public bool enabled = true;
}
