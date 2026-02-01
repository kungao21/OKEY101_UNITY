using System.Collections.Generic;
using UnityEngine;

public class RackInteractionController : MonoBehaviour
{
    [Header("Rack Root (genelde bu objenin kendisi)")]
    public Transform rackRoot;

    [Header("Server yokken test için")]
    public bool spawnTestHandOnStart = true;

    public string[] testHand = new string[]
    {
        "B03-1","R07-2","K03-1","G11-2","R01-1","B13-2","G05-1",
        "K12-1","R09-2","B07-1","G03-2","K01-2","R13-1","B01-2",
        "G09-1","K07-2","R05-1","B10-2","G13-2","K05-1","R11-2"
    };

    // slotIndex -> slot transform
    private readonly Dictionary<int, Transform> slots = new Dictionary<int, Transform>(32);

    // slotIndex -> tile
    private readonly Dictionary<int, TileObj> slotToTile = new Dictionary<int, TileObj>(32);

    private Transform tilesTopParent;
    private Transform tilesBottomParent;
    private Transform hitPlaneTf;

    // Drag state
    private TileObj dragTile;
    private int dragFromSlot = -1;

    // ✅ Screen-space drag (drift yok)
    private float dragScreenZ;
    private Vector3 dragScreenOffset;
    private bool dragReady = false;

    [Header("Drag Surface Raycast (Zone)")]
    public LayerMask dragSurfaceMask;     // RackRoot + DagitmaAlani collider layer'ı (Ignore Raycast önerilir)
    public float rackHover = 0.03f;       // istaka üstünde
    public float tableHover = 0.01f;      // masa üstünde
    public float rayMaxDistance = 50f;

    public Transform dragRoot;            // sürüklerken taşın parent'ı (boş bırakılırsa this.transform)

    public Collider rackSurfaceCollider;   // RackRoot collider
    public Collider tableSurfaceCollider;  // DagitmaAlani collider





    void Awake()
    {
        if (rackRoot == null) rackRoot = transform;

        CacheRefs();
        CacheSlots();
    }

    void Start()
    {
        if (spawnTestHandOnStart)
            SpawnHandIntoFirstEmptySlots(testHand);
        else
            RegisterExistingTiles();
    }

    public bool OwnsTile(TileObj tile)
    {
        if (tile == null || rackRoot == null) return false;
        return tile.transform.IsChildOf(rackRoot);
    }


    void CacheRefs()
    {
        tilesTopParent = rackRoot.Find("UST_CONTAINER/TilesTop");
        tilesBottomParent = rackRoot.Find("ALT_CONTAINER/TilesBottom");

        hitPlaneTf = rackRoot.Find("HitPlane"); // Collider şart değil, sadece referans noktası/normal için

        if (tilesTopParent == null || tilesBottomParent == null)
            Debug.LogError("[RackInteraction] TilesTop/TilesBottom bulunamadı.");

        if (hitPlaneTf == null)
            Debug.LogWarning("[RackInteraction] HitPlane bulunamadı. Drag plane rackRoot üzerinden kurulacak (fallback).");
    }


    void CacheSlots()
    {
        slots.Clear();

        var top = rackRoot.Find("UST_CONTAINER/SlotsTop");
        var bottom = rackRoot.Find("ALT_CONTAINER/SlotsBottom");

        if (top == null || bottom == null)
        {
            Debug.LogError("[RackInteraction] SlotsTop/SlotsBottom bulunamadı.");
            return;
        }

        for (int i = 1; i <= 16; i++)
            slots[i] = top.Find($"S{i:00}");

        for (int i = 17; i <= 32; i++)
            slots[i] = bottom.Find($"S{i:00}");
    }

    // ------------------ TEST SPAWN (server yokken) ------------------

    public void SpawnHandIntoFirstEmptySlots(string[] hand)
    {
        if (hand == null) return;

        int cursor = 1;
        for (int i = 0; i < hand.Length; i++)
        {
            int slotIndex = FindFirstEmptySlot(cursor);
            if (slotIndex == -1) return;

            var tile = CreateTileObj(hand[i]);
            PlaceTile(tile, slotIndex);

            cursor = slotIndex + 1;
        }
    }

    int FindFirstEmptySlot(int start)
    {
        for (int i = start; i <= 32; i++)
            if (!slotToTile.ContainsKey(i)) return i;
        for (int i = 1; i < start; i++)
            if (!slotToTile.ContainsKey(i)) return i;
        return -1;
    }

    TileObj CreateTileObj(string tileId)
    {
        var go = new GameObject($"Tile_{tileId}");
        var tile = go.AddComponent<TileObj>();

        // Sürüklemek için collider
        if (go.GetComponent<Collider>() == null)
            go.AddComponent<BoxCollider>();

        tile.AssignTileId(tileId);

        string code = TileIdToCode(tileId);
        var visualPrefab = Resources.Load<GameObject>($"taslar/{code}");
        if (visualPrefab == null)
        {
            Debug.LogError($"[RackInteraction] Missing visual prefab: Resources/taslar/{code}.prefab (tileId={tileId})");
            return tile;
        }

        var visualInstance = Instantiate(visualPrefab);
        tile.AttachVisual(visualInstance, code);

        return tile;
    }

    static string TileIdToCode(string tileId)
    {
        int dash = tileId.IndexOf('-');
        return dash > 0 ? tileId.Substring(0, dash) : tileId;
    }

    void RegisterExistingTiles()
    {
        slotToTile.Clear();

        var all = new List<TileObj>();
        all.AddRange(tilesTopParent.GetComponentsInChildren<TileObj>(true));
        all.AddRange(tilesBottomParent.GetComponentsInChildren<TileObj>(true));

        foreach (var t in all)
        {
            int s = FindNearestSlot(t.transform.position);
            if (s != -1 && !slotToTile.ContainsKey(s))
            {
                slotToTile[s] = t;
                SnapToSlot(t, s);
            }
        }
    }

    // ------------------ DRAG API (TileObj çağıracak) ------------------
    public void BeginDrag(TileObj tile)
    {
        dragTile = tile;
        dragFromSlot = FindTileSlot(tile);

        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[BeginDrag] Camera.main yok!");
            dragReady = false;
            return;
        }

        // Taşın ekrandaki derinliği (z)
        Vector3 tileScreen = cam.WorldToScreenPoint(tile.transform.position);
        dragScreenZ = tileScreen.z;

        // Mouse ile taş merkezi arasındaki screen offset (piksel)
        Vector3 mouse = Input.mousePosition;
        mouse.z = dragScreenZ;
        dragScreenOffset = tileScreen - mouse;

        dragReady = true;

        // Drag sırasında parent (rackRoot altında kalmasın diye ayrı root daha sağlıklı)
        var parent = (dragRoot != null) ? dragRoot : this.transform;
        tile.transform.SetParent(parent, true);
    }






    public void DragMove()
    {
        if (dragTile == null || !dragReady) return;

        var cam = Camera.main;
        if (cam == null) return;
        
        // Mouse’u taşın aynı Z derinliğine al
        Vector3 mouse = Input.mousePosition;
        mouse.z = dragScreenZ;

        // Mouse + başlangıç screen offset’i → hedef screen nokta
        Vector3 targetScreen = mouse + dragScreenOffset;
        targetScreen.z = dragScreenZ;

        // ✅ Yüzey bulmak için ray’i targetScreen üzerinden atıyoruz (offset bozulmaz)
        Ray ray = cam.ScreenPointToRay(targetScreen);

        if (Physics.Raycast(ray, out RaycastHit hit, rayMaxDistance, dragSurfaceMask, QueryTriggerInteraction.Collide))
        {
            // ===== DEBUG: HANGİ COLLIDER? =====
            Debug.Log(
                $"[DRAG HIT] surface={DebugSurfaceType(hit.collider)} " +
                $"collider='{hit.collider.name}' " +
                $"layer='{LayerMask.LayerToName(hit.collider.gameObject.layer)}' " +
                $"root='{hit.collider.transform.root.name}' " +
                $"tile='{dragTile.transform.position}' " +
                $"point={hit.point}"
            );



            float hover = ResolveHoverFor(hit.collider);

            // ✅ Her zaman yüzeyin üstünde tut
            Vector3 finalPos = hit.point + hit.normal * hover;
            dragTile.transform.position = finalPos;
            return;
        }

        Debug.LogWarning("[DRAG HIT] ❌ Hiçbir drag surface collider vurulmadı (fallback kullanılıyor)");


        // Fallback: hiç zone vurmazsa (nadiren)
        Vector3 worldPos = cam.ScreenToWorldPoint(targetScreen);
        dragTile.transform.position = worldPos;
    }


    private float ResolveHoverFor(Collider col)
    {
        if (col == tableSurfaceCollider) return tableHover;
        if (col == rackSurfaceCollider) return rackHover;

        return rackHover; // güvenli default
    }

    private string DebugSurfaceType(Collider col)
    {
        if (col == tableSurfaceCollider) return "TABLE";
        if (col == rackSurfaceCollider) return "RACK";

        return "UNKNOWN";
    }







    public void EndDrag()
    {
        if (dragTile == null) return;

        // eski slot kaydını çıkar
        if (dragFromSlot != -1 && slotToTile.TryGetValue(dragFromSlot, out var t) && t == dragTile)
            slotToTile.Remove(dragFromSlot);

        PlaceTileAtNearestSlot(dragTile, dragTile.transform.position);
        Debug.Log("Sürüklenme bitti....");
        dragTile = null;
        dragFromSlot = -1;
        dragReady = false;
        dragScreenZ = 0f;
        dragScreenOffset = Vector3.zero;


    }

    //bool RayToDragPlane(out Vector3 hitPoint)
    //{
    //    hitPoint = Vector3.zero;

    //    if (!dragPlaneReady) return false;

    //    var cam = Camera.main;
    //    if (cam == null) return false;

    //    var ray = cam.ScreenPointToRay(Input.mousePosition);

    //    if (dragPlane.Raycast(ray, out float enter))
    //    {
    //        hitPoint = ray.GetPoint(enter);
    //        return true;
    //    }

    //    return false;
    //}


    // ------------------ SLOT + INSERT/SHIFT ------------------

    void PlaceTileAtNearestSlot(TileObj tile, Vector3 dropWorldPos)
    {
        int target = FindNearestSlot(dropWorldPos);
        if (target == -1)
        {
            if (dragFromSlot != -1) PlaceTile(tile, dragFromSlot);
            return;
        }

        if (!slotToTile.ContainsKey(target))
        {
            PlaceTile(tile, target);
            return;
        }

        int empty = FindNearestEmptySlotPreferRight(target);
        if (empty == -1)
        {
            if (dragFromSlot != -1) PlaceTile(tile, dragFromSlot);
            return;
        }

        ShiftTiles(target, empty);
        PlaceTile(tile, target);
    }

    int FindNearestSlot(Vector3 worldPos)
    {
        float best = float.MaxValue;
        int bestIndex = -1;

        foreach (var kv in slots)
        {
            if (kv.Value == null) continue;
            float d = Vector3.Distance(worldPos, kv.Value.position);
            if (d < best)
            {
                best = d;
                bestIndex = kv.Key;
            }
        }
        return bestIndex;
    }

    int FindTileSlot(TileObj tile)
    {
        foreach (var kv in slotToTile)
            if (kv.Value == tile) return kv.Key;
        return -1;
    }

    int FindNearestEmptySlotPreferRight(int from)
    {
        for (int i = from + 1; i <= 32; i++)
            if (!slotToTile.ContainsKey(i)) return i;

        for (int i = from - 1; i >= 1; i--)
            if (!slotToTile.ContainsKey(i)) return i;

        return -1;
    }

    void ShiftTiles(int from, int empty)
    {
        if (empty > from)
        {
            for (int i = empty; i > from; i--)
                MoveTile(i - 1, i);
        }
        else
        {
            for (int i = empty; i < from; i++)
                MoveTile(i + 1, i);
        }
    }

    void MoveTile(int from, int to)
    {
        if (!slotToTile.TryGetValue(from, out var tile)) return;

        slotToTile.Remove(from);
        slotToTile[to] = tile;

        SnapToSlot(tile, to);
    }

    void PlaceTile(TileObj tile, int slotIndex)
    {
        slotToTile[slotIndex] = tile;
        SnapToSlot(tile, slotIndex);
    }

    void SnapToSlot(TileObj tile, int slotIndex)
    {
        var parent = (slotIndex <= 16) ? tilesTopParent : tilesBottomParent;

        tile.transform.SetParent(parent, true);
        tile.transform.position = slots[slotIndex].position;
        tile.transform.rotation = slots[slotIndex].rotation;
        tile.transform.localScale = Vector3.one;
    }
}
