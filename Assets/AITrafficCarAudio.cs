using TurnTheGameOn.SimpleTrafficSystem;
using UnityEngine;

public class AITrafficCarAudio : MonoBehaviour
{
    private AudioSource engineAudio;
    private AITrafficCar aiTrafficCar;

    public float minPitch = 0.5f;
    public float maxPitch = 1.5f;
    public float maxSpeed = 100f; // Set this to match your car's max speed

    // Optional: Add sound variations
    public AudioClip idleSound;
    public AudioClip accelerationSound;

    private bool isDriving = false;
    private AudioClip lastClip = null;

    void Start()
    {
        engineAudio = GetComponent<AudioSource>();
        aiTrafficCar = GetComponent<AITrafficCar>();

        // Make sure we have an AudioSource
        if (engineAudio == null)
        {
            engineAudio = gameObject.AddComponent<AudioSource>();
            engineAudio.spatialBlend = 1.0f; // Full 3D sound
            engineAudio.minDistance = 5f;
            engineAudio.maxDistance = 50f;
            engineAudio.loop = true;
            engineAudio.playOnAwake = false;
        }

        // Set default clip if available and none is set
        if (engineAudio.clip == null && idleSound != null)
        {
            engineAudio.clip = idleSound;
            lastClip = idleSound;
        }
    }

    void Update()
    {
        if (engineAudio == null || aiTrafficCar == null) return;

        // Get the current speed from the AITrafficCar
        float currentSpeed = aiTrafficCar.CurrentSpeed();

        // Check if the car is currently driving (by checking if speed > 0 or acceleration input > 0)
        bool currentlyDriving = currentSpeed > 0.1f || aiTrafficCar.AccelerationInput() > 0.01f;

        // Handle the engine audio state
        if (currentlyDriving)
        {
            // Start audio if it wasn't playing
            if (!isDriving)
            {
                // Make sure we have a valid clip before playing
                if (engineAudio.clip != null)
                {
                    engineAudio.Play();
                    isDriving = true;
                }
                else if (idleSound != null)
                {
                    engineAudio.clip = idleSound;
                    lastClip = idleSound;
                    engineAudio.Play();
                    isDriving = true;
                }
            }

            // Calculate pitch based on speed
            float normalizedSpeed = Mathf.Clamp01(currentSpeed / maxSpeed);
            float currentPitch = Mathf.Lerp(minPitch, maxPitch, normalizedSpeed);

            // Apply pitch to engine sound
            engineAudio.pitch = currentPitch;

            // Optional: Change sound clip based on acceleration
            if (accelerationSound != null && aiTrafficCar.AccelerationInput() > 0.7f && engineAudio.clip != accelerationSound)
            {
                // Only attempt to save playback position if we have a valid current clip
                float playbackPosition = 0f;
                if (engineAudio.clip != null && engineAudio.clip.length > 0)
                {
                    playbackPosition = Mathf.Clamp01(engineAudio.time / engineAudio.clip.length);
                }

                bool wasPlaying = engineAudio.isPlaying;
                engineAudio.clip = accelerationSound;
                lastClip = accelerationSound;

                // Make sure we don't exceed the clip length
                if (accelerationSound.length > 0)
                {
                    engineAudio.time = Mathf.Clamp(playbackPosition * accelerationSound.length, 0f, accelerationSound.length - 0.01f);
                }
                else
                {
                    engineAudio.time = 0f;
                }

                if (wasPlaying || !engineAudio.isPlaying) engineAudio.Play();
            }
            else if (idleSound != null && aiTrafficCar.AccelerationInput() <= 0.7f && engineAudio.clip != idleSound)
            {
                // Only attempt to save playback position if we have a valid current clip
                float playbackPosition = 0f;
                if (engineAudio.clip != null && engineAudio.clip.length > 0)
                {
                    playbackPosition = Mathf.Clamp01(engineAudio.time / engineAudio.clip.length);
                }

                bool wasPlaying = engineAudio.isPlaying;
                engineAudio.clip = idleSound;
                lastClip = idleSound;

                // Make sure we don't exceed the clip length
                if (idleSound.length > 0)
                {
                    engineAudio.time = Mathf.Clamp(playbackPosition * idleSound.length, 0f, idleSound.length - 0.01f);
                }
                else
                {
                    engineAudio.time = 0f;
                }

                if (wasPlaying || !engineAudio.isPlaying) engineAudio.Play();
            }

            // Adjust volume based on speed
            engineAudio.volume = Mathf.Lerp(0.5f, 1.0f, normalizedSpeed);
        }
        else if (isDriving)
        {
            // Gradually reduce sound when stopping
            engineAudio.pitch = Mathf.Lerp(engineAudio.pitch, minPitch, Time.deltaTime * 5f);
            engineAudio.volume = Mathf.Lerp(engineAudio.volume, 0.5f, Time.deltaTime * 3f);

            // If car has completely stopped, possibly stop the audio
            if (currentSpeed < 0.01f && aiTrafficCar.AccelerationInput() < 0.01f)
            {
                isDriving = false;
                // Optional: keep idle sound playing at low volume instead of stopping
                // engineAudio.Stop();
            }
        }

        // Handle braking sound effect if needed
        if (aiTrafficCar.IsBraking())
        {
            // Could add brake sound effect here
        }
    }

    // You might want to sync with the AITrafficCar's lifecycle
    void OnEnable()
    {
        // Start audio when car becomes active in the scene
        if (engineAudio != null && !engineAudio.isPlaying && idleSound != null)
        {
            engineAudio.clip = idleSound;
            lastClip = idleSound;
            engineAudio.pitch = minPitch;
            engineAudio.volume = 0.5f;
            engineAudio.Play();
        }
    }

    void OnDisable()
    {
        // Stop audio when car is disabled/pooled
        if (engineAudio != null && engineAudio.isPlaying)
        {
            engineAudio.Stop();
        }
        isDriving = false;
    }
}