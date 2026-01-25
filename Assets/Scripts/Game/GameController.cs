using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameController : MonoBehaviour
{
    [Header("HUD")]
    public TMP_Text txtPlayers;
    public TMP_Text txtCountdown;

    [Header("BUILD PILES (DagitmaAlani)")]
    public Transform mainContainer;
    public Transform rightContainer;
    public Transform topContainer;
    public Transform leftContainer;

    public GameObject arkaPrefab; // ARKA prefab

    [Header("Layout")]
    public float pileXStep = 0.035f;   // Deste kolonlari arasi X
    public float tileYStep = 0.004f;   // Kolon icinde taslarin Y stack farki

    private WsClient _ws;

    void Start()
    {
        _ws = FindObjectOfType<WsClient>();
        if (_ws == null)
        {
            Debug.LogError("[Game] WsClient not found. NetworkClient sahneler arası taşınıyor mu?");
            return;
        }

        _ws.OnMessage += OnWsMessage;

        // ✅ İlk açılışta "0/4" basma. Snapshot varsa onu uygula.
        if (_ws.LastRoomSnapshot != null)
        {
            OnWsMessage(_ws.LastRoomSnapshot);
        }
        else
        {
            // Snapshot gelene kadar placeholder
            if (txtPlayers != null) txtPlayers.SetText("Oyuncu: --/4");
            SetCountdown(null);
        }
    }

    void OnDestroy()
    {
        if (_ws != null) _ws.OnMessage -= OnWsMessage;
    }

    private void OnWsMessage(JObject obj)
    {
        var t = obj.Value<string>("t");
        if (t != "ROOM_SNAPSHOT") return;

        var p = obj["p"] as JObject;
        if (p == null) return;

        // players sayısı
        int count = CountPlayers(p["players"] as JObject);
        SetPlayers(count);

        // state ve autostartLeft
        var state = p.Value<string>("state") ?? "";
        if (state == "AUTO_START")
        {
            int left = p.Value<int?>("autoStartLeft") ?? 0;
            SetCountdown(left);
        }
        else
        {
            SetCountdown(null);
        }

        // ✅ BUILD_PILES çizimi: BUILD_PILES + DICE (istersen DEALING de ekleyebilirsin)
        if (state == "BUILD_PILES" || state == "DICE")
        {
            RenderBuildPiles(p);
        }
    }

    private int CountPlayers(JObject playersObj)
    {
        if (playersObj == null) return 0;

        int n = 0;
        foreach (var prop in playersObj.Properties())
        {
            var pj = prop.Value as JObject;
            if (pj == null) continue;

            var userId = pj.Value<string>("userId") ?? "";
            if (!string.IsNullOrEmpty(userId)) n++;
        }
        return n;
    }

    private void SetPlayers(int count)
    {
        if (txtPlayers == null) return;
        txtPlayers.SetText("Oyuncu: {0}/4", count);
        Canvas.ForceUpdateCanvases();
    }

    private void SetCountdown(int? secondsLeft)
    {
        if (txtCountdown == null) return;

        if (secondsLeft.HasValue)
            txtCountdown.SetText("Başlıyor: {0}", secondsLeft.Value);
        else
            txtCountdown.SetText("");
    }

    // ===================== BUILD PILES =====================

    private void RenderBuildPiles(JObject p)
    {
        // gerekli referanslar yoksa sessizce çık
        if (mainContainer == null || rightContainer == null || topContainer == null || leftContainer == null) return;
        if (arkaPrefab == null) return;

        // mySeat bul (WsClient içinde LocalUserId tutuyor olmalısın; sende yoksa söyle, ekleriz)
        int mySeat = FindMySeat(p["players"] as JObject);
        if (mySeat == 0) return;

        var seatToContainer = BuildSeatToContainerMap(mySeat);

        // temizle
        ClearChildren(mainContainer);
        ClearChildren(rightContainer);
        ClearChildren(topContainer);
        ClearChildren(leftContainer);

        var owners = p["pileOwners"] as JObject;
        var counts = p["pileCounts"] as JObject;
        if (owners == null || counts == null) return;

        // her seat için kolon index
        var colIndexBySeat = new Dictionary<int, int> { { 1, 0 }, { 2, 0 }, { 3, 0 }, { 4, 0 } };

        // 1..15 desteyi sırayla çiz
        for (int pileId = 1; pileId <= 15; pileId++)
        {
            int c = counts.Value<int?>($"{pileId}") ?? 0;
            if (c <= 0) continue;

            int ownerSeat = owners.Value<int?>($"{pileId}") ?? 0;
            if (ownerSeat < 1 || ownerSeat > 4) continue;

            if (!seatToContainer.TryGetValue(ownerSeat, out var container))
                continue;

            int k = colIndexBySeat[ownerSeat];
            colIndexBySeat[ownerSeat] = k + 1;

            // ✅ Senin istediğin kolon mantığı:
            // Deste1 X=0, Deste2 X=0.035, Deste3 X=0.070, ...
            var pileRoot = new GameObject($"Pile_{pileId}_S{ownerSeat}").transform;
            pileRoot.SetParent(container, false);
            pileRoot.localPosition = new Vector3(k * pileXStep, 0f, 0f);
            pileRoot.localRotation = Quaternion.identity;

            // ✅ Taşlar aynı X’te, Y’de 0.004 artarak stack
            for (int i = 0; i < c; i++)
            {
                var go = Instantiate(arkaPrefab, pileRoot, false);
                go.transform.localPosition = new Vector3(0f, 0f, i * tileYStep);
                go.transform.localRotation = Quaternion.identity;
            }
        }
    }

    private int FindMySeat(JObject playersObj)
    {
        if (playersObj == null) return 0;
        if (_ws == null) return 0;

        // ⚠️ Burada WsClient'te LocalUserId olması lazım
        var myId = _ws.LocalUserId;
        if (string.IsNullOrEmpty(myId)) return 0;

        foreach (var prop in playersObj.Properties())
        {
            var pj = prop.Value as JObject;
            if (pj == null) continue;

            var uid = pj.Value<string>("userId") ?? "";
            if (uid == myId)
                return pj.Value<int?>("seat") ?? 0;
        }
        return 0;
    }

    private Dictionary<int, Transform> BuildSeatToContainerMap(int mySeat)
    {
        int right = NextSeat(mySeat);
        int top = NextSeat(right);
        int left = NextSeat(top);

        return new Dictionary<int, Transform>
        {
            { mySeat, mainContainer },
            { right, rightContainer },
            { top, topContainer },
            { left, leftContainer },
        };
    }

    private int NextSeat(int s) => (s == 4) ? 1 : s + 1;

    private void ClearChildren(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--)
            Destroy(t.GetChild(i).gameObject);
    }
}
