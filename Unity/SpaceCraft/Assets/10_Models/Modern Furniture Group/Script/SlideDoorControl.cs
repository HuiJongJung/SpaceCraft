using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SlideDoorControl : MonoBehaviour
{
    public string moveAxis = "Z";
    public float openLength = 0.35f;
    public float openSpeed = 0.3f;
    private bool nowOpen = false;
    private bool isAct = false;
    private Vector3 startPosition;
    public AudioClip open;
    public AudioClip close;
    private AudioSource audioSource;
    public bool isOpen = false;
    void Start()
    {
        audioSource = GetComponentInParent<AudioSource>();
        startPosition = transform.localPosition;
    }

    // Update is called once per frame
    void Update()
    {
        if (isOpen != nowOpen)
        {
            if (isOpen)
            {
                if (!audioSource.isPlaying && !isAct)
                {
                    audioSource.PlayOneShot(open);
                    isAct = true;
                }
                if (moveAxis == "X" || moveAxis == "x")
                {
                    transform.Translate(transform.right * openSpeed*Time.deltaTime, Space.World);
                    if (Mathf.Abs(transform.localPosition.x - startPosition.x) >= openLength)
                    {
                        nowOpen = !nowOpen;
                        isAct = false;
                    }
                }
                else if(moveAxis == "Y" || moveAxis == "y")
                {
                    transform.Translate(transform.up * openSpeed*Time.deltaTime, Space.World);
                    if (Mathf.Abs(transform.localPosition.y - startPosition.y) >= openLength)
                    {
                        nowOpen = !nowOpen;
                        isAct = false;
                    }
                }
                else
                {
                    transform.Translate(transform.forward * openSpeed * Time.deltaTime,Space.World);
                    if (Mathf.Abs(transform.localPosition.z - startPosition.z) >= openLength)
                    {
                        nowOpen = !nowOpen;
                        isAct = false;
                    }
                }                                   
            }
            else
            {
                if (!audioSource.isPlaying && !isAct)
                {
                    audioSource.PlayOneShot(close);
                    isAct = true;
                }
                if (moveAxis == "X" || moveAxis == "x")
                {
                    transform.Translate(transform.right * openSpeed * Time.deltaTime * -1, Space.World);
                    if (Mathf.Abs(transform.localPosition.x - startPosition.x) <= 0.01f)
                    {
                        nowOpen = !nowOpen;
                        isAct = false;
                        transform.localPosition = startPosition;
                    }
                }
                else if (moveAxis == "Y" || moveAxis == "y")
                {
                    transform.Translate(transform.up * openSpeed * Time.deltaTime * -1, Space.World);
                    if (Mathf.Abs(transform.localPosition.y - startPosition.y) <= 0.01f)
                    {
                        nowOpen = !nowOpen;
                        isAct = false;
                        transform.localPosition = startPosition;

                    }
                }
                else
                {
                    transform.Translate(transform.forward * openSpeed * Time.deltaTime * -1, Space.World);
                    if (Mathf.Abs(transform.localPosition.z - startPosition.z) <= 0.01f)
                    {
                        nowOpen = !nowOpen;
                        isAct = false;
                        transform.localPosition = startPosition;

                    }
                }
            }
        }
    }
}
