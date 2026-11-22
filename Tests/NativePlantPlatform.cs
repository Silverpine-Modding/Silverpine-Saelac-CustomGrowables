using System.Runtime.CompilerServices;
using UnityEngine;

// Minimal native call shapes for patch discovery/IL tests. Not Unity rendering
// or a replacement for checking the actual installed game's method bodies.
public class Crop : MonoBehaviour
{
    private bool mature;
    private string visibleName = "Turnip";
    public bool MatureValue => mature;
    public string DisplayName => visibleName;
    public void SetMature(bool value) => mature = value;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Mature() => mature = true;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void HarvestInto(Inventory inventory) { inventory.TryAddItem(new Item()); UnityEngine.Object.Destroy(gameObject); }
}
public enum ResourceNodeType { None, Herb, Ore }
public class ResourceNode : MonoBehaviour
{
    public Quality quality;
    public ResourceNodeType resourceNodeType;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AddItem(Inventory inventory, Quality toolQuality)
    {
        var item = new Item(); item.Components.Add(new QualityTestComponent { Quality = (Quality)Math.Min((int)quality, (int)toolQuality) });
        inventory.TryAddItem(item);
    }
}
public class InGameTimeCrop : MonoBehaviour
{
    public GameObject prefab = new("prefab_tree_oak");
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void OnPostProcessTurn()
    {
        UnityEngine.Object.Instantiate(prefab, transform.position, new Quaternion());
        UnityEngine.Object.Destroy(gameObject);
    }
}
public class ItemComponent_Seeds : ItemComponent
{
    public override void OnUse()
    {
        var prefab = new GameObject("prefab_crop_turnip"); prefab.AddComponent<Crop>();
        Player.Instance.StartLongInteraction(() =>
        {
            UnityEngine.Object.Instantiate(prefab, new Vector3(), new Quaternion());
            item.RemoveItemFromInventory();
        }, 5, null, () => { });
    }
}
