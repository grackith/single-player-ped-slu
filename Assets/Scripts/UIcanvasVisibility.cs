using UnityEngine;

public class UIcanvasVisibility : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            gameObject.SetActive(!gameObject.activeSelf);
        }
    }
}
