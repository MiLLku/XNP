using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class FixButtonColors
{
    private static readonly Color DarkBg   = new Color(0.14f, 0.16f, 0.20f, 1f);
    private static readonly Color IconBlue = new Color(0.25f, 0.55f, 1.00f, 1f);

    [MenuItem("Tools/Fix BottomBar Button Colors")]
    public static void Fix()
    {
        var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Asset/Maplestory OTF Bold SDF.asset");

        FixBtn("Canvas/BottomBar/ButtonContainer/ResearchBtn", "연구", fontAsset);
        FixBtn("Canvas/BottomBar/ButtonContainer/WorkBtn",     "작업", fontAsset);

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[Fix] 완료");
    }

    private static void FixBtn(string path, string labelText, TMP_FontAsset fontAsset)
    {
        var go = GameObject.Find(path);
        if (go == null) { Debug.LogWarning($"[Fix] Not found: {path}"); return; }

        // 버튼 배경 Image
        var img = go.GetComponent<Image>();
        if (img != null)
        {
            img.sprite = null;
            SetColor(img, DarkBg);

            var btn = go.GetComponent<Button>();
            if (btn != null)
            {
                var bso = new SerializedObject(btn);
                bso.FindProperty("m_TargetGraphic").objectReferenceValue = img;
                bso.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(img);
        }

        // IconArea 파란색
        var iconTf = go.transform.Find("IconArea");
        if (iconTf != null)
        {
            var iconImg = iconTf.GetComponent<Image>();
            if (iconImg != null)
            {
                SetColor(iconImg, IconBlue);
                EditorUtility.SetDirty(iconImg);
            }
        }

        // Label 텍스트 + 폰트
        var labelTf = go.transform.Find("Label");
        if (labelTf != null)
        {
            var tmp = labelTf.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.text = labelText;
                if (fontAsset != null) tmp.font = fontAsset;
                EditorUtility.SetDirty(tmp);
                Debug.Log($"[Fix] {go.name}/Label = \"{labelText}\"");
            }
        }

        Debug.Log($"[Fix] {go.name} done");
    }

    private static void SetColor(Image img, Color color)
    {
        var so = new SerializedObject(img);
        so.FindProperty("m_Color").colorValue = color;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
