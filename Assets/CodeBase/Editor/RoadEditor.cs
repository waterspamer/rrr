using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Road))]
public class RoadEditor : Editor
{
    private const float NewSegmentLength = 8.0f;
    private static readonly Color NodeColor = new Color(0.15f, 0.85f, 1.0f, 1.0f);
    private static readonly Color SelectedNodeColor = new Color(1.0f, 0.85f, 0.2f, 1.0f);
    private static readonly Color AddButtonColor = new Color(0.35f, 1.0f, 0.35f, 1.0f);
    private static readonly Color LinkButtonColor = new Color(1.0f, 0.55f, 0.2f, 1.0f);
    private static readonly Color LinkActiveColor = new Color(1.0f, 0.2f, 0.2f, 1.0f);
    private static readonly Color DeleteButtonColor = new Color(1.0f, 0.35f, 0.35f, 1.0f);
    private static readonly Color CurveColor = new Color(1.0f, 1.0f, 1.0f, 0.9f);
    private static readonly Color StartControlColor = new Color(0.35f, 0.9f, 1.0f, 1.0f);
    private static readonly Color EndControlColor = new Color(1.0f, 0.7f, 0.35f, 1.0f);
    private static RoadNode linkStartNode;

    private Road road;

    private void OnEnable()
    {
        road = (Road)target;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Refresh Road"))
                RefreshRoad();

            if (GUILayout.Button("Add Node At Center"))
                CreateStandaloneNode();
        }
    }

    private void OnSceneGUI()
    {
        if (road == null)
            return;

        HandleLinkModeInput();
        road.Refresh();

        IReadOnlyList<RoadNode> nodes = road.Nodes;
        for (int i = 0; i < nodes.Count; i++)
        {
            RoadNode node = nodes[i];
            if (node == null)
                continue;

            DrawNodeHandle(node, i);
            DrawNodeExpansionHandles(node);
        }

        DrawSegmentCurveHandles();
        DrawSegmentDeleteHandles();
        DrawLinkPreview();
    }

    private void DrawNodeHandle(RoadNode node, int index)
    {
        Vector3 position = node.transform.position;
        float size = HandleUtility.GetHandleSize(position) * 0.18f;

        EditorGUI.BeginChangeCheck();
        Vector3 newPosition = Handles.PositionHandle(position, Quaternion.identity);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(node.transform, "Move Road Node");
            node.transform.position = newPosition;
            RefreshRoad();
        }

        Handles.color = Selection.activeGameObject == node.gameObject
            ? SelectedNodeColor
            : NodeColor;

        if (Handles.Button(position, Quaternion.identity, size, size, Handles.SphereHandleCap))
            Selection.activeGameObject = road.gameObject;

        GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            normal = { textColor = Color.white }
        };
        Handles.Label(position + Vector3.up * size * 1.5f, $"Node {index}", labelStyle);

        Vector3 linkButtonPosition = position + Vector3.up * size * 2.7f;
        bool linkActive = linkStartNode == node;
        if (DrawActionButton(linkButtonPosition, size * 0.95f, linkActive ? "CANCEL" : "LINK", linkActive ? LinkActiveColor : LinkButtonColor))
            HandleLinkButtonClicked(node);

        Vector3 deleteButtonPosition = position + Vector3.up * size * 1.4f + Vector3.right * size * 1.25f;
        if (DrawActionButton(deleteButtonPosition, size * 0.8f, "X", DeleteButtonColor))
            DeleteNode(node);
    }

    private void DrawNodeExpansionHandles(RoadNode node)
    {
        Vector3[] directions = node.GetSortedExitDirections();
        Vector3 position = node.transform.position;

        if (directions.Length == 0)
        {
            DrawCreateButton(node, Vector3.forward);
            DrawCreateButton(node, Vector3.right);
            DrawCreateButton(node, Vector3.back);
            DrawCreateButton(node, Vector3.left);
            return;
        }

        if (directions.Length == 1)
        {
            DrawCreateButton(node, directions[0]);
            return;
        }

        for (int i = 0; i < directions.Length; i++)
        {
            Vector3 current = directions[i];
            Vector3 next = directions[(i + 1) % directions.Length];
            Vector3 between = GetBisectorDirection(current, next);

            if (between.sqrMagnitude < 0.0001f)
                between = Quaternion.AngleAxis(90.0f, Vector3.up) * current;

            DrawCreateButton(node, between.normalized);
        }

        Handles.color = new Color(1.0f, 1.0f, 1.0f, 0.25f);
        for (int i = 0; i < directions.Length; i++)
            Handles.DrawLine(position, position + directions[i] * HandleUtility.GetHandleSize(position) * 1.2f);
    }

    private void DrawCreateButton(RoadNode sourceNode, Vector3 direction)
    {
        direction.y = 0.0f;
        if (direction.sqrMagnitude < 0.0001f)
            return;

        direction.Normalize();

        Vector3 position = sourceNode.transform.position;
        float handleSize = HandleUtility.GetHandleSize(position);
        Vector3 buttonPosition = position + direction * handleSize * 1.1f;
        float buttonSize = handleSize * 0.3f;

        if (DrawActionButton(buttonPosition, buttonSize, "+", AddButtonColor))
            CreateAdjacentNode(sourceNode, direction);
    }

    private void CreateAdjacentNode(RoadNode sourceNode, Vector3 direction)
    {
        Undo.RegisterFullObjectHierarchyUndo(road.gameObject, "Create Adjacent Road Node");

        RoadNode newNode = road.CreateNode(sourceNode.transform.position + direction.normalized * NewSegmentLength);
        RoadSegment newSegment = road.CreateSegment(sourceNode, newNode);

        if (newNode != null)
            Undo.RegisterCreatedObjectUndo(newNode.gameObject, "Create Adjacent Road Node");
        if (newSegment != null)
            Undo.RegisterCreatedObjectUndo(newSegment.gameObject, "Create Adjacent Road Segment");

        RefreshRoad();
        Selection.activeGameObject = road.gameObject;
    }

    private void CreateStandaloneNode()
    {
        Undo.RegisterFullObjectHierarchyUndo(road.gameObject, "Create Road Node");
        RoadNode newNode = road.CreateNode(road.transform.position);
        if (newNode != null)
            Undo.RegisterCreatedObjectUndo(newNode.gameObject, "Create Road Node");

        RefreshRoad();
        Selection.activeGameObject = road.gameObject;
    }

    private void RefreshRoad()
    {
        if (road == null)
            return;

        Undo.RecordObject(road, "Refresh Road");
        road.Refresh();
        EditorUtility.SetDirty(road);
    }

    private void HandleLinkModeInput()
    {
        Event currentEvent = Event.current;
        if (currentEvent == null || linkStartNode == null)
            return;

        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 1)
        {
            linkStartNode = null;
            currentEvent.Use();
            Repaint();
            SceneView.RepaintAll();
        }
    }

    private void HandleLinkButtonClicked(RoadNode node)
    {
        if (node == null)
            return;

        if (linkStartNode == node)
        {
            linkStartNode = null;
            SceneView.RepaintAll();
            return;
        }

        if (linkStartNode == null)
        {
            linkStartNode = node;
            Selection.activeGameObject = road.gameObject;
            SceneView.RepaintAll();
            return;
        }

        CreateLink(linkStartNode, node);
        linkStartNode = null;
        SceneView.RepaintAll();
    }

    private void CreateLink(RoadNode startNode, RoadNode endNode)
    {
        if (startNode == null || endNode == null || startNode == endNode || SegmentExists(startNode, endNode))
            return;

        Undo.RegisterFullObjectHierarchyUndo(road.gameObject, "Create Road Link");
        RoadSegment segment = road.CreateSegment(startNode, endNode);
        if (segment != null)
            Undo.RegisterCreatedObjectUndo(segment.gameObject, "Create Road Link");

        RefreshRoad();
        Selection.activeGameObject = road.gameObject;
    }

    private bool SegmentExists(RoadNode startNode, RoadNode endNode)
    {
        IReadOnlyList<RoadSegment> segments = road.Segments;
        for (int i = 0; i < segments.Count; i++)
        {
            RoadSegment segment = segments[i];
            if (segment == null)
                continue;

            bool sameDirection = segment.StartNode == startNode && segment.EndNode == endNode;
            bool oppositeDirection = segment.StartNode == endNode && segment.EndNode == startNode;
            if (sameDirection || oppositeDirection)
                return true;
        }

        return false;
    }

    private void DrawLinkPreview()
    {
        if (linkStartNode == null)
            return;

        Vector3 start = linkStartNode.transform.position;
        Vector3 mouseWorld = GetMouseWorldPosition(start.y);
        Handles.color = new Color(LinkActiveColor.r, LinkActiveColor.g, LinkActiveColor.b, 0.9f);
        Handles.DrawAAPolyLine(4.0f, start, mouseWorld);

        GUIStyle hintStyle = new GUIStyle(EditorStyles.helpBox)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
        Handles.Label(start + Vector3.up * HandleUtility.GetHandleSize(start) * 3.4f, "LMB LINK on another node\nRMB cancel", hintStyle);
    }

    private void DrawSegmentDeleteHandles()
    {
        IReadOnlyList<RoadSegment> segments = road.Segments;
        for (int i = 0; i < segments.Count; i++)
        {
            RoadSegment segment = segments[i];
            if (segment == null || segment.StartNode == null || segment.EndNode == null)
                continue;

            Vector3 midPoint = (segment.StartNode.transform.position + segment.EndNode.transform.position) * 0.5f;
            float size = HandleUtility.GetHandleSize(midPoint) * 0.18f;
            Vector3 buttonPosition = midPoint + Vector3.up * size * 0.8f;
            if (DrawActionButton(buttonPosition, size, "X", DeleteButtonColor))
                DeleteSegment(segment);
        }
    }

    private void DrawSegmentCurveHandles()
    {
        IReadOnlyList<RoadSegment> segments = road.Segments;
        for (int i = 0; i < segments.Count; i++)
        {
            RoadSegment segment = segments[i];
            if (segment == null || segment.StartNode == null || segment.EndNode == null)
                continue;

            DrawSegmentCurvePreview(segment);
            DrawSegmentControlHandle(segment, true);
            DrawSegmentControlHandle(segment, false);
        }
    }

    private void DrawSegmentCurvePreview(RoadSegment segment)
    {
        Vector3 start = segment.EvaluateCenter(0.0f);
        Vector3 end = segment.EvaluateCenter(1.0f);
        Vector3 startControl = segment.GetStartControlPointWorld();
        Vector3 endControl = segment.GetEndControlPointWorld();

        Handles.color = CurveColor;
        Handles.DrawBezier(start, end, startControl, endControl, CurveColor, null, 3.0f);

        Handles.color = new Color(StartControlColor.r, StartControlColor.g, StartControlColor.b, 0.6f);
        Handles.DrawDottedLine(start, startControl, 4.0f);
        Handles.color = new Color(EndControlColor.r, EndControlColor.g, EndControlColor.b, 0.6f);
        Handles.DrawDottedLine(end, endControl, 4.0f);
    }

    private void DrawSegmentControlHandle(RoadSegment segment, bool isStartHandle)
    {
        Vector3 handlePosition = isStartHandle
            ? segment.GetStartControlPointWorld()
            : segment.GetEndControlPointWorld();
        Color color = isStartHandle ? StartControlColor : EndControlColor;
        float size = HandleUtility.GetHandleSize(handlePosition) * 0.1f;

        if (!IsFinite(handlePosition))
            return;

        Handles.color = color;
        EditorGUI.BeginChangeCheck();
        Vector3 newPosition = Handles.FreeMoveHandle(
            handlePosition,
            size,
            Vector3.zero,
            Handles.SphereHandleCap);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(segment, isStartHandle ? "Move Road Start Handle" : "Move Road End Handle");
            if (isStartHandle)
                segment.SetStartControlPointWorld(newPosition);
            else
                segment.SetEndControlPointWorld(newPosition);

            RefreshRoad();
            Selection.activeGameObject = road.gameObject;
        }

        GUIStyle labelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            normal = { textColor = Color.white }
        };
        Handles.Label(
            handlePosition + Vector3.up * size * 1.8f,
            isStartHandle ? "H1" : "H2",
            labelStyle);
    }

    private static bool DrawActionButton(Vector3 position, float size, string label, Color fillColor)
    {
        if (!IsFinite(position) || !float.IsFinite(size) || size <= 0.0f)
            return false;

        float pickSize = size * 0.55f;
        DrawRoundButtonCap(position, pickSize, EventType.Repaint, fillColor);
        bool pressed = Handles.Button(position, Quaternion.identity, pickSize, pickSize, Handles.CircleHandleCap);

        GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = label.Length > 2 ? 9 : 12,
            normal = { textColor = Color.black }
        };
        Handles.Label(position - Vector3.up * size * 0.1f, label, style);
        return pressed;
    }

    private static void DrawRoundButtonCap(Vector3 position, float size, EventType eventType, Color fillColor)
    {
        if (eventType != EventType.Repaint)
            return;

        Color outline = new Color(0.08f, 0.08f, 0.08f, 0.95f);
        Handles.color = outline;
        Handles.DrawSolidDisc(position, Vector3.up, size * 1.15f);
        Handles.color = fillColor;
        Handles.DrawSolidDisc(position, Vector3.up, size);
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }

    private static Vector3 GetMouseWorldPosition(float y)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        Plane plane = new Plane(Vector3.up, new Vector3(0.0f, y, 0.0f));
        if (plane.Raycast(ray, out float distance))
            return ray.GetPoint(distance);

        return ray.origin + ray.direction * 10.0f;
    }

    private static Vector3 GetBisectorDirection(Vector3 from, Vector3 to)
    {
        float fromAngle = Mathf.Atan2(from.z, from.x);
        float toAngle = Mathf.Atan2(to.z, to.x);
        float delta = Mathf.DeltaAngle(fromAngle * Mathf.Rad2Deg, toAngle * Mathf.Rad2Deg);
        float bisectorAngle = fromAngle + Mathf.Deg2Rad * (delta * 0.5f);
        return new Vector3(Mathf.Cos(bisectorAngle), 0.0f, Mathf.Sin(bisectorAngle));
    }

    private void DeleteNode(RoadNode node)
    {
        if (node == null)
            return;

        Undo.RegisterFullObjectHierarchyUndo(road.gameObject, "Delete Road Node");
        List<RoadSegment> segmentsToDelete = new List<RoadSegment>();
        IReadOnlyList<RoadSegment> segments = road.Segments;
        for (int i = 0; i < segments.Count; i++)
        {
            RoadSegment segment = segments[i];
            if (segment == null)
                continue;

            if (segment.StartNode == node || segment.EndNode == node)
                segmentsToDelete.Add(segment);
        }

        for (int i = 0; i < segmentsToDelete.Count; i++)
            Undo.DestroyObjectImmediate(segmentsToDelete[i].gameObject);

        if (linkStartNode == node)
            linkStartNode = null;

        Undo.DestroyObjectImmediate(node.gameObject);
        RefreshRoad();
        Selection.activeGameObject = road.gameObject;
        SceneView.RepaintAll();
    }

    private void DeleteSegment(RoadSegment segment)
    {
        if (segment == null)
            return;

        Undo.RegisterFullObjectHierarchyUndo(road.gameObject, "Delete Road Segment");
        Undo.DestroyObjectImmediate(segment.gameObject);
        RefreshRoad();
        Selection.activeGameObject = road.gameObject;
        SceneView.RepaintAll();
    }
}
