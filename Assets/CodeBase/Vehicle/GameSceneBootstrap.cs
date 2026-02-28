using UnityEngine;

public class GameSceneBootstrap : MonoBehaviour
{
    [SerializeField] private PlayerCar playerCar;
    [SerializeField] private FollowCarCamera followCamera;
    [SerializeField] private bool ensureFollowCamera = true;
    [SerializeField] private bool lockCursor = true;

    private void Awake()
    {
        if (playerCar == null)
            playerCar = FindFirstObjectByType<PlayerCar>();

        if (playerCar == null)
        {
            Debug.LogError("GameSceneBootstrap: PlayerCar not found in scene. Add PlayerCar to Game scene.", this);
            return;
        }

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (PlayerCarSelection.SelectedCarConfig == null &&
            PlayerCarSelection.SelectedHandling == null &&
            PlayerCarSelection.SelectedEngine == null &&
            PlayerCarSelection.SelectedSuspension == null)
        {
            if (ensureFollowCamera)
                EnsureFollowCamera(playerCar.transform);
            return;
        }

        playerCar.OverrideLoadout(
            PlayerCarSelection.SelectedCarConfig,
            PlayerCarSelection.SelectedHandling,
            PlayerCarSelection.SelectedEngine,
            PlayerCarSelection.SelectedSuspension);

        if (PlayerCarSelection.HasPaint)
            playerCar.SetPaint(PlayerCarSelection.SelectedPaint);

        if (ensureFollowCamera)
            EnsureFollowCamera(playerCar.transform);
    }

    private void EnsureFollowCamera(Transform target)
    {
        if (target == null)
            return;

        if (followCamera == null)
            followCamera = FindFirstObjectByType<FollowCarCamera>();

        if (followCamera != null)
            followCamera.SetTarget(target);
        else
            Debug.LogWarning("GameSceneBootstrap: FollowCarCamera not found in scene.", this);
    }
}
