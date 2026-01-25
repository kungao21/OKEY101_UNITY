using System;
using System.Collections.Generic;
using Newtonsoft.Json;

[Serializable]
public class RoomsListEnvelope
{
    [JsonProperty("t")] public string t;
    [JsonProperty("p")] public RoomsListPayload p;
}

[Serializable]
public class RoomsListPayload
{
    [JsonProperty("rooms")] public List<RoomPublic> rooms;
}

[Serializable]
public class RoomPublic
{
    [JsonProperty("roomId")] public string roomId;
    [JsonProperty("state")] public string state;
    [JsonProperty("ownerId")] public string ownerId;

    [JsonProperty("dealerSeat")] public int dealerSeat;
    [JsonProperty("turnSeat")] public int turnSeat;
    [JsonProperty("turnPhase")] public string turnPhase;
    [JsonProperty("turnDeadline")] public long turnDeadline;

    // players: backend map olarak gönderiyor -> Dictionary<int, PlayerPublic>
    [JsonProperty("players")] public Dictionary<string, PlayerPublic> players;
}

[Serializable]
public class PlayerPublic
{
    [JsonProperty("userId")] public string userId;
    [JsonProperty("seat")] public int seat;
    [JsonProperty("connected")] public bool connected;
}
