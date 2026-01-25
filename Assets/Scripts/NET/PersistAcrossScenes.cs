using UnityEngine;

public class PersistAcrossScenes : MonoBehaviour
{
    private static PersistAcrossScenes _instance;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
