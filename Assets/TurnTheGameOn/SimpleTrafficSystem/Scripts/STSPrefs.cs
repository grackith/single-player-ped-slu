namespace TurnTheGameOn.SimpleTrafficSystem
{
    using UnityEngine;

    public static class STSPrefs
    {
        // Runtime defaults for builds - these will be used when editor is not available
        public static bool routeGizmos = true;
        public static Color pathColor = new Color(1, 0.7725532f, 0, 1);
        public static Color selectedPathColor = new Color(0, 1, 0, 1);
        public static Color junctionColor = new Color(0, 0.5f, 1, 1);
        public static Color selectedJunctionColor = new Color(0, 1, 0, 1);
        public static Color yieldTriggerColor = new Color(1, 1, 0, 0.25f);
        public static Color selectedYieldTriggerColor = new Color(1, 1, 0, 0.5f);
        public static Vector3 arrowScale = Vector3.one;

        public static bool waypointGizmos = true;
        public static Color pointColor = new Color(1, 1, 1, 0.75f);
        public static Color firstPointColor = new Color(1, 1, 1, 0.75f);
        public static Color noConnectionColor = new Color(1, 0, 0, 0.75f);

        public static bool hideSpawnPointsInEditMode = false;

        public static bool sensorGizmos = false; // Usually false in builds for performance
        public static bool sideSensorGizmos = true;
        public static Color detectColor = new Color(1, 0, 0, 0.25f);
        public static Color normalColor = new Color(1, 1, 1, 0.25f);

        public static bool poolGizmos = true;
        public static Color minSpawnZoneColor = new Color(0.86f, 0.86f, 0.86f, 0.45f);
        public static Color cullHeadLightZone = new Color(1, 0, 0, 0.39f);
        public static Color activeZoneColor = new Color(0, 0.04f, 0.61f, 0.25f);
        public static Color spawnZoneColor = new Color(0.88f, 0, 1, 0.25f);

        public static bool debugProcessTime = false;

        public static Color handleColor = new Color(1, 1, 1, 0.8f);
        public static Color handleSelectedColor = new Color(1, 0, 0, 0.8f);
        public static Color handleTextColor = new Color(0, 0, 0, 1);
        public static Color handleTextSelectedColor = new Color(1, 1, 1, 1);
        public static Color connectionColor = new Color(1, 0, 0, 1);
        public static float drawDistance = 150;

        public static string fr_wheelName = "FR";
        public static string fl_wheelName = "FL";
        public static string br_wheelName = "BR";
        public static string bl_wheelName = "BL";

        public static string brakeMaterialName = "BrakeLights";

        public static string CiDyIntegrationPath = "Assets/TurnTheGameOn/SimpleTrafficSystem/Integration/CiDy.unitypackage";
        public static string StylizedVehiclesIntegrationPath = "Assets/TurnTheGameOn/SimpleTrafficSystem/Integration/StylizedVehiclePack.unitypackage";
        public static string HDRP_DemosPath = "Assets/TurnTheGameOn/SimpleTrafficSystem/Integration/HDRP.unitypackage";
        public static string URP_DemosPath = "Assets/TurnTheGameOn/SimpleTrafficSystem/Integration/URP.unitypackage";
    }
}