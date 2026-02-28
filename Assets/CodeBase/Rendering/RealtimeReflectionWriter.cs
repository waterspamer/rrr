using UnityEngine;
using UnityEngine.Rendering;

public class RealtimeReflectionWriter : MonoBehaviour
{
    [SerializeField] private ReflectionProbe reflectionProbe;
    [SerializeField] private RenderTexture cubeMapTexture;

    private void OnEnable()
    {
        if (reflectionProbe != null)
            reflectionProbe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
    }

    private void LateUpdate()
    {
        if (reflectionProbe == null || cubeMapTexture == null)
            return;

        EnsureCubeTarget(cubeMapTexture);
        reflectionProbe.RenderProbe(cubeMapTexture);
    }

    private static void EnsureCubeTarget(RenderTexture target)
    {
        bool needsRecreate = target.dimension != TextureDimension.Cube;
        if (needsRecreate)
            target.Release();

        target.dimension = TextureDimension.Cube;

        if (needsRecreate || !target.IsCreated())
            target.Create();
    }
}
