using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>제놉스 도감 저장 데이터 (구 세이브에는 없음 — null이면 처음부터)</summary>
[Serializable]
public class XenopsCodexSaveData
{
    public List<int> xenopsIds = new List<int>();
    public List<float> progress = new List<float>();
    /// <summary>이미 '처음 목격'으로 센 개체 (같은 개체를 두 번 세지 않게)</summary>
    public List<int> observedInstanceIds = new List<int>();
}

/// <summary>
/// 제놉스 도감 — 종류(XenopsData)마다 연구도 0~100%.
///
///   • 처음 보는 개체가 시야(안개가 걷힌 칸)에 들어오면 +codexObservePercent (개체당 1회)
///   • 처치하면 +codexKillPercent
///   • 100%가 되면 그 종류의 연구 3개(무기·착용·다용도구, ResearchNodeData.requiredCodexXenopsId)가 열린다.
///     셋 사이에 순서는 없다. 연구를 마치면 그 제놉스 재료로 만드는 장비 제작법이 열린다.
///
/// 연구도 수치는 XenopsData에 있다 — 위협도(등급)에 맞춰 종류별로 정한다.
/// </summary>
public class XenopsCodexManager : DestroySingleton<XenopsCodexManager>, ISaveModule
{
    private const float OBSERVE_CHECK_INTERVAL = 1f;

    private readonly Dictionary<int, float> progress = new Dictionary<int, float>();
    private readonly HashSet<int> observed = new HashSet<int>();
    private float observeTimer;

    /// <summary>그 종류의 연구도 (0~100)</summary>
    public float GetProgress(int xenopsId) => progress.TryGetValue(xenopsId, out float p) ? p : 0f;

    /// <summary>그 종류의 도감이 완성됐는지 (매니저가 없으면 false)</summary>
    public static bool IsComplete(int xenopsId) => instance != null && instance.GetProgress(xenopsId) >= 100f;

    /// <summary>처치 — XenopsHealth가 사망 시 호출</summary>
    public void OnKilled(XenopsData data)
    {
        if (data != null) AddProgress(data, data.codexKillPercent);
    }

    private void Update()
    {
        observeTimer += Time.deltaTime;
        if (observeTimer < OBSERVE_CHECK_INTERVAL) return;
        observeTimer = 0f;

        var xenopsManager = XenopsManager.instance;
        var fog = FogOfWarManager.instance;
        if (xenopsManager == null || fog == null) return;

        foreach (var x in xenopsManager.GetAllXenops())
        {
            if (x == null || x.Data == null || observed.Contains(x.InstanceId)) continue;
            Vector3 p = x.transform.position;
            if (!fog.IsRevealed(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y))) continue;

            observed.Add(x.InstanceId);
            AddProgress(x.Data, x.Data.codexObservePercent);
        }
    }

    private void AddProgress(XenopsData data, float amount)
    {
        if (amount <= 0f) return;
        float before = GetProgress(data.xenopsID);
        if (before >= 100f) return;

        float after = Mathf.Min(100f, before + amount);
        progress[data.xenopsID] = after;

        if (after >= 100f)
        {
            ResearchTreeManager.instance?.RefreshLockedNodes();
            NotificationManager.instance?.PushLetter(new Letter
            {
                title = $"도감 완성: {data.xenopsName}",
                body = $"{data.xenopsName}의 연구도가 100%가 되었습니다. 연구 트리에서 이 제놉스의 무기·착용·다용도구 연구 3개를 순서 없이 진행할 수 있습니다.",
                type = LetterType.Positive
            });
        }
    }

    #region ISaveModule

    /// <summary>연구 트리(85) 다음 — 도감으로 잠긴 연구를 복원 뒤 다시 연다</summary>
    public int SaveOrder => 86;

    public void Capture(SaveData data)
    {
        var save = new XenopsCodexSaveData();
        foreach (var pair in progress)
        {
            save.xenopsIds.Add(pair.Key);
            save.progress.Add(pair.Value);
        }
        save.observedInstanceIds.AddRange(observed);
        data.xenopsCodex = save;
    }

    public void Restore(SaveData data)
    {
        progress.Clear();
        observed.Clear();
        var save = data.xenopsCodex;
        if (save != null)
        {
            for (int i = 0; i < save.xenopsIds.Count && i < save.progress.Count; i++)
                progress[save.xenopsIds[i]] = save.progress[i];
            foreach (int id in save.observedInstanceIds) observed.Add(id);
        }
        ResearchTreeManager.instance?.RefreshLockedNodes();
    }

    public void PostRestore(SaveData data) { }

    #endregion
}
