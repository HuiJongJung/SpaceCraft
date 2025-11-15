using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RevolvingDoorControl : MonoBehaviour
{
    public string  rotateAxis = "Y";
    public float openAngle = 60;
    public float openSpeed = 250;
    private bool nowOpen = false;
    private bool isAct = false;
    public AudioClip open;
    public AudioClip close;
    private AudioSource audioSource;
    public bool isOpen = false;
    void Start()
    {
        audioSource = GetComponentInParent<AudioSource>();
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
                if (rotateAxis == "X" || rotateAxis == "x")
                {
                    transform.Rotate(Vector3.right, openSpeed * Time.deltaTime);
                    if (Mathf.Abs(transform.localEulerAngles.x) >= openAngle&& Mathf.Abs(transform.localEulerAngles.x)<=(360-openAngle))
                    {
                        nowOpen = !nowOpen;
                        isAct = false;
                    }
                            
                }
                else if (rotateAxis == "Z" || rotateAxis == "z")
                {
                    transform.Rotate(Vector3.forward, openSpeed * Time.deltaTime);
                    if (Mathf.Abs(transform.localEulerAngles.z) >= openAngle && Mathf.Abs(transform.localEulerAngles.z) <= (360 - openAngle))
                    {
                        nowOpen = !nowOpen;
                        isAct = false;
                    }
                }
                else 
                {
                    transform.Rotate(Vector3.up, openSpeed * Time.deltaTime);
                    if (Mathf.Abs(transform.localEulerAngles.y) >= openAngle && Mathf.Abs(transform.localEulerAngles.y) <= (360 - openAngle))
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
                if (rotateAxis == "X" || rotateAxis == "x")
                {
                    transform.Rotate(Vector3.right, openSpeed * Time.deltaTime * -1);
                    if (Mathf.Abs(transform.localEulerAngles.x) <= 5)
                    {
                        nowOpen = !nowOpen;
                        transform.localEulerAngles = Vector3.zero;
                        isAct = false;
                    }
                }
                else if (rotateAxis == "Z" || rotateAxis == "z")
                {
                    transform.Rotate(Vector3.forward, openSpeed * Time.deltaTime * -1);
                    if (Mathf.Abs(transform.localEulerAngles.z) <= 5)
                    {
                        nowOpen = !nowOpen;
                        transform.localEulerAngles = Vector3.zero;
                        isAct = false;
                    }
                }
                else
                {
                    transform.Rotate(Vector3.up, openSpeed * Time.deltaTime * -1);
                    if (Mathf.Abs(transform.localEulerAngles.y) <= 5)
                    {
                        nowOpen = !nowOpen;
                        transform.localEulerAngles = Vector3.zero;
                        isAct = false;
                    }
                }
            }
        }
    }
}
