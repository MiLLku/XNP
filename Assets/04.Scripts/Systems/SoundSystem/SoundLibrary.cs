using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 소리 등록부 — SoundId → SoundData. SoundManager가 하나를 들고 조회합니다.
/// 새 소리: SoundId에 값을 추가하고, SoundData 에셋을 만들어 여기 목록에 넣으면 끝입니다.
/// </summary>
[CreateAssetMenu(fileName = "SoundLibrary", menuName = "XNP/Sound/Sound Library")]
public class SoundLibrary : ScriptableObject
{
    public List<SoundData> sounds = new List<SoundData>();

    private Dictionary<SoundId, SoundData> _byId;

    public SoundData Get(SoundId id)
    {
        if (_byId == null)
        {
            _byId = new Dictionary<SoundId, SoundData>();
            foreach (var s in sounds)
            {
                if (s == null || s.id == SoundId.None) continue;
                if (_byId.ContainsKey(s.id)) Debug.LogError($"[SoundLibrary] 중복 id {s.id}: {s.name}");
                else _byId[s.id] = s;
            }
        }
        return _byId.TryGetValue(id, out var data) ? data : null;
    }

    private void OnValidate() => _byId = null; // 에디터에서 목록을 바꾸면 다시 만든다
}
