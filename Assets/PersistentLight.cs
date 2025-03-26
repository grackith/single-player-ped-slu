using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Add this to your main scene on the directional light
public class PersistentLight : MonoBehaviour
{
    void Start()
    {
        DontDestroyOnLoad(gameObject);
    }
}
