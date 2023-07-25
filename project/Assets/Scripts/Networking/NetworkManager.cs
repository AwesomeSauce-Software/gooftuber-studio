using NativeWebSocket;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

[System.Serializable]
public class SessionInformation
{
    public SessionPayload SessionPayload;
    public List<string> VerifiedUserIDs;
    public string DiscordID;

    public SessionInformation()
    {
        VerifiedUserIDs = new List<string>();
    }
}

public class NetworkManager : MonoBehaviour
{
    public bool HasSession => sessionInfo?.SessionPayload != null && sessionInfo.SessionPayload.session_id.Length > 0;

    [SerializeField] SpriteInternalManager spriteManager;
    [SerializeField] CharacterAnimator selfCharacter;
    [SerializeField] CharacterManager characterManager;
    [Header("UI References")]
    [SerializeField] InputField userIDInputField;
    [SerializeField] InputField friendIDInputField;
    [SerializeField] InputField verifyIDInputField;
    [Header("Network Options")]
    [SerializeField] string networkFolder;
    [SerializeField] string sessionFileName;
    [SerializeField] string uri;

    WebSocket masterSocket;
    System.DateTime apiRequestTime;
    List<double> apiResponseTimes;

    string networkPath;
    string sessionFilePath;

    SessionInformation sessionInfo;
    List<VerifiedUser> verifiedUsers;

    #region Callbacks
    void InitiateSessionCallback(long result, string data)
    {
        LogEx.Log(LogTopics.Networking, $"Verification ID result: {result} {data}");
    }

    void RquestSessionCallback(long result, VerifiedUser user)
    {
        LogEx.Log(LogTopics.Networking, $"Request session user ID result: {result}");
        if (result == 200)
        {
            if (!sessionInfo.VerifiedUserIDs.Contains(user.UserID))
                sessionInfo.VerifiedUserIDs.Add(user.UserID);
        }
        else
        {
            verifiedUsers.RemoveAll(u => u.UserID == user.UserID);
        }
    }

    void GetSessionIDCallback(long result, string data)
    {
        LogEx.Log(LogTopics.Networking, $"Session ID result: {result} {data}");
        if (result == 200)
        {
            sessionInfo.SessionPayload = JsonUtility.FromJson<SessionPayload>(data);
        }
    }

    void UploadAvatarsCallback(long result, string data)
    {
        LogEx.Log(LogTopics.Networking, $"Uploading Sprites result: {result} {data}");
    }

    void GetAvatarsCallback(long result, VerifiedUser user, string data)
    {
        LogEx.Log(LogTopics.Networking, $"Get Avatars result: {result} for ID {user.UserID}");
        if (result == 200)
        {
            var avatarPayload = JsonUtility.FromJson<AvatarPayload>(data);

            if (user.Character != null)
            {
                user.Character.Initialize(characterManager.CreateExtSpriteManager(avatarPayload));
            }
            else
            {
                user.Character = characterManager.CreateExtCharacter(avatarPayload);
            }

#if UNITY_EDITOR
            foreach (var serializedAvatar in avatarPayload.avatars)
            {
                LogEx.Log(LogTopics.Networking, serializedAvatar.filename);
            }
#endif
        }
    }

    void PingAPICallback(long result, string data)
    {
        var apiResponseTime = System.DateTime.Now - apiRequestTime;
        LogEx.Log(LogTopics.NetworkingPinging, $"API Ping: {result} {apiResponseTime.TotalMilliseconds}ms");
        apiResponseTimes.Add(apiResponseTime.TotalMilliseconds);
    }
    #endregion

    #region Requests
    public void InitiateSession()
    {
        StartCoroutine(NetworkHelper.GetRequest(InitiateSessionCallback, uri, $"verify/{userIDInputField.text}"));
    }

    public void RequestSession()
    {
        if (HasSession)
        {
            var newUser = new VerifiedUser(friendIDInputField.text);
            verifiedUsers.Add(newUser);
            StartCoroutine(NetworkHelper.SessionRequest(RquestSessionCallback, newUser, uri, $"request-session/{sessionInfo.SessionPayload.session_id}/{friendIDInputField.text}"));
        }
    }

    public void GetSessionID()
    {
        StartCoroutine(NetworkHelper.GetRequest(GetSessionIDCallback, uri, $"verify/{userIDInputField.text}/{verifyIDInputField.text}"));
    }

    public void GetAvatars()
    {
        if (HasSession && verifiedUsers.Count > 0)
        {
            foreach (var verifiedUser in verifiedUsers)
            {
                LogEx.Log(LogTopics.Networking, $"Attempting to get avatars from {verifiedUser.UserID}");
                StartCoroutine(NetworkHelper.GetUserData(GetAvatarsCallback, verifiedUser, uri, $"get-avatars/{sessionInfo.SessionPayload.session_id}/{verifiedUser.UserID}"));
            }
        }
    }

    public void UploadAvatars()
    {
        if (HasSession)
        {
            var cachedSpritePaths = spriteManager.CachedSpritePaths;
            WWWForm uploadForm = new WWWForm();

            foreach (var cachedSpritePath in cachedSpritePaths)
            {
                uploadForm.AddBinaryData("avatar", File.ReadAllBytes(cachedSpritePath), Path.GetFileName(cachedSpritePath));
            }

            StartCoroutine(NetworkHelper.PostRequest(UploadAvatarsCallback, uri, $"upload-avatar/{sessionInfo.SessionPayload.session_id}", uploadForm));
        }
    }

    public void PingAPI()
    {
        apiRequestTime = System.DateTime.Now;
        StartCoroutine(NetworkHelper.GetRequest(PingAPICallback, uri, "ping"));
    }
    #endregion

    #region Websockets
    public async void InitializeWebsocket()
    {
        List<string> userIDs = new List<string>();
        foreach (var verifiedUser in verifiedUsers)
            userIDs.Add(verifiedUser.UserID);
        string url = "ws://" + uri + "/websocket/" + sessionInfo.SessionPayload.session_id + '/' + string.Join(",", userIDs);
        LogEx.Log(LogTopics.NetworkingWebsockets, $"Initializing Websocket: {url}");

        masterSocket = new WebSocket(url);

        masterSocket.OnOpen += () =>
        {
            LogEx.Log(LogTopics.NetworkingWebsockets, "Websocket Opened");
        };
        masterSocket.OnError += (err) =>
        {
            LogEx.Error(LogTopics.NetworkingWebsockets, $"Websocket Error: {err}");
        };
        masterSocket.OnMessage += (data) =>
        {
            LogEx.Log(LogTopics.NetworkingWebsockets, $"Received Websocket Data");
            ReceiveWebsocketData(data);
        };

        InvokeRepeating("SendWebsocketData", 0.0f, 0.05f);

        await masterSocket.Connect();
    }

    void ReceiveWebsocketData(byte[] data)
    {
        string serializedData = System.Text.Encoding.UTF8.GetString(data);
        LogEx.Log(LogTopics.NetworkingWebsockets, $"Received Websocket Data: {serializedData}");

        if (serializedData.Length > 0 && serializedData != "OK")
        {
            var activityPayload = JsonUtility.FromJson<ActivityPayload>(serializedData);
            var verifiedUser = verifiedUsers.Find(u => u.UserID == activityPayload.userid);
            if (verifiedUser != null && verifiedUser.Character != null)
            {
                
            }
        }
    }

    async void SendWebsocketData()
    {
        if (masterSocket.State == WebSocketState.Open)
        {
            var activityPayload = new ActivityPayload(selfCharacter.MeanVolume, selfCharacter.CurrentExpressionName);
            LogEx.Log(LogTopics.NetworkingWebsocketsSending, $"Sending Websocket Data: {activityPayload.voice_activity}");

            await masterSocket.SendText("SEND " + JsonUtility.ToJson(activityPayload));
        }
        else if (masterSocket.State == WebSocketState.Closed)
        {
            CancelInvoke("PollWebsocketData");
        }
    }
    #endregion

    public void AddUserID(string userID)
    {
        var user = new VerifiedUser(userID);
        verifiedUsers.Add(user);
    }

    public void SaveCache()
    {
        File.WriteAllText(sessionFilePath, JsonUtility.ToJson(sessionInfo));
    }

    void LoadCache()
    {
        networkPath = DataSystem.CreateSpace(networkFolder);
        sessionFilePath = networkPath + sessionFileName;

        sessionInfo = new SessionInformation();
        verifiedUsers = new List<VerifiedUser>();
        if (File.Exists(sessionFilePath))
        {
            var serializedSessionPayload = File.ReadAllText(sessionFilePath);
            sessionInfo = JsonUtility.FromJson<SessionInformation>(serializedSessionPayload);
        }
    }

    void Awake()
    {
        apiResponseTimes = new List<double>();
        LoadCache();
    }
}
