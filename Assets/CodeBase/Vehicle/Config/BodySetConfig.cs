using UnityEngine;

[CreateAssetMenu(menuName = "Vehicles/Body Set", fileName = "BodySet")]
public class BodySetConfig : ScriptableObject
{
    [Header("UI")]
    [SerializeField] private Sprite icon;
    [SerializeField] private string displayName = "Body Set";

    [Header("Prefab")]
    [SerializeField] private GameObject bodySetPrefab;

    public Sprite Icon => icon;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public GameObject BodySetPrefab => bodySetPrefab;
}
