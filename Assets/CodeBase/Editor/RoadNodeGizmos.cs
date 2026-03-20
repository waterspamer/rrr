using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class RoadNodeGizmos
{
    [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Pickable)]
    private static void DrawNodeGizmo(RoadNode node, GizmoType gizmoType)
    {
        if (node == null)
            return;

        Vector3 position = node.transform.position;
        float size = HandleUtility.GetHandleSize(position) * 0.12f;

        Gizmos.color = Selection.activeGameObject == node.gameObject
            ? new Color(1.0f, 0.85f, 0.2f, 0.95f)
            : new Color(0.15f, 0.85f, 1.0f, 0.9f);
        Gizmos.DrawSphere(position, size);

        IReadOnlyList<RoadSegment> segments = node.ConnectedSegments;
        Gizmos.color = new Color(1.0f, 1.0f, 1.0f, 0.3f);
        for (int i = 0; i < segments.Count; i++)
        {
            RoadSegment segment = segments[i];
            if (segment == null)
                continue;

            Vector3 direction = segment.GetDirectionFromNode(node);
            direction.y = 0.0f;
            if (direction.sqrMagnitude < 0.0001f)
                continue;

            Gizmos.DrawLine(position, position + direction.normalized * size * 4.0f);
        }
    }
}
