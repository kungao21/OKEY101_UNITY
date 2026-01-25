using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class WsClient : MonoBehaviour
{
    public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;

    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;

    private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

    // Events -> UI katmanı buradan dinler
    public event Action OnOpen;
    public event Action<string> OnClose;
    public event Action<string> OnError;
    public event Action<JObject> OnMessage;
    public JObject LastRoomSnapshot { get; private set; }
    public string LastRoomId { get; private set; } = "";
    public string LocalUserId { get; private set; } = "";



    private int _reqSeq = 0;

    void Update()
    {
        while (_mainThreadQueue.TryDequeue(out var action))
        {
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }

    void OnDestroy()
    {
        _ = Disconnect("OnDestroy");
    }

    public async Task Connect(string url)
    {
        if (IsConnected) return;

        _ws = new ClientWebSocket();
        _cts = new CancellationTokenSource();

        try
        {
            await _ws.ConnectAsync(new Uri(url), _cts.Token);

            EnqueueMain(() => OnOpen?.Invoke());

            _ = Task.Run(ReceiveLoop);
        }
        catch (Exception ex)
        {
            EnqueueMain(() => OnError?.Invoke("Connect error: " + ex.Message));
        }
    }

    public async Task Disconnect(string reason)
    {
        try
        {
            _cts?.Cancel();

            if (_ws != null)
            {
                if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, CancellationToken.None);
                }
                _ws.Dispose();
                _ws = null;
            }
        }
        catch { /* ignore */ }

        EnqueueMain(() => OnClose?.Invoke(reason));
    }

    private async Task ReceiveLoop()
    {
        var buffer = new byte[64 * 1024];

        try
        {
            while (_ws != null && _ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult result;

                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        EnqueueMain(() => OnClose?.Invoke("Server closed"));
                        await Disconnect("Server closed");
                        return;
                    }

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                } while (!result.EndOfMessage);

                var jsonText = sb.ToString();

                Debug.Log("[Lobby] " + jsonText);
                try
                {
                    var obj = JObject.Parse(jsonText);

                    // ✅ cache: sahne geçişinde snapshot kaçmasın
                    var t = obj.Value<string>("t");
                    if (t == "ROOM_SNAPSHOT")
                    {
                        var p = obj["p"] as JObject;
                        var rid = p?.Value<string>("roomId") ?? "";
                        LastRoomId = rid;
                        LastRoomSnapshot = obj;
                    }

                    EnqueueMain(() => OnMessage?.Invoke(obj));

                }
                catch (Exception parseEx)
                {
                    EnqueueMain(() => OnError?.Invoke("JSON parse error: " + parseEx.Message + " | " + jsonText));
                }
            }
        }
        catch (Exception ex)
        {
            EnqueueMain(() => OnError?.Invoke("ReceiveLoop error: " + ex.Message));
            await Disconnect("ReceiveLoop error");
        }
    }

    private void EnqueueMain(Action a) => _mainThreadQueue.Enqueue(a);

    private string NextReqId() => Interlocked.Increment(ref _reqSeq).ToString();

    public Task SendHello(string userId)
    {
        LocalUserId = userId;
        var msg = new JObject
        {
            ["t"] = "HELLO",
            ["reqId"] = NextReqId(),
            ["p"] = new JObject { ["userId"] = userId }
        };
        return Send(msg);
    }

    public Task SendRoomsListRequest()
    {
        var msg = new JObject
        {
            ["t"] = "ROOMS_LIST_REQUEST",
            ["reqId"] = NextReqId(),
            ["p"] = new JObject()
        };
        return Send(msg);
    }

    public Task SendRoomCreate(string userId)
    {
        var msg = new JObject
        {
            ["t"] = "ROOM_CREATE",
            ["reqId"] = NextReqId(),
            ["p"] = new JObject { ["userId"] = userId }
        };
        return Send(msg);
    }

    public Task SendRoomJoin(string userId, string roomId)
    {
        var msg = new JObject
        {
            ["t"] = "ROOM_JOIN",
            ["reqId"] = NextReqId(),
            ["p"] = new JObject
            {
                ["userId"] = userId,
                ["roomId"] = roomId
            }
        };
        return Send(msg);
    }

    public Task SendGameStart(string userId, string roomId)
    {
        var msg = new JObject
        {
            ["t"] = "GAME_START",
            ["reqId"] = NextReqId(),
            ["p"] = new JObject
            {
                ["userId"] = userId,
                ["roomId"] = roomId
            }
        };
        return Send(msg);
    }

    private async Task Send(JObject msg)
    {
        if (!IsConnected)
        {
            EnqueueMain(() => OnError?.Invoke("Send failed: not connected"));
            return;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(msg.ToString(Formatting.None));
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }
        catch (Exception ex)
        {
            EnqueueMain(() => OnError?.Invoke("Send error: " + ex.Message));
        }
    }
}
