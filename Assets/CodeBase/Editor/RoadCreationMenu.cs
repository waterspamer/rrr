using UnityEditor;
using UnityEngine;

public static class RoadCreationMenu
{
    [MenuItem("GameObject/CreateRoad", false, 10)]
    private static void CreateRoad(MenuCommand command)
    {
        GameObject roadObject = new GameObject("Road");
        GameObjectUtility.SetParentAndAlign(roadObject, command.context as GameObject);
        Undo.RegisterCreatedObjectUndo(roadObject, "Create Road");

        Road road = roadObject.AddComponent<Road>();
        road.EnsureStructure();
        road.EnsureDefaultLayout();
        road.Refresh();

        Selection.activeGameObject = roadObject;
    }
}
