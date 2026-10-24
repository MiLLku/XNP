using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 메인 메뉴 씬 — 새 게임 / 이어하기(세이브가 있을 때만) / 종료.
/// UI는 실행 시 RuntimeUI로 만든다 (씬에는 카메라와 이 컴포넌트만 있으면 된다).
///
/// 이어하기는 가장 최근 세이브를 고르고 게임 씬을 연 뒤 SaveManager.PendingLoadSlot으로 불러온다.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    public const string MENU_SCENE = "MainMenu";
    public const string GAME_SCENE = "SampleScene";

    // SaveManager와 같은 폴더·확장자 — 메뉴 씬에는 SaveManager가 없다
    private const string SAVE_FOLDER = "Saves";
    private const string SAVE_EXTENSION = ".json";

    private void Start()
    {
        Time.timeScale = 1f;
        if (FindAnyObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        BuildUI();
    }

    private void BuildUI()
    {
        var root = new GameObject("MainMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(root.transform, false);
        var prt = (RectTransform)panel.transform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        RuntimeUI.SetupPanel(prt, 360f);

        var title = RuntimeUI.Text(panel.transform, "XENOPS", 40);
        title.alignment = TextAlignmentOptions.Center;

        RuntimeUI.Btn(panel.transform, "새 게임", 0, NewGame, RuntimeUI.BTN_ON);
        var latest = LatestSaveSlot();
        var cont = RuntimeUI.Btn(panel.transform, latest != null ? "이어하기" : "이어하기 (세이브 없음)", 0, () => Continue(latest),
                                 latest != null ? RuntimeUI.BTN : RuntimeUI.BTN_DIM);
        cont.interactable = latest != null;
        RuntimeUI.Btn(panel.transform, "종료", 0, Quit, RuntimeUI.BTN);
    }

    private static void NewGame()
    {
        SaveManager.PendingLoadSlot = null;
        SceneManager.LoadScene(GAME_SCENE);
    }

    private static void Continue(string slot)
    {
        if (slot == null) return;
        SaveManager.PendingLoadSlot = slot;
        SceneManager.LoadScene(GAME_SCENE);
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>가장 최근에 쓴 세이브 슬롯 이름 (파일 이름 = 슬롯 이름). 없으면 null</summary>
    private static string LatestSaveSlot()
    {
        string dir = Path.Combine(Application.persistentDataPath, SAVE_FOLDER);
        if (!Directory.Exists(dir)) return null;
        var file = new DirectoryInfo(dir).GetFiles("*" + SAVE_EXTENSION).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
        return file != null ? Path.GetFileNameWithoutExtension(file.Name) : null;
    }

    /// <summary>메인 메뉴로 (결과 화면 버튼)</summary>
    public static void GoToMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(MENU_SCENE);
    }
}
