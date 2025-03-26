using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace CityGen3D
{
	// Example script showing how you can create a 2D map building in code
	// This approach can be easily applied to other map object types
	// In this example we have to add the lat/lon coords in Inspector, but in practice you would most likely be reading them in from a file from external source
	public class CreateBuilding : MonoBehaviour
    {
		public int type = 0; // building type ID
		public List<GeoCoord> coords;

		public void CreateNewBuilding()
		{
			// need at least three coordinates to make a building
			if( coords == null ||
				coords.Count < 3 )
            {
				Debug.Log( "CreateNewBuilding - need at least three coordinates" );
				return;
            }

			// convert lat/lon to Unity world space
			List<Vector3> points = new List<Vector3>();
			foreach( GeoCoord location in coords )
            {
				points.Add( location.GetMapCoord( Map.Instance.data.GetOrigin() ).GetPosition() );
			}

			// create 2D map building
			GameObject obj = Map.Instance.mapBuildings.Create( type, points );
			if ( obj != null )
			{
				obj.GetComponent<MapBuilding>().RefreshPivot();
				obj.GetComponent<MapBuilding>().BuildMap();
			}
		}
	}

	[CustomEditor( typeof( CreateBuilding ) )]
	public class EditorCreateBuilding : Editor
	{
		public override void OnInspectorGUI()
		{
			DrawDefaultInspector();

			EditorGUILayout.Separator();

			CreateBuilding cb = (CreateBuilding)target;

			if ( GUILayout.Button( "Create Building" ) )
			{
				cb.CreateNewBuilding();
			}
		}
	}
}
