using ControlFreak2;
using Quantum;
using UnityEngine;
using UnityEngine.UI;
using Button = UnityEngine.UI.Button;

// Touch emoji picker (see docs/emoji.md). Authored in the scene inside the CF2 rig's panel, next to the
// Dash/Skill touch buttons: the "..." toggle button and the bubble are tweaked there by hand. Only the four
// options are built from code - clones of optionTemplate (left inactive), one per EmojiKind, showing the
// local hero's emoji sprite.
public class EmojiPickerWidget : MonoBehaviour
{
    [SerializeField] private Button toggleButton;
    [SerializeField] private RectTransform bubble;
    [Tooltip("Inactive option cloned once per emoji (Happy, Angry, Cry, Shock) into its own parent. The sprite goes on the Image child named iconChildName, else on the Button's own Image.")]
    [SerializeField] private Button optionTemplate;
    [SerializeField] private string iconChildName = "Icon";
    [SerializeField] private float animTime = 0.15f;
    [Tooltip("Hide the whole picker unless Control Freak 2 is in mobile mode (a phone, or CF2 forced to mobile in the Editor).")]
    [SerializeField] private bool onlyInMobileMode = true;

    private readonly Image[] _icons = new Image[EmojiPresentation.Count];
    private bool _open;
    private float _openT;

    private void Awake()
    {
        if (toggleButton != null)
            toggleButton.onClick.AddListener(Toggle);

        if (optionTemplate != null)
        {
            optionTemplate.gameObject.SetActive(false);
            for (int i = 0; i < _icons.Length; i++)
            {
                var kind = (EmojiKind)i;
                Button option = Instantiate(optionTemplate, optionTemplate.transform.parent, false);
                option.name = "Option_" + kind;
                option.gameObject.SetActive(true);
                option.onClick.AddListener(() => Pick(kind));

                Transform icon = option.transform.Find(iconChildName);
                _icons[i] = icon != null ? icon.GetComponent<Image>() : option.image;
            }
        }

        if (bubble != null)
            bubble.gameObject.SetActive(false);
    }

    private void Toggle()
    {
        _open = !_open;
        if (_open)
            RefreshSprites();
    }

    private void Pick(EmojiKind kind)
    {
        if (EmojiWidget.Instance != null)
            EmojiWidget.Instance.Send(kind);
        _open = false;
    }

    private void RefreshSprites()
    {
        EntityRef local = EmojiWidget.LocalEntity;
        for (int i = 0; i < _icons.Length; i++)
        {
            if (_icons[i] == null)
                continue;

            Sprite sprite = EmojiPresentation.GetSprite(local, (EmojiKind)i);
            _icons[i].sprite = sprite;
            _icons[i].enabled = sprite != null;
        }
    }

    private void Update()
    {
        bool visible = onlyInMobileMode == false || CF2Input.IsInMobileMode();
        if (toggleButton != null && toggleButton.gameObject.activeSelf != visible)
            toggleButton.gameObject.SetActive(visible);

        if (visible == false)
            _open = false;

        _openT = Mathf.MoveTowards(_openT, _open ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, animTime));
        if (bubble == null)
            return;

        bool show = _openT > 0f;
        if (bubble.gameObject.activeSelf != show)
            bubble.gameObject.SetActive(show);
        bubble.localScale = Vector3.one * Mathf.SmoothStep(0.6f, 1f, _openT);
    }
}
