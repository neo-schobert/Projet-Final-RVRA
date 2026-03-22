using UnityEngine;

public class ArrowBob : MonoBehaviour
{
    public float amplitude = 0.15f;
    public float frequency = 1.2f;

    private Vector3 _startPosition;

    void Start()
    {
        _startPosition = transform.position;
    }

    void Update()
    {
        float bob = Mathf.Sin(Time.time * frequency * Mathf.PI * 2f) * amplitude;
        transform.position = _startPosition + Vector3.up * bob;
    }
}