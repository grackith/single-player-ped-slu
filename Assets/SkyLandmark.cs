using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SkyLandmark : MonoBehaviour
{
    [Header("Landmark Settings")]
    [Tooltip("The height of the beam pointing to the sky")]
    public float beamHeight = 50f;

    [Tooltip("Base width of the landmark")]
    public float baseWidth = 2f;

    [Tooltip("Color of the landmark beam")]
    public Color beamColor = new Color(0.2f, 0.8f, 1f, 0.8f);

    [Tooltip("Pulse speed (set to 0 for no pulsing)")]
    public float pulseSpeed = 1.5f;

    [Tooltip("Pulse intensity")]
    [Range(0f, 1f)]
    public float pulseIntensity = 0.2f;

    [Tooltip("Rotation speed (set to 0 for no rotation)")]
    public float rotationSpeed = 15f;

    [Header("Materials")]
    [Tooltip("Assign a transparent material for the beam")]
    public Material beamMaterialTemplate;

    [Tooltip("Assign a material for the base")]
    public Material baseMaterialTemplate;

    // References to created objects
    private GameObject beamObject;
    private Material beamMaterial;
    private Material baseMaterial;

    void Start()
    {
        CreateLandmark();
    }

    void Update()
    {
        if (pulseSpeed > 0 && beamMaterial != null)
        {
            // Create a pulsing effect
            float pulse = (Mathf.Sin(Time.time * pulseSpeed) * pulseIntensity) + (1f - pulseIntensity);

            Color pulsedColor = beamColor;
            pulsedColor.a = beamColor.a * pulse;
            beamMaterial.SetColor("_Color", pulsedColor);

            if (beamMaterial.HasProperty("_EmissionColor"))
            {
                beamMaterial.SetColor("_EmissionColor", pulsedColor * 2f);
            }

            if (baseMaterial != null)
            {
                if (baseMaterial.HasProperty("_EmissionColor"))
                {
                    baseMaterial.SetColor("_EmissionColor", pulsedColor * 2f);
                }
            }
        }

        if (rotationSpeed > 0 && beamObject != null)
        {
            // Rotate the beam around the Y-axis
            beamObject.transform.Rotate(0, rotationSpeed * Time.deltaTime, 0);
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
}