using UnityEngine;

[CreateAssetMenu(menuName = "Vehicles/Configs/Paint", fileName = "PaintConfig")]
public class PaintConfig : ScriptableObject
{
    [SerializeField] private string displayName = "Paint";
    [SerializeField] private Color color = Color.white;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public Color Color => color;
}
