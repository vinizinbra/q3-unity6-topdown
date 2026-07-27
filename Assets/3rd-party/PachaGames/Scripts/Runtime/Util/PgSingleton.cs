using System;
using UnityEngine;
using Object = UnityEngine.Object;

public abstract class PgSingleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;
    private static bool _quitting;
    private static bool _initialized;

    public static T I
    {
        get
        {
            if (_quitting) return null;

            if (_instance == null)
            {
                _instance = Object.FindFirstObjectByType<T>();

                if (_instance != null)
                {
                    DontDestroyOnLoad(_instance.gameObject);
                    CallOnInit();

                }

            }

            return _instance;
        }
    }

    private void OnDestroy()
    {
        _instance = null;
        _initialized = false;
    }

    protected virtual void Awake()
    {
        if (_instance == null)
        {
            _instance = this as T;
            DontDestroyOnLoad(gameObject);
            CallOnInit();
        }
        else if (_instance != this)
        {
            Destroy(this); // Prevent duplicate
        }
    }

    private static void CallOnInit()
    {
        if (!_initialized && _instance is PgSingleton<T> singleton)
        {
            singleton.OnInit();
            _initialized = true;
        }
    }

    protected virtual void OnApplicationQuit()
    {
        _quitting = true;
    }

    /// <summary>
    /// Called once when the singleton is initialized. Override in subclass.
    /// </summary>
    protected virtual void OnInit() { }
}