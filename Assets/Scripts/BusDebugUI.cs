using UnityEngine;

public class BusDebugUI : MonoBehaviour
{
    public BusSpawnerSimple busSpawner;

    void OnGUI()
    {
        // Draw a small window with debug buttons
        GUILayout.BeginArea(new Rect(10, 10, 200, 200));
        GUILayout.BeginVertical("box");

        GUILayout.Label("Bus Debug Controls");

        if (GUILayout.Button("Check Bus Status"))
        {
            if (busSpawner != null)
                busSpawner.CheckBusStatus();
            else
                Debug.LogError("BusSpawner not assigned!");
        }

        if (GUILayout.Button("Fix Bus Movement"))
        {
            if (busSpawner != null)
                busSpawner.CheckAndFixBusMovement();
            else
                Debug.LogError("BusSpawner not assigned!");
        }

        if (GUILayout.Button("Spawn New Bus"))
        {
            if (busSpawner != null)
                busSpawner.SpawnBus();
            else
                Debug.LogError("BusSpawner not assigned!");
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}