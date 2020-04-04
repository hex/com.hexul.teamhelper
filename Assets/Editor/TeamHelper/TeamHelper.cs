using System;
using System.Collections.Generic;
using UnityEditor;
using NativeWebSocket;
using TinyJson;
using UniRx;
using UnityEngine;

namespace erpk
{
    public class UnityUser
    {
        public string User;
        public string Id;
        public string Channel;
    }

    public class TeamHelper : EditorWindow
    {
        private WebSocket _socket;

        private static Texture2D _backgroundTexture;
        private static GUIStyle _textureStyle;
        private Vector2 _scrollPos;
        private StringReactiveProperty _serverJson = new StringReactiveProperty();
        private List<UnityUser> _activeClients = new List<UnityUser>();
        private BoolReactiveProperty _isConnected = new BoolReactiveProperty();
        private IDisposable _reconnectObserver;
        private IDisposable _isConnectedObserver;
        private IDisposable _serverJsonObserver;

        [MenuItem("Game/Team Helper")]
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
                Repaint();
            });
        }

        private async void ConnectSocket()
        {
            Debug.Log("[UTH] Connecting to server");

            _socket = new WebSocket(
                $"ws://localhost:8080?clientName={SystemInfo.deviceName}&clientId={SystemInfo.deviceUniqueIdentifier}&channel={Application.identifier}");

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
                await _socket.SendText("msg/" + route + "/" + msg);
            }
        }

        private async void OnDestroy()
        {
            if (_socket != null) await _socket.Close();

            _isConnectedObserver.Dispose();
            _serverJson.Dispose();
            _reconnectObserver.Dispose();
        }

        private void CheckServerStatus()
        {
            switch (_socket?.State)
            {
                case WebSocketState.Open:
                    DrawBox(new Rect(Screen.width - 17, 7, 9, 9), Color.green);
                    break;
                case WebSocketState.Closed:
                case WebSocketState.Closing:
                    DrawBox(new Rect(Screen.width - 17, 7, 9, 9),
                        DateTime.Now.Second % 2 == 0 ? Color.yellow : Color.red);
                    break;
                default:
                    DrawBox(new Rect(Screen.width - 17, 7, 9, 9), Color.yellow);
                    break;
            }
        }

        private void OnGUI()
        {
            if (!_isConnected.Value)
            {
                ShowNotification(new GUIContent("Connecting..."));
            }

            EditorGUILayout.HelpBox(_socket?.State.ToString(), MessageType.None);
            CheckServerStatus();

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUIStyle.none);

            EditorGUI.BeginDisabledGroup(!_isConnected.Value);

            if (GUILayout.Button("Request scene access"))
            {
                _socket.Close();
                this.ShowNotification(new GUIContent("Scene requested"));
                EditorApplication.Beep();
            }

            Header("Status");

            if (_activeClients != null)
            {
                Header("Active team members (" + _activeClients.Count + ")");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("User", EditorStyles.foldoutHeader);
                EditorGUILayout.LabelField("Status", EditorStyles.foldoutHeader);
                EditorGUILayout.EndHorizontal();
                foreach (var client in _activeClients)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(client.User, EditorStyles.whiteLargeLabel, GUILayout.Height(20));
                    EditorGUILayout.SelectableLabel(client.Channel, EditorStyles.label,
                        GUILayout.Height(20));

                    EditorGUILayout.EndHorizontal();
                    GUILayout.Box("", new GUILayoutOption[] {GUILayout.ExpandWidth(true), GUILayout.Height(1)});
                }
            }

            Header("Log");

            // GUI.Label(new Rect(Screen.width - 17, 7, 9, 9), "", new GUIStyle {normal = new GUIStyleState { background = Texture2D.whiteTexture } });
            //
            //
            //
            // GUI.Box(
            //     new Rect(0,0,Screen.width, 100),
            //     new GUIContent("Scene requested"),
            //     _textureStyle
            //     );
            // if (GUILayout.Button("Show Notification"))
            // {
            //      
            //     this.ShowNotification(new GUIContent("Scene requested"), 10);
            // }
            EditorGUI.EndDisabledGroup();

            GUILayout.EndScrollView();
        }

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

        private void Header(string text)
        {
            GUILayout.Space(10.00f);
            GUILayout.Label(text, EditorStyles.whiteLargeLabel);
            GUILayout.Space(2.0f);
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));
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