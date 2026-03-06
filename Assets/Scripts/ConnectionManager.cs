using UnityEngine;

/// <summary>
/// ConnectionManager pour la DesktopScene.
/// Affiche une UI IMGUI pour saisir le profil et le nom de session.
/// </summary>
public class ConnectionManager : BaseConnectionManager
{
    private string _profileName;
    private string _inputSessionName;

    private void OnGUI()
    {
        if (_state == ConnectionState.Connected) return;

        GUI.enabled = _state != ConnectionState.Connecting;

        using (new GUILayout.HorizontalScope(GUILayout.Width(250)))
        {
            GUILayout.Label("Profile Name", GUILayout.Width(100));
            _profileName = GUILayout.TextField(_profileName);
        }

        using (new GUILayout.HorizontalScope(GUILayout.Width(250)))
        {
            GUILayout.Label("Session Name", GUILayout.Width(100));
            _inputSessionName = GUILayout.TextField(_inputSessionName);
        }

        GUI.enabled = GUI.enabled
            && !string.IsNullOrEmpty(_profileName)
            && !string.IsNullOrEmpty(_inputSessionName);

        if (GUILayout.Button("Create or Join Session"))
            _ = ConnectAsync(_profileName, _inputSessionName);
    }
}