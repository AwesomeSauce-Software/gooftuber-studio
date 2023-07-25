#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(NetworkManager))]
public class NetworkManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Networking Debug");
        EditorGUI.BeginDisabledGroup(!Application.isPlaying);

        var networkObject = target as NetworkManager;
        if (networkObject != null)
        {
            if (GUILayout.Button("Save Network Cache"))
                networkObject.SaveCache();

            if (GUILayout.Button("Ping API"))
                networkObject.PingAPI();

            EditorGUI.BeginDisabledGroup(!networkObject.HasSession);
            if (GUILayout.Button("Initialize Websocket Sender"))
                networkObject.InitializeWebsocketSender();

            if (GUILayout.Button("Upload Sprite Data"))
                networkObject.UploadAvatars();

            if (GUILayout.Button("Get Sprite Data"))
                networkObject.GetAvatars();
            EditorGUI.EndDisabledGroup();
        }

        EditorGUI.EndDisabledGroup();
    }
}
#endif
