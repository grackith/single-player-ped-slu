using CityGen3D;
using UnityEngine;

[ExecuteInEditMode()]
// attach to gameobject to draw a line to nearest road from it's location
public class ShowNearestRoad : MonoBehaviour
{
    public void OnDrawGizmos()
    {      
        // find nearest road to this gameobject
		Vector2 closestPos = Vector2.zero;
		MapRoad mapRoad = Map.Instance.mapRoads.GetNearestRoad( new Vector2( transform.position.x, transform.position.z ), ref closestPos );
		
		// draw line to nearest road (make sure Gizmos are on in scene view)
        if ( mapRoad != null )
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine( transform.position, new Vector3( closestPos.x, transform.position.y, closestPos.y ) );
        }
    }
}