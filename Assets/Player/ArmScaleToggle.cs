using UnityEngine;
using UnityEngine.InputSystem;

public class ArmScaleToggle : MonoBehaviour
{
    [SerializeField] private InputActionReference toggleInput;
    [SerializeField] private Transform[] bonesToScale;

    private bool _extended = false;

    private void OnEnable()  => toggleInput.action.performed += OnToggle;
    private void OnDisable() => toggleInput.action.performed -= OnToggle;

    private void OnToggle(InputAction.CallbackContext _)
    {
        _extended = !_extended;
        float y = _extended ? 1.5f : 1f;
        foreach (Transform bone in bonesToScale)
        {
            Vector3 s = bone.localScale;
            bone.localScale = new Vector3(s.x, y, s.z);
        }
    }
}
