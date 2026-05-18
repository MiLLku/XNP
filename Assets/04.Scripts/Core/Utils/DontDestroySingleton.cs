using UnityEngine;

/// <summary>
/// 씬 전환 시 파괴되지 않는 싱글톤 베이스 클래스.
/// 게임 전체에서 유지되어야 하는 매니저에 사용합니다.
/// 중복 인스턴스가 생성되면 자동으로 파괴됩니다.
/// </summary>
/// <typeparam name="T">싱글톤으로 사용할 MonoBehaviour 타입</typeparam>
public class DontDestroySingleton<T> : MonoBehaviour where T : MonoBehaviour
{
    public static T instance;

    protected virtual void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this as T;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }
}
