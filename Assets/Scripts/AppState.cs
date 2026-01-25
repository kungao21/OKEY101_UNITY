using Newtonsoft.Json.Linq;

public static class AppState
{
    public static string CurrentRoomId = "";
    public static JObject LastRoomSnapshot = null;
}
