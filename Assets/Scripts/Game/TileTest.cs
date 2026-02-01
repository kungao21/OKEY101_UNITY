using System;
using System.Collections.Generic;
using UnityEngine;

public class TileTest : MonoBehaviour
{
    [Header("Bind only this")]
    public Transform rackRoot; // ISTAKALAR/MAIN_ISTAKA/RackRoot

    [Header("21 tiles (your hand)")]
    public string[] testHand = new string[]
    {
        "B03-1","R07-2","K03-1","G11-2","R01-1","B13-2","G05-1",
        "K12-1","R09-2","B07-1","G03-2","K01-2","R13-1","B01-2",
        "G09-1","K07-2","R05-1","B10-2","G13-2","K05-1","R11-2"
    };

    private readonly List<Transform> slots = new List<Transform>(32);
    private Transform tilesTopParent;
    private Transform tilesBottomParent;

    void Start()
    {
        if (rackRoot == null)
        {
            Debug.LogError("[TileTest] rackRoot NULL. Inspector'da ISTAKALAR/MAIN_ISTAKA/RackRoot bağla.");
            return;
        }

        // parents (yerleri belli)
        tilesTopParent = rackRoot.Find("UST_CONTAINER/TilesTop");
        tilesBottomParent = rackRoot.Find("ALT_CONTAINER/TilesBottom");
        if (tilesTopParent == null || tilesBottomParent == null)
        {
            Debug.LogError("[TileTest] TilesTop / TilesBottom bulunamadı. Hiyerarşi path'lerini kontrol et.");
            return;
        }

        CacheSlots();
        Debug.Log($"[TileTest] Slot cache OK. Count={slots.Count}");

        SpawnHandToFirstEmptySlots();
    }

    void CacheSlots()
    {
        slots.Clear();

        var top = rackRoot.Find("UST_CONTAINER/SlotsTop");
        var bottom = rackRoot.Find("ALT_CONTAINER/SlotsBottom");

        if (top == null || bottom == null)
        {
            Debug.LogError("[TileTest] SlotsTop / SlotsBottom bulunamadı. Path: UST_CONTAINER/SlotsTop ve ALT_CONTAINER/SlotsBottom");
            return;
        }

        foreach (Transform s in top) slots.Add(s);
        foreach (Transform s in bottom) slots.Add(s);

        // S01..S32 sırala
        slots.Sort((a, b) =>
        {
            int ia = int.Parse(a.name.Substring(1));
            int ib = int.Parse(b.name.Substring(1));
            return ia.CompareTo(ib);
        });
    }

    void SpawnHandToFirstEmptySlots()
    {
        int count = Mathf.Min(testHand.Length, slots.Count);

        for (int i = 0; i < count; i++)
        {
            string tileId = testHand[i];
            int slotIndex = i + 1; // testte direkt 1..21'e koyuyoruz

            var slot = slots[i];
            var parent = (slotIndex <= 16) ? tilesTopParent : tilesBottomParent;

            // ✅ TileObj prefab yok: direkt GO üret
            var go = new GameObject($"TILE_{slotIndex:00}_{tileId}");
            go.transform.SetParent(parent, false);

            // TileObj ekle (Awake -> VisualSlot otomatik yaratır) :contentReference[oaicite:1]{index=1}
            var tile = go.AddComponent<TileObj>();

            // Collider istersen kalsın; istemezsen commentle
            if (go.GetComponent<Collider>() == null)
                go.AddComponent<BoxCollider>();

            // slot poz/rot
            go.transform.position = slot.position;
            go.transform.rotation = slot.rotation;
            go.transform.localScale = Vector3.one;

            // tileId
            tile.AssignTileId(tileId);

            // "B03-1" -> "B03"
            string code = TileIdToCode(tileId);

            // visual load: Resources/taslar/B03.prefab gibi
            var visualPrefab = Resources.Load<GameObject>($"taslar/{code}");
            if (visualPrefab == null)
            {
                Debug.LogError($"[TileTest] Missing visual prefab: Resources/taslar/{code}.prefab  (tileId={tileId})");
                continue;
            }

            var visualInstance = Instantiate(visualPrefab);
            visualInstance.name = $"V_{code}_{tileId}";

            tile.AttachVisual(visualInstance, code); // görseli TileObj'ye takar :contentReference[oaicite:2]{index=2}
        }
    }

    static string TileIdToCode(string tileId)
    {
        if (string.IsNullOrEmpty(tileId)) return "";
        int dash = tileId.IndexOf('-');
        return dash > 0 ? tileId.Substring(0, dash) : tileId;
    }
}
