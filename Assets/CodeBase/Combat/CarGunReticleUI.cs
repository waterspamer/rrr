using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CarGunReticleUI : MonoBehaviour
{
    [SerializeField] private CarGunShooter shooter;
    [SerializeField] private bool autoFindShooter = true;
    [SerializeField] private Color reticleColor = new Color(1.0f, 1.0f, 1.0f, 0.95f);
    [SerializeField, Min(2.0f)] private float lineLength = 12.0f;
    [SerializeField, Min(1.0f)] private float lineThickness = 2.0f;
    [SerializeField, Min(0.0f)] private float lineGap = 10.0f;
    [SerializeField, Min(1.0f)] private float centerDotSize = 3.0f;

    private Canvas canvas;
    private RectTransform canvasRect;
    private RectTransform reticleRoot;

    private void Awake()
    {
        if (autoFindShooter && shooter == null)
            shooter = GetComponent<CarGunShooter>();
        BuildRuntimeUi();
    }

    private void Update()
    {
        if (canvas == null)
            return;
        canvas.enabled = shooter == null || shooter.isActiveAndEnabled;
        if (!canvas.enabled || reticleRoot == null)
            return;

        UpdateReticlePosition(GetAimScreenPosition());
    }

    private void BuildRuntimeUi()
    {
        GameObject canvasObject = new GameObject("CarGunReticleCanvas");
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
            canvasObject.layer = uiLayer;
        canvasObject.transform.SetParent(transform, false);

        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        canvasRect = canvas.transform as RectTransform;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject root = new GameObject("Reticle");
        root.transform.SetParent(canvasObject.transform, false);
        RectTransform rootRect = root.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        float size = (lineGap + lineLength + lineThickness) * 2.0f;
        rootRect.sizeDelta = new Vector2(size, size);
        reticleRoot = rootRect;
        UpdateReticlePosition(GetAimScreenPosition());

        CreateRect("Top", rootRect, new Vector2(0.0f, lineGap + lineLength * 0.5f), new Vector2(lineThickness, lineLength));
        CreateRect("Bottom", rootRect, new Vector2(0.0f, -(lineGap + lineLength * 0.5f)), new Vector2(lineThickness, lineLength));
        CreateRect("Left", rootRect, new Vector2(-(lineGap + lineLength * 0.5f), 0.0f), new Vector2(lineLength, lineThickness));
        CreateRect("Right", rootRect, new Vector2(lineGap + lineLength * 0.5f, 0.0f), new Vector2(lineLength, lineThickness));
        CreateRect("Dot", rootRect, Vector2.zero, new Vector2(centerDotSize, centerDotSize));
    }

    private void CreateRect(string name, RectTransform parent, Vector2 anchoredPos, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        Image image = obj.AddComponent<Image>();
        image.color = reticleColor;
        image.raycastTarget = false;

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
    }

    private static Vector2 GetAimScreenPosition()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.position.ReadValue();
#endif
        return Input.mousePosition;
    }

    private void UpdateReticlePosition(Vector2 screenPos)
    {
        if (reticleRoot == null || canvasRect == null)
            return;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, null, out Vector2 localPos))
            reticleRoot.anchoredPosition = localPos;
    }
}
