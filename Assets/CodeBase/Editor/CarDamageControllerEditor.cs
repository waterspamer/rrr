using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CarDamageController))]
public class CarDamageControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CarDamageController controller = (CarDamageController)target;
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Repair"))
                controller.RepairDamage();
        }
    }
}
