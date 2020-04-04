using System.Collections;
using System.Collections.Generic;
using Unity.EditorCoroutines.Editor;
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
    }

    public class TeamHelper : EditorWindow
    {
        private WebSocket _socket;

        private static Texture2D _backgroundTexture;
        private static GUIStyle _textureStyle;
        private StringReactiveProperty _serverJson = new StringReactiveProperty();
        private List<UnityUser> _activeClients = new List<UnityUser>();

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

            _serverJson.Subscribe(value =>
            {
                Debug.Log("SUBSCRIBE");
                _activeClients = value.FromJson<List<UnityUser>>();
                Repaint();
            });

            // this.StartCoroutine(SendHeartbeat());
        }

        private IEnumerator SendHeartbeat()
        {
            yield return new EditorWaitForSeconds(5f);

            SendWebSocketMessage("heartbeat", SystemInfo.deviceUniqueIdentifier);
            this.StartCoroutine(SendHeartbeat());
        }

        private async void ConnectSocket()
        {
            Debug.Log("[UTH] Connecting to server");

            _socket = new WebSocket("ws://localhost:8080?clientName=" + SystemInfo.deviceName + "&clientId=" +
                                    SystemInfo.deviceUniqueIdentifier);

            _socket.OnError += (e) => { Debug.LogError("[UTH] Error! " + e); };
            _socket.OnClose += (e) => { Debug.LogWarning("[UTH] Connection closed!"); };
            _socket.OnMessage += ParseMessages;
            _socket.OnOpen += () => { SendWebSocketMessage("user", "sadsadasdas" + SystemInfo.deviceName); };

            await _socket.Connect();
        }

        private void ParseMessages(byte[] bytes)
        {
            var msg = System.Text.Encoding.UTF8.GetString(bytes);

            Debug.Log("[UTH] Server message: " + msg);

            _serverJson.Value = msg;

            var clients = msg.FromJson<List<UnityUser>>();

            foreach (var client in clients)
            {
                Debug.LogError(client.User);
                Debug.LogError(client.Id);
            }

            // switch (msg)
            // {
            //     case "clientCount":
            //         _activeClients.Value = int.Parse(msg);
            //         break;
            // }
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
            await _socket.Close();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Team Helper", MessageType.None);
            DrawBox(new Rect(Screen.width - 17, 7, 9, 9), Color.green);

            EditorGUILayout.LabelField("Active team members");
            EditorGUILayout.LabelField(_activeClients.Count.ToString());

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("User", EditorStyles.foldoutHeader);
            EditorGUILayout.LabelField("Status", EditorStyles.foldoutHeader);
            EditorGUILayout.LabelField("Actions", EditorStyles.foldoutHeader);
            EditorGUILayout.EndHorizontal();
            foreach (var client in _activeClients)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(client.User, EditorStyles.whiteLargeLabel, GUILayout.Height(20));
                EditorGUILayout.SelectableLabel(client.Id, EditorStyles.label, GUILayout.Height(20));
                if (GUILayout.Button("Show Notification"))
                {
                    GUI.Box(new Rect(0,0,Screen.width, Screen.height), "tt" );
                    this.ShowNotification(new GUIContent("Scene requested"), 10);
                }

                if (GUILayout.Button("Remove Notification"))
                {
                    this.RemoveNotification();
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Box("", new GUILayoutOption[] { GUILayout.ExpandWidth(true), GUILayout.Height(1) });
            }

            GUI.Label(new Rect(Screen.width - 17, 7, 9, 9), "", new GUIStyle {normal = new GUIStyleState { background = Texture2D.whiteTexture } });

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

        public static void LayoutBox(Color color, GUIContent content = null)
        {
            var backgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = color;
            GUILayout.Box(content ?? GUIContent.none, _textureStyle);
            GUI.backgroundColor = backgroundColor;
        }
    }
}