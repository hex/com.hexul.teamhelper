using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using NativeWebSocket;
using TinyJson;
using UnityEngine;
using Unity.EditorCoroutines.Editor;
using Object = UnityEngine.Object;

namespace hexul.TeamHelper.Editor
{
    public class UnityUser
    {
        public string User;
        public string Id;
        public string Channel;
        public bool OnScene;
        public bool HasSceneRequest;
        public string LockedAsset;
    }

    public class RequestDetails
    {
        public string RequestUser;
    }

    public class TeamHelper : EditorWindow
    {
        private WebSocket _socket;

        private static Texture2D _backgroundTexture;
        private static GUIStyle _textureStyle;
        private Vector2 _scrollPos;
        private bool _showSettings;
        private Object _activeObject;
        private List<UnityUser> _activeClients = new List<UnityUser>();
        private string _serverJson;
        private bool _isConnected;
        private Action<string> _serverJsonAction;
        private Action<bool> _isConnectedAction;
        private IDisposable _reconnectObserver;
        private IDisposable _isConnectedObserver;
        private IDisposable _serverJsonObserver;
        private string _userName;
        private string _serverAddress;
        private bool _enableLogging = true;
        private EditorCoroutine _retryConnectionCoroutine;
        private bool _isWindowActive;

        private string UserName
        {
            get
            {
                if (string.IsNullOrEmpty(_userName))
                    _userName = EditorPrefs.GetString("DisplayName", SystemInfo.deviceName);
                return _userName;
            }
            set
            {
                _userName = value;
                EditorPrefs.SetString("DisplayName", _userName);
            }
        }

        private string ServerAddress
        {
            get
            {
                if (string.IsNullOrEmpty(_serverAddress))
                    _serverAddress = EditorPrefs.GetString("ServerAddress", "localhost:8080");
                return _serverAddress;
            }
            set
            {
                _serverAddress = value;
                EditorPrefs.SetString("ServerAddress", _serverAddress);
            }
        }

        private bool EnableLogging
        {
            get => _enableLogging;
            set
            {
                _enableLogging = value;
                EditorPrefs.SetBool("EnableLogging", _enableLogging);
            }
        }

        [MenuItem("Window/Team Helper")]
        static void Init()
        {
            var window = (TeamHelper) GetWindow(typeof(TeamHelper));
            window.titleContent = new GUIContent("Team Helper");

            window.Show();
        }

        private void Awake()
        {
            _backgroundTexture = new Texture2D(1, 1);
            _textureStyle = new GUIStyle();

            _serverJsonAction += UpdateServerJson;
            _isConnectedAction += UpdateConnectedStatus;
        }

        private void UpdateServerJson(string data)
        {
            _activeClients = data?.FromJson<List<UnityUser>>();

            if (_activeClients != null)
            {
                var currentUser = _activeClients.Find(u => u.Id == SystemInfo.deviceUniqueIdentifier);

                _activeObject = string.IsNullOrEmpty(currentUser.LockedAsset)
                    ? null
                    : AssetDatabase.LoadMainAssetAtPath(currentUser.LockedAsset);

                Repaint();
            }
            else
            {
                var requestDetails = data?.FromJson<RequestDetails>();

                if (requestDetails != null)
                    SceneRequest(requestDetails.RequestUser);
            }
        }

        private void UpdateConnectedStatus(bool status)
        {
            _isConnected = status;

            if (status || !_isWindowActive) return;

            _retryConnectionCoroutine = this.StartCoroutine(RetryConnection());
        }

        private IEnumerator RetryConnection()
        {
            if (_isConnected) this.StopCoroutine(_retryConnectionCoroutine);

            yield return new EditorWaitForSeconds(5);

            _activeClients = new List<UnityUser>();
            ConnectSocket();

            if (EnableLogging)
                Debug.Log("[UTH] Trying to reconnect " + _socket.State);
        }

        private void OnEnable()
        {
            _activeClients = new List<UnityUser>();
            _isWindowActive = true;

            ConnectSocket();
        }

        private async void ConnectSocket()
        {
            if (EnableLogging)
                Debug.Log("[UTH] Connecting to server");

            _socket = new WebSocket(
                $"ws://{ServerAddress}?clientName={UserName}&clientId={SystemInfo.deviceUniqueIdentifier}&channel={Application.identifier}");

            _socket.OnError += (e) =>
            {
                if (EnableLogging)
                    Debug.LogError("[UTH] Error! " + e);

                Repaint();
                _isConnectedAction.Invoke(false);
            };
            _socket.OnClose += (e) =>
            {
                if (EnableLogging)
                    Debug.LogWarning("[UTH] Connection closed!");

#pragma warning disable 4014
                _socket.Close();
#pragma warning restore 4014

                Repaint();
                _isConnectedAction.Invoke(false);
            };
            _socket.OnMessage += ParseMessages;
            _socket.OnOpen += () =>
            {
                SendWebSocketMessage("Hello from " + UserName);
                Repaint();
                _isConnectedAction.Invoke(true);
            };

            await _socket.Connect();
        }

        private void ParseMessages(byte[] bytes)
        {
            var msg = System.Text.Encoding.UTF8.GetString(bytes);

            if (EnableLogging)
                Debug.Log("[UTH] Server message: " + msg);

            _serverJsonAction.Invoke(msg);
        }

        private async void SendWebSocketMessage(string msg)
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.SendText(msg);
            }
        }

        private async void SendWebSocketMessage(string route, string msg)
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.SendText(route + ":" + msg);
            }
        }

        private async void OnDestroy()
        {
            _isWindowActive = false;

            if (_socket != null) await _socket.Close();
        }

        private void CheckServerStatus()
        {
            if (_activeClients?.Exists(user => user.OnScene) == true)
            {
                var sceneUser = _activeClients.Find(user => user.OnScene);

                DrawBox(new Rect(0, 0, Screen.width - 40, 3),
                    sceneUser.Id == SystemInfo.deviceUniqueIdentifier ? Color.yellow : Color.red);
            }
            else
            {
                DrawBox(new Rect(0, 0, Screen.width - 40, 3), Color.green);
            }


            switch (_socket?.State)
            {
                case WebSocketState.Open:
                    DrawBox(new Rect(Screen.width - 38, 0, 38, 3), Color.green);
                    break;
                case WebSocketState.Closed:
                case WebSocketState.Closing:
                    DrawBox(new Rect(Screen.width - 38, 0, 38, 3),
                        DateTime.Now.Second % 2 == 0 ? Color.yellow : Color.red);
                    break;
                default:
                    DrawBox(new Rect(Screen.width - 38, 0, 38, 3), Color.yellow);
                    break;
            }
        }

        private void SceneRequest(string user)
        {
            ShowNotification(new GUIContent("Scene request from: " + user), 5);
            EditorApplication.Beep();
        }

        private void RequestScene()
        {
            SendWebSocketMessage("request", "scene");
        }

        private void SceneStatusView()
        {
            Header("Status");
            GUILayout.Space(2.0f);

            if (_activeClients?.Exists(user => user.OnScene) == true)
            {
                var sceneUser = _activeClients.Find(user => user.OnScene);
                var activeRequestUser = _activeClients.Find(user => user.HasSceneRequest);

                if (sceneUser.Id == SystemInfo.deviceUniqueIdentifier)
                {
                    GUI.color = Color.yellow;
                    if (GUILayout.Button("Unlock scene", GUILayout.Height(50)))
                    {
                        RequestScene();
                        ShowNotification(new GUIContent("Unlocked scene"));
                    }

                    GUI.color = Color.white;
                }
                else
                {
                    GUI.color = Color.red;

                    if (GUILayout.Button(activeRequestUser != null &&
                                         activeRequestUser.Id == SystemInfo.deviceUniqueIdentifier
                        ? "Cancel scene request"
                        : "Request scene access", GUILayout.Height(50)))
                    {
                        RequestScene();
                        ShowNotification(new GUIContent("Scene requested"));
                    }

                    GUI.color = Color.white;
                }
            }
            else
            {
                GUI.color = Color.green;
                if (GUILayout.Button("Lock scene", GUILayout.Height(50)))
                {
                    RequestScene();
                    ShowNotification(new GUIContent("Scene locked"));
                }

                GUI.color = Color.white;
            }

            GUILayout.Space(10.0f);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Scene status");

            if (_activeClients?.Exists(user => user.OnScene) == true)
            {
                var sceneUser = _activeClients.Find(user => user.OnScene);
                GUI.color = Color.yellow;
                EditorGUILayout.LabelField("Locked by " + sceneUser.User, EditorStyles.boldLabel);
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = Color.green;
                EditorGUILayout.LabelField("Free", EditorStyles.boldLabel);
                GUI.color = Color.white;
            }

            EditorGUILayout.EndHorizontal();

            _activeObject = EditorGUILayout.ObjectField("Active object", _activeObject, typeof(Object), true);
            EditorGUI.BeginDisabledGroup(!_activeObject);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(" ");
            if (GUILayout.Button("Mark dirty"))
            {
                SendWebSocketMessage("object", AssetDatabase.GetAssetPath(_activeObject));
            }

            if (GUILayout.Button("Clear"))
            {
                SendWebSocketMessage("object", "");
            }

            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
        }

        private void SettingsView()
        {
            Header("Settings");

            UserName = EditorGUILayout.TextField("Display name", UserName);
            ServerAddress = EditorGUILayout.TextField("Server address", ServerAddress);
            EnableLogging = EditorGUILayout.Toggle("Enable logs", EnableLogging);

            if (GUILayout.Button("Reload"))
            {
#pragma warning disable 4014
                _socket.Close();
#pragma warning restore 4014
            }
        }

        private void ActiveUsersView()
        {
            if (_activeClients == null) return;

            Header("Active team members (" + _activeClients.Count + ")");

            foreach (var client in _activeClients.OrderBy(x => x.User))
            {
                EditorGUILayout.BeginHorizontal();
                if (client.HasSceneRequest)
                {
                    GUI.color = Color.red;
                }

                EditorGUILayout.LabelField(new GUIContent(client.User, client.Id), EditorStyles.boldLabel,
                    GUILayout.Height(20));
                GUI.color = Color.white;
                GUI.color = Color.yellow;
                EditorGUILayout.LabelField(new GUIContent(
                        (client.HasSceneRequest ? "Scene request    " : "") + (client.OnScene ? "Scene    " : "") +
                        client.LockedAsset, (client.HasSceneRequest ? "Scene request    " : "") +
                                            (client.OnScene ? "Scene    " : "") +
                                            client.LockedAsset),
                    EditorStyles.miniBoldLabel);
                GUI.color = Color.white;
                EditorGUILayout.EndHorizontal();
                DrawUILine(new Color(0.49f, 0.49f, 0.49f, 0.2f));
            }
        }

        private void OnGUI()
        {
            if (!_isConnected)
            {
                ShowNotification(new GUIContent("Connecting..."));
            }

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUIStyle.none);

            EditorGUI.BeginDisabledGroup(!_isConnected);

            SceneStatusView();

            ActiveUsersView();

            EditorGUI.EndDisabledGroup();

            SettingsView();

            GUILayout.EndScrollView();

            CheckServerStatus();
        }

        private static void DrawBox(Rect position, Color color)
        {
            if (_backgroundTexture == null)
            {
                _backgroundTexture = new Texture2D(1, 1);
            }

            if (_textureStyle == null)
            {
                _textureStyle = new GUIStyle();
            }

            _backgroundTexture.SetPixel(0, 0, color);
            _backgroundTexture.Apply();

            _textureStyle.normal.background = _backgroundTexture;

            GUI.Box(position, GUIContent.none, _textureStyle);
        }

        private static void DrawUILine(Color color)
        {
            var rect = EditorGUILayout.BeginHorizontal();
            Handles.color = color;
            Handles.DrawLine(new Vector2(rect.x - 15, rect.y), new Vector2(rect.width + 15, rect.y));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        private static void Header(string text)
        {
            GUILayout.Space(10.00f);
            GUILayout.Label(text, EditorStyles.whiteLargeLabel);
            GUILayout.Space(2.0f);
            DrawUILine(Color.gray);
            GUILayout.Space(2.0f);
        }
    }
}