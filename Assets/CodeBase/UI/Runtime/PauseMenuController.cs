using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class PauseMenuController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Scene")]
    [SerializeField] private string mainMenuScene = "MainMenu";

    private VisualElement root;
    private VisualElement panel;
    private bool isOpen;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();
        root = uiDocument.rootVisualElement;
        BuildUi();
        Close();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            Toggle();
    }

    private void BuildUi()
    {
        if (root == null)
            return;

        root.Clear();
        root.style.flexGrow = 1.0f;
        root.style.justifyContent = Justify.Center;
        root.style.alignItems = Align.Center;
        root.style.backgroundColor = new Color(0.02f, 0.03f, 0.035f, 0.5f);

        panel = new VisualElement();
        panel.style.width = 360;
        panel.style.paddingLeft = 16;
        panel.style.paddingRight = 16;
        panel.style.paddingTop = 14;
        panel.style.paddingBottom = 14;
        panel.style.backgroundColor = new Color(0.06f, 0.07f, 0.08f, 0.95f);
        panel.style.borderTopWidth = 2;
        panel.style.borderBottomWidth = 2;
        panel.style.borderLeftWidth = 2;
        panel.style.borderRightWidth = 2;
        panel.style.borderTopColor = new Color(0.67f, 0.58f, 0.37f, 1f);
        panel.style.borderBottomColor = new Color(0.67f, 0.58f, 0.37f, 1f);
        panel.style.borderLeftColor = new Color(0.67f, 0.58f, 0.37f, 1f);
        panel.style.borderRightColor = new Color(0.67f, 0.58f, 0.37f, 1f);
        panel.style.borderTopLeftRadius = 8;
        panel.style.borderTopRightRadius = 8;
        panel.style.borderBottomLeftRadius = 8;
        panel.style.borderBottomRightRadius = 8;
        root.Add(panel);

        var title = new Label("PAUSE");
        title.style.fontSize = 20;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = new Color(0.96f, 0.93f, 0.88f, 1f);
        title.style.unityTextAlign = TextAnchor.MiddleCenter;
        title.style.marginBottom = 8;
        panel.Add(title);

        var resumeButton = new Button(Resume) { text = "Resume" };
        StyleButton(resumeButton, primary: true);
        panel.Add(resumeButton);

        var menuButton = new Button(BackToMenu) { text = "Main Menu" };
        StyleButton(menuButton, primary: false);
        panel.Add(menuButton);
    }

    private static void StyleButton(Button button, bool primary)
    {
        button.style.height = 36;
        button.style.marginTop = 6;
        button.style.backgroundColor = primary ? new Color(0.95f, 0.2f, 0.18f, 1f) : new Color(0.12f, 0.13f, 0.15f, 1f);
        button.style.color = primary ? Color.white : new Color(0.9f, 0.92f, 0.94f, 1f);
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.borderTopLeftRadius = 6;
        button.style.borderTopRightRadius = 6;
        button.style.borderBottomLeftRadius = 6;
        button.style.borderBottomRightRadius = 6;
    }

    private void Toggle()
    {
        if (isOpen)
            Resume();
        else
            Open();
    }

    private void Open()
    {
        isOpen = true;
        if (root != null)
            root.style.display = DisplayStyle.Flex;
        Time.timeScale = 0.0f;
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
    }

    private void Close()
    {
        isOpen = false;
        if (root != null)
            root.style.display = DisplayStyle.None;
    }

    private void Resume()
    {
        Close();
        Time.timeScale = 1.0f;
        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        UnityEngine.Cursor.visible = false;
    }

    private void BackToMenu()
    {
        Time.timeScale = 1.0f;
        SceneManager.LoadScene(mainMenuScene);
    }
}
