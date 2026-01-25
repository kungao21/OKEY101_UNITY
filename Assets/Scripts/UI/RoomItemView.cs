using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoomItemView : MonoBehaviour
{
    public TMP_Text roomIdText;
    public TMP_Text stateText;
    public TMP_Text ownerText;
    public TMP_Text playersText;
    public Button joinButton;

    private string _roomId;

    public void Bind(RoomPublic room, System.Action<string> onJoinClicked)
    {
        _roomId = room.roomId;

        roomIdText.text = room.roomId;
        stateText.text = room.state;
        ownerText.text = "Owner: " + room.ownerId;

        Debug.Log("------------"+room.roomId+" - "+room.state+" - "+room.ownerId);

        // Seat1..4 yazdır
        playersText.text = BuildPlayersText(room);

        joinButton.onClick.RemoveAllListeners();
        joinButton.onClick.AddListener(() => onJoinClicked?.Invoke(_roomId));
    }

    private string BuildPlayersText(RoomPublic room)
    {
        // players map: key seat string olabilir
        // her seat için basit çıktı:
        string SeatLine(int seat)
        {
            if (room.players != null && room.players.TryGetValue(seat.ToString(), out var p))
                return $"S{seat}: {p.userId} {(p.connected ? "●" : "○")}";
            return $"S{seat}: -";
        }

        return $"{SeatLine(1)}\n{SeatLine(2)}\n{SeatLine(3)}\n{SeatLine(4)}";
    }
}
