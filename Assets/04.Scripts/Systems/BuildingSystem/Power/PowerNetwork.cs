using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 하나의 연결된 전력망. 4방향으로 이어진 발전기·축전기·소비건물·전선의 집합입니다.
/// 매 틱 실시간 수급(W)을 계산하고 잉여/부족을 축전기로 버퍼링합니다.
///
/// 분배 규칙:
/// 1. 가용 전력 = 총 생산 + 축전기 순간 방전 가능량.
/// 2. 소비자를 순차로 가동시키되, 누적 소비가 가용 전력을 넘으면 나머지는 정전(IsPowered=false).
/// 3. 가동 소비자 기준 순수입(net)이 양수면 축전기 충전, 음수면 축전기 방전.
/// </summary>
public class PowerNetwork
{
    public readonly List<PowerProducer> Producers = new List<PowerProducer>();
    public readonly List<PowerBattery> Batteries = new List<PowerBattery>();
    public readonly List<PowerConsumer> Consumers = new List<PowerConsumer>();
    public readonly List<PowerWire> Wires = new List<PowerWire>();

    /// <summary>직전 틱의 총 생산(W).</summary>
    public float LastProducedW { get; private set; }
    /// <summary>직전 틱의 충족된 소비(W).</summary>
    public float LastDemandW { get; private set; }
    /// <summary>현재 전력망에 저장된 총 전력(Joule).</summary>
    public float TotalStored
    {
        get { float s = 0f; foreach (var b in Batteries) s += b.CurrentCharge; return s; }
    }

    public void Tick(float dt)
    {
        if (dt <= 0f) return;

        float produced = 0f;
        foreach (var p in Producers) produced += p.CurrentOutput;

        float maxDischargeW = 0f;
        foreach (var b in Batteries) maxDischargeW += b.AvailableDischargeW(dt);

        float availableW = produced + maxDischargeW;

        // 소비자에 순차 배분 (가용 한도까지 가동, 초과분 정전)
        float used = 0f;
        foreach (var c in Consumers)
        {
            int need = Mathf.Max(0, c.Consumption);
            bool canPower = c.IsOnline && (used + need <= availableW + 0.001f);
            c.SetPowered(canPower);
            if (canPower) used += need;
        }

        LastProducedW = produced;
        LastDemandW = used;

        // 가동 소비자 기준 순수입을 축전기에 반영
        float net = produced - used;
        if (net >= 0f)
            DistributeCharge(net * dt, dt);
        else
            DistributeDischarge(-net * dt, dt);

        // 연료형 발전기 연료 소비
        foreach (var p in Producers) p.ConsumeFuel(dt);
    }

    private void DistributeCharge(float joules, float dt)
    {
        foreach (var b in Batteries)
        {
            if (joules <= 0f) break;
            joules -= b.Charge(joules, dt);
        }
    }

    private void DistributeDischarge(float joules, float dt)
    {
        foreach (var b in Batteries)
        {
            if (joules <= 0f) break;
            joules -= b.Discharge(joules, dt);
        }
    }
}
