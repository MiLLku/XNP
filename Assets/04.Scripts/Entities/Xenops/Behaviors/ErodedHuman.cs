using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>무기 값을 받는 적 행동 (침식된 인간의 몸 — 그래플러·침식 사수)</summary>
public interface IWeaponWielder
{
    /// <summary>피해·공격 간격(초)·사거리·이동 속도 배율을 덮어쓴다 (Start 전에 불려도 Start 뒤에 적용된다)</summary>
    void ApplyWeapon(float damage, float interval, float range, float moveMult);
}

/// <summary>
/// 침식된 인간(침식된 인간 습격) — 완전히 침식돼 적이 된 사람. 몸은 근접(그래플러)·원거리(침식 사수) 적 개체를 빌리고,
/// 이 컴포넌트가 '원래 사람'(무작위 생성 직원 데이터)과 착용 장비를 들고 있다.
///
/// · 장비: 플레이어가 지금 만들 수 있는 장비 위주로 칸마다 고른다(<see cref="RollLoadout"/>). 무기가 피해·간격·사거리, 방어구가 방어를 정한다.
/// · 특성: 전투 관련만 — 체력·이동 속도·근접/원거리 피해·공격 간격 배율, 무기 제한(싸움꾼/총기광).
/// · 체력 0 → 바로 죽지 않고 DOWN_SECONDS 동안 제압. 그동안만 정화 혈청을 쓸 수 있다. 시간이 지나거나 제압 중 또 맞으면 죽고 장비 1개를 떨군다.
/// · 정화 혈청 → 같은 사람이 같은 장비를 입고 직원으로 합류 (침식 30, 정화 직후 효과).
/// · 도감 없음, 전용 장비 없음. 클릭하면 정보 창.
/// </summary>
[RequireComponent(typeof(Xenops))]
public class ErodedHuman : MonoBehaviour
{
    // shortcut: 임시 수치 — 밸런스 확정 시 데이터로 뺄 것
    private const float DOWN_SECONDS = 300f;
    private const float OPTIONAL_SLOT_CHANCE = 0.5f;
    private const float NEXT_TIER_CHANCE = 0.1f;
    private const float RESEARCHED_WEIGHT = 3f;
    private const float JOIN_EROSION = 30f;
    private const float FIST_DAMAGE = 6f, FIST_INTERVAL = 1.2f, FIST_RANGE = 1.2f;
    private static readonly Color ErodedTint = new Color(0.7f, 0.55f, 0.95f, 1f);
    private static readonly Color DownedTint = new Color(0.45f, 0.4f, 0.5f, 1f);

    /// <summary>근접·원거리 몸 XenopsData ID</summary>
    public const int MELEE_ID = 6106, RANGED_ID = 6107;

    public static readonly EquipmentSlot[] OptionalSlots =
        { EquipmentSlot.Suit, EquipmentSlot.Helmet, EquipmentSlot.Gloves, EquipmentSlot.Shoes, EquipmentSlot.Protector };

    private Xenops _xenops;
    private XenopsHealth _health;
    private SpriteRenderer _sr;
    private Animator _anim;
    private SpriteRenderer _hair;
    private Sprite _bodySprite;

    /// <summary>원래 사람 (무작위 생성 직원 데이터)</summary>
    public EmployeeData Person { get; private set; }

    /// <summary>착용 장비와 내구도 (칸별)</summary>
    public readonly Dictionary<EquipmentSlot, EquipmentData> Loadout = new Dictionary<EquipmentSlot, EquipmentData>();
    public readonly Dictionary<EquipmentSlot, float> Durability = new Dictionary<EquipmentSlot, float>();

    /// <summary>제압 상태로 남은 시간 (0 = 제압 아님)</summary>
    public float DownedRemaining { get; private set; }
    public bool IsDowned => DownedRemaining > 0f;
    private bool _wasDowned;

    private void Awake()
    {
        _xenops = GetComponent<Xenops>();
        _health = GetComponent<XenopsHealth>();
        _sr = GetComponent<SpriteRenderer>();
        _anim = GetComponent<Animator>();
    }

    #region 생성

    /// <summary>원래 사람을 만든다 — 싸우지 못하는 특성(비폭력)은 다시 뽑는다</summary>
    public static EmployeeData RollPerson()
    {
        var cfg = EmployeeManager.instance != null ? EmployeeManager.instance.GenerationConfig : null;
        EmployeeData p = null;
        for (int i = 0; i < 6; i++)
        {
            p = RandomEmployeeGenerator.Generate(cfg);
            if (p == null || !Fx(p, f => f.nonViolent)) break;
        }
        return p;
    }

    /// <summary>
    /// 연구 수준에 맞춘 장비 — 칸마다 지금 만들 수 있는 장비(레시피 없는 기본 장비 포함, 연구로 해금된 것은 가중치 ×3)에서 고르고,
    /// NEXT_TIER_CHANCE로 아직 잠긴 연구 장비 하나. 무기는 항상(특성 무기 제한 반영), 나머지는 OPTIONAL_SLOT_CHANCE.
    /// </summary>
    public static Dictionary<EquipmentSlot, EquipmentData> RollLoadout(EmployeeData person)
    {
        var result = new Dictionary<EquipmentSlot, EquipmentData>();
        bool noMelee = person != null && Fx(person, f => f.cannotEquipMelee);
        bool noRanged = person != null && Fx(person, f => f.cannotEquipRanged);
        var w = Pick(EquipmentSlot.Weapon, d => !(noMelee && d.weaponClass == WeaponClass.Melee) && !(noRanged && d.weaponClass == WeaponClass.Ranged));
        if (w != null) result[EquipmentSlot.Weapon] = w;
        foreach (var slot in OptionalSlots)
        {
            if (Random.value >= OPTIONAL_SLOT_CHANCE) continue;
            var d = Pick(slot, null);
            if (d != null) result[slot] = d;
        }
        return result;
    }

    private static EquipmentData Pick(EquipmentSlot slot, System.Func<EquipmentData, bool> filter)
    {
        var db = GameDatabase.Instance;
        if (db == null) return null;
        var avail = new List<EquipmentData>();
        var weights = new List<float>();
        var locked = new List<EquipmentData>();
        foreach (var d in db.allEquipmentData)
        {
            if (d == null || d.itemData == null || d.slot != slot || d.IsConsumable) continue;
            if (filter != null && !filter(d)) continue;
            var recipe = RecipeFor(d);
            if (recipe == null) { avail.Add(d); weights.Add(1f); }
            else if (recipe.IsAvailable()) { avail.Add(d); weights.Add(recipe.requiresResearch ? RESEARCHED_WEIGHT : 1f); }
            else if (recipe.requiresResearch && recipe.requiredCodexXenopsId == 0 && IsNextTier(recipe)) locked.Add(d);
        }
        if (locked.Count > 0 && Random.value < NEXT_TIER_CHANCE) return locked[Random.Range(0, locked.Count)];
        if (avail.Count == 0) return null;
        float total = 0f;
        foreach (var x in weights) total += x;
        float r = Random.value * total;
        for (int i = 0; i < avail.Count; i++) { if (r < weights[i]) return avail[i]; r -= weights[i]; }
        return avail[avail.Count - 1];
    }

    private static CraftingRecipe RecipeFor(EquipmentData d)
    {
        var db = GameDatabase.Instance;
        if (db == null || db.allRecipeData == null) return null;
        foreach (var r in db.allRecipeData) if (r != null && r.outputItem == d.itemData) return r;
        return null;
    }

    /// <summary>한 단계 위 — 이 레시피를 해금하는 연구가 지금 연구 가능(선행 완료)하거나 진행 중인지</summary>
    private static bool IsNextTier(CraftingRecipe recipe)
    {
        var rt = ResearchTreeManager.instance;
        var cfg = rt != null ? rt.TreeConfig : null;
        if (cfg == null) return false;
        foreach (var node in cfg.nodes)
        {
            if (node == null || node.unlockEffects == null) continue;
            foreach (var fx in node.unlockEffects)
                if (fx is ResearchRecipeUnlockEffect ru && ru.recipe == recipe)
                {
                    var st = rt.GetNodeState(node.nodeId);
                    return st == ResearchNodeState.Available || st == ResearchNodeState.InProgress;
                }
        }
        return false;
    }

    private static bool Fx(EmployeeData p, System.Func<TraitEffects, bool> f)
    {
        if (p == null || p.traits == null) return false;
        foreach (var t in p.traits) if (t != null && t.effects != null && f(t.effects)) return true;
        return false;
    }

    private static float FxMult(EmployeeData p, System.Func<TraitEffects, float> f)
    {
        float m = 1f;
        if (p != null && p.traits != null)
            foreach (var t in p.traits) if (t != null && t.effects != null) m *= f(t.effects);
        return m;
    }

    /// <summary>근접·원거리 몸 ID (무기 종류로)</summary>
    public static int BodyIdFor(Dictionary<EquipmentSlot, EquipmentData> loadout)
        => loadout.TryGetValue(EquipmentSlot.Weapon, out var w) && w != null && w.weaponClass == WeaponClass.Ranged ? RANGED_ID : MELEE_ID;

    /// <summary>스폰 직후 — 사람·장비를 붙이고 능력치를 맞춘다 (체력 가득). 내구도는 30~100% 무작위</summary>
    public void Setup(EmployeeData person, Dictionary<EquipmentSlot, EquipmentData> loadout)
    {
        Person = person;
        Loadout.Clear(); Durability.Clear();
        foreach (var kv in loadout)
        {
            Loadout[kv.Key] = kv.Value;
            Durability[kv.Key] = kv.Value.MaxDurability * Random.Range(0.3f, 1f);
        }
        ApplyStats(true);
    }

    /// <summary>특성·장비로 체력·방어·무기 값을 맞춘다</summary>
    private void ApplyStats(bool fullHealth)
    {
        if (_xenops == null || _xenops.Data == null) return;
        var s = _xenops.Data.hostileStats;
        float armor = 0f;
        foreach (var d in Loadout.Values) if (d != null && d.passiveEffects != null) armor += d.DamageReduction;
        float hp = s.maxHealth * FxMult(Person, f => f.healthMult);
        float cur = _health != null ? _health.CurrentHealth : hp;
        _health?.Initialize(hp, Mathf.Min(0.8f, s.defense + armor / 100f));
        if (!fullHealth && _health != null) _health.SetCurrentHealth(Mathf.Min(cur, hp));

        Loadout.TryGetValue(EquipmentSlot.Weapon, out var w);
        bool ranged = w != null && w.weaponClass == WeaponClass.Ranged;
        float dmg = (w != null ? w.Damage : FIST_DAMAGE) * FxMult(Person, f => ranged ? f.rangedDamageMult : f.meleeDamageMult);
        float interval = (w != null ? w.attackInterval : FIST_INTERVAL)
                       * FxMult(Person, f => f.attackIntervalMult * (ranged ? f.rangedAttackIntervalMult : f.meleeAttackIntervalMult));
        float range = w != null ? w.attackRange : FIST_RANGE;
        float move = FxMult(Person, f => f.moveSpeedMult);
        foreach (var b in GetComponents<IWeaponWielder>()) b.ApplyWeapon(dmg, interval, range, move);

        // 원래 사람의 머리 그림
        var hairSprite = Person != null && Person.appearance != null ? Person.appearance.hairSprite : null;
        if (hairSprite != null && _hair == null)
        {
            var go = new GameObject("Hair", typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            _hair = go.GetComponent<SpriteRenderer>();
            _hair.sortingOrder = (_sr != null ? _sr.sortingOrder : 0) + 1;
        }
        if (_hair != null) { _hair.sprite = hairSprite; _hair.color = Person.appearance.hairColor * ErodedTint; }
        if (_xenops != null && Person != null) _xenops.name = $"Xenops_침식된 {Person.employeeName}_{_xenops.InstanceId}";
    }

    #endregion

    #region 제압·사망·정화

    /// <summary>XenopsHealth.Die 직전 — 처음 쓰러지면 죽지 않고 제압 상태가 된다(true). 제압 중이면 그대로 죽는다(false)</summary>
    public bool InterceptDeath()
    {
        if (_wasDowned) return false; // 제압 중이거나 제압 시간이 끝났으면 그대로 죽는다
        _wasDowned = true;
        DownedRemaining = DOWN_SECONDS;
        _xenops?.Subdue();
        Debug.Log($"[ErodedHuman] {Person?.employeeName} 제압 — {DOWN_SECONDS:F0}초 안에 정화 혈청을 쓰면 되돌릴 수 있음");
        return true;
    }

    /// <summary>죽을 때 — 착용 장비 중 1개를 바닥에 떨군다</summary>
    public void OnDied()
    {
        if (Loadout.Count == 0) return;
        var list = new List<EquipmentData>(Loadout.Values);
        var d = list[Random.Range(0, list.Count)];
        if (d == null || d.itemData == null) return;
        // shortcut: 바닥 아이템은 수량만 가져 내구도가 보존되지 않는다(운반 뒤 최대치) — EmployeeEquipment.DropOnDeath와 같은 제약
        ItemRefundHelper.SpawnRefunds(new Dictionary<ItemData, int> { { d.itemData, 1 } }, transform.position, Vector2Int.one);
        Debug.Log($"[ErodedHuman] {Person?.employeeName} 사망 — {d.itemData.itemName} 드롭");
    }

    /// <summary>정화 혈청 — 같은 사람이 같은 장비로 직원이 된다 (하루 채용 제한과 무관). 이 적은 드롭 없이 사라진다</summary>
    public Employee Purify()
    {
        if (!IsDowned || Person == null || EmployeeManager.instance == null) return null;
        var pos = transform.position;
        var emp = EmployeeManager.instance.SpawnEmployee(Person, pos, equipOutfit: false);
        if (emp == null) return null;
        var esm = EquipmentStorageManager.instance;
        foreach (var kv in Loadout)
        {
            var inst = esm != null ? esm.NewInstance(kv.Value) : null;
            if (inst == null) continue;
            inst.durability = Durability.TryGetValue(kv.Key, out var dur) ? dur : kv.Value.MaxDurability;
            if (!emp.Equipment.EquipInstance(kv.Key, inst)) esm.ReturnInstance(inst); // 특성상 못 입으면 보관소로
        }
        emp.StatsController?.SetErosion(JOIN_EROSION);
        emp.GetComponent<EmployeeErosionController>()?.StartPurifiedAftercare();

        NotificationManager.instance?.PushLetter(new Letter
        {
            title = "정화 성공",
            body = $"{Person.employeeName}이(가) 침식에서 돌아와 직원으로 합류했습니다. 한동안 침식이 덜 쌓이고 침식 계열 정신 이상이 일어나지 않습니다.",
            type = LetterType.Positive
        });
        Debug.Log($"[ErodedHuman] {Person.employeeName} 정화 — 직원 합류");

        DownedRemaining = 0f;
        if (XenopsManager.instance != null) XenopsManager.instance.RemoveXenops(_xenops);
        else Destroy(gameObject);
        return emp;
    }

    #endregion

    private void Update()
    {
        if (SaveManager.instance != null && SaveManager.instance.IsLoading) return;
        if (!IsDowned) return;
        DownedRemaining -= Time.deltaTime;
        if (DownedRemaining <= 0f) { DownedRemaining = 0f; _health?.ForceDie(); }
    }

    // 몸 개체(그래플러·침식 사수)의 애니메이터가 그림을 덮으므로 직원 그림·침식 색을 다시 입힌다
    private void LateUpdate()
    {
        if (_sr == null) return;
        if (_anim != null && _anim.enabled) _anim.enabled = false;
        if (_bodySprite == null && _xenops != null && _xenops.Data != null) _bodySprite = _xenops.Data.icon; // 데이터는 Awake 뒤 Initialize에서 붙는다
        if (_bodySprite != null) _sr.sprite = _bodySprite;
        _sr.color = IsDowned ? DownedTint : ErodedTint;
        if (_hair != null) _hair.flipX = _sr.flipX;
    }

    #region 정보 창

    /// <summary>이름·특성·기술·능력치·착용 장비 (읽기 전용)</summary>
    public void ShowInfo()
    {
        var popup = UIManager.instance != null ? UIManager.instance.GetPanel<LetterDetailPopup>(UIPanelType.LetterDetail) : null;
        if (popup == null || Person == null) return;
        popup.Show(new Letter { title = $"침식된 인간 — {Person.employeeName}", body = BuildInfo(), type = LetterType.Neutral });
    }

    public string BuildInfo()
    {
        var sb = new StringBuilder();
        if (IsDowned) sb.AppendLine($"<b>제압됨</b> — {DownedRemaining:F0}초 안에 정화 혈청을 쓰면 되돌릴 수 있습니다.\n");

        sb.AppendLine("<b>특성</b>");
        if (Person.traits == null || Person.traits.Count == 0) sb.AppendLine("· 없음");
        else foreach (var t in Person.traits) if (t != null) sb.AppendLine($"· {t.traitName} — {t.description}");

        sb.AppendLine("\n<b>기술</b>");
        sb.AppendLine($"· 근접 Lv.{Person.initialMeleeLevel} · 원거리 Lv.{Person.initialRangedLevel}");
        if (Person.initialWorkLevels != null)
            foreach (var w in Person.initialWorkLevels)
                if (w.levels > 0) sb.AppendLine($"· {WorkTypeCategory.GetDisplayName(w.workType)} +{w.levels}");

        sb.AppendLine("\n<b>능력치</b>");
        sb.AppendLine($"· 체력 {(_health != null ? _health.CurrentHealth : 0f):F0} / {(_health != null ? _health.MaxHealth : 0f):F0} · 방어 {(_health != null ? _health.Defense * 100f : 0f):F0}%");
        sb.AppendLine($"· 정신력 최대 {Person.maxMental} · 원래 체력 {Person.maxHealth}");

        sb.AppendLine("\n<b>착용 장비</b>");
        if (Loadout.Count == 0) sb.AppendLine("· 없음 (맨손)");
        foreach (var kv in Loadout)
        {
            if (kv.Value == null || kv.Value.itemData == null) continue;
            float dur = Durability.TryGetValue(kv.Key, out var v) ? v : kv.Value.MaxDurability;
            sb.AppendLine($"· {EquipmentSlotNames.Label(kv.Key)}: {kv.Value.itemData.itemName} (내구도 {dur / Mathf.Max(1f, kv.Value.MaxDurability) * 100f:F0}%)");
        }
        return sb.ToString().TrimEnd();
    }

    #endregion

    #region 세이브

    public ErodedHumanSaveData Capture()
    {
        var s = new ErodedHumanSaveData
        {
            has = Person != null,
            person = Person != null ? RandomEmployeeGenerator.CreateSnapshot(Person) : null,
            templateId = Person != null ? Person.employeeID : 0,
            abilities = Person != null && Person.abilities != null ? WorkAbilitiesSaveData.FromWorkAbilities(Person.abilities) : null,
            downedRemaining = DownedRemaining
        };
        foreach (var kv in Loadout)
        {
            if (kv.Value == null || kv.Value.itemData == null) continue;
            s.slots.Add((int)kv.Key);
            s.equipmentIds.Add(kv.Value.itemData.itemID);
            s.durabilities.Add(Durability.TryGetValue(kv.Key, out var d) ? d : kv.Value.MaxDurability);
        }
        return s;
    }

    public void Restore(ErodedHumanSaveData s)
    {
        if (s == null || !s.has) return;
        var cfg = EmployeeManager.instance != null ? EmployeeManager.instance.GenerationConfig : null;
        Person = RandomEmployeeGenerator.Rebuild(s.person, s.templateId, s.abilities, cfg);
        Loadout.Clear(); Durability.Clear();
        for (int i = 0; i < s.slots.Count; i++)
        {
            var d = GameDatabase.Instance != null ? GameDatabase.Instance.GetEquipmentData(s.equipmentIds[i]) : null;
            if (d == null) continue;
            Loadout[(EquipmentSlot)s.slots[i]] = d;
            Durability[(EquipmentSlot)s.slots[i]] = s.durabilities[i];
        }
        DownedRemaining = s.downedRemaining;
        _wasDowned = DownedRemaining > 0f;
        ApplyStats(false);
    }

    #endregion
}
