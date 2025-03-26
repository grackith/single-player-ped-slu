using UnityEngine;

namespace CityGen3D
{
	// toggles emissive lights on procedural buildings on/off based on ambient light brightness from sunlight
    public class BuildingLights : MonoBehaviour
    {
		[Tooltip( "Current time (where 0 = peak darkness and 1 = peak light, so a value around 0.5 means dusk/dawn)." )]
		[Range( 0.0f, 1.0f )] public float solarTime = 0.5f;
		
		[Tooltip( "Time of day threshold at which building lights are switched on/off." )]
		[Range( 0.0f, 1.0f )] public float lightsOn = 0.5f;
		
		[Tooltip( "The higher the number, the more windows will emit light at night time." )]
		[Range( 0.0f, 1.0f )] public float lightDistribution = 0.5f;
		
		[Tooltip( "How often in seconds should we check time to see if lights should be on/off." )]
		public float checkFrequency = 2.0f;

        void Start()
		{			
			// periodically check time to see if we are transitioning from day to night or vice-versa
			InvokeRepeating( nameof(Refresh), 0.0f, checkFrequency );
		}
		
		// for the building shader pass in the current time of day to control interior lighting
		void Refresh()
		{
			// update solarTime using your own code or a third party time of day controller
			// solarTime = ?;
			
			// pass values into shaders
			Shader.SetGlobalFloat( "solarTime", solarTime );
			Shader.SetGlobalFloat( "lightsOn", lightsOn );
			Shader.SetGlobalFloat( "lightDistribution", lightDistribution );
		}

		// called when Inspector changes to refresh shader in case lightsOn time has changed
		void OnValidate()
		{
			Refresh();
		}
	}
}