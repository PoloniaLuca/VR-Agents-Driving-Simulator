using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SpeedLimiter : MonoBehaviour
{
    public float maxSpeedKmh = 90f;

    Rigidbody rb;
    float maxSpeedMs;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        maxSpeedMs = maxSpeedKmh / 3.6f;
    }

    void FixedUpdate()
    {
        maxSpeedMs = maxSpeedKmh / 3.6f; // allows changing it in the inspector at runtime

        // Unity 6: rb.linearVelocity   |   older Unity: rb.velocity
        Vector3 v = rb.linearVelocity;
        if (v.magnitude > maxSpeedMs)
            rb.linearVelocity = v.normalized * maxSpeedMs;
    }
}