using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

public abstract class PgUiWindow<T> : UiWindowBase where T : PgUiWindow<T>
{
    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
                if (_instance == null)
                {
                    Debug.LogWarning("No instance of " + typeof(T).Name + " found in scene.");
                }
            }
            return _instance;
        }
        private set => _instance = value;
    }

    private static T _instance;
    public GameObject[] hide;
    public System.Action onShow;
    public System.Action onHide;
    public Image image;
    public Camera camera;
    [SerializeField] private bool animateCameraColor = true;
    private bool isActive = false;
    public override void Awake()
    {
        if(_instance != null) return;
        
        Instance = this as T;
        
        image = GetComponent<Image>();
        if (image)
        {
            image.enabled = false;
        }
    }

    [NaughtyAttributes.Button]
    void FindCamera()
    {
        camera = Object.FindFirstObjectByType<Camera>();
    }
    public void SetCameraColor()
    {
       if (camera != null && image != null)
       {
           camera.backgroundColor = image.color;
       }
    }

    public override void Show()
    {
        gameObject.SetActive(true);
        hide.SetActive(false);
        if(animateCameraColor)
            SetCameraColor();
        onShow?.Invoke();
        isActive = true;
        
        Debug.Log(gameObject.name + " UI Show");
    }

    public override void Hide()
    {
        Debug.Log(gameObject.name + "UI Hide");
        
        hide.SetActive(true);
        gameObject.SetActive(false);

        if (isActive)
        {
            onHide?.Invoke();
            Debug.Log(gameObject.name + " UI onHide");

        }
        isActive = false;

    }
    protected virtual void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }
}