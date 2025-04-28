using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class SkyLandmark : MonoBehaviour
{
    [Header("Landmark Settings")]
    public float beamHeight = 50f;
    public float baseWidth = 2f;
    public Color beamColor = new Color(0.2f, 0.8f, 1f, 0.8f);

    [Header("Timer Settings")]
    public bool showBusTimer = true;
    public Color timerTextColor = Color.yellow;
    public float timerTextSize = 4f;
    public float timerHeight = 10f;

    [Header("Bus Timer")]
    [Tooltip("Directly set the bus spawn delay for this scenario")]
    public float busSpawnDelay = 30f;

    [Header("Materials")]
    public Material beamMaterialTemplate;
    public Material baseMaterialTemplate;

    private GameObject beamObject;
    private Material beamMaterial;
    private Material baseMaterial;
    private TextMeshProUGUI timerText;
    private GameObject timerObject;

    private float busTimer = 0f;
    private bool busTimerStarted = false;
    private bool busHasSpawned = false;

    void Start()
    {
        CreateLandmark();

        // Initialize timer with directly assigned value
        busTimer = busSpawnDelay;
        busTimerStarted = true;

        // Initially set the timer text
        UpdateTimerText();
    }

    void Update()
    {
        // Update bus timer if active
        if (busTimerStarted && !busHasSpawned)
        {
            busTimer -= Time.deltaTime;

            if (busTimer <= 0)
            {
                busTimer = 0;
                busHasSpawned = true;
            }

            UpdateTimerText();
        }
    }

    void CreateLandmark()
    {
        // Create parent object
        beamObject = new GameObject("SkyLandmark");
        beamObject.transform.position = transform.position;
        beamObject.transform.SetParent(transform);

        // Create beam mesh
        GameObject beam = CreateBeamMesh();
        beam.transform.SetParent(beamObject.transform, false);

        // Create base indicator (optional)
        GameObject baseIndicator = CreateBaseIndicator();
        baseIndicator.transform.SetParent(beamObject.transform, false);

        // Create timer display
        if (showBusTimer)
        {
            CreateTimerDisplay();
        }
    }

    GameObject CreateBeamMesh()
    {
        // Create beam GameObject
        GameObject beam = new GameObject("Beam");

        // Add mesh components
        MeshFilter meshFilter = beam.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = beam.AddComponent<MeshRenderer>();

        // Create mesh for beam
        Mesh mesh = new Mesh();

        // Define beam vertices (square pillar)
        float halfWidth = baseWidth / 2f;
        Vector3[] vertices = new Vector3[]
        {
            // Bottom square
            new Vector3(-halfWidth, 0, -halfWidth),
            new Vector3(halfWidth, 0, -halfWidth),
            new Vector3(halfWidth, 0, halfWidth),
            new Vector3(-halfWidth, 0, halfWidth),
            
            // Top square (narrower for better visual effect)
            new Vector3(-halfWidth * 0.5f, beamHeight, -halfWidth * 0.5f),
            new Vector3(halfWidth * 0.5f, beamHeight, -halfWidth * 0.5f),
            new Vector3(halfWidth * 0.5f, beamHeight, halfWidth * 0.5f),
            new Vector3(-halfWidth * 0.5f, beamHeight, halfWidth * 0.5f)
        };

        // Define triangles (sides of the beam)
        int[] triangles = new int[]
        {
            // Bottom face
            0, 1, 2,
            0, 2, 3,
            
            // Top face
            4, 6, 5,
            4, 7, 6,
            
            // Side faces
            0, 4, 1,
            1, 4, 5,

            1, 5, 2,
            2, 5, 6,

            2, 6, 3,
            3, 6, 7,

            3, 7, 0,
            0, 7, 4
        };

        // Assign vertices and triangles to mesh
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        // Assign mesh to the mesh filter
        meshFilter.mesh = mesh;

        // Check if material template is assigned, otherwise use a basic material
        if (beamMaterialTemplate != null)
        {
            // Create instance of the template material
            beamMaterial = new Material(beamMaterialTemplate);
        }
        else
        {
            // Create a fallback material
            beamMaterial = new Material(Shader.Find("Transparent/Diffuse"));

            // If that also fails, create a very basic material
            if (beamMaterial.shader.name.Contains("Hidden"))
            {
                beamMaterial = new Material(Shader.Find("Mobile/Diffuse"));
                Debug.LogWarning("SkyLandmark: Please assign a beam material in the inspector as fallback materials failed to load");
            }
        }

        // Set color
        beamMaterial.SetColor("_Color", beamColor);

        // Try to set emission if the shader supports it
        if (beamMaterial.HasProperty("_EmissionColor"))
        {
            beamMaterial.EnableKeyword("_EMISSION");
            beamMaterial.SetColor("_EmissionColor", beamColor * 2f);
        }

        // Assign material to the mesh renderer
        meshRenderer.material = beamMaterial;

        return beam;
    }

    GameObject CreateBaseIndicator()
    {
        // Create a cylinder at the base
        GameObject baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseObject.name = "BaseIndicator";

        // Scale and position
        baseObject.transform.localScale = new Vector3(baseWidth, 0.2f, baseWidth);
        baseObject.transform.localPosition = new Vector3(0, 0.1f, 0);

        // Check if material template is assigned, otherwise use a basic material
        if (baseMaterialTemplate != null)
        {
            // Create instance of the template material
            baseMaterial = new Material(baseMaterialTemplate);
        }
        else
        {
            // Create a fallback material
            baseMaterial = new Material(Shader.Find("Standard"));

            // If that also fails, create a very basic material
            if (baseMaterial.shader.name.Contains("Hidden"))
            {
                baseMaterial = new Material(Shader.Find("Mobile/Diffuse"));
                Debug.LogWarning("SkyLandmark: Please assign a base material in the inspector as fallback materials failed to load");
            }
        }

        // Set color
        baseMaterial.SetColor("_Color", beamColor);

        // Try to set emission if the shader supports it
        if (baseMaterial.HasProperty("_EmissionColor"))
        {
            baseMaterial.EnableKeyword("_EMISSION");
            baseMaterial.SetColor("_EmissionColor", beamColor * 2f);
        }

        // Apply material
        baseObject.GetComponent<MeshRenderer>().material = baseMaterial;

        return baseObject;
    }
    // Add to SkyLandmark.cs
    public void UpdateBusTimer(float newDelay)
    {
        busSpawnDelay = newDelay;
        busTimer = newDelay;
        busTimerStarted = true;
        busHasSpawned = false;
        UpdateTimerText();
    }

    void CreateTimerDisplay()
    {
        // Create a parent object for the timer
        timerObject = new GameObject("BusTimer");
        timerObject.transform.SetParent(beamObject.transform, false);
        timerObject.transform.localPosition = new Vector3(0, timerHeight, 0);

        // Create a single TextMeshPro text at the top
        GameObject textObj = new GameObject("TimerText");
        textObj.transform.SetParent(timerObject.transform, false);
        textObj.transform.localPosition = Vector3.zero;

        // Create a world-space canvas for the timer
        Canvas canvas = textObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        // Add a rectangular background
        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(200, 100); // Width and height of the canvas

        // Add a text component
        GameObject textChild = new GameObject("Text");
        textChild.transform.SetParent(canvasRect, false);

        // Position the text in the center of the canvas
        RectTransform textRect = textChild.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        // Add TextMeshPro component
        TextMeshProUGUI tmp = textChild.AddComponent<TextMeshProUGUI>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = timerTextSize;
        tmp.color = timerTextColor;
        tmp.fontStyle = FontStyles.Bold;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;

        // Add an outline for better visibility
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = Color.black;

        // Save a reference to our text component
        timerText = tmp; // This now matches the variable type

        // Make the canvas face forward
        
        // Face in different directions by changing the Y rotation value
        //textObj.transform.rotation = Quaternion.Euler(0, 0, 0);   // Face forward (Z axis)
        //textObj.transform.rotation = Quaternion.Euler(0, 90, 0);  // Face right (X axis)
        //textObj.transform.rotation = Quaternion.Euler(0, 180, 0); // Face backward (negative Z axis)
        textObj.transform.rotation = Quaternion.Euler(0, 270, 0); // Face left (negative X axis)

        // Initial text update
        UpdateTimerText();
    }

    void UpdateTimerText()
    {
        if (timerText != null)
        {
            string timeText;

            if (busHasSpawned)
            {
                timeText = "BUS IS ARRIVING";
            }
            else
            {
                // Format the time as minutes:seconds
                int minutes = Mathf.FloorToInt(busTimer / 60f);
                int seconds = Mathf.FloorToInt(busTimer % 60f);
                timeText = $"BUS TIMER\n{minutes:00}:{seconds:00}";
            }

            timerText.text = timeText;
        }
    }
}