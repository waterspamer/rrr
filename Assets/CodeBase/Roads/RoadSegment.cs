using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class RoadSegment : MonoBehaviour
{
    [SerializeField] private Road owner;
    [SerializeField] private RoadNode startNode;
    [SerializeField] private RoadNode endNode;
    [Min(1.0f)]
    [SerializeField] private float width = 6.0f;
    [Min(1)]
    [SerializeField] private int curveSubdivisions = 8;
    [SerializeField] private Vector3 startControlPointLocal;
    [SerializeField] private Vector3 endControlPointLocal;
    [SerializeField] private bool controlPointsInitialized;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private Mesh mesh;
    private Mesh colliderMesh;

    public Road Owner => owner;
    public RoadNode StartNode => startNode;
    public RoadNode EndNode => endNode;
    public float Width => width;
    public int CurveSubdivisions => curveSubdivisions;
    public float Length => startNode != null && endNode != null
        ? Vector3.Distance(startNode.transform.position, endNode.transform.position)
        : 0.0f;

    private void OnValidate()
    {
        if (owner == null)
            owner = GetComponentInParent<Road>();

        width = Mathf.Max(1.0f, width);
        curveSubdivisions = Mathf.Max(1, curveSubdivisions);
        EnsureMeshComponents();

        if (!controlPointsInitialized && startNode != null && endNode != null)
            ResetControlPoints();

        owner?.Refresh();
    }

    private void Awake()
    {
        EnsureMeshComponents();
    }

    private void Update()
    {
        if (Application.isPlaying || !transform.hasChanged)
            return;

        transform.hasChanged = false;
        owner?.Refresh();
    }

    public void SetOwner(Road road)
    {
        owner = road;
    }

    public void SetNodes(RoadNode start, RoadNode end)
    {
        startNode = start;
        endNode = end;
        ResetControlPoints();
        RegisterWithNodes();
    }

    public void SetWidth(float segmentWidth)
    {
        width = Mathf.Max(1.0f, segmentWidth);
    }

    public void SetCurveSubdivisions(int subdivisions)
    {
        curveSubdivisions = Mathf.Max(1, subdivisions);
    }

    public void SetStartControlPointWorld(Vector3 worldPoint)
    {
        startControlPointLocal = transform.InverseTransformPoint(worldPoint);
        controlPointsInitialized = true;
    }

    public void SetEndControlPointWorld(Vector3 worldPoint)
    {
        endControlPointLocal = transform.InverseTransformPoint(worldPoint);
        controlPointsInitialized = true;
    }

    public Vector3 GetStartControlPointWorld()
    {
        EnsureDefaultControlPoints();
        return transform.TransformPoint(startControlPointLocal);
    }

    public Vector3 GetEndControlPointWorld()
    {
        EnsureDefaultControlPoints();
        return transform.TransformPoint(endControlPointLocal);
    }

    public void ResetControlPoints()
    {
        if (startNode == null || endNode == null)
        {
            controlPointsInitialized = false;
            return;
        }

        Vector3 start = startNode.transform.position;
        Vector3 end = endNode.transform.position;
        Vector3 direction = GetPlanarDirection(start, end);
        float distance = Vector3.Distance(start, end);
        float handleLength = Mathf.Max(distance / 3.0f, width);

        startControlPointLocal = transform.InverseTransformPoint(start + direction * handleLength);
        endControlPointLocal = transform.InverseTransformPoint(end - direction * handleLength);
        controlPointsInitialized = true;
    }

    public void RegisterWithNodes()
    {
        startNode?.RegisterSegment(this);

        if (endNode != null && endNode != startNode)
            endNode.RegisterSegment(this);
    }

    public void RebuildGeometry()
    {
        EnsureMeshComponents();
        UpdateMaterial();

        if (startNode == null || endNode == null)
        {
            mesh.Clear();
            UpdateCollider(null, null);
            return;
        }

        EnsureDefaultControlPoints();

        if (!TryBuildCurveFrameSamples(out List<CurveFrame> frames))
        {
            mesh.Clear();
            UpdateCollider(null, null);
            return;
        }

        int ringCount = frames.Count;
        Vector3[] vertices = new Vector3[ringCount * 2];
        Vector3[] normals = new Vector3[ringCount * 2];
        Vector2[] uvs = new Vector2[ringCount * 2];
        int[] triangles = new int[(ringCount - 1) * 6];

        float accumulatedLength = 0.0f;
        for (int i = 0; i < ringCount; i++)
        {
            CurveFrame frame = frames[i];
            if (i > 0)
                accumulatedLength += Vector3.Distance(frames[i - 1].center, frame.center);

            int vertexIndex = i * 2;
            vertices[vertexIndex] = transform.InverseTransformPoint(frame.left);
            vertices[vertexIndex + 1] = transform.InverseTransformPoint(frame.right);
            normals[vertexIndex] = Vector3.up;
            normals[vertexIndex + 1] = Vector3.up;
            uvs[vertexIndex] = new Vector2(0.0f, accumulatedLength);
            uvs[vertexIndex + 1] = new Vector2(1.0f, accumulatedLength);
        }

        int triangleIndex = 0;
        for (int i = 0; i < ringCount - 1; i++)
        {
            int current = i * 2;
            int next = current + 2;

            triangles[triangleIndex++] = current;
            triangles[triangleIndex++] = next;
            triangles[triangleIndex++] = next + 1;

            triangles[triangleIndex++] = current;
            triangles[triangleIndex++] = next + 1;
            triangles[triangleIndex++] = current + 1;
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.RecalculateBounds();
        UpdateCollider(vertices, triangles);
    }

    public Vector3[] GetEdgePointsForNode(RoadNode node)
    {
        if (node == null || startNode == null || endNode == null)
            return System.Array.Empty<Vector3>();

        if (node.TryGetPortalEdgePoints(this, out Vector3 leftPoint, out Vector3 rightPoint))
            return new[] { leftPoint, rightPoint };

        return System.Array.Empty<Vector3>();
    }

    public Vector3 GetDirectionFromNode(RoadNode node)
    {
        if (node == null || startNode == null || endNode == null)
            return Vector3.forward;

        EnsureDefaultControlPoints();

        if (node == startNode)
            return GetPlanarDirection(startNode.transform.position, GetStartControlPointWorld());

        if (node == endNode)
            return GetPlanarDirection(endNode.transform.position, GetEndControlPointWorld());

        return Vector3.forward;
    }

    public Vector3 EvaluateCenter(float t)
    {
        EnsureDefaultControlPoints();
        return EvaluateCubicBezier(
            GetStartPortalCenter(),
            GetStartControlPointWorld(),
            GetEndControlPointWorld(),
            GetEndPortalCenter(),
            Mathf.Clamp01(t));
    }

    public Vector3 EvaluateTangent(float t)
    {
        EnsureDefaultControlPoints();
        Vector3 tangent = EvaluateCubicBezierTangent(
            GetStartPortalCenter(),
            GetStartControlPointWorld(),
            GetEndControlPointWorld(),
            GetEndPortalCenter(),
            Mathf.Clamp01(t));
        tangent.y = 0.0f;
        if (tangent.sqrMagnitude < 0.0001f)
            tangent = GetPlanarDirection(GetStartPortalCenter(), GetEndPortalCenter());

        return tangent.normalized;
    }

    private bool TryBuildCurveFrameSamples(out List<CurveFrame> frames)
    {
        frames = new List<CurveFrame>();

        Vector3 startCenter = GetStartPortalCenter();
        Vector3 endCenter = GetEndPortalCenter();
        if (!startNode.TryGetPortalEdgePoints(this, out Vector3 startLeftPortal, out Vector3 startRightPortal))
            return false;
        if (!endNode.TryGetPortalEdgePoints(this, out Vector3 endLeftPortal, out Vector3 endRightPortal))
            return false;

        Vector3 previousLateral = GetOrderedLateral(startLeftPortal, startRightPortal);
        if (previousLateral.sqrMagnitude < 0.0001f)
            previousLateral = GetFallbackLateral(startCenter, endCenter);

        int sampleCount = Mathf.Max(2, curveSubdivisions + 1);
        Vector3 startControl = GetStartControlPointWorld();
        Vector3 endControl = GetEndControlPointWorld();
        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (sampleCount - 1.0f);
            Vector3 center = EvaluateCubicBezier(startCenter, startControl, endControl, endCenter, t);

            Vector3 tangent = EvaluateCubicBezierTangent(startCenter, startControl, endControl, endCenter, t);
            tangent.y = 0.0f;
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = GetPlanarDirection(startCenter, endCenter);

            Vector3 lateral = Vector3.Cross(Vector3.up, tangent.normalized);
            if (lateral.sqrMagnitude < 0.0001f)
            {
                lateral = previousLateral;
            }
            else
            {
                lateral.Normalize();
                if (Vector3.Dot(lateral, previousLateral) < 0.0f)
                    lateral = -lateral;
            }

            Vector3 left = center - lateral * (width * 0.5f);
            Vector3 right = center + lateral * (width * 0.5f);

            if (i == 0)
            {
                OrderPortalPair(startLeftPortal, startRightPortal, center, lateral, out left, out right);
            }
            else if (i == sampleCount - 1)
            {
                OrderPortalPair(endLeftPortal, endRightPortal, center, lateral, out left, out right);
            }

            previousLateral = (right - left).normalized;

            frames.Add(new CurveFrame
            {
                center = center,
                left = left,
                right = right
            });
        }

        return frames.Count >= 2;
    }

    private Vector3 GetStartPortalCenter()
    {
        return startNode != null ? startNode.GetPortalCenter(this) : transform.position;
    }

    private Vector3 GetEndPortalCenter()
    {
        return endNode != null ? endNode.GetPortalCenter(this) : transform.position;
    }

    private void EnsureDefaultControlPoints()
    {
        if (!controlPointsInitialized && startNode != null && endNode != null)
            ResetControlPoints();
    }

    private void EnsureMeshComponents()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();

        if (mesh == null)
        {
            Mesh existingMesh = meshFilter.sharedMesh;
            if (existingMesh != null && existingMesh.name == "RoadSegmentMesh")
            {
                mesh = existingMesh;
            }
            else
            {
                mesh = new Mesh
                {
                    name = "RoadSegmentMesh"
                };
                meshFilter.sharedMesh = mesh;
            }
        }

        if (meshCollider != null)
            meshCollider.convex = false;
    }

    private static Vector3 GetPlanarDirection(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        direction.y = 0.0f;
        if (direction.sqrMagnitude < 0.0001f)
            return Vector3.forward;

        return direction.normalized;
    }

    private void UpdateMaterial()
    {
        if (meshRenderer != null && owner != null && owner.RoadMaterial != null)
            meshRenderer.sharedMaterial = owner.RoadMaterial;
    }

    private void UpdateCollider(Vector3[] localTopVertices, int[] topTriangles)
    {
        if (meshCollider == null)
            return;

        if (colliderMesh != null)
        {
            if (Application.isPlaying)
                Object.Destroy(colliderMesh);
            else
                Object.DestroyImmediate(colliderMesh);

            colliderMesh = null;
        }

        if (localTopVertices == null || localTopVertices.Length < 3 || topTriangles == null || topTriangles.Length < 3)
        {
            meshCollider.sharedMesh = null;
            return;
        }

        colliderMesh = RoadCollisionMeshBuilder.BuildExtrudedSurface(
            localTopVertices,
            topTriangles,
            owner != null ? owner.CollisionThickness : 0.35f,
            "RoadSegmentColliderMesh");
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = colliderMesh;
    }

    private static void OrderPortalPair(Vector3 pointA, Vector3 pointB, Vector3 center, Vector3 lateral, out Vector3 left, out Vector3 right)
    {
        float dotA = Vector3.Dot(pointA - center, lateral);
        float dotB = Vector3.Dot(pointB - center, lateral);
        if (dotA <= dotB)
        {
            left = pointA;
            right = pointB;
        }
        else
        {
            left = pointB;
            right = pointA;
        }
    }

    private static Vector3 GetOrderedLateral(Vector3 left, Vector3 right)
    {
        Vector3 lateral = right - left;
        lateral.y = 0.0f;
        return lateral.sqrMagnitude < 0.0001f ? Vector3.zero : lateral.normalized;
    }

    private static Vector3 GetFallbackLateral(Vector3 start, Vector3 end)
    {
        Vector3 direction = GetPlanarDirection(start, end);
        Vector3 lateral = Vector3.Cross(Vector3.up, direction);
        return lateral.sqrMagnitude < 0.0001f ? Vector3.right : lateral.normalized;
    }

    private static Vector3 EvaluateCubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float oneMinusT = 1.0f - t;
        return oneMinusT * oneMinusT * oneMinusT * p0
            + 3.0f * oneMinusT * oneMinusT * t * p1
            + 3.0f * oneMinusT * t * t * p2
            + t * t * t * p3;
    }

    private static Vector3 EvaluateCubicBezierTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float oneMinusT = 1.0f - t;
        return 3.0f * oneMinusT * oneMinusT * (p1 - p0)
            + 6.0f * oneMinusT * t * (p2 - p1)
            + 3.0f * t * t * (p3 - p2);
    }

    private struct CurveFrame
    {
        public Vector3 center;
        public Vector3 left;
        public Vector3 right;
    }
}
