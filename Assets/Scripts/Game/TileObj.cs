using System;
using UnityEngine;

/// <summary>
/// Oyundaki TEK bir fiziksel taşı temsil eder.
/// Bu GameObject oyun boyunca sabit kalır.
/// Sadece üstündeki görsel prefab (arka / yüz) değişir.
/// </summary>
public class TileObj : MonoBehaviour
{
    /// <summary>
    /// Server tileId: "R01-2", "G03-1", "JOKER-2" vb.
    /// Henüz bilinmiyorsa boş string.
    /// </summary>
    public string TileId { get; private set; } = "";
    private RackInteractionController _rack;




    /// <summary>
    /// Görsel prefabın takıldığı slot
    /// </summary>
    [SerializeField]
    private Transform visualSlot;

    /// <summary>
    /// Şu anda takılı olan görsel instance
    /// </summary>
    public GameObject CurrentVisual { get; private set; }

    /// <summary>
    /// İlk kurulumda otomatik VisualSlot oluşturur
    /// </summary>
    private void Awake()
    {
        if (visualSlot == null)
        {
            var go = new GameObject("VisualSlot");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            visualSlot = go.transform;
        }
    }

    // ===================== TILE ID =====================

    public void AssignTileId(string tileId)
    {
        TileId = tileId ?? "";
    }

    public void ClearTileId()
    {
        TileId = "";
    }

    // ===================== VISUAL CONTROL =====================

    /// <summary>
    /// Havuzdan alınmış bir görseli bu taşa takar
    /// </summary>
    public string CurrentVisualKey { get; private set; } = "";

    public void AttachVisual(GameObject visual, string key)
    {
        if (visual == null) return;

        DetachVisual();

        CurrentVisual = visual;
        CurrentVisualKey = key ?? "";

        visual.transform.SetParent(visualSlot, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one; // senin çözdüğün scale fix burada
        visual.SetActive(true);

        FitColliderToCurrentVisual();

    }


    /// <summary>
    /// Görseli taştan ayırır (pool'a geri verilecek)
    /// </summary>
    public GameObject DetachVisual()
    {
        if (CurrentVisual == null) return null;

        var v = CurrentVisual;
        CurrentVisual = null;
        CurrentVisualKey = "";

        v.transform.SetParent(null);
        v.SetActive(false);
        return v;
    }


    //private void OnMouseDown()
    //{
    //    Debug.Log("Tıklandı...." + TileId);
    //    GameController.Instance.OnTileClicked(this);
    //}
    void OnMouseDown()
    {
        // 1) Sahnedeki tek RackInteractionController'ı bul
        if (_rack == null)
            _rack = FindObjectOfType<RackInteractionController>();

        if (_rack == null)
        {
            Debug.LogError("[TileObj] RackInteractionController bulunamadı (GameController objesine component ekli mi?)");
            return;
        }

        // 2) Yetki kontrolü: sadece MAIN rackRoot altındaki taşlara izin
        if (!_rack.OwnsTile(this))
            return;

        _rack.BeginDrag(this);
    }

    void OnMouseDrag()
    {
        if (_rack == null) return;
        if (!_rack.OwnsTile(this)) return;

        _rack.DragMove();
    }

    void OnMouseUp()
    {
        if (_rack == null) return;
        if (!_rack.OwnsTile(this)) return;

        _rack.EndDrag();
    }



    //void Update()
    //{
    //    if (Input.GetMouseButtonDown(0))
    //    {
    //        var cam = Camera.main;
    //        Debug.Log("Camera.main = " + (cam ? cam.name : "NULL"));

    //        if (!cam) return;

    //        var ray = cam.ScreenPointToRay(Input.mousePosition);
    //        if (Physics.Raycast(ray, out var hit, 999f))
    //            Debug.Log("HIT: " + hit.collider.name);
    //        else
    //            Debug.Log("NO HIT");
    //    }
    //}





    public void FitColliderToCurrentVisual()
    {
        // VisualSlot boşsa çık
        if (visualSlot == null) return;

        // VisualSlot altında aktif visual'ı bul (V_K01_2 gibi)
        Renderer[] renderers = visualSlot.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) return;

        // Dünya uzayında birleşik bounds
        var wb = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            wb.Encapsulate(renderers[i].bounds);

        // TileObj local space'e çevir
        var bc = GetComponent<BoxCollider>();
        if (bc == null) bc = gameObject.AddComponent<BoxCollider>();

        Vector3 centerLocal = transform.InverseTransformPoint(wb.center);

        // extents -> local (lossyScale ile böl)
        Vector3 e = wb.extents;
        Vector3 s = transform.lossyScale;
        float sx = Mathf.Abs(s.x) < 1e-6f ? 1f : Mathf.Abs(s.x);
        float sy = Mathf.Abs(s.y) < 1e-6f ? 1f : Mathf.Abs(s.y);
        float sz = Mathf.Abs(s.z) < 1e-6f ? 1f : Mathf.Abs(s.z);

        Vector3 localExtents = new Vector3(e.x / sx, e.y / sy, e.z / sz);

        bc.center = centerLocal;
        bc.size = localExtents * 2f;
    }



}
