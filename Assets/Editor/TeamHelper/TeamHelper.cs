using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using NativeWebSocket;
using TinyJson;
using UniRx;
using UnityEngine;
using Object = UnityEngine.Object;

namespace erpk
{
    public class UnityUser
    {
        public string User;
        public string Id;
        public string Channel;
        public bool OnScene;
        public string LockedAsset;
    }

    public class TeamHelper : EditorWindow
    {
        private WebSocket _socket;

        private static Texture2D _backgroundTexture;
        private static GUIStyle _textureStyle;
        private Vector2 _scrollPos;
        private bool _showSettings;
        private Object _activeObject;
        private StringReactiveProperty _serverJson = new StringReactiveProperty();
        private List<UnityUser> _activeClients = new List<UnityUser>();
        private BoolReactiveProperty _isConnected = new BoolReactiveProperty();
        private BoolReactiveProperty _isOnScene = new BoolReactiveProperty();
        private IDisposable _reconnectObserver;
        private IDisposable _isConnectedObserver;
        private IDisposable _serverJsonObserver;
        private string _userName;
        private string _serverAddress;

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
                EditorPrefs.SetString("DisplayName", _serverAddress);
            }
        }

        private Color _editorBackgroundColor = Color.white;

        [MenuItem("Game/Team Helper")]
        static void Init()
        {
            var window = (TeamHelper) GetWindow(typeof(TeamHelper));
            window.titleContent = new GUIContent("Team Helper");
            // window.position = new Rect(Screen.width / 2, Screen.height / 2, 599, 299);

            window.Show();
        }

        private void Awake()
        {
            _backgroundTexture = new Texture2D(1, 1);
            _textureStyle = new GUIStyle();
        }

        private void OnEnable()
        {
            ConnectSocket();

            _isConnectedObserver = _isConnected.Subscribe(status =>
            {
                if (status) return;

                _reconnectObserver = Observable.Interval(TimeSpan.FromSeconds(5))
                    .TakeUntil(_isConnected.Where(connected => connected))
                    .Subscribe(observer =>
                    {
                        _activeClients = new List<UnityUser>();
                        ConnectSocket();
                        Debug.Log("Trying to reconnect " + _socket.State);
                    });
            });

            _serverJsonObserver = _serverJson.Subscribe(value =>
            {
                _activeClients = value?.FromJson<List<UnityUser>>();

                var currentUser = _activeClients.Find(u => u.Id == SystemInfo.deviceUniqueIdentifier);

                _activeObject = string.IsNullOrEmpty(currentUser.LockedAsset)
                    ? null
                    : AssetDatabase.LoadMainAssetAtPath(currentUser.LockedAsset);

                Repaint();
            });
        }

        private async void ConnectSocket()
        {
            Debug.Log("[UTH] Connecting to server");

            _socket = new WebSocket(
                $"ws://{ServerAddress}?clientName={UserName}&clientId={SystemInfo.deviceUniqueIdentifier}&channel={Application.identifier}");

            _socket.OnError += (e) =>
            {
                Debug.LogError("[UTH] Error! " + e);
                Repaint();
                _isConnected.Value = false;
            };
            _socket.OnClose += (e) =>
            {
                Debug.LogWarning("[UTH] Connection closed!");
                _socket.Close();
                Repaint();
                _isConnected.Value = false;
            };
            _socket.OnMessage += ParseMessages;
            _socket.OnOpen += () =>
            {
                SendWebSocketMessage("user", "sadsadasdas" + SystemInfo.deviceName);
                Repaint();
                _isConnected.Value = true;
            };

            await _socket.Connect();
        }

        private void ParseMessages(byte[] bytes)
        {
            var msg = System.Text.Encoding.UTF8.GetString(bytes);

            Debug.Log("[UTH] Server message: " + msg);

            _serverJson.SetValueAndForceNotify(msg);
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
            if (_socket != null) await _socket.Close();

            _isConnectedObserver?.Dispose();
            _serverJson?.Dispose();
            _reconnectObserver?.Dispose();
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
                    if (GUILayout.Button("Request scene access", GUILayout.Height(50)))
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
                EditorGUILayout.LabelField("Locked by " + sceneUser.User, EditorStyles.boldLabel);
            }
            else
            {
                EditorGUILayout.LabelField("Free", EditorStyles.boldLabel);
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

            if (GUILayout.Button("Reload"))
            {
                _socket.Close();
            }
        }

        private void ActiveUsersView()
        {
            if (_activeClients == null) return;

            Header("Active team members (" + _activeClients.Count + ")");

            foreach (var client in _activeClients.OrderBy(x => x.User))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(client.User, EditorStyles.foldoutHeader, GUILayout.Height(20));
                EditorGUILayout.LabelField((client.OnScene?"Scene    ":"") + client.LockedAsset.ToString(), EditorStyles.miniBoldLabel);
                EditorGUILayout.EndHorizontal();
                DrawUILine(Color.grey);
            }
        }

        private void OnGUI()
        {
            if (!_isConnected.Value)
            {
                ShowNotification(new GUIContent("Connecting..."));
            }

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUIStyle.none);

            EditorGUI.BeginDisabledGroup(!_isConnected.Value);

            SceneStatusView();

            ActiveUsersView();

            EditorGUI.EndDisabledGroup();

            SettingsView();

            GUILayout.EndScrollView();

            CheckServerStatus();
        }

        // private void OnInspectorUpdate()
        // {
        //     Repaint();
        // }

        private void DrawBox(Rect position, Color color)
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

        public static void DrawUILine(Color color)
        {
            var rect = EditorGUILayout.BeginHorizontal();
            Handles.color = color;
            Handles.DrawLine(new Vector2(rect.x - 15, rect.y), new Vector2(rect.width + 15, rect.y));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        private void Header(string text)
        {
            GUILayout.Space(10.00f);
            GUILayout.Label(text, EditorStyles.whiteLargeLabel);
            GUILayout.Space(2.0f);
            DrawUILine(Color.gray);
            GUILayout.Space(2.0f);
        }

        public static void LayoutBox(Color color, GUIContent content = null)
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

            GUILayout.Box(GUIContent.none, _textureStyle);
        }
    }
}