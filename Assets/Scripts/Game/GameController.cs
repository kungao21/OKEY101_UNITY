using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using TMPro;

public class GameController : MonoBehaviour
{
    public static GameController Instance { get; private set; }
    private void Awake()
    {
        Instance = this;
    }



    [Header("HUD")]
    public TMP_Text txtPlayers;
    public TMP_Text txtCountdown;



    [Header("BUILD PILES (DagitmaAlani)")]
    public Transform mainContainer;
    public Transform rightContainer;
    public Transform topContainer;
    public Transform leftContainer;

    [Header("ISTAKALAR (Hands)")]
    public Transform mainRack;
    public Transform rightRack;
    public Transform topRack;
    public Transform leftRack;

    public Transform tilePoolRoot;


    public GameObject arkaPrefab; // ARKA prefab

    [Header("Layout")]
    public float pileXStep = 0.035f;   // Deste kolonlari arasi X
    public float tileYStep = 0.004f;   // Kolon icinde taslarin Y stack farki
    public float dealFlySeconds = 1.00f;    // kisa lineer ucus







    private WsClient _ws;

    // ===== DEALING visual cache =====
    private bool _hasLast = false;
    private int _lastDealLeft = -1;
    private int _lastDealCursor = 0;
    private int _lastDealSeatCursor = 0;



    // last pileCounts (1..15)
    private readonly int[] _lastPileCounts = new int[16];

    // last myHand
    private List<string> _lastMyHand = new List<string>();

    // pileId -> pileRoot (RenderBuildPiles içinde doldurulacak)
    private readonly Dictionary<int, Transform> _pileRootById = new Dictionary<int, Transform>();

    // Tas prefab cache (Resources/taslar/R01 vb)
    private readonly Dictionary<string, GameObject> _tilePrefabCache = new Dictionary<string, GameObject>();

    // ===================== TILE POOL (106) + VISUAL POOL =====================
    private const int TOTAL_TILES = 106;
    

    private readonly List<TileObj> _tilesAll = new List<TileObj>(TOTAL_TILES);
    private readonly Stack<TileObj> _tilesFree = new Stack<TileObj>(TOTAL_TILES);

    // VisualPool key: "ARKA", "R01", "G03", "JOKER" ...
    private readonly Dictionary<string, Stack<GameObject>> _visualPool = new Dictionary<string, Stack<GameObject>>(128);

    // Pile roots (kalıcı)
    private readonly Dictionary<int, Transform> _pileRootPool = new Dictionary<int, Transform>(15);

    // Pile içindeki taş objeleri (pileId -> tiles)
    private readonly Dictionary<int, List<TileObj>> _pileTiles = new Dictionary<int, List<TileObj>>(15);

    // Bana gelen ama tileId’si henüz resolve olmayan taşlar
    private readonly Queue<TileObj> _pendingMineQueue = new Queue<TileObj>(32);

    private readonly Queue<string> _earlyMyTileIds = new Queue<string>(64);


    // Seat -> rack taş listesi (arka taşlar dahil)
    private readonly Dictionary<int, List<TileObj>> _rackTiles = new Dictionary<int, List<TileObj>>(4);

    // tileId -> taş objesi (backend tileId event’leri için)
    private readonly Dictionary<string, TileObj> _tileById = new Dictionary<string, TileObj>(256);

    private string _prevState = "";

    // ===== DEAL BUNDLE POOL (her animasyon kendi bundle'ını alır) =====
    private readonly Stack<Transform> _dealBundlePool = new Stack<Transform>(16);



    // zaten var
    public void OnTileClicked(TileObj tile)
    {
        Debug.Log($"Discard: {tile.TileId}");
    }




    void Start()
    {


        _ws = FindObjectOfType<WsClient>();

        EnsureTilePoolInitialized();
        WarmupVisualPool();
        EnsurePileRootsInitialized();

        if (_rackTiles.Count == 0)
        {
            _rackTiles[1] = new List<TileObj>(32);
            _rackTiles[2] = new List<TileObj>(32);
            _rackTiles[3] = new List<TileObj>(32);
            _rackTiles[4] = new List<TileObj>(32);
        }

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





        // ✅ state değiştiyse (el başlangıcı / dealing başlangıcı) rack listelerini resetle
        if (state != _prevState)
        {
            if (state == "DEALING")
            {
                // ✅ sadece listeleri değil, taşları da gerçekten pool’a geri topla
                ReturnAllRackTilesToFree();
                //ReturnAllPilesToFree();

                _earlyMyTileIds.Clear();
                _pendingMineQueue.Clear();
                _tileById.Clear();
                _hasLast = false;      // ✅ BUNU EKLE
                _lastDealLeft = -1;    // ✅ BUNU EKLE
            }

            _prevState = state;
        }






        if (state == "AUTO_START")
        {
            int left = p.Value<int?>("autoStartLeft") ?? 0;
            SetCountdown(left);
        }
        else
        {
            SetCountdown(null);
        }

        // ✅ Önce DEALING tick yakala (eski pileRoot'lar hala duruyor)
        if (state == "DEALING")
        {
            HandleDealingTick(p);
        }

        // ✅ Sonra render et (pileRoot map temizlenip yeniden kurulacak)
        if (state == "BUILD_PILES")
        {
            RenderBuildPiles(p);
        }

        if (state == "DICE_RESULT")
        {
            SyncPilesFromCounts(p);
            RenderBuildPiles(p);
        }

        if (state == "DICE")
        {
            //RenderBuildPiles(p);
        }


        // ✅ Eğer benim rack’e arka taşlar geldiyse, myHand diff ile yüz’e çevir
        ResolveMyPendingHand(p);

        CacheLastSnapshot(p);


    }

    private void SyncPilesFromCounts(JObject p)
    {
        int startPile = p.Value<int?>("startPile") ?? 1;
        int indicatorPile = p.Value<int?>("indicatorPile") ?? 0;
        int basePile = 1; // 8’li deste (server tarafında buradan eksiltiyorsun)

        bool IsAllowed(int pid) =>
            pid == basePile || pid == startPile || (indicatorPile > 0 && pid == indicatorPile);



        var counts = p["pileCounts"] as JObject;
        if (counts == null) return;

        for (int pileId = 1; pileId <= 15; pileId++)
        {
            if (!IsAllowed(pileId)) continue;

            // serverCount oku
            int serverCount = counts.Value<int?>($"{pileId}") ?? -1;
            if (serverCount < 0) continue;

            // unity list
            if (!_pileTiles.TryGetValue(pileId, out var list) || list == null) continue;
            int before = list.Count;

            // ✅ hedef sayıya çek (fazlaysa ReturnTileToFree ile pool'a gider, azsa _tilesFree'den alınır)
            EnsurePileTileCount(pileId, serverCount);

            int after = list.Count;

            // ✅ Eksikse pool'dan yeni tile geldi: bunları pileRoot altına koy + arka yap
            if (after > before)
            {
                if (_pileRootPool.TryGetValue(pileId, out var root) && root != null)
                {
                    for (int i = before; i < after; i++)
                    {
                        var tile = list[i];
                        if (tile == null) continue;

                        tile.transform.SetParent(root, false);
                        tile.transform.localPosition = Vector3.zero;
                        tile.transform.localRotation = Quaternion.identity;
                        tile.transform.localScale = Vector3.one;

                        // yeni eklenen taşlar ARKA kalsın
                        SetTileBack(tile);
                    }
                }
            }

            // ✅ Hepsini tekrar stackle
            RestackPileVisual(pileId);
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
        if (mainContainer == null || rightContainer == null || topContainer == null || leftContainer == null) return;
        if (arkaPrefab == null) return;
        if (tilePoolRoot == null) return;

        EnsureTilePoolInitialized();
        WarmupVisualPool();
        EnsurePileRootsInitialized();

        int mySeat = FindMySeat(p["players"] as JObject);
        if (mySeat == 0) return;

        var seatToContainer = BuildSeatToContainerMap(mySeat);

        var owners = p["pileOwners"] as JObject;
        var counts = p["pileCounts"] as JObject;
        if (owners == null || counts == null) return;

        int startPile = p.Value<int?>("startPile") ?? 1;
        var dealOrder = BuildDealOrder(startPile);


        // her seat için kolon index
        // her seat için kolon index
        var colIndexBySeat = new Dictionary<int, int> { { 1, 0 }, { 2, 0 }, { 3, 0 }, { 4, 0 } };

        // ❗ pileId 1..15 yerine startPile’dan başlayan “deal order” ile geziyoruz
        for (int oi = 0; oi < dealOrder.Count; oi++)
        {
            int pileId = dealOrder[oi];

            int c = counts.Value<int?>($"{pileId}") ?? 0;
            int ownerSeat = owners.Value<int?>($"{pileId}") ?? 0;

            if (!_pileRootPool.TryGetValue(pileId, out var pileRoot) || pileRoot == null)
                continue;

            // c <= 0 veya ownerSeat geçersizse: pile'ı gizle ve taşları free'ye iade et
            if (c <= 0 || ownerSeat < 1 || ownerSeat > 4 || !seatToContainer.TryGetValue(ownerSeat, out var container) || container == null)
            {
                ReturnAllPileTilesToFree(pileId);
                pileRoot.gameObject.SetActive(false);
                continue;
            }

            // pile root'u doğru container altına koy
            pileRoot.SetParent(container, false);

            // ✅ kolon index artık deal-order’a göre birikir
            int k = colIndexBySeat[ownerSeat];
            colIndexBySeat[ownerSeat] = k + 1;




            pileRoot.localPosition = new Vector3(k * pileXStep, 0f, 0f);
            pileRoot.localRotation = Quaternion.identity;
            pileRoot.localScale = Vector3.one;
            pileRoot.gameObject.SetActive(true);

            // pile tile count'u c'ye uydur
            EnsurePileTileCount(pileId, c);

            // pile içindeki tüm taşlar arka olsun ve stack dizilsin
            var list = _pileTiles[pileId];
            for (int i = 0; i < list.Count; i++)
            {
                var tile = list[i];
                tile.ClearTileId();
                SetTileBack(tile);

                var tr = tile.transform;
                tr.SetParent(pileRoot, false);
                tr.localPosition = new Vector3(0f, 0f, i * tileYStep);
                tr.localRotation = Quaternion.identity;
                tr.localScale = Vector3.one;
            }

            _pileRootById[pileId] = pileRoot;
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





    // ===================== DEALING VISUAL =====================

    private void CacheLastSnapshot(JObject p)
    {
        _lastDealLeft = p.Value<int?>("dealLeft") ?? -1;
        _lastDealCursor = p.Value<int?>("dealCursor") ?? 0;
        _lastDealSeatCursor = p.Value<int?>("dealSeatCursor") ?? 0;

        var counts = p["pileCounts"] as JObject;
        if (counts != null)
        {
            for (int i = 1; i <= 15; i++)
                _lastPileCounts[i] = counts.Value<int?>($"{i}") ?? 0;
        }

        _lastMyHand = ReadMyHand(p);
        _hasLast = true;
    }

    private void HandleDealingTick(JObject p)
    {
        if (!_hasLast) return;

        int dealLeftNow = p.Value<int?>("dealLeft") ?? -1;
        if (dealLeftNow < 0) return;

        // dealLeft azaldıysa: bu tick'te 1 deste dağıtıldı
        if (dealLeftNow < _lastDealLeft)
        {
            int pileId = _lastDealCursor;
            int targetSeat = _lastDealSeatCursor;

            int count = (pileId >= 1 && pileId <= 15) ? _lastPileCounts[pileId] : 0;
            if (count <= 0) count = 7;

            Vector3 src = mainContainer != null ? mainContainer.position : Vector3.zero;
            Quaternion srcRot = Quaternion.identity;

            if (_pileRootById.TryGetValue(pileId, out var pileRoot) && pileRoot != null)
            {
                src = pileRoot.position;
                srcRot = pileRoot.rotation;
            }

            // Bu deste kaç taş? (7/8) — backend zaten _lastPileCounts ile doğru sayıyı veriyor.
            var moved = TakeTilesFromPile(pileId, count);
            if (moved == null || moved.Count == 0) return;
            Debug.Log($"[DEAL] pileId={pileId} countWanted={count} moved={moved.Count} free={_tilesFree.Count} pileNow={(_pileTiles.TryGetValue(pileId, out var l) ? l.Count : -1)}");

            // ✅ bundle uçuş: bu tick’te giden 7/8 taş tek blok gibi uçar
            StartCoroutine(FlyBundleThenDistribute(p, targetSeat, moved, src, srcRot));

            Debug.Log("Target Seat : "+targetSeat.ToString());



        }
    }

    private System.Collections.IEnumerator FlyBundleThenDistribute(JObject p, int targetSeat, List<TileObj> tiles, Vector3 src, Quaternion srcRot)
    {
        if (tiles == null || tiles.Count == 0) yield break;

        if (mainRack == null || rightRack == null || topRack == null || leftRack == null) yield break;

        int mySeat = FindMySeat(p["players"] as JObject);
        if (mySeat == 0) yield break;

        var seatToRack = BuildSeatToRackMap(mySeat);
        if (!seatToRack.TryGetValue(targetSeat, out var rack) || rack == null) yield break;

        var bundle = GetDealBundle();


        // bundle başlangıç transform
        bundle.SetParent(null, true);
        bundle.position = src;
        bundle.rotation = srcRot;
        bundle.localScale = Vector3.one;

        // taşları bundle altına topla (stack gibi)
        for (int i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            if (t == null) continue;

            SetTileBack(t);

            var tr = t.transform;
            tr.SetParent(bundle, true);
            tr.localPosition = new Vector3(0f, 0f, i * tileYStep);
            tr.localRotation = Quaternion.identity;
            tr.localScale = Vector3.one;
        }

        Vector3 dst = rack.position;

        float a = 0f;
        float seconds = Mathf.Max(0.01f, dealFlySeconds);
        while (a < 1f)
        {
            a += Time.deltaTime / seconds;
            float t = Mathf.Clamp01(a);
            bundle.position = Vector3.Lerp(src, dst, t);
            yield return null;
        }

        // varınca bundle'ı rack'e “konum olarak” getir (parent şart değil)
        bundle.position = dst;
        bundle.rotation = Quaternion.identity;

        // taşları rack'e dağıt
        bool isMine = (targetSeat == mySeat);

        for (int i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            if (tile == null) continue;

            // bundle’dan çıkarıp rack’e koy
            tile.transform.SetParent(rack, true);
            tile.transform.rotation = Quaternion.identity;
            tile.transform.localScale = Vector3.one;

            _rackTiles[targetSeat].Add(tile);
            int idx = _rackTiles[targetSeat].Count - 1;
            tile.transform.localPosition = new Vector3(idx * pileXStep, 0f, 0f);
            tile.transform.localRotation = Quaternion.identity;

            if (isMine)
                _pendingMineQueue.Enqueue(tile);
        }

        // ✅ benim taşlarım geldiyse: early id buffer varsa anında yüz aç
        if (isMine)
            TryFlushEarlyIdsToPending();

        // bundle’ı tekrar pool altına sakla
        ReturnDealBundle(bundle);

    }



    private Dictionary<int, Transform> BuildSeatToRackMap(int mySeat)
    {
        int right = NextSeat(mySeat);
        int top = NextSeat(right);
        int left = NextSeat(top);

        return new Dictionary<int, Transform>
        {
            { mySeat, mainRack },
            { right, rightRack },
            { top, topRack },
            { left, leftRack },
        };
    }

    private List<string> ReadMyHand(JObject p)
    {
        var list = new List<string>();
        var arr = p["myHand"] as JArray;
        if (arr == null) return list;

        foreach (var it in arr)
        {
            var s = it?.ToString() ?? "";
            if (!string.IsNullOrEmpty(s)) list.Add(s);
        }
        return list;
    }

    // after - before (multiset)
    private List<string> MultiSetDiff(List<string> before, List<string> after)
    {
        var cnt = new Dictionary<string, int>();
        if (before != null)
        {
            foreach (var s in before)
            {
                if (cnt.ContainsKey(s)) cnt[s]++;
                else cnt[s] = 1;
            }
        }

        var res = new List<string>();
        if (after != null)
        {
            foreach (var s in after)
            {
                if (cnt.TryGetValue(s, out var n) && n > 0) cnt[s] = n - 1;
                else res.Add(s);
            }
        }
        return res;
    }



    private string TileIdToPrefabName(string tileId)
    {
        // "R01-2" -> "R01"
        int dash = tileId.IndexOf('-');
        return dash > 0 ? tileId.Substring(0, dash) : tileId;
    }

    private GameObject LoadTilePrefab(string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName)) return null;

        if (_tilePrefabCache.TryGetValue(prefabName, out var cached) && cached != null)
            return cached;

        // ⚠️ Resources şart: Assets/Resources/taslar/R01.prefab
        var prefab = Resources.Load<GameObject>($"taslar/{prefabName}");
        _tilePrefabCache[prefabName] = prefab;
        return prefab;
    }

    // ===================== INIT: 106 TileObj =====================


    private Transform GetDealBundle()
    {
        Transform b = null;

        if (_dealBundlePool.Count > 0)
            b = _dealBundlePool.Pop();

        if (b == null)
        {
            var go = new GameObject("DealBundle");
            b = go.transform;
        }

        b.gameObject.SetActive(true);
        b.SetParent(tilePoolRoot != null ? tilePoolRoot : transform, false);
        b.localPosition = Vector3.zero;
        b.localRotation = Quaternion.identity;
        b.localScale = Vector3.one;
        return b;
    }

    private void ReturnDealBundle(Transform b)
    {
        if (b == null) return;

        // güvenlik: bundle altında child kaldıysa ayır
        for (int i = b.childCount - 1; i >= 0; i--)
            b.GetChild(i).SetParent(null, true);

        b.gameObject.SetActive(false);
        b.SetParent(tilePoolRoot != null ? tilePoolRoot : transform, false);
        b.localPosition = Vector3.zero;
        b.localRotation = Quaternion.identity;
        b.localScale = Vector3.one;

        _dealBundlePool.Push(b);
    }


    // startPile’dan başlayıp 15’e kadar, sonra 1..startPile-1
    private List<int> BuildDealOrder(int startPile)
    {
        if (startPile < 1 || startPile > 15) startPile = 1;
        var order = new List<int>(15);
        for (int i = 0; i < 15; i++)
            order.Add(((startPile - 1 + i) % 15) + 1);
        return order;
    }

    // pending + early buffer'ı “hemen” eşleştirmek için
    private void TryFlushEarlyIdsToPending()
    {
        while (_pendingMineQueue.Count > 0 && _earlyMyTileIds.Count > 0)
        {
            AssignOnePending(_pendingMineQueue.Dequeue(), _earlyMyTileIds.Dequeue());
        }
    }


    private void EnsureTilePoolInitialized()
    {
        if (tilePoolRoot == null) return;

        if (_tilesAll.Count >= TOTAL_TILES) return;

        int need = TOTAL_TILES - _tilesAll.Count;
        for (int i = 0; i < need; i++)
        {
            var go = new GameObject($"Tile_{_tilesAll.Count + 1:000}");
            go.transform.SetParent(tilePoolRoot, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var tile = go.AddComponent<TileObj>();
            tile.ClearTileId();

            _tilesAll.Add(tile);
            _tilesFree.Push(tile);
        }
    }

    // ===================== VISUAL POOL =====================

    private void WarmupVisualPool()
    {
        if (tilePoolRoot == null) return;
        if (arkaPrefab == null) return;

        // Bir kere warmup: ARKA ve birkaç yüz hazırsa tekrar yapmayalım
        // (ARKA yoksa hiç yoktur)
        if (_visualPool.ContainsKey("ARKA")) return;

        _visualPool["ARKA"] = new Stack<GameObject>(TOTAL_TILES);

        // ARKA: 106 adet
        for (int i = 0; i < TOTAL_TILES; i++)
        {
            var v = Instantiate(arkaPrefab, tilePoolRoot, false);
            v.name = $"V_ARKA_{i + 1:000}";
            v.SetActive(false);
            _visualPool["ARKA"].Push(v);
        }

        // YÜZ: her code için 2 adet (B,G,R,K 01..13)
        WarmupFacesForColor("B");
        WarmupFacesForColor("G");
        WarmupFacesForColor("R");
        WarmupFacesForColor("K");

        // JOKER: 2 adet
        WarmupFace("JOKER", 2);
    }



    private void WarmupFacesForColor(string prefix)
    {
        for (int n = 1; n <= 13; n++)
        {
            string code = $"{prefix}{n:00}";
            WarmupFace(code, 2);
        }
    }

    private void WarmupFace(string code, int count)
    {
        if (_visualPool.ContainsKey(code)) return;

        var prefab = LoadTilePrefab(code);
        if (prefab == null)
        {
            Debug.LogError($"[VisualPool] Missing prefab Resources/taslar/{code}.prefab");
            _visualPool[code] = new Stack<GameObject>(0);
            return;
        }

        var st = new Stack<GameObject>(count);
        for (int i = 0; i < count; i++)
        {
            var v = Instantiate(prefab, tilePoolRoot, false);
            v.name = $"V_{code}_{i + 1}";
            v.SetActive(false);
            st.Push(v);
        }
        _visualPool[code] = st;
    }

    private GameObject TakeVisual(string key)
    {
        if (_visualPool.TryGetValue(key, out var st) && st.Count > 0)
            return st.Pop();

        // Normalde buraya düşmemeli (warmup var). Düşerse loglayalım.
        Debug.LogError($"[VisualPool] EMPTY key={key}. Warmup yetersiz veya kod hatası.");
        return null;
    }

    private void ReturnVisual(string key, GameObject visual)
    {
        if (visual == null) return;
        visual.SetActive(false);
        visual.transform.SetParent(tilePoolRoot, false);

        if (!_visualPool.TryGetValue(key, out var st))
        {
            st = new Stack<GameObject>(8);
            _visualPool[key] = st;
        }
        st.Push(visual);
    }



    // ===================== PILE ROOTS (15 adet kalıcı) =====================

    private void EnsurePileRootsInitialized()
    {
        if (_pileRootPool.Count == 15) return;

        for (int pileId = 1; pileId <= 15; pileId++)
        {
            if (_pileRootPool.ContainsKey(pileId)) continue;

            var root = new GameObject($"PileRoot_{pileId}").transform;
            root.SetParent(tilePoolRoot != null ? tilePoolRoot : transform, false); // başlangıçta pool altında dursun
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
            root.gameObject.SetActive(false);

            _pileRootPool[pileId] = root;
            _pileTiles[pileId] = new List<TileObj>(8);

            // Deal src için eski map'i de doldur
            _pileRootById[pileId] = root;
        }
    }

    private void EnsurePileTileCount(int pileId, int targetCount)
    {
        if (!_pileTiles.TryGetValue(pileId, out var list))
        {
            list = new List<TileObj>(8);
            _pileTiles[pileId] = list;
        }

        // artır
        while (list.Count < targetCount && _tilesFree.Count > 0)
        {
            var tile = _tilesFree.Pop();
            list.Add(tile);
        }

        // azalt
        while (list.Count > targetCount)
        {
            var tile = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            ReturnTileToFree(tile);
        }
    }

    private void ReturnAllPileTilesToFree(int pileId)
    {
        if (!_pileTiles.TryGetValue(pileId, out var list)) return;
        for (int i = list.Count - 1; i >= 0; i--)
            ReturnTileToFree(list[i]);
        list.Clear();
    }

    private void ReturnTileToFree(TileObj tile)
    {
        if (tile == null) return;

        // üstündeki visual'ı da pool'a iade et
        string oldKey = tile.CurrentVisualKey;     // ✅ detach'ten önce al
        var old = tile.DetachVisual();
        if (old != null && !string.IsNullOrEmpty(oldKey))
            ReturnVisual(oldKey, old);


        tile.ClearTileId();
        tile.transform.SetParent(tilePoolRoot, false);
        tile.transform.localPosition = Vector3.zero;
        tile.transform.localRotation = Quaternion.identity;
        tile.transform.localScale = Vector3.one;

        _tilesFree.Push(tile);
    }

    private void ReturnAllRackTilesToFree()
    {
        for (int seat = 1; seat <= 4; seat++)
        {
            if (!_rackTiles.TryGetValue(seat, out var list) || list == null) continue;

            for (int i = list.Count - 1; i >= 0; i--)
                ReturnTileToFree(list[i]);

            list.Clear();
        }
    }

    private void ReturnAllPilesToFree()
    {
        for (int pileId = 1; pileId <= 15; pileId++)
            ReturnAllPileTilesToFree(pileId);
    }


    private void ResolveMyPendingHand(JObject p)
    {
        // 1) myHand'ı oku
        var nowHand = ReadMyHand(p);

        // 2) last -> now farkını bul (bu snapshot'ta yeni gelen tileId'ler)
        var newTiles = MultiSetDiff(_lastMyHand, nowHand);

        // 3) Eğer bu snapshot'ta yeni tileId yoksa, sadece buffer'ı boşaltmayı deneyebiliriz
        // (bazı durumlarda buffer dolu + pending dolu olabilir ama diff 0 çıkar)
        // Bu yüzden sadece "newTiles boşsa return" demiyoruz; pending+buffer varsa yine eşleştiriyoruz.
        // Ama her halükarda önce newTiles'leri buffer'a ekleyeceğiz (varsa).

        // 4) newTiles varsa ama pending yoksa: tileId'leri kaybetme -> bufferla ve çık
        if (newTiles != null && newTiles.Count > 0 && _pendingMineQueue.Count == 0)
        {
            for (int i = 0; i < newTiles.Count; i++)
                _earlyMyTileIds.Enqueue(newTiles[i]);
            return;
        }

        // 5) newTiles varsa ve pending de varsa:
        // önce buffer'daki id'leri tüket (öncelik: eskiden kaçırılanlar)
        while (_pendingMineQueue.Count > 0 && _earlyMyTileIds.Count > 0)
        {
            AssignOnePending(_pendingMineQueue.Dequeue(), _earlyMyTileIds.Dequeue());
        }

        // 6) Şimdi bu snapshot'ın newTiles'lerini işle
        if (newTiles != null && newTiles.Count > 0)
        {
            for (int i = 0; i < newTiles.Count; i++)
            {
                var id = newTiles[i];

                // pending bitti ama id kaldıysa -> bufferla
                if (_pendingMineQueue.Count == 0)
                {
                    _earlyMyTileIds.Enqueue(id);
                    continue;
                }

                AssignOnePending(_pendingMineQueue.Dequeue(), id);
            }
        }

        // 7) Eğer newTiles boş olsa bile (diff 0),
        // ama hem buffer hem pending doluysa yine de eşleştirebiliriz.
        // Bu durum şu senaryoda olur: tileId erken geldi (buffer'a girdi),
        // sonra _lastMyHand update oldu, artık diff üretmiyor, ama taş fiziksel yeni geldi.
        while (_pendingMineQueue.Count > 0 && _earlyMyTileIds.Count > 0)
        {
            AssignOnePending(_pendingMineQueue.Dequeue(), _earlyMyTileIds.Dequeue());
        }
    }

    private void AssignOnePending(TileObj tile, string tileId)
    {
        if (tile == null) return;
        if (string.IsNullOrEmpty(tileId)) return;

        tile.AssignTileId(tileId);
        _tileById[tileId] = tile;

        // "B05-2" -> "B05", "JOKER-1" -> "JOKER"
        string code = TileIdToPrefabName(tileId);
        SetTileFace(tile, code);
    }


    private List<TileObj> TakeTilesFromPile(int pileId, int count)
    {
        if (!_pileTiles.TryGetValue(pileId, out var list)) return null;
        if (list.Count <= 0) return null;

        int take = Mathf.Min(count, list.Count);
        var res = new List<TileObj>(take);

        // üstten al (listenin sonu üst kabul)
        for (int i = 0; i < take; i++)
        {
            var t = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            res.Add(t);
        }

        RestackPileVisual(pileId);

        return res;
    }

    private void RestackPileVisual(int pileId)
    {
        if (!_pileRootPool.TryGetValue(pileId, out var pileRoot) || pileRoot == null) return;
        if (!_pileTiles.TryGetValue(pileId, out var list) || list == null) return;

        // kalan taşları sıkıştır: 0..count-1
        for (int i = 0; i < list.Count; i++)
        {
            var tile = list[i];
            if (tile == null) continue;

            var tr = tile.transform;

            // Eğer bu tile hala o pileRoot altındaysa zaten doğru; değilse parent etmiyoruz
            // (çünkü bazı taşlar alınmış olabilir)
            if (tr.parent == pileRoot)
            {
                tr.localPosition = new Vector3(0f, 0f, i * tileYStep);
                tr.localRotation = Quaternion.identity;
                tr.localScale = Vector3.one;
            }
        }
    }


    private void SetTileBack(TileObj tile)
    {
        if (tile == null) return;

        string oldKey = tile.CurrentVisualKey;
        var old = tile.DetachVisual();
        if (old != null && !string.IsNullOrEmpty(oldKey))
            ReturnVisual(oldKey, old);

        var v = TakeVisual("ARKA");
        tile.AttachVisual(v, "ARKA");
    }


    private void SetTileFace(TileObj tile, string code)
    {
        if (tile == null) return;
        if (string.IsNullOrEmpty(code)) return;

        string oldKey = tile.CurrentVisualKey;
        var old = tile.DetachVisual();
        if (old != null && !string.IsNullOrEmpty(oldKey))
            ReturnVisual(oldKey, old);

        var v = TakeVisual(code);
        tile.AttachVisual(v, code);
    }

}
