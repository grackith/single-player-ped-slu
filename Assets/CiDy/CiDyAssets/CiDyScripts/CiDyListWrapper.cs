using System.Collections.Generic;
using UnityEngine;

namespace CiDy
{
    [System.Serializable]
    public class CiDyListWrapper
    {

        public List<Vector3> vectorList;

        public CiDyListWrapper(List<Vector3> newList)
        {
            vectorList = newList;
        }
    }
}
