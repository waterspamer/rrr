using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class Road : MonoBehaviour
{
    [SerializeField] private Transform nodesRoot;
    [SerializeField] private Transform segmentsRoot;
    [SerializeField] private List<RoadNode> nodes = new List<RoadNode>();
    [SerializeField] private List<RoadSegment> segments = new List<RoadSegment>();
    [SerializeField] private Material roadMaterial;
    [SerializeField] private float defaultSegmentWidth = 6.0f;
    [SerializeField] private float defaultNodeRadius = 4.0f;
    [Min(0.05f)]
    [SerializeField] private float collisionThickness = 0.35f;
    [Min(0)]
    [SerializeField] private int nodeCornerSubdivisions = 0;
    [Range(0.0f, 1.0f)]
    [SerializeField] private float nodeCornerInsetStrength = 0.6f;
    [SerializeField] private Vector3 defaultStartNodePosition = new Vector3(-5.0f, 0.0f, 0.0f);
    [SerializeField] private Vector3 defaultEndNodePosition = new Vector3(5.0f, 0.0f, 0.0f);

    private int lastTopologyHash;

    public Transform NodesRoot => nodesRoot;
    public Transform SegmentsRoot => segmentsRoot;
    public IReadOnlyList<RoadNode> Nodes => nodes;
    public IReadOnlyList<RoadSegment> Segments => segments;
    public Material RoadMaterial => roadMaterial;
    public float DefaultNodeRadius => defaultNodeRadius;
    public float CollisionThickness => collisionThickness;
    public int NodeCornerSubdivisions => nodeCornerSubdivisions;
    public float NodeCornerInsetStrength => nodeCornerInsetStrength;

    private void Reset()
    {
        EnsureStructure();
        EnsureDefaultLayout();
        Refresh();
    }

    private void OnValidate()
    {
        EnsureStructure();
        defaultSegmentWidth = Mathf.Max(1.0f, defaultSegmentWidth);
        defaultNodeRadius = Mathf.Max(0.1f, defaultNodeRadius);
        collisionThickness = Mathf.Max(0.05f, collisionThickness);
        nodeCornerSubdivisions = Mathf.Max(0, nodeCornerSubdivisions);
        nodeCornerInsetStrength = Mathf.Clamp01(nodeCornerInsetStrength);
        EnsureDefaultLayout();
        Refresh();
    }

    private void Update()
    {
        if (Application.isPlaying)
            return;

        int topologyHash = CalculateTopologyHash();
        if (topologyHash == lastTopologyHash)
            return;

        Refresh();
    }

    public void EnsureStructure()
    {
        nodesRoot = EnsureChildRoot(nodesRoot, "Nodes");
        segmentsRoot = EnsureChildRoot(segmentsRoot, "Segments");
    }

    public void Refresh()
    {
        EnsureStructure();

        nodes.Clear();
        segments.Clear();

        RoadNode[] roadNodes = nodesRoot != null
            ? nodesRoot.GetComponentsInChildren<RoadNode>(true)
            : GetComponentsInChildren<RoadNode>(true);
        for (int i = 0; i < roadNodes.Length; i++)
        {
            RoadNode node = roadNodes[i];
            if (node == null)
                continue;

            node.SetOwner(this);
            nodes.Add(node);
        }

        RoadSegment[] roadSegments = segmentsRoot != null
            ? segmentsRoot.GetComponentsInChildren<RoadSegment>(true)
            : GetComponentsInChildren<RoadSegment>(true);
        for (int i = 0; i < roadSegments.Length; i++)
        {
            RoadSegment segment = roadSegments[i];
            if (segment == null)
                continue;

            segment.SetOwner(this);
            segments.Add(segment);
        }

        SyncNodeConnections();
        RebuildGeometry();
        lastTopologyHash = CalculateTopologyHash();
    }

    public RoadNode CreateNode(Vector3 worldPosition, string nodeName = null)
    {
        EnsureStructure();

        GameObject nodeObject = new GameObject(string.IsNullOrWhiteSpace(nodeName) ? $"Node {nodes.Count}" : nodeName);
        nodeObject.transform.SetParent(nodesRoot, true);
        nodeObject.transform.position = worldPosition;

        RoadNode node = nodeObject.AddComponent<RoadNode>();
        node.SetOwner(this);
        nodes.Add(node);
        return node;
    }

    public RoadSegment CreateSegment(RoadNode startNode, RoadNode endNode, string segmentName = null)
    {
        if (startNode == null || endNode == null)
            return null;

        EnsureStructure();

        GameObject segmentObject = new GameObject(string.IsNullOrWhiteSpace(segmentName) ? $"Segment {segments.Count}" : segmentName);
        segmentObject.transform.SetParent(segmentsRoot, false);

        RoadSegment segment = segmentObject.AddComponent<RoadSegment>();
        segment.SetOwner(this);
        segment.SetWidth(defaultSegmentWidth);
        segment.SetNodes(startNode, endNode);
        segments.Add(segment);
        return segment;
    }

    public void EnsureDefaultLayout()
    {
        EnsureStructure();

        if (nodesRoot == null || segmentsRoot == null)
            return;

        if (nodesRoot.GetComponentsInChildren<RoadNode>(true).Length > 0)
            return;

        RoadNode startNode = CreateNode(transform.TransformPoint(defaultStartNodePosition), "Node 0");
        RoadNode endNode = CreateNode(transform.TransformPoint(defaultEndNodePosition), "Node 1");
        CreateSegment(startNode, endNode, "Segment 0");
    }

    private Transform EnsureChildRoot(Transform existingRoot, string rootName)
    {
        if (existingRoot != null && existingRoot.parent == transform)
            return existingRoot;

        Transform child = transform.Find(rootName);
        if (child != null)
            return child;

        GameObject rootObject = new GameObject(rootName);
        Transform rootTransform = rootObject.transform;
        rootTransform.SetParent(transform, false);
        return rootTransform;
    }

    private void SyncNodeConnections()
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] != null)
                nodes[i].ClearConnections();
        }

        for (int i = 0; i < segments.Count; i++)
        {
            RoadSegment segment = segments[i];
            if (segment == null)
                continue;

            segment.RegisterWithNodes();
        }
    }

    private void RebuildGeometry()
    {
        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i] != null)
                segments[i].RebuildGeometry();
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] != null)
                nodes[i].RebuildGeometry();
        }
    }

    private int CalculateTopologyHash()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + transform.childCount;

            for (int i = 0; i < nodes.Count; i++)
            {
                RoadNode node = nodes[i];
                if (node == null)
                    continue;

                Vector3 position = node.transform.position;
                hash = hash * 23 + position.GetHashCode();
                hash = hash * 23 + node.transform.childCount;
            }

            for (int i = 0; i < segments.Count; i++)
            {
                RoadSegment segment = segments[i];
                if (segment == null)
                    continue;

                hash = hash * 23 + segment.Width.GetHashCode();
                hash = hash * 23 + segment.CurveSubdivisions;
                hash = hash * 23 + segment.GetStartControlPointWorld().GetHashCode();
                hash = hash * 23 + segment.GetEndControlPointWorld().GetHashCode();
                hash = hash * 23 + (segment.StartNode != null ? segment.StartNode.GetInstanceID() : 0);
                hash = hash * 23 + (segment.EndNode != null ? segment.EndNode.GetInstanceID() : 0);
            }

            return hash;
        }
    }
}
