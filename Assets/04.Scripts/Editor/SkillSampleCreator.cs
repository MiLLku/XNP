#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 스킬 트리 샘플 데이터 자동 생성 에디터 도구.
///
/// 메뉴: Tools → 스킬 트리 → 샘플 데이터 생성
///
/// 실행하면 Assets/ScriptableObject/Skills/ 폴더에
/// SkillData × 18 + SkillTreeConfig × 1 을 생성합니다.
/// 이미 존재하는 에셋은 덮어쓰지 않습니다 (ID 충돌 방지).
///
/// 스킬 트리 구조 (거미줄, 중앙 → 6방향 확장):
///
///                  건설I(1,3.5)
///                      |
///        채광I(-3.5,1) [핵심자질(0,0)] 연구I(3.5,1)
///       /              /    |    \              \
///  채광II(-5,2)   적응력  강인함  집중력       연구II(5,2)
///  채광III(-6.5,1) (-2,0) (0,2)   (2,0)        연구III(6.5,1)
///               |                    |
///           원예I(-3.5,-1)       제작I(3.5,-1)
///           원예II(-5,-2)        제작II(5,-2)
///
///           운반I(0,-2) ← 핵심자질
///           운반II(0,-3.5)
/// </summary>
public static class SkillSampleCreator
{
    private const string BASE_FOLDER   = "Assets/ScriptableObject";
    private const string SKILLS_FOLDER = "Assets/ScriptableObject/Skills";

    [MenuItem("Tools/스킬 트리/샘플 데이터 생성", priority = 200)]
    public static SkillTreeConfig CreateAll()
    {
        EnsureFolder(SKILLS_FOLDER);

        var skills = new List<SkillData>();

        // ── 스킬 정의 (id, 이름, 설명, 카테고리, x, y, 선행ID[], 기본해제)
        var defs = new (int id, string name, string desc,
                        SkillCategory cat, float x, float y,
                        int[] prereqs, bool def)[]
        {
            // ── 공통 핵심
            ( 1, "핵심 자질",        "모든 능력의 근원. 직원의 기본 잠재력을 열어준다.",
                SkillCategory.General,    0,     0,    new int[]{},   true ),
            ( 2, "강인함",           "체력과 지구력이 향상된다. 힘든 작업도 끄떡없다.",
                SkillCategory.General,    0,     2,    new[]{ 1 },    false ),
            ( 3, "집중력",           "정밀 작업 효율이 높아진다. 연구·제작 분야에 유리.",
                SkillCategory.General,    2,     0,    new[]{ 1 },    false ),
            ( 4, "적응력",           "다양한 환경에 빠르게 적응한다. 채광·원예 분야에 유리.",
                SkillCategory.General,   -2,     0,    new[]{ 1 },    false ),

            // ── 채광 (왼쪽 위)
            ( 5, "채광 I: 기초",     "기본 채광 기술을 습득한다.",
                SkillCategory.Mining,    -3.5f,  1,    new[]{ 4 },    false ),
            ( 6, "채광 II: 심층",    "더 깊은 광맥에서 광석을 발굴할 수 있다.",
                SkillCategory.Mining,    -5,     2,    new[]{ 5 },    false ),
            ( 7, "채광 III: 정밀",   "광석 수율을 최대화하는 정밀 채광 기법.",
                SkillCategory.Mining,    -6.5f,  1,    new[]{ 6 },    false ),

            // ── 연구 (오른쪽 위)
            ( 8, "연구 I: 기초",     "기초 이론 연구 능력을 습득한다.",
                SkillCategory.Research,   3.5f,  1,    new[]{ 3 },    false ),
            ( 9, "연구 II: 심층",    "복잡한 연구 과제를 처리할 수 있다.",
                SkillCategory.Research,   5,     2,    new[]{ 8 },    false ),
            (10, "연구 III: 돌파",   "연구 속도가 크게 향상되며 새 분야를 개척한다.",
                SkillCategory.Research,   6.5f,  1,    new[]{ 9 },    false ),

            // ── 건설 (위쪽)
            (11, "건설 I: 기초",     "기본 건설 작업을 수행한다.",
                SkillCategory.Building,   1,     3.5f, new[]{ 2 },    false ),
            (12, "건설 II: 정밀",    "정밀 구조물 설계 및 시공이 가능해진다.",
                SkillCategory.Building,   2,     5,    new[]{ 11 },   false ),

            // ── 원예 (왼쪽 아래)
            (13, "원예 I: 기초",     "식물 재배의 기초를 익힌다.",
                SkillCategory.Gardening, -3.5f, -1,    new[]{ 4 },    false ),
            (14, "원예 II: 숙련",    "수확량과 작물 성장 속도가 향상된다.",
                SkillCategory.Gardening, -5,    -2,    new[]{ 13 },   false ),

            // ── 운반 (아래쪽)
            (15, "운반 I: 기초",     "효율적인 화물 운반 기법을 익힌다.",
                SkillCategory.Hauling,    0,    -2,    new[]{ 1 },    false ),
            (16, "운반 II: 신속",    "운반 속도와 최대 적재량이 향상된다.",
                SkillCategory.Hauling,    0,    -3.5f, new[]{ 15 },   false ),

            // ── 제작 (오른쪽 아래)
            (17, "제작 I: 기초",     "기본 아이템 제작 기술을 습득한다.",
                SkillCategory.Crafting,   3.5f, -1,    new[]{ 3 },    false ),
            (18, "제작 II: 숙련",    "더 복잡한 아이템을 더 빠르게 제작할 수 있다.",
                SkillCategory.Crafting,   5,    -2,    new[]{ 17 },   false ),
        };

        foreach (var d in defs)
        {
            // 파일명에 사용 불가 문자 제거
            string safeName = d.name.Replace(" ", "_").Replace(":", "").Replace("/", "");
            string path     = $"{SKILLS_FOLDER}/Skill_{d.id:D2}_{safeName}.asset";

            // 이미 존재하면 재사용
            var existing = AssetDatabase.LoadAssetAtPath<SkillData>(path);
            if (existing != null)
            {
                skills.Add(existing);
                continue;
            }

            var sd                  = ScriptableObject.CreateInstance<SkillData>();
            sd.skillId              = d.id;
            sd.skillName            = d.name;
            sd.description          = d.desc;
            sd.category             = d.cat;
            sd.treePosition         = new Vector2(d.x, d.y);
            sd.prerequisiteSkillIds = new List<int>(d.prereqs);
            sd.defaultUnlocked      = d.def;

            AssetDatabase.CreateAsset(sd, path);
            skills.Add(sd);
        }

        // ── SkillTreeConfig 생성 또는 갱신
        var configPath = $"{SKILLS_FOLDER}/SkillTreeConfig.asset";
        var config     = AssetDatabase.LoadAssetAtPath<SkillTreeConfig>(configPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<SkillTreeConfig>();
            AssetDatabase.CreateAsset(config, configPath);
        }

        config.skills = skills;
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[스킬 트리] 샘플 데이터 생성 완료 ── 스킬 {skills.Count}개, Config: {configPath}");
        EditorGUIUtility.PingObject(config);
        Selection.activeObject = config;

        return config;
    }

    // ─────────────────────────────────────────────
    /// <summary>중간 폴더가 없어도 경로를 재귀 생성합니다.</summary>
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string[] parts  = path.Split('/');
        string   current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
