using UnityEngine;

public class WeaponRotation : MonoBehaviour
{
    [Header("Rotation continue")]
    [Tooltip("Vitesse de rotation en degrés/seconde")]
    public float rotationSpeed = 90f;
    public Vector3 rotationAxis = Vector3.up;

    [Header("Flottement (bob)")]
    [Tooltip("Amplitude verticale en unités Unity")]
    public float bobAmplitude = 0.15f;
    [Tooltip("Fréquence de l'oscillation verticale")]
    public float bobFrequency = 1.2f;

    [Header("Inclinaison oscillante")]
    [Tooltip("Amplitude de l'inclinaison en degrés")]
    public float tiltAmplitude = 5f;
    [Tooltip("Fréquence de l'inclinaison")]
    public float tiltFrequency = 0.8f;
    public Vector3 tiltAxis = Vector3.forward;

    private Vector3 _startPosition;

    void Start()
    {
        _startPosition = transform.position;
    }

    void Update()
    {
        // --- Rotation continue ---
        transform.Rotate(rotationAxis, rotationSpeed * Time.deltaTime, Space.World);

        // --- Flottement vertical sinusoïdal ---
        float bobOffset = Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
        transform.position = _startPosition + Vector3.up * bobOffset;

        // --- Légère inclinaison oscillante ---
        float tiltAngle = Mathf.Sin(Time.time * tiltFrequency * Mathf.PI * 2f) * tiltAmplitude;
        // On applique l'inclinaison PAR-DESSUS la rotation actuelle
        transform.localRotation *= Quaternion.AngleAxis(tiltAngle * Time.deltaTime, tiltAxis);
    }
}