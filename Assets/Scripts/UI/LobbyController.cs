using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;


public class LobbyController : MonoBehaviour
{
    [Header("Refs")]
    public WsClient ws;

    [Header("Top UI")]
    public TMP_InputField serverUrlInput;
    public TMP_InputField userIdInput;
    public Button connectButton;
    public TMP_Text statusText;

    [Header("Room actions")]
    public Button createRoomButton;
    public TMP_InputField roomIdInput;
    public Button joinRoomButton;
    public Button refreshRoomsButton;

    [Header("Rooms list UI")]
    public Transform roomsContent;            // ScrollView/Content
    public GameObject roomItemPrefab;         // RoomItem.prefab

    private string _currentRoomId = "";
    private bool _loadingGameScene = false;


    private readonly List<GameObject> _spawnedItems = new List<GameObject>();

    void Awake()
    {
        // WS events
        ws.OnOpen += HandleOpen;
        ws.OnClose += HandleClose;
        ws.OnError += HandleError;
        ws.OnMessage += HandleMessage;

        // UI buttons
        connectButton.onClick.AddListener(OnClickConnect);
        createRoomButton.onClick.AddListener(OnClickCreate);
        joinRoomButton.onClick.AddListener(OnClickJoin);
        if (refreshRoomsButton != null)
            refreshRoomsButton.onClick.AddListener(OnClickRefresh);

        SetStatus("disconnected");
    }

    private async void OnClickConnect()
    {
        var url = "ws://localhost:8080/ws";
        var userId = userIdInput.text.Trim();

        SetStatus("connecting...");
        await ws.Connect(url);

        // Connect olunca HELLO gönderiyoruz (room list push için önemli)
        await ws.SendHello(userId);

        // Garanti olsun diye bir de request atalım
        await ws.SendRoomsListRequest();
    }

    private async void OnClickCreate()
    {
        var userId = userIdInput.text.Trim();
        await ws.SendRoomCreate(userId);
    }

    private async void OnClickJoin()
    {
        var userId = userIdInput.text.Trim();
        var roomId = roomIdInput.text.Trim();
        if (string.IsNullOrEmpty(roomId))
        {
            SetStatus("roomId boş");
            return;
        }
        await ws.SendRoomJoin(userId, roomId);
    }

    private async void OnClickRefresh()
    {
        await ws.SendRoomsListRequest();
    }

    private void HandleOpen()
    {
        SetStatus("connected");
    }

    private void HandleClose(string reason)
    {
        SetStatus("closed: " + reason);
    }

    private void HandleError(string err)
    {
        SetStatus("error: " + err);
    }

    private void HandleMessage(JObject obj)
    {
        var t = obj.Value<string>("t");

        // 1) Rooms list
        if (t == "ROOMS_LIST")
        {
            var env = JsonConvert.DeserializeObject<RoomsListEnvelope>(obj.ToString(Formatting.None));
            RenderRooms(env?.p?.rooms);
            return;
        }

        // 2) Room created gibi eventlerde roomId input doldurmak istersen:
        if (t == "ROOM_CREATED")
        {
            // server payload'ı değişebilir; olabildiğince toleranslı okuyalım
            var p = obj["p"] as JObject;
            var roomId = p?.Value<string>("roomId");
            if (!string.IsNullOrEmpty(roomId))
            {
                roomIdInput.text = roomId;
                SetStatus("room created: " + roomId);

                // yaratınca listeyi tazele
                _ = ws.SendRoomsListRequest();
            }
            return;
        }

        // 3) Join ok / snapshot vb. loglamak istersen:
        if (t == "ROOM_SNAPSHOT")
        {
            SetStatus("snapshot received");
            return;
        }


        if (t == "ROOM_JOINED")
        {
            var p = obj["p"] as JObject;
            var roomId = p?.Value<string>("roomId") ?? "";
            if (!string.IsNullOrEmpty(roomId))
            {
                _currentRoomId = roomId;
                roomIdInput.text = roomId;
                GoGameSceneOnce();
            }
            return;
        }



    }

    private void RenderRooms(List<RoomPublic> rooms)
    {
        // temizle
        for (int i = 0; i < _spawnedItems.Count; i++)
            Destroy(_spawnedItems[i]);
        _spawnedItems.Clear();

        if (rooms == null)
        {
            SetStatus("rooms null");
            return;
        }

        SetStatus("rooms: " + rooms.Count);


        // bas
        foreach (var room in rooms)
        {
            SetStatus("selected....: " + room.ownerId+" - "+room.roomId+" - "+room.players);
            var go = Instantiate(roomItemPrefab, roomsContent);
            _spawnedItems.Add(go);

            var view = go.GetComponent<RoomItemView>();
            view.Bind(room, (rid) =>
            {
                roomIdInput.text = rid; // join kolaylığı
                SetStatus("selected: " + rid);
            });
        }

        SetStatus("rooms: " + rooms.Count);
    }

    private void SetStatus(string s)
    {
        if (statusText != null) statusText.text = s;
        Debug.Log("[Lobby] " + s);
    }

    private void GoGameSceneOnce()
    {
        if (_loadingGameScene) return;
        _loadingGameScene = true;

        // İstersen roomId'yi global bir yere de yazabilirsin ama şimdilik GameController snapshot'tan okur.
        SceneManager.LoadScene("GameScene");
    }

}
