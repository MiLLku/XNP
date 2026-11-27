using System.Collections.Generic;

/// <summary>
/// '낡은 옷' 주의 — 내구도 <see cref="THRESHOLD"/> 이하인 옷(헬멧·옷·장갑·신발·보호구)을 입은 직원이 있으면 띄운다 (사용자 결정).
/// 배너를 누르면 바로 아래로 해당 직원 상자(이름 · 낡은 장비 · 내구도 %)가 세로로 펼쳐지고, 다시 누르면 접힌다.
/// 상자를 누르면 그 직원의 관리창(장비 칸)이 열린다.
/// 해당 직원이 없어지면 배너가 사라진다 (경고는 매 폴링 다시 평가되므로 쌓이지 않는다).
/// </summary>
public class WornClothesAlertEvaluator : IAlertEvaluator
{
    /// <summary>내구도 비율이 이 값 이하면 낡은 옷</summary>
    public const float THRESHOLD = 0.3f;

    private static readonly EquipmentSlot[] ClothingSlots =
        { EquipmentSlot.Helmet, EquipmentSlot.Suit, EquipmentSlot.Gloves, EquipmentSlot.Shoes, EquipmentSlot.Protector };

    public struct Entry
    {
        public Employee employee;
        public string itemName;
        public float ratio;
    }

    public bool Enabled => true;

    public AlertReport Evaluate()
    {
        var entries = Collect();
        if (entries.Count == 0) return AlertReport.Inactive;

        var people = new List<Employee>();
        foreach (var en in entries) if (!people.Contains(en.employee)) people.Add(en.employee);

        return new AlertReport
        {
            active = true,
            severity = AlertSeverity.Caution,
            label = $"낡은 옷 — {people.Count}명 (내구도 {THRESHOLD * 100f:F0}% 이하)",
            culprits = people,
            key = "worn_clothes",
            details = BuildRows
        };
    }

    private static List<AlertDetailRow> BuildRows()
    {
        var rows = new List<AlertDetailRow>();
        foreach (var en in Collect())
        {
            var target = en.employee;
            rows.Add(new AlertDetailRow
            {
                label = $"{target.DisplayName} · {en.itemName} {en.ratio * 100f:F0}%",
                onClick = () => { if (target != null) EmployeeManagePanel.OpenFor(target); }
            });
        }
        return rows;
    }

    public static List<Entry> Collect()
    {
        var list = new List<Entry>();
        var em = EmployeeManager.instance;
        var db = GameDatabase.Instance;
        if (em == null || db == null) return list;

        foreach (var e in em.AllEmployees)
        {
            if (e == null || e.State == EmployeeState.Dead) continue;
            var eq = e.GetComponent<EmployeeEquipment>();
            if (eq == null) continue;
            foreach (var slot in ClothingSlots)
            {
                var inst = eq.GetInstanceInSlot(slot);
                if (inst == null) continue;
                var data = db.GetEquipmentData(inst.equipmentId, logMissing: false);
                if (data == null || data.indestructible) continue;
                float max = inst.MaxDurability(data);
                if (max <= 0f) continue;
                float ratio = inst.durability / max;
                if (ratio <= THRESHOLD)
                    list.Add(new Entry { employee = e, itemName = data.itemData != null ? data.itemData.itemName : slot.ToString(), ratio = ratio });
            }
        }
        return list;
    }
}
