using TMPro;
using UnityEngine;

/// <summary>
/// 쓰러짐 — 체력이 0이 되면 사망 대신 이 상태가 된다 (사용자 결정).
///
///   - 움직이지 못하고 작업·욕구 행동이 멈춘다 (욕구도 멈춘다).
///   - 쓰러진 채로 피해를 입을 때마다 <see cref="DEATH_CHANCE"/> 확률로 사망.
///   - 2게임시간 안에 소생 치료를 받지 못하면 같은 확률로 사망, 살아남으면 다시 2시간을 센다.
///   - '치료' 담당이 침대(본인 침대 또는 의료 침대)로 옮겨 소생시키면 체력 <see cref="REVIVE_HEALTH_RATIO"/>로 깨어난다.
///
/// 소생 작업물은 <see cref="MedicalManager"/>가 걸고, 옮기기·소생은 EmployeeWork(RescueOrder)가 한다.
/// Employee.Awake가 없으면 붙인다 (프리팹 수정 없음).
/// </summary>
[DisallowMultipleComponent]
public class EmployeeDowned : MonoBehaviour
{
    /// <summary>쓰러진 채 피해를 입거나 2시간이 지날 때 사망할 확률</summary>
    public const float DEATH_CHANCE = 0.2f;
    /// <summary>사망 판정 주기 (게임 시간)</summary>
    public const float CHECK_GAME_HOURS = 2f;
    /// <summary>소생 직후 체력 비율 (임시 값)</summary>
    public const float REVIVE_HEALTH_RATIO = 0.2f;

    private Employee employee;
    private float timer;
    private Employee carrier;
    private TextMeshPro label;

    public bool IsDowned => employee != null && employee.State == EmployeeState.Downed;
    /// <summary>다음 사망 판정까지 남은 시간 (실제 게임 초)</summary>
    public float TimeLeft => timer;
    /// <summary>지금 옮기고 있는 직원 (없으면 null)</summary>
    public Employee Carrier => carrier;

    private static float SecondsPerGameHour
        => DayCycle.instance != null ? DayCycle.instance.DayLengthInSeconds / 24f : 60f;

    private void Awake() => employee = GetComponent<Employee>();

    /// <summary>체력이 0이 된 순간 — 사망 대신 쓰러진다 (EmployeeStatsController가 부름)</summary>
    public void Down()
    {
        if (employee == null || employee.State == EmployeeState.Dead || IsDowned) return;

        employee.GetComponent<EmployeeDraft>()?.SetDrafted(false);
        employee.GetComponent<EmployeeWork>()?.CancelWork();
        employee.GetComponent<EmployeeMovement>()?.StopMoving();
        employee.ForceState(EmployeeState.Downed);
        timer = CHECK_GAME_HOURS * SecondsPerGameHour;
        carrier = null;

        NotificationManager.instance?.PushLetter(new Letter
        {
            title = "직원 쓰러짐",
            body = $"{employee.DisplayName}이(가) 쓰러졌습니다. '치료'를 맡은 직원이 침대로 옮겨 소생시켜야 합니다.\n" +
                   $"{CHECK_GAME_HOURS:F0}시간 안에 치료하지 못하거나 쓰러진 채 공격받으면 사망할 수 있습니다.",
            type = LetterType.Threat,
            pauseUntilRead = true
        });
        TimeManager.instance?.ForcePause();
    }

    /// <summary>불러오기 — 쓰러진 채로 되돌린다 (알림 없이)</summary>
    public void RestoreDowned(float timeLeft)
    {
        if (employee == null) employee = GetComponent<Employee>();
        employee.ForceState(EmployeeState.Downed);
        timer = timeLeft > 0f ? timeLeft : CHECK_GAME_HOURS * SecondsPerGameHour;
        carrier = null;
    }

    /// <summary>쓰러진 채 피해를 입음 — 확률 사망</summary>
    public void OnDamagedWhileDowned()
    {
        if (!IsDowned) return;
        if (Random.value < DEATH_CHANCE) Die("쓰러진 채 공격받아");
    }

    /// <summary>소생 — 체력 일부로 깨어난다</summary>
    public void Revive()
    {
        if (!IsDowned) return;
        carrier = null;
        var stats = employee.StatsController;
        employee.ForceState(EmployeeState.Idle);
        stats?.SetHealthDirect(stats.Stats.maxHealth * REVIVE_HEALTH_RATIO);

        NotificationManager.instance?.PushLetter(new Letter
        {
            title = "소생",
            body = $"{employee.DisplayName}이(가) 깨어났습니다. 체력이 낮아 요양이 필요합니다.",
            type = LetterType.Positive
        });
    }

    #region 옮기기

    public void PickUp(Employee by)
    {
        if (!IsDowned) return;
        carrier = by;
        SetPhysics(false);
    }

    /// <summary>내려놓기 (world = 놓을 위치, null이면 옮기던 직원 발밑)</summary>
    public void PutDown(Vector3? world = null)
    {
        var pos = world ?? (carrier != null ? carrier.transform.position : transform.position);
        carrier = null;
        transform.position = pos;
        SetPhysics(true);
    }

    private void SetPhysics(bool on)
    {
        var rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.bodyType = on ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
        }
    }

    #endregion

    private void Update()
    {
        if (!IsDowned)
        {
            if (label != null && label.gameObject.activeSelf) label.gameObject.SetActive(false);
            return;
        }

        // 옮기는 사람이 사라지거나 쓰러지면 그 자리에 떨어진다
        if (carrier != null)
        {
            if (carrier.State == EmployeeState.Dead || carrier.State == EmployeeState.Downed) PutDown();
            else transform.position = carrier.transform.position + new Vector3(0f, 0.6f, 0f);
        }

        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            if (Random.value < DEATH_CHANCE) { Die("치료를 받지 못해"); return; }
            timer = CHECK_GAME_HOURS * SecondsPerGameHour;
        }

        UpdateLabel();
    }

    private void UpdateLabel()
    {
        if (label == null)
        {
            var go = new GameObject("DownedLabel");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            label = go.AddComponent<TextMeshPro>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 3f;
            label.color = new Color(1f, 0.35f, 0.3f);
            label.sortingOrder = 50;
            var font = TMP_Settings.defaultFontAsset;
            if (font != null) label.font = font;
        }
        if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);

        float hours = timer / Mathf.Max(0.01f, SecondsPerGameHour);
        int h = Mathf.FloorToInt(hours);
        int m = Mathf.FloorToInt((hours - h) * 60f);
        label.text = $"쓰러짐 {h}:{m:00}";
    }

    private void Die(string cause)
    {
        if (employee == null || employee.State == EmployeeState.Dead) return;
        if (carrier != null) PutDown();
        employee.ForceState(EmployeeState.Dead);
        employee.StatsController?.NotifyStatsChanged();

        NotificationManager.instance?.PushLetter(new Letter
        {
            title = "직원 사망",
            body = $"{employee.DisplayName}이(가) {cause} 사망했습니다.",
            type = LetterType.Threat,
            pauseUntilRead = true
        });
        TimeManager.instance?.ForcePause();
        EmployeeManager.instance?.HandleDeath(employee);
    }
}
