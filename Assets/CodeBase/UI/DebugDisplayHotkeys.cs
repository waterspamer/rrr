using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public sealed class DebugDisplayHotkeys : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<DebugDisplayHotkeys>() != null)
            return;

        GameObject root = new GameObject("DebugDisplayHotkeys");
        DontDestroyOnLoad(root);
        root.AddComponent<DebugDisplayHotkeys>();
    }

    private void Update()
    {
        if (!WasTogglePressed())
            return;

        bool goWindowed = Screen.fullScreenMode != FullScreenMode.Windowed;
        Screen.fullScreenMode = goWindowed ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
        Screen.fullScreen = !goWindowed;
    }

    private static bool WasTogglePressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            return true;
#endif
        return Input.GetKeyDown(KeyCode.F1);
    }
}
