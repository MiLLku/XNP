using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// '치료' 작업물 관리 — 쓰러진 직원마다 소생 작업물(<see cref="RescueOrder"/>),
/// 요양 중인 직원마다 돌보기 작업물(<see cref="TreatOrder"/>)을 걸고, 끝나면 치운다.
/// 작업물은 저장하지 않으므로 불러온 뒤에도 여기서 다시 건다.
///
/// 씬 배선 없이 스스로 생긴다 (RunStatsManager와 같은 방식, 게임 씬에서만).
/// </summary>
public class MedicalManager : MonoBehaviour
{
    private const float POLL_INTERVAL = 1f;
    private const int PRIORITY = 1;

    private readonly Dictionary<Employee, WorkOrder> rescueOrders = new Dictionary<Employee, WorkOrder>();
    private readonly Dictionary<Employee, WorkOrder> treatOrders = new Dictionary<Employee, WorkOrder>();
    private float timer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        Ensure();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Ensure();

    private static void Ensure()
    {
        if (FindAnyObjectByType<MapGenerator>() == null) return;
        if (FindAnyObjectByType<MedicalManager>() == null)
            new GameObject("MedicalManager").AddComponent<MedicalManager>();
    }

    private void Update()
    {
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f) return;
        timer = POLL_INTERVAL;

        var wsm = WorkSystemManager.instance;
        var em = EmployeeManager.instance;
        if (wsm == null || em == null) return;
        if (SaveManager.instance != null && SaveManager.instance.IsLoading) return;

        foreach (var e in em.AllEmployees)
        {
            if (e == null) continue;

            if (e.IsDowned)
                Ensure(rescueOrders, e, wsm, () => new RescueOrder(e), "소생");
            else if (e.State != EmployeeState.Dead)
            {
                var ai = e.GetComponent<EmployeeAI>();
                if (ai != null && ai.IsRecuperating && e.Stats.health < e.Stats.maxHealth)
                    Ensure(treatOrders, e, wsm, () => new TreatOrder(e), "치료");
            }
        }

        Prune(rescueOrders, wsm);
        Prune(treatOrders, wsm);
    }

    private static void Ensure(Dictionary<Employee, WorkOrder> map, Employee patient, WorkSystemManager wsm,
        System.Func<IWorkTarget> make, string verb)
    {
        if (map.TryGetValue(patient, out var existing) && existing != null && wsm.HasWorkOrder(existing)) return;
        var order = wsm.CreateWorkOrder($"{verb}: {patient.DisplayName}", WorkType.Treatment, 1, PRIORITY);
        order.AddTarget(make());
        map[patient] = order;
    }

    /// <summary>끝났거나 필요 없어진 작업물을 치운다 (맡은 사람이 없을 때만 — 진행 중이면 끝나기를 기다린다)</summary>
    private static void Prune(Dictionary<Employee, WorkOrder> map, WorkSystemManager wsm)
    {
        List<Employee> drop = null;
        foreach (var kv in map)
        {
            var order = kv.Value;
            bool gone = order == null || !wsm.HasWorkOrder(order);
            bool expired = !gone && order.targets.TrueForAll(t => t is IExpiringWorkTarget x && x.IsExpired);
            if (gone || (expired && order.assignedWorkers.Count == 0))
            {
                if (!gone) wsm.RemoveWorkOrder(order, isCancellation: true);
                (drop ??= new List<Employee>()).Add(kv.Key);
            }
        }
        if (drop != null) foreach (var k in drop) map.Remove(k);
    }
}
