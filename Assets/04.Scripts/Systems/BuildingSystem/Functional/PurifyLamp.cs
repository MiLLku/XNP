using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 정화 등 — 거점 습격 파훼 설치물. 전력이 들어오는 동안 반경 RADIUS 안의 적 구조물 건설 속도와 효과를 절반으로 줄인다.
/// 판정은 구조물 쪽(<see cref="OutpostStructure"/>)이 <see cref="Suppresses"/>로 묻는다.
/// </summary>
public class PurifyLamp : MonoBehaviour
{
    public const float RADIUS = 10f; // shortcut: 임시 수치

    private static readonly List<PurifyLamp> lamps = new List<PurifyLamp>();

    private Building _building;
    private PowerConsumer _power;

    private void Awake()
    {
        _building = GetComponent<Building>();
        _power = GetComponent<PowerConsumer>();
    }

    private void OnEnable() => lamps.Add(this);
    private void OnDisable() => lamps.Remove(this);

    private bool Working => (_building == null || _building.IsFunctional) && (_power == null || _power.IsPowered);

    /// <summary>이 자리가 켜진 정화 등 반경 안인지</summary>
    public static bool Suppresses(Vector2 pos)
    {
        foreach (var l in lamps)
            if (l != null && l.Working && Vector2.Distance(pos, l.transform.position) <= RADIUS) return true;
        return false;
    }
}
