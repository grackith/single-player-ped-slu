using TurnTheGameOn.SimpleTrafficSystem;
using UnityEngine;
using System.Collections;
using UnityEngine.Audio; // Add this line to access AudioMixer classes


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
            engineAudio.minDistance = 10f;
            engineAudio.maxDistance = 50f;
            engineAudio.rolloffMode = AudioRolloffMode.Linear;
            engineAudio.loop = true;
            engineAudio.playOnAwake = false;
        }

        // Connect to the Audio Mixer Group
        AudioMixer vehicleMixer = Resources.Load<AudioMixer>("VehicleAudioMixer");
        if (vehicleMixer != null)
        {
            AudioMixerGroup[] groups = vehicleMixer.FindMatchingGroups("CarEngines");
            if (groups.Length > 0)
            {
                engineAudio.outputAudioMixerGroup = groups[0];
            }
        }

        // Rest of your initialization code...
    }
    void Update()
    {
        if (engineAudio == null || aiTrafficCar == null) return;

        // Get the current speed from the AITrafficCar
        float currentSpeed = aiTrafficCar.CurrentSpeed();

        // Check if the car is currently driving
        bool currentlyDriving = currentSpeed > 0.1f || aiTrafficCar.AccelerationInput() > 0.01f;

        // Calculate normalized speed for pitch and volume
        float normalizedSpeed = Mathf.Clamp01(currentSpeed / maxSpeed);

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
            float currentPitch = Mathf.Lerp(minPitch, maxPitch, normalizedSpeed);

            // Apply pitch to engine sound
            engineAudio.pitch = currentPitch;

            // Adjust volume based on speed
            engineAudio.volume = Mathf.Lerp(0.5f, 1.0f, normalizedSpeed);

            // Optional: Change sound clip based on acceleration with crossfade
            if (accelerationSound != null && aiTrafficCar.AccelerationInput() > 0.7f && engineAudio.clip != accelerationSound)
            {
                // Use crossfade instead of abrupt switching
                StartCoroutine(CrossfadeToClip(accelerationSound, 0.2f));
            }
            else if (idleSound != null && aiTrafficCar.AccelerationInput() <= 0.7f && engineAudio.clip != idleSound)
            {
                // Use crossfade for idle sound too
                StartCoroutine(CrossfadeToClip(idleSound, 0.2f));
            }
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

    private IEnumerator CrossfadeToClip(AudioClip newClip, float fadeDuration)
    {
        if (newClip == null) yield break;

        // Create temporary audio source for crossfade
        AudioSource tempSource = gameObject.AddComponent<AudioSource>();
        tempSource.clip = newClip;
        tempSource.volume = 0;
        tempSource.pitch = engineAudio.pitch; // Match current pitch
        tempSource.spatialBlend = engineAudio.spatialBlend;
        tempSource.minDistance = engineAudio.minDistance;
        tempSource.maxDistance = engineAudio.maxDistance;

        // Copy the output audio mixer group
        if (engineAudio.outputAudioMixerGroup != null)
        {
            tempSource.outputAudioMixerGroup = engineAudio.outputAudioMixerGroup;
        }

        tempSource.Play();

        // Crossfade volume
        float startTime = Time.time;
        while (Time.time < startTime + fadeDuration)
        {
            float t = (Time.time - startTime) / fadeDuration;
            tempSource.volume = t * engineAudio.volume;
            engineAudio.volume = (1 - t) * engineAudio.volume;
            yield return null;
        }

        // Switch primary audio source to new clip
        engineAudio.Stop();
        engineAudio.clip = newClip;
        engineAudio.volume = tempSource.volume;

        // Don't copy time position, start from beginning
        // engineAudio.time = tempSource.time; // This was causing the issue

        // Make sure we have a valid clip before playing
        if (engineAudio.clip != null)
        {
            engineAudio.Play();
        }

        // Clean up temp source
        Destroy(tempSource);

        // Update last clip reference
        lastClip = newClip;
    }
}