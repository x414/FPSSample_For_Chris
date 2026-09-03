using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SinglePlayerMenuUI : MonoBehaviour
{
    Action<SinglePlayerGameLoop.Mode, SinglePlayerGameLoop.Difficulty> m_OnStart;
    Button[] m_ModeButtons;
    Button[] m_DifficultyButtons;
    Button m_StartButton;
    int m_ModeIndex;
    int m_DifficultyIndex = 0;

    readonly string[] m_ModeNames = { "Wave", "Explore" };
    readonly string[] m_DifficultyNames = { "Easy", "Normal", "Hard" };

    void Awake()
    {
        var canvasObject = new GameObject("SinglePlayerMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        if (FindObjectOfType<EventSystem>() == null)
        {
            var eventSystemObject = new GameObject("SinglePlayerEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystemObject.transform.SetParent(transform, false);
        }

        var font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        var panel = CreatePanel(canvasObject.transform, new Vector2(0f, 0f), new Vector2(520f, 420f), new Color(0f, 0f, 0f, 0.75f));
        CreateText(panel.transform, "Single Player", 36, TextAnchor.UpperCenter, new Vector2(0f, 155f), new Vector2(460f, 50f), Color.white, font);
        CreateText(panel.transform, "Mode", 24, TextAnchor.MiddleLeft, new Vector2(0f, 90f), new Vector2(440f, 32f), Color.white, font);

        m_ModeButtons = new Button[m_ModeNames.Length];
        for (int i = 0; i < m_ModeNames.Length; i++)
        {
            var x = i == 0 ? -115f : 115f;
            var index = i;
            m_ModeButtons[i] = CreateButton(panel.transform, m_ModeNames[i], new Vector2(x, 40f), new Vector2(200f, 60f), font, () =>
            {
                m_ModeIndex = index;
                UpdateSelectionColors();
            });
        }

        CreateText(panel.transform, "Difficulty", 24, TextAnchor.MiddleLeft, new Vector2(0f, -15f), new Vector2(440f, 32f), Color.white, font);
        m_DifficultyButtons = new Button[m_DifficultyNames.Length];
        for (int i = 0; i < m_DifficultyNames.Length; i++)
        {
            var x = (i - 1) * 160f;
            var index = i;
            m_DifficultyButtons[i] = CreateButton(panel.transform, m_DifficultyNames[i], new Vector2(x, -70f), new Vector2(140f, 60f), font, () =>
            {
                m_DifficultyIndex = index;
                UpdateSelectionColors();
            });
        }

        m_StartButton = CreateButton(panel.transform, "Start Game", new Vector2(0f, -160f), new Vector2(360f, 70f), font, StartGame);
        UpdateSelectionColors();
    }

    void Start()
    {
        if (EventSystem.current != null && m_StartButton != null)
            EventSystem.current.SetSelectedGameObject(m_StartButton.gameObject);

    }

    void Update()
    {
        Game.SetMousePointerLock(false);

        if (Input.GetKeyDown(KeyCode.LeftArrow) && m_ModeIndex > 0)
        {
            m_ModeIndex--;
            UpdateSelectionColors();
        }

        if (Input.GetKeyDown(KeyCode.RightArrow) && m_ModeIndex < m_ModeNames.Length - 1)
        {
            m_ModeIndex++;
            UpdateSelectionColors();
        }

        if (Input.GetKeyDown(KeyCode.UpArrow) && m_DifficultyIndex > 0)
        {
            m_DifficultyIndex--;
            UpdateSelectionColors();
        }

        if (Input.GetKeyDown(KeyCode.DownArrow) && m_DifficultyIndex < m_DifficultyNames.Length - 1)
        {
            m_DifficultyIndex++;
            UpdateSelectionColors();
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
            StartGame();
    }

    public void Initialize(Action<SinglePlayerGameLoop.Mode, SinglePlayerGameLoop.Difficulty> onStart)
    {
        m_OnStart = onStart;
    }

    void UpdateSelectionColors()
    {
        for (int i = 0; i < m_ModeButtons.Length; i++)
            m_ModeButtons[i].image.color = i == m_ModeIndex ? new Color(0.2f, 0.6f, 0.2f) : new Color(0.15f, 0.15f, 0.2f);

        for (int i = 0; i < m_DifficultyButtons.Length; i++)
            m_DifficultyButtons[i].image.color = i == m_DifficultyIndex ? new Color(0.2f, 0.6f, 0.2f) : new Color(0.15f, 0.15f, 0.2f);
    }

    void StartGame()
    {
        Console.SetOpen(false);
        if (m_OnStart != null)
            m_OnStart((SinglePlayerGameLoop.Mode)m_ModeIndex, (SinglePlayerGameLoop.Difficulty)m_DifficultyIndex);
    }

    GameObject CreatePanel(Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var panelObject = new GameObject("Panel", typeof(Image));
        panelObject.transform.SetParent(parent, false);
        var rect = panelObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        panelObject.GetComponent<Image>().color = color;
        return panelObject;
    }

    Text CreateText(Transform parent, string content, int fontSize, TextAnchor alignment, Vector2 position, Vector2 size, Color color, Font font)
    {
        var textObject = new GameObject(content + "Text", typeof(Text));
        textObject.transform.SetParent(parent, false);
        var rect = textObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        var text = textObject.GetComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        return text;
    }

    Button CreateButton(Transform parent, string content, Vector2 position, Vector2 size, Font font, Action onClick)
    {
        var buttonObject = new GameObject(content + "Button", typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        var rect = buttonObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        var button = buttonObject.GetComponent<Button>();
        button.image.color = new Color(0.15f, 0.15f, 0.2f);
        button.onClick.AddListener(() => onClick());

        var text = CreateText(buttonObject.transform, content, 26, TextAnchor.MiddleCenter, Vector2.zero, size, Color.white, font);
        text.rectTransform.anchoredPosition = Vector2.zero;
        return button;
    }
}

public class SinglePlayerHudUI : MonoBehaviour
{
   Text m_StatusText;
   Text m_ProgressText;
   Text m_BannerText;
    void Awake()
    {
        var canvasObject = new GameObject("SinglePlayerHudCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        var font = CreateFont();
        var background = CreatePanel(canvasObject.transform, new Vector2(0f, 352f), new Vector2(640f, 76f), new Color(0f, 0f, 0f, 0.35f));
        m_StatusText = CreateText(background.transform, "", 26, TextAnchor.MiddleCenter, new Vector2(0f, 8f), new Vector2(600f, 30f), Color.white, font);
        m_ProgressText = CreateText(background.transform, "", 21, TextAnchor.MiddleCenter, new Vector2(0f, -22f), new Vector2(600f, 28f), new Color(0.9f, 1f, 0.9f), font);
       var bannerBackground = CreatePanel(canvasObject.transform, Vector2.zero, new Vector2(640f, 180f), new Color(0f, 0f, 0f, 0.78f));
       m_BannerText = CreateText(bannerBackground.transform, "", 48, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(600f, 130f), Color.white, font);
        bannerBackground.gameObject.SetActive(false);
    }

   public void UpdateStats(string status, string progress, string banner)
    {
        m_StatusText.text = status;
        m_ProgressText.text = progress;
        m_BannerText.text = banner;
        m_BannerText.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(banner));
    }

    Font CreateFont()
    {
        foreach (var candidate in new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei" })
        {
            if (Array.IndexOf(Font.GetOSInstalledFontNames(), candidate) >= 0)
                return Font.CreateDynamicFontFromOSFont(candidate, 36);
        }
        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    GameObject CreatePanel(Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var panelObject = new GameObject("HudPanel", typeof(Image));
        panelObject.transform.SetParent(parent, false);
        var rect = panelObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        panelObject.GetComponent<Image>().color = color;
        return panelObject;
    }

    Text CreateText(Transform parent, string content, int fontSize, TextAnchor alignment, Vector2 position, Vector2 size, Color color, Font font)
    {
        var textObject = new GameObject(content + "Text", typeof(Text));
        textObject.transform.SetParent(parent, false);
        var rect = textObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        var text = textObject.GetComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        return text;
    }
}

public class SinglePlayerResultUI : MonoBehaviour
{
    public static bool IsShowing { get; private set; }
    Text m_TitleText;
    Text m_StatsText;
   Button m_PlayAgainButton;
    bool m_RestartRequested;
    Action m_OnRestart;
    Action m_OnQuit;

    void Awake()
    {
        var canvasObject = new GameObject("ResultCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        if (FindObjectOfType<EventSystem>() == null)
        {
            var eventSystemObject = new GameObject("ResultEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystemObject.transform.SetParent(canvasObject.transform, false);
        }

        var font = CreateFont();
        var panel = new GameObject("ResultPanel", typeof(Image)).GetComponent<RectTransform>();
        panel.SetParent(canvasObject.transform, false);
        panel.sizeDelta = new Vector2(560f, 380f);
        panel.anchoredPosition = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.86f);

        m_TitleText = CreateText(panel, "RESULT", 42, TextAnchor.MiddleCenter, new Vector2(0f, 125f), new Vector2(480f, 65f), Color.white, font);
        m_StatsText = CreateText(panel, "", 26, TextAnchor.MiddleCenter, new Vector2(0f, 55f), new Vector2(480f, 80f), new Color(0.85f, 0.9f, 1f), font);
        m_PlayAgainButton = CreateButton(panel, "PLAY AGAIN", new Vector2(0f, -55f), new Vector2(380f, 72f), font, Restart);
        CreateButton(panel, "QUIT GAME", new Vector2(0f, -140f), new Vector2(380f, 60f), font, QuitGame);
    }

    void Start()
    {
        if (EventSystem.current != null && m_PlayAgainButton != null)
            EventSystem.current.SetSelectedGameObject(m_PlayAgainButton.gameObject);
    }

    void OnEnable()
    {
        IsShowing = true;
    }

    void OnDisable()
    {
        IsShowing = false;
    }

   public void Initialize(string title, string stats, Action onRestart, Action onQuit)
    {
        m_TitleText.text = title;
        m_StatsText.text = stats;
        m_OnRestart = onRestart;
        m_OnQuit = onQuit;
    }

    void Restart()
    {
        if (m_RestartRequested) return;
        m_RestartRequested = true;
        Console.SetOpen(false);
        if (m_OnRestart != null)
            m_OnRestart();
    }

    void QuitGame()
    {
        if (m_OnQuit != null)
            m_OnQuit();
    }

    Font CreateFont()
    {
        var installedFonts = Font.GetOSInstalledFontNames();
        foreach (var candidate in new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei" })
        {
            foreach (var installedFont in installedFonts)
            {
                if (installedFont == candidate)
                    return Font.CreateDynamicFontFromOSFont(installedFont, 36);
            }
        }

        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    Text CreateText(Transform parent, string content, int fontSize, TextAnchor alignment, Vector2 position, Vector2 size, Color color, Font font)
    {
        var textObject = new GameObject(content + "Text", typeof(Text));
        textObject.transform.SetParent(parent, false);
        var rect = textObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        var text = textObject.GetComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        return text;
    }

    Button CreateButton(Transform parent, string content, Vector2 position, Vector2 size, Font font, Action onClick)
    {
        var buttonObject = new GameObject(content + "Button", typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        var rect = buttonObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        var button = buttonObject.GetComponent<Button>();
       button.image.color = new Color(0.2f, 0.6f, 0.2f);
       button.onClick.AddListener(() => onClick());
       CreateText(buttonObject.transform, content, 24, TextAnchor.MiddleCenter, Vector2.zero, size, Color.white, font);
        return button;
   }
}
