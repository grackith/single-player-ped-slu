using UnityEngine;

namespace CityGen3D
{
	// attach to gameobject to get road data at its location
    public class RoadInfo : MonoBehaviour
    {      
		public MapRoad road; // the road at current location
        public float checkRadius = 1.0f; // how far away from location do we consider to be within bounds?
		public float checkFrequency = 1.0f; // how often should we check for change? (seconds)

        void Start()
		{
			InvokeRepeating( nameof(RefreshRoad), 0.0f, checkFrequency );
		}

        void RefreshRoad()
		{
            road = Map.Instance.mapRoads.GetMapRoadAtWorldPosition( transform.position.x, transform.position.z, checkRadius );
		}
    }
}
