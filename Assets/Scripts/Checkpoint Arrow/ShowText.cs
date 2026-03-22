using UnityEngine;

public class ShowText : MonoBehaviour
{
    public GameObject targetObject;
    public float displayDuration = 3f;

    private float _timer = 0f;
    private bool _isShowing = false;

    public void ShowForDuration()
    {
        if (targetObject == null) return;

        targetObject.SetActive(true);
        _timer = displayDuration;
        _isShowing = true;
    }

    void Update()
    {
        if (!_isShowing) return;

        _timer -= Time.deltaTime;

        if (_timer <= 0f)
        {
            targetObject.SetActive(false);
            _isShowing = false;
        }
    }
}