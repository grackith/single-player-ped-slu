using UnityEngine;

namespace CityGen3D
{
	// attach to gameobject to get terrain texture information from terrain below
    public class TerrainInfo : MonoBehaviour
    {      
		public int textureIndex; // the terrain texture index at current location corresponding to the SplatMap texture list (0 = first texture in list)
		public float checkFrequency = 1.0f; // how often should we check for change? (seconds)

        void Start()
		{
			InvokeRepeating( nameof(Refresh), 0.0f, checkFrequency );
		}

        void Refresh()
		{
			textureIndex = LandscapeManager.Instance.GetTextureIndexAtWorldPosition( transform.position );
		}
    }
}
