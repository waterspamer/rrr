using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class RoadNode : MonoBehaviour
{
    [SerializeField] private Road owner;
    [SerializeField] private List<RoadSegment> connectedSegments = new List<RoadSegment>();

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private Mesh mesh;
    private Mesh colliderMesh;

    public Road Owner => owner;
    public IReadOnlyList<RoadSegment> ConnectedSegments => connectedSegments;

    private void OnValidate()
    {
        if (owner == null)
            owner = GetComponentInParent<Road>();

        EnsureMeshComponents();
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

    public void ClearConnections()
    {
        connectedSegments.Clear();
    }

    public void RegisterSegment(RoadSegment segment)
    {
        if (segment == null || connectedSegments.Contains(segment))
            return;

        connectedSegments.Add(segment);
    }

    public void RebuildGeometry()
    {
        EnsureMeshComponents();
        UpdateMaterial();

        List<NodeConnectionData> connections = GetSortedConnections();
        if (connections.Count == 0)
        {
            mesh.Clear();
            UpdateCollider(null, null);
            return;
        }

        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        vertices.Add(Vector3.zero);
        normals.Add(Vector3.up);
        uvs.Add(new Vector2(0.5f, 0.5f));

        if (TryBuildNodePolygon(connections, out List<Vector3> polygonVertices))
            AddFanFromPolygon(polygonVertices, vertices, normals, uvs, triangles);

        if (triangles.Count == 0)
        {
            mesh.Clear();
            UpdateCollider(null, null);
            return;
        }

        mesh.Clear();
        mesh.vertices = vertices.ToArray();
        mesh.normals = normals.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateBounds();
        UpdateCollider(mesh.vertices, mesh.triangles);
    }

    public Vector3[] GetSortedExitDirections()
    {
        return GetSortedConnections()
            .Select(entry => entry.direction)
            .ToArray();
    }

    private List<NodeConnectionData> GetSortedConnections()
    {
        List<NodeConnectionData> entries = new List<NodeConnectionData>();
        for (int i = 0; i < connectedSegments.Count; i++)
        {
            RoadSegment segment = connectedSegments[i];
            if (segment == null)
                continue;

            Vector3 direction = segment.GetDirectionFromNode(this);
            direction.y = 0.0f;
            if (direction.sqrMagnitude < 0.0001f)
                continue;

            entries.Add(new NodeConnectionData
            {
                segment = segment,
                angle = Mathf.Atan2(direction.z, direction.x),
                direction = direction.normalized
            });
        }

        entries = entries
            .OrderBy(entry => entry.angle)
            .ToList();

        PopulateConnectionGeometry(entries);
        return entries;
    }

    public bool TryGetPortalEdgePoints(RoadSegment segment, out Vector3 leftPoint, out Vector3 rightPoint)
    {
        leftPoint = Vector3.zero;
        rightPoint = Vector3.zero;

        if (!TryGetConnectionData(segment, out NodeConnectionData connection))
            return false;

        leftPoint = connection.leftPoint;
        rightPoint = connection.rightPoint;
        return true;
    }

    public Vector3 GetPortalCenter(RoadSegment segment)
    {
        return TryGetConnectionData(segment, out NodeConnectionData connection)
            ? connection.portalCenter
            : transform.position;
    }

    private void AddTriangle(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles,
        Vector3 worldA,
        Vector3 worldB)
    {
        int startIndex = vertices.Count;
        Vector3 localA = transform.InverseTransformPoint(worldA);
        Vector3 localB = transform.InverseTransformPoint(worldB);

        vertices.Add(localA);
        vertices.Add(localB);
        normals.Add(Vector3.up);
        normals.Add(Vector3.up);
        uvs.Add(new Vector2(localA.x, localA.z));
        uvs.Add(new Vector2(localB.x, localB.z));
        triangles.Add(0);
        triangles.Add(startIndex + 1);
        triangles.Add(startIndex);
    }

    private float GetNodeRadius()
    {
        return owner != null ? owner.DefaultNodeRadius : 4.0f;
    }

    private bool TryBuildNodePolygon(List<NodeConnectionData> connections, out List<Vector3> polygonVertices)
    {
        polygonVertices = new List<Vector3>();

        if (connections.Count == 1)
        {
            polygonVertices.Add(connections[0].rightPoint);
            polygonVertices.Add(connections[0].leftPoint);
            return true;
        }

        for (int i = 0; i < connections.Count; i++)
        {
            NodeConnectionData current = connections[i];
            NodeConnectionData next = connections[(i + 1) % connections.Count];
            float gapRadians = GetPositiveDeltaAngle(current.angle, next.angle) * Mathf.Deg2Rad;
            bool isOuterCornerForTwoWay = connections.Count == 2 && gapRadians > Mathf.PI;

            polygonVertices.Add(current.rightPoint);
            polygonVertices.Add(current.leftPoint);

            AddRoundedConnectorPoints(polygonVertices, current.leftPoint, next.rightPoint, gapRadians, isOuterCornerForTwoWay);
        }

        RemoveNearDuplicatePoints(polygonVertices);

        return polygonVertices.Count >= 2;
    }

    private static bool TryIntersectLinesXZ(
        Vector3 pointA,
        Vector3 directionA,
        Vector3 pointB,
        Vector3 directionB,
        out Vector3 intersection)
    {
        intersection = Vector3.zero;

        Vector2 a = new Vector2(pointA.x, pointA.z);
        Vector2 b = new Vector2(pointB.x, pointB.z);
        Vector2 da = new Vector2(directionA.x, directionA.z);
        Vector2 db = new Vector2(directionB.x, directionB.z);
        float cross = da.x * db.y - da.y * db.x;
        if (Mathf.Abs(cross) < 0.0001f)
            return false;

        Vector2 delta = b - a;
        float t = (delta.x * db.y - delta.y * db.x) / cross;
        Vector2 result = a + da * t;
        intersection = new Vector3(result.x, pointA.y, result.y);
        return true;
    }

    private void AddFanFromPolygon(
        List<Vector3> polygonVertices,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles)
    {
        if (polygonVertices.Count < 2)
            return;

        int baseIndex = vertices.Count;
        List<Vector3> localPolygonVertices = new List<Vector3>(polygonVertices.Count);
        for (int i = 0; i < polygonVertices.Count; i++)
        {
            Vector3 localPoint = transform.InverseTransformPoint(polygonVertices[i]);
            localPolygonVertices.Add(localPoint);
            vertices.Add(localPoint);
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(localPoint.x, localPoint.z));
        }

        for (int i = 0; i < polygonVertices.Count; i++)
        {
            int current = baseIndex + i;
            int next = baseIndex + ((i + 1) % polygonVertices.Count);
            AddOrientedTriangle(triangles, vertices[0], vertices[current], vertices[next], current, next);
        }
    }

    private bool TryGetConnectionData(RoadSegment segment, out NodeConnectionData connectionData)
    {
        List<NodeConnectionData> connections = GetSortedConnections();
        for (int i = 0; i < connections.Count; i++)
        {
            if (connections[i].segment == segment)
            {
                connectionData = connections[i];
                return true;
            }
        }

        connectionData = default;
        return false;
    }

    private void PopulateConnectionGeometry(List<NodeConnectionData> connections)
    {
        if (connections.Count == 0)
            return;

        if (connections.Count == 1)
        {
            NodeConnectionData single = connections[0];
            Vector3 lateral = Vector3.Cross(Vector3.up, single.direction).normalized;
            single.lateral = lateral;
            single.portalCenter = transform.position + single.direction * GetNodeRadius();
            single.leftPoint = single.portalCenter - lateral * (single.segment.Width * 0.5f);
            single.rightPoint = single.portalCenter + lateral * (single.segment.Width * 0.5f);
            connections[0] = single;
            return;
        }

        for (int i = 0; i < connections.Count; i++)
        {
            int prevIndex = (i - 1 + connections.Count) % connections.Count;
            int nextIndex = (i + 1) % connections.Count;

            NodeConnectionData current = connections[i];
            current.lateral = Vector3.Cross(Vector3.up, current.direction).normalized;

            float prevGap = GetPositiveDeltaAngle(connections[prevIndex].angle, current.angle) * Mathf.Deg2Rad;
            float nextGap = GetPositiveDeltaAngle(current.angle, connections[nextIndex].angle) * Mathf.Deg2Rad;
            float halfWidth = current.segment.Width * 0.5f;
            float prevRadius = GetRequiredRadius(halfWidth, prevGap);
            float nextRadius = GetRequiredRadius(halfWidth, nextGap);
            float radius = Mathf.Max(GetNodeRadius(), prevRadius, nextRadius);

            current.portalCenter = transform.position + current.direction * Mathf.Max(radius, 0.01f);
            current.leftPoint = current.portalCenter - current.lateral * (current.segment.Width * 0.5f);
            current.rightPoint = current.portalCenter + current.lateral * (current.segment.Width * 0.5f);

            connections[i] = current;
        }
    }

    private static void AddOrientedTriangle(
        List<int> triangles,
        Vector3 center,
        Vector3 current,
        Vector3 next,
        int currentIndex,
        int nextIndex)
    {
        Vector3 normal = Vector3.Cross(current - center, next - center);
        triangles.Add(0);
        if (normal.y >= 0.0f)
        {
            triangles.Add(currentIndex);
            triangles.Add(nextIndex);
        }
        else
        {
            triangles.Add(nextIndex);
            triangles.Add(currentIndex);
        }
    }

    private static float GetPositiveDeltaAngle(float fromRadians, float toRadians)
    {
        float fromDegrees = fromRadians * Mathf.Rad2Deg;
        float toDegrees = toRadians * Mathf.Rad2Deg;
        return Mathf.Repeat(toDegrees - fromDegrees, 360.0f);
    }

    private static float GetRequiredRadius(float halfWidth, float gapRadians)
    {
        float tangent = Mathf.Tan(gapRadians * 0.5f);
        if (Mathf.Abs(tangent) < 0.0001f)
            return 0.0f;

        return halfWidth / tangent;
    }

    private void AddRoundedConnectorPoints(List<Vector3> polygonVertices, Vector3 fromPoint, Vector3 toPoint, float gapRadians, bool useConvexOuterRounding)
    {
        int subdivisions = owner != null ? owner.NodeCornerSubdivisions : 0;
        if (subdivisions <= 0)
            return;

        Vector3 center = transform.position;
        float gap01 = Mathf.Clamp01(gapRadians / Mathf.PI);
        float insetStrength = owner != null ? owner.NodeCornerInsetStrength : 0.6f;
        float insetT = Mathf.Clamp01(gap01 * insetStrength);
        Vector3 edgeMidpoint = (fromPoint + toPoint) * 0.5f;
        Vector3 controlPoint;
        if (useConvexOuterRounding)
        {
            Vector3 outward = edgeMidpoint - center;
            if (outward.sqrMagnitude < 0.0001f)
                outward = Vector3.Cross(Vector3.up, toPoint - fromPoint);

            controlPoint = edgeMidpoint + outward.normalized * outward.magnitude * insetT;
        }
        else
        {
            controlPoint = Vector3.Lerp(edgeMidpoint, center, insetT);
        }

        for (int step = 1; step <= subdivisions; step++)
        {
            float t = step / (subdivisions + 1.0f);
            polygonVertices.Add(EvaluateQuadraticBezier(fromPoint, controlPoint, toPoint, t));
        }
    }

    private static Vector3 EvaluateQuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float oneMinusT = 1.0f - t;
        return oneMinusT * oneMinusT * a
            + 2.0f * oneMinusT * t * b
            + t * t * c;
    }

    private static void RemoveNearDuplicatePoints(List<Vector3> points)
    {
        for (int i = points.Count - 1; i >= 0; i--)
        {
            Vector3 current = points[i];
            Vector3 next = points[(i + 1) % points.Count];
            if ((current - next).sqrMagnitude < 0.0001f)
                points.RemoveAt(i);
        }
    }

    private void EnsureMeshComponents()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();

        if (mesh == null)
        {
            Mesh existingMesh = meshFilter.sharedMesh;
            if (existingMesh != null && existingMesh.name == "RoadNodeMesh")
            {
                mesh = existingMesh;
            }
            else
            {
                mesh = new Mesh
                {
                    name = "RoadNodeMesh"
                };
                meshFilter.sharedMesh = mesh;
            }
        }

        if (meshCollider != null)
            meshCollider.convex = false;
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
            "RoadNodeColliderMesh");
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = colliderMesh;
    }

    private struct NodeConnectionData
    {
        public RoadSegment segment;
        public float angle;
        public Vector3 direction;
        public Vector3 portalCenter;
        public Vector3 lateral;
        public Vector3 leftPoint;
        public Vector3 rightPoint;
    }
}

public static class RoadCollisionMeshBuilder
{
    public static Mesh BuildExtrudedSurface(IReadOnlyList<Vector3> localTopVertices, IReadOnlyList<int> topTriangles, float thickness, string meshName)
    {
        if (localTopVertices == null || localTopVertices.Count < 3 || topTriangles == null || topTriangles.Count < 3)
            return null;

        float prismDepth = Mathf.Max(0.05f, thickness);
        int vertexCount = localTopVertices.Count;
        Vector3[] vertices = new Vector3[vertexCount * 2];
        List<int> triangles = new List<int>(topTriangles.Count * 2 + vertexCount * 6);

        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i] = localTopVertices[i];
            vertices[i + vertexCount] = localTopVertices[i] + Vector3.down * prismDepth;
        }

        for (int i = 0; i < topTriangles.Count; i += 3)
        {
            int a = topTriangles[i];
            int b = topTriangles[i + 1];
            int c = topTriangles[i + 2];

            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);

            triangles.Add(vertexCount + a);
            triangles.Add(vertexCount + c);
            triangles.Add(vertexCount + b);
        }

        Dictionary<Edge, int> edgeUse = new Dictionary<Edge, int>();
        for (int i = 0; i < topTriangles.Count; i += 3)
        {
            RegisterEdge(edgeUse, topTriangles[i], topTriangles[i + 1]);
            RegisterEdge(edgeUse, topTriangles[i + 1], topTriangles[i + 2]);
            RegisterEdge(edgeUse, topTriangles[i + 2], topTriangles[i]);
        }

        foreach (KeyValuePair<Edge, int> pair in edgeUse)
        {
            if (pair.Value != 1)
                continue;

            int a = pair.Key.from;
            int b = pair.Key.to;
            int bottomA = vertexCount + a;
            int bottomB = vertexCount + b;

            triangles.Add(a);
            triangles.Add(bottomA);
            triangles.Add(b);

            triangles.Add(b);
            triangles.Add(bottomA);
            triangles.Add(bottomB);
        }

        Mesh mesh = new Mesh
        {
            name = meshName
        };
        mesh.vertices = vertices;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void RegisterEdge(Dictionary<Edge, int> edgeUse, int from, int to)
    {
        Edge direct = new Edge(from, to);
        Edge reverse = new Edge(to, from);
        if (edgeUse.ContainsKey(reverse))
        {
            edgeUse[reverse]++;
            return;
        }

        edgeUse[direct] = 1;
    }

    private readonly struct Edge
    {
        public readonly int from;
        public readonly int to;

        public Edge(int from, int to)
        {
            this.from = from;
            this.to = to;
        }
    }
}
