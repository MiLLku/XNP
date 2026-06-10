/// <summary>
/// 건물별 추가 상태를 BuildingSaveData.extraData(string JSON)에 직렬화/복원하는 컴포넌트.
///
/// Building.CreateSaveData()가 같은 GameObject의 IBuildingExtraSerializable을 찾아
/// SerializeExtra()를 호출하고 결과를 extraData에 저장합니다.
/// Building.RestoreState()는 extraData가 비어있지 않으면 DeserializeExtra(json)을 호출합니다.
///
/// 한 Building 당 IBuildingExtraSerializable 컴포넌트는 1개만 권장합니다.
/// (현재 구현은 첫 번째만 사용)
/// </summary>
public interface IBuildingExtraSerializable
{
    /// <summary>저장할 상태가 있으면 JSON 문자열, 없으면 빈 문자열을 반환합니다.</summary>
    string SerializeExtra();

    /// <summary>저장된 JSON 문자열로부터 상태를 복원합니다.</summary>
    void DeserializeExtra(string json);
}
