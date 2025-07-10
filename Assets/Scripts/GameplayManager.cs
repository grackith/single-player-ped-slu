using UnityEngine;
using UnityEngine.Events;

public class GameplayManager : MonoBehaviour
{
    // Reference to ScenarioManager
    public ScenarioManager scenarioManager;

    // Event that will be triggered when gameplay ends
    public UnityEvent onGameplayEnded = new UnityEvent();

    // Flag to track if gameplay is active
    private bool isGameplayActive = false;

    // Method to start gameplay with a specific scenario
    public void StartGameplay(string scenarioName)
    {
        if (scenarioManager != null)
        {
            scenarioManager.LaunchScenario(scenarioName);
            isGameplayActive = true;
            Debug.Log("Gameplay started with scenario: " + scenarioName);
        }
    }

    // Method to be called by participant's "end gameplay" button
    public void EndGameplay()
    {
        if (isGameplayActive)
        {
            // End the current scenario
            if (scenarioManager != null)
            {
                scenarioManager.EndCurrentScenario();
            }

            isGameplayActive = false;

            // Trigger the event
            onGameplayEnded.Invoke();

            Debug.Log("Gameplay ended by participant");
        }
    }
}