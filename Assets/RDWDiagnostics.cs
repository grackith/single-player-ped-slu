using UnityEngine;
using System.Collections;

public class RDWDiagnostics : MonoBehaviour
{
    [Header("References")]
    public RedirectionManager redirectionManager;
    public GlobalConfiguration globalConfig;

    [Header("Debug Settings")]
    public bool enableLogging = true;
    public float loggingInterval = 0.5f; // How often to log (seconds)
    public bool showBufferZones = true;
    public float debugLinesDuration = 0.1f; // How long debug lines remain visible

    private float logTimer;
    private Vector3 lastHeadPosition;
    private Vector3 lastRealPosition;

    void Start()
    {
        // Find components if not assigned
        if (redirectionManager == null)
            redirectionManager = FindObjectOfType<RedirectionManager>();

        if (globalConfig == null)
            globalConfig = FindObjectOfType<GlobalConfiguration>();

        // Start periodic logging
        StartCoroutine(PeriodicLogging());

        // Create visual buffer zones
        if (showBufferZones)
            CreateBufferZoneVisualizations();
    }

    private IEnumerator PeriodicLogging()
    {
        while (enableLogging)
        {
            LogDiagnosticInfo();
            yield return new WaitForSeconds(loggingInterval);
        }
    }

    private void LogDiagnosticInfo()
    {
        if (redirectionManager == null || redirectionManager.headTransform == null)
            return;

        // Get positions
        Vector3 headPos = redirectionManager.headTransform.position;
        Vector3 realPos = redirectionManager.GetPosReal(headPos);

        // Get distance to closest boundary
        float distanceToBoundary = CalculateDistanceToBoundary(realPos);

        // Calculate movement since last log
        float headMovement = Vector3.Distance(headPos, lastHeadPosition);
        float realMovement = Vector3.Distance(realPos, lastRealPosition);

        // Store for next frame
        lastHeadPosition = headPos;
        lastRealPosition = realPos;

        // Log the info
        Debug.Log($"=== RDW DIAGNOSTICS ===");
        Debug.Log($"Head Position: {headPos}");
        Debug.Log($"Real Position: {realPos}");
        Debug.Log($"Distance to Boundary: {distanceToBoundary}m");
        Debug.Log($"Reset Buffer: {globalConfig.RESET_TRIGGER_BUFFER}m");
        Debug.Log($"Will Reset When: Distance to Boundary < {globalConfig.RESET_TRIGGER_BUFFER}m");
        Debug.Log($"Movement since last log - Virtual: {headMovement}m, Real: {realMovement}m");

        // Check gains
        if (redirectionManager != null && redirectionManager.redirector != null)
        {
            Debug.Log($"Current Gains - Translation: {redirectionManager.gt}, Rotation: {redirectionManager.gr}, Curvature: {redirectionManager.curvature}");
        }

        // Draw debug lines
        DrawDebugLines(realPos, distanceToBoundary);
    }

    private float CalculateDistanceToBoundary(Vector3 realPosition)
    {
        if (globalConfig == null || globalConfig.physicalSpaces == null ||
            globalConfig.physicalSpaces.Count == 0 || globalConfig.physicalSpaces[0].trackingSpace == null)
            return -1f;

        var trackingSpace = globalConfig.physicalSpaces[0].trackingSpace;
        float minDistance = float.MaxValue;

        // Check distance to each edge of the tracking space
        for (int i = 0; i < trackingSpace.Count; i++)
        {
            Vector2 p1 = trackingSpace[i];
            Vector2 p2 = trackingSpace[(i + 1) % trackingSpace.Count];

            // Calculate distance from realPosition to line segment p1-p2
            Vector2 realPos2D = new Vector2(realPosition.x, realPosition.z);
            float distance = Utilities.PointLineDistance(realPos2D, p1, p2);

            minDistance = Mathf.Min(minDistance, distance);
        }

        return minDistance;
    }

    private void DrawDebugLines(Vector3 realPosition, float distanceToBoundary)
    {
        if (redirectionManager == null || redirectionManager.trackingSpace == null)
            return;

        // Convert real position to world position
        Vector3 worldPos = redirectionManager.trackingSpace.TransformPoint(
            new Vector3(realPosition.x, 0.1f, realPosition.z));

        // Draw a vertical line at the real position
        Debug.DrawLine(worldPos, worldPos + Vector3.up * 2f, Color.cyan, debugLinesDuration);

        // Draw a sphere showing the reset buffer
        // (Can't directly draw a sphere, so we'll use DrawLine to approximate)
        int segments = 16;
        float radius = globalConfig.RESET_TRIGGER_BUFFER;

        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * 2f * Mathf.PI / segments;
            float angle2 = (i + 1) * 2f * Mathf.PI / segments;

            Vector3 p1 = worldPos + new Vector3(Mathf.Cos(angle1) * radius, 0.1f, Mathf.Sin(angle1) * radius);
            Vector3 p2 = worldPos + new Vector3(Mathf.Cos(angle2) * radius, 0.1f, Mathf.Sin(angle2) * radius);

            Debug.DrawLine(p1, p2, Color.red, debugLinesDuration);
        }
    }

    private void CreateBufferZoneVisualizations()
    {
        if (globalConfig == null || globalConfig.physicalSpaces == null ||
            globalConfig.physicalSpaces.Count == 0 || globalConfig.physicalSpaces[0].trackingSpace == null)
            return;

        var trackingSpace = globalConfig.physicalSpaces[0].trackingSpace;

        // Create a parent object for the buffer zone visualizations
        GameObject bufferParent = new GameObject("DiagnosticBufferZones");

        // For each edge, create a quad representing the buffer zone
        for (int i = 0; i < trackingSpace.Count; i++)
        {
            Vector2 p1 = trackingSpace[i];
            Vector2 p2 = trackingSpace[(i + 1) % trackingSpace.Count];

            // Calculate edge direction and perpendicular
            Vector2 edgeDir = (p2 - p1).normalized;
            Vector2 perpDir = new Vector2(-edgeDir.y, edgeDir.x);

            // Create four corners of the buffer zone quad
            Vector2 corner1 = p1 + perpDir * globalConfig.RESET_TRIGGER_BUFFER;
            Vector2 corner2 = p2 + perpDir * globalConfig.RESET_TRIGGER_BUFFER;
            Vector2 corner3 = p2;
            Vector2 corner4 = p1;

            // Create a quad GameObject
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"BufferZone_{i}";
            quad.transform.parent = bufferParent.transform;

            // Create a mesh for the quad
            Mesh mesh = new Mesh();
            quad.GetComponent<MeshFilter>().mesh = mesh;

            // Set mesh vertices
            Vector3[] vertices = new Vector3[4];
            vertices[0] = new Vector3(corner1.x, 0.02f, corner1.y);
            vertices[1] = new Vector3(corner2.x, 0.02f, corner2.y);
            vertices[2] = new Vector3(corner3.x, 0.02f, corner3.y);
            vertices[3] = new Vector3(corner4.x, 0.02f, corner4.y);

            mesh.vertices = vertices;

            // Set mesh triangles
            int[] triangles = new int[6];
            triangles[0] = 0;
            triangles[1] = 1;
            triangles[2] = 2;
            triangles[3] = 0;
            triangles[4] = 2;
            triangles[5] = 3;

            mesh.triangles = triangles;

            // Set mesh UVs
            Vector2[] uvs = new Vector2[4];
            uvs[0] = new Vector2(0, 0);
            uvs[1] = new Vector2(1, 0);
            uvs[2] = new Vector2(1, 1);
            uvs[3] = new Vector2(0, 1);

            mesh.uv = uvs;

            // Calculate normals
            mesh.RecalculateNormals();

            // Set material
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (mat.shader == null)
                mat = new Material(Shader.Find("Standard"));

            mat.color = new Color(1, 0, 0, 0.3f); // Red, semi-transparent
            quad.GetComponent<Renderer>().material = mat;

            // Remove collider
            Destroy(quad.GetComponent<Collider>());

            // Transform quad to world space if we have a redirectionManager
            if (redirectionManager != null && redirectionManager.trackingSpace != null)
            {
                for (int j = 0; j < vertices.Length; j++)
                {
                    vertices[j] = redirectionManager.trackingSpace.TransformPoint(vertices[j]);
                }

                mesh.vertices = vertices;
                mesh.RecalculateBounds();
            }
        }
    }
}