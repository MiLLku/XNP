using UnityEngine;

/// <summary>
/// 씬 전환 시 파괴되는 싱글톤 베이스 클래스.
/// 씬마다 새로 생성되어야 하는 매니저에 사용합니다.
/// </summary>
/// <typeparam name="T">싱글톤으로 사용할 MonoBehaviour 타입</typeparam>
public class DestroySingleton<T> : MonoBehaviour where T : MonoBehaviour
{
    public static T instance;

    protected virtual void Awake()
    {
        instance = this as T;
    }
}
