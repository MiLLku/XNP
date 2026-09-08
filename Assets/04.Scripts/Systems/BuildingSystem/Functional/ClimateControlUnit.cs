using UnityEngine;

/// <summary>
/// 냉난방기 — 플레이어가 <b>목표 온도를 직접 지정</b>하는 능동 공조기.
///
/// 다른 열원(대장간의 폐열, 뜨거운 광맥)은 목표 온도가 에셋에 박혀 있지만,
/// 이 컴포넌트가 붙은 건물은 방마다 다른 값을 쓸 수 있습니다.
///
/// <b>전력은 부하에 비례합니다.</b> 소비량을 정하는 것은 현재 방 온도가 아니라
/// <see cref="Room.NaturalEquilibrium"/> — 즉 <i>이 공조기가 없었다면 방이 도달했을 온도</i>입니다.
/// 그래서
/// <list type="bullet">
/// <item>깊이 팔수록(지열이 셀수록) 같은 목표를 유지하는 데 더 많은 전력이 든다</item>
/// <item>단열 벽으로 감싸면 자연 평형이 목표에 가까워져 전력이 줄어든다</item>
/// <item>목표에 도달한 뒤에도 계속 먹는다 — 유지에도 비용이 든다</item>
/// </list>
/// 가 전부 공식 하나에서 나옵니다.
///
/// 부하가 <b>자기 자신을 뺀</b> 값이라 소비 전력이 자기 동작에 영향받지 않습니다.
/// 정전 → 소비 0 → 급전 → 소비 급증 같은 되먹임 진동이 생기지 않는 이유입니다.
/// </summary>
[RequireComponent(typeof(Building))]
public class ClimateControlUnit : MonoBehaviour, IBuildingExtraSerializable, IVariablePowerDraw
{
    #region 인스펙터

    [Tooltip("플레이어가 지정하지 않았을 때의 목표 온도(℃). 온열기는 쾌적 상한 바로 아래, 냉방기는 하한 위로 둡니다.")]
    [SerializeField] private float targetTemperature = 22f;

    #endregion

    #region 상태

    private Building _building;

    #endregion

    #region 프로퍼티

    /// <summary>플레이어가 지정한 목표 온도(℃)</summary>
    public float TargetTemperature => targetTemperature;

    /// <summary>난방기인지 (열 출력이 양수). 아니면 냉방기입니다.</summary>
    public bool IsHeater => _building != null && _building.HeatOutput > 0f;

    /// <summary>이 공조기가 없었다면 방이 도달했을 온도(℃)</summary>
    public float NaturalEquilibrium => TemperatureManager.instance != null
        ? TemperatureManager.instance.GetNaturalEquilibriumFor(_building)
        : targetTemperature;

    /// <summary>현재 방 온도(℃). 방을 못 찾으면 자기 높이의 주변 온도.</summary>
    public float RoomTemperature
    {
        get
        {
            var temperature = TemperatureManager.instance;
            if (temperature == null || _building == null) return targetTemperature;

            Room room = RoomManager.instance != null
                ? RoomManager.instance.GetRoomById(temperature.GetResolvedRoomId(_building))
                : null;

            return room != null ? room.Temperature : temperature.GetAmbientAt(transform.position.y);
        }
    }

    /// <summary>
    /// 지금 실제로 부하가 걸리는 정도(℃).
    /// 난방기는 방이 목표보다 <b>추울 때만</b>, 냉방기는 <b>더울 때만</b> 일합니다 —
    /// 반대 방향은 이 건물이 어차피 못 하는 일이라 전력도 들지 않습니다.
    /// </summary>
    public float Load
    {
        get
        {
            float natural = NaturalEquilibrium;
            return Mathf.Max(0f, IsHeater ? targetTemperature - natural : natural - targetTemperature);
        }
    }

    #endregion

    #region 생명주기

    void Awake()
    {
        _building = GetComponent<Building>();
    }

    #endregion

    #region 목표 온도

    /// <summary>목표 온도를 설정합니다. 설정의 허용 범위로 잘립니다.</summary>
    public void SetTargetTemperature(float value)
    {
        var config = TemperatureManager.instance != null ? TemperatureManager.instance.Config : null;
        float min = config != null ? config.climateTargetMin : -20f;
        float max = config != null ? config.climateTargetMax : 60f;

        targetTemperature = Mathf.Clamp(value, min, max);
    }

    #endregion

    #region IVariablePowerDraw

    /// <summary>
    /// 부하 비례 전력. <c>대기 전력 + 부하 × 도당 전력</c>이며 최대치에서 멈춥니다.
    ///
    /// 급전 여부(<c>IsPowered</c>)를 <b>보지 않습니다</b> — 보면 정전과 급전 사이를 오가는
    /// 되먹임 진동이 생깁니다. 파괴된 건물만 0을 돌려줍니다.
    /// </summary>
    public int GetPowerDraw()
    {
        if (_building == null || !_building.IsFunctional) return 0;

        BuildingData data = _building.buildingData;
        if (data == null) return 0;

        int draw = data.climateIdleWatts + Mathf.CeilToInt(Load * data.climateWattsPerDegree);
        return Mathf.Clamp(draw, 0, Mathf.Max(data.climateIdleWatts, data.climateMaxWatts));
    }

    #endregion

    #region 클릭 및 UI

    private void OnMouseDown()
    {
        if (_building != null && !_building.IsFunctional) return;
        if (UIManager.instance == null) return;

        var panel = UIManager.instance.GetPanel<ClimateControlUI>(UIPanelType.ClimateControlUI);
        if (panel == null)
        {
            Debug.LogError("[ClimateControlUnit] ClimateControlUI를 찾을 수 없습니다!");
            return;
        }

        panel.Setup(this);
        UIManager.instance.ShowPanel(UIPanelType.ClimateControlUI);
    }

    #endregion

    #region 세이브

    /// <summary>목표 온도만 저장합니다. 나머지는 전부 방 상태에서 파생됩니다.</summary>
    [System.Serializable]
    private class SaveState
    {
        public float target;
    }

    public string SerializeExtra()
        => JsonUtility.ToJson(new SaveState { target = targetTemperature });

    public void DeserializeExtra(string json)
    {
        if (string.IsNullOrEmpty(json)) return;

        var state = JsonUtility.FromJson<SaveState>(json);
        if (state != null) SetTargetTemperature(state.target);
    }

    #endregion
}
