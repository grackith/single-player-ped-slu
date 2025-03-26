using UnityEngine;

namespace CityGen3D
{
    public class Screenshot : MonoBehaviour
    {
        public string filename = "screenshot";
        public int index = 0;

        void Update()
        {
            if ( Input.GetKeyDown( KeyCode.F1 ) )
            {
                string str_file = filename + index.ToString() + ".png";
                ScreenCapture.CaptureScreenshot( str_file );
                Debug.Log( "CityGen3D: screenshot taken (" + str_file + ")" );

                index++;
            }
        }
    }
}