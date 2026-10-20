using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 폭력 정신 이상 — 화풀이.
///
/// 둘 중 하나를 고른다 (할 수 있는 쪽만, 둘 다 되면 반반):
///   • 근처 건물 하나를 찾아가 부서질 때까지 때린다 (건물 파괴 충동의 '한 건물' 판)
///   • 창고의 무작위 아이템 한 종류를 일부 부숴 없앤다 (즉시, 레터로 무엇을 잃었는지 알림)
/// </summary>
public class AbnormalBehaviorTantrum : AbnormalBehaviorBase
{
    /// <summary>건물을 부술 때 지속 시간 (초)</summary>
    private const float SMASH_DURATION = 30f;

    /// <summary>물건을 부술 때 지속 시간 — 부수는 건 즉시, 남은 시간은 씩씩대며 손을 놓는다 (초)</summary>
    private const float ITEM_DURATION = 8f;

    /// <summary>한 종류에서 부수는 비율과 상한</summary>
    private const float ITEM_LOSS_FRACTION = 0.5f;
    private const int ITEM_LOSS_MAX = 20;

    private readonly AbnormalBehaviorAttackBuilding smash =
        new AbnormalBehaviorAttackBuilding(AbnormalBehaviorType.Tantrum, SMASH_DURATION, singleTarget: true);

    private readonly HashSet<Employee> smashing = new HashSet<Employee>();

    public override AbnormalBehaviorType BehaviorType => AbnormalBehaviorType.Tantrum;

    public override bool CanExecute(Employee employee)
        => base.CanExecute(employee) && (CanSmash(employee) || HasItems());

    public override float Execute(Employee employee)
    {
        bool canSmash = CanSmash(employee);
        if (canSmash && (!HasItems() || Random.value < 0.5f))
        {
            smashing.Add(employee);
            return smash.Execute(employee);
        }

        BreakRandomItems(employee);
        return ITEM_DURATION;
    }

    public override void Tick(Employee employee, float deltaTime)
    {
        if (smashing.Contains(employee)) smash.Tick(employee, deltaTime);
    }

    public override void OnEnd(Employee employee)
    {
        if (smashing.Remove(employee)) smash.OnEnd(employee);
    }

    private static bool CanSmash(Employee employee)
        => AbnormalBehaviorAttackBuilding.FindNearestTarget(employee) != null;

    private static bool HasItems()
        => InventoryManager.instance != null && InventoryManager.instance.GetTotalItemCount() > 0;

    private static void BreakRandomItems(Employee employee)
    {
        var inv = InventoryManager.instance;
        if (inv == null) return;

        var stacks = inv.GetAllItems().FindAll(kv => kv.Key != null && kv.Value > 0);
        if (stacks.Count == 0) return;

        var pick = stacks[Random.Range(0, stacks.Count)];
        int amount = Mathf.Clamp(Mathf.CeilToInt(pick.Value * ITEM_LOSS_FRACTION), 1, ITEM_LOSS_MAX);
        if (!inv.RemoveItem(pick.Key, amount)) return;

        NotificationManager.instance?.PushLetter(new Letter
        {
            title = $"화풀이 — {employee.DisplayName}",
            body = $"{employee.DisplayName}이(가) 창고의 {pick.Key.itemName} {amount}개를 부숴 버렸습니다.",
            type = LetterType.Threat
        });
    }
}
