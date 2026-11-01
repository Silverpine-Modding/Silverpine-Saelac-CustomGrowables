// Minimal deterministic host for exercising the production CustomGrowable component.
// Rendering, Unity lifecycle dispatch and biome generation still require in-game checks.
using UnityEngine;

namespace UnityEngine
{
    public sealed class SerializeField : Attribute { }
    public enum HideFlags { None }
    public class Object { public static void Destroy(object value) { if (value is GameObject gameObject) gameObject.Destroyed = true; } }
    public class MonoBehaviour
    {
        public GameObject gameObject = new();
        public Transform transform => gameObject.transform;
        public bool isActiveAndEnabled = true;
        public T GetComponent<T>() where T : new() => gameObject.GetComponent<T>();
    }
    public class Transform
    {
        public Vector3 position, localScale = Vector3.one;
        public Transform? parent;
        public Vector3 lossyScale => parent == null ? localScale : new(
            parent.lossyScale.x * localScale.x, parent.lossyScale.y * localScale.y, parent.lossyScale.z * localScale.z);
        public void SetParent(Transform value, bool worldPositionStays) => parent = value;
    }
    public struct Vector2Int
    {
        public int x, y;
        public static bool operator ==(Vector2Int a, Vector2Int b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2Int a, Vector2Int b) => !(a == b);
        public override bool Equals(object? o) => o is Vector2Int v && this == v;
        public override int GetHashCode() => HashCode.Combine(x, y);
    }
    public class SpriteRenderer { public Sprite? sprite; public int sortingOrder; }
    public static class Time { public static float fixedUnscaledDeltaTime = .02f; }
    public static class Random
    {
        private static float sample;
        public static readonly Queue<float> Samples = new();
        public static int Reads;
        public static float value
        {
            get { Reads++; return Samples.Count > 0 ? Samples.Dequeue() : sample; }
            set { sample = value; Samples.Clear(); Reads = 0; }
        }
        public static int Range(int minimum, int maximum) => minimum;
    }
}

public interface ISerializableMonoBehavior { }
public interface IInteractionHandler { }
public interface INPCVisibleObject { }
public interface INPCInteractable { }
public class LightFlickerer { }
public enum Quality { Normal, Quality, Superior, Exceptional, Pristine, Blessed, Legendary }
public interface IQualityItemComponent { Quality Quality { get; set; } }
public class QualityTestComponent : ItemComponent, IQualityItemComponent { public Quality Quality { get; set; } }
// Native YSorter does not cache its renderer until Awake. Keep this strict:
// a no-op SetSortingOrder hides cross-component activation-order failures.
public class YSorter : MonoBehaviour
{
    private SpriteRenderer? spriteRenderer;
    public int offset;
    public bool HasAwakened => spriteRenderer != null;
    public void Awake() => spriteRenderer = GetComponent<SpriteRenderer>();
    public void SetSortingOrder(int additionalOffset = 0) =>
        spriteRenderer!.sortingOrder = Mathf.RoundToInt((0f - transform.position.y) * 10f) + offset + additionalOffset;
}
public static class SerializationManager { public static bool loadingSave; }
public enum Season { Spring, Summer, Autumn, Winter }
public class WorldInfoManager
{
    public static WorldInfoManager? Instance;
    public Season currentSeason;
    public Season? LocalSeason;
    public bool Owned = true;
    public bool IsOnPlayerOwnedPlot(Vector2Int p) => Owned;
    public Season GetSeasonFor(GameObject plant) => LocalSeason ?? currentSeason;
}
public class DungeonGenerationManager { public static DungeonGenerationManager? Instance; }
public class ActionQueue
{
    public static ActionQueue? Instance;
    public event Action? OnPostProcessTurn;
    public int Subscribers => OnPostProcessTurn?.GetInvocationList().Length ?? 0;
    public void Turn() => OnPostProcessTurn?.Invoke();
}
public static class GameExtensions
{
    public static Vector2Int GetVector2IntPosition(this Transform t) => new() { x = (int)t.position.x, y = (int)t.position.y };
    public static bool ContainsAnyWordIgnoreCase(this string text, params string[] words) => true;
}
public class Item
{
    public readonly List<ItemComponent> Components = new();
    public List<T> GetItemComponents<T>() => Components.OfType<T>().ToList();
    public bool TryGetItemComponent<T>(out T result) { result = Components.OfType<T>().FirstOrDefault()!; return result != null; }
    public Item SetQuality(Quality quality) { foreach (var component in Components.OfType<IQualityItemComponent>()) component.Quality = quality; return this; }
    public Inventory inventory = null!;
    public void RemoveItemFromInventory() => inventory.Items.Remove(this);
}
public class ItemComponent
{
    public Item item = null!;
    public virtual void Serialize(BinaryWriter writer) { }
    public virtual void Deserialize(BinaryReader reader) { }
    public virtual string GetDescriptionSuffix() => "";
    public virtual void OnUse() { }
}
public class ItemComponent_Sticky : ItemComponent { }
public class ActiveSeedTestComponent : ItemComponent { public override void OnUse() { } }
public class InheritedSeedTestComponent : ActiveSeedTestComponent { }
public class PassiveSeedTestComponent : ItemComponent { }
public enum TileTag { Roofed }
public static class TileTags { public static bool Roofed; public static bool HasTileTag(Vector2Int position, TileTag tag) => Roofed; }
public class UpperNotificationUI { public static UpperNotificationUI Instance = new(); public void OneOff(string text) { } }
public class Inventory
{
    public GameObject owner = new();
    public readonly List<Item> Items = new();
    public bool Full, ThrowBeforeAdd, ThrowAfterAdd;
    public Action? Added;
    public bool ContainsItem(Item item) => Items.Contains(item);
    public bool TryAddItem(Item item)
    {
        if (Full) return false;
        if (ThrowBeforeAdd) throw new Exception("Before insertion");
        Items.Add(item); item.inventory = this; Added?.Invoke();
        if (ThrowAfterAdd) throw new Exception("After insertion");
        return true;
    }
}
public class WorldInventory { public Inventory inventory = new(); }
public class NeuralNPC
{
    public string GetFinalName() => "NPC";
    public T GetComponent<T>() where T : new() => new();
}
public class Player : MonoBehaviour
{
    public static Player Instance = new();
    public WorldInventory worldInventory = new();
    public Action? Success, Finished;
    public int Starts;
    public void StartLongInteraction(Action action, int turns, object? unused, Action finish)
    { Success = action; Finished = finish; Starts++; }
}
public class AudioPlayer
{
    public static AudioPlayer Instance = new();
    public void PlayLoopAtPosition(string sound, float volume, Vector2Int position) { }
    public void StopLoop(Vector2Int position) { }
}
public class DialogOption { public DialogOption(string name, Action? callback) { } }
public class DialogBox
{
    public static DialogBox Instance = new();
    public void DisplayTextNoDialog(string text, params DialogOption[] options) { }
}
public class AnimationHelper
{
    public static AnimationHelper Instance = new();
    public bool ThrowEffects;
    public void SpawnFadeDummy(GameObject target) { if (ThrowEffects) throw new Exception("Effect failure"); }
    public void SpawnItemFloatingText(Item item, Transform owner) { if (ThrowEffects) throw new Exception("Effect failure"); }
}
public static class LootDropper
{
    public static readonly List<Item> Dropped = new();
    public static void DropLoot(List<Item> items, Vector2Int position) => Dropped.AddRange(items);
}

// The same bool/float/int/string field types and wire tags used by VersionSafeStorage.
public class VersionSafeStorage
{
    private readonly Dictionary<string, object> values = new();
    public void AddToStorage(string name, object value) => values.Add(name, value);
    public string GetString(string name, string fallback) => values.TryGetValue(name, out var value) ? (string)value : fallback;
    public float GetFloat(string name, float fallback) => values.TryGetValue(name, out var value) ? (float)value : fallback;
    public int GetInt(string name, int fallback) => values.TryGetValue(name, out var value) ? (int)value : fallback;
    public bool GetBool(string name, bool fallback) => values.TryGetValue(name, out var value) ? (bool)value : fallback;
    public void Serialize(BinaryWriter writer)
    {
        writer.Write((byte)values.Count);
        foreach (var field in values)
        {
            writer.Write(field.Key);
            switch (field.Value)
            {
                case bool b: writer.Write((byte)0); writer.Write(b); break;
                case float f: writer.Write((byte)1); writer.Write(f); break;
                case int i: writer.Write((byte)2); writer.Write(i); break;
                case string s: writer.Write((byte)3); writer.Write(s); break;
                default: throw new NotSupportedException();
            }
        }
    }
    public void Deserialize(BinaryReader reader)
    {
        int count = reader.ReadByte();
        for (int i = 0; i < count; i++)
        {
            string name = reader.ReadString();
            values.Add(name, reader.ReadByte() switch
            {
                0 => (object)reader.ReadBoolean(), 1 => reader.ReadSingle(),
                2 => reader.ReadInt32(), 3 => reader.ReadString(), _ => throw new NotSupportedException()
            });
        }
    }
}

namespace SilverpineMods.CustomGrowables
{
    internal static class GrowableRegistry
    {
        internal static readonly Dictionary<string, GrowableDefinition> Definitions = new();
        internal static int Lookups;
        internal static int Planted;
        internal static bool FailPlant;
        internal static Quality LastPlantedQuality;
        internal static GameObject Plant(GrowableDefinition definition, Vector2Int position, Quality quality = Quality.Normal)
        { if (FailPlant) throw new Exception("Plant allocation failed"); Planted++; LastPlantedQuality = quality; return new(); }
        internal static bool TryGet(string id, out GrowableDefinition definition)
        { Lookups++; return Definitions.TryGetValue(id, out definition!); }
        internal static string? FailItem;
        internal static Func<string, Item>? ItemFactory;
        internal static bool TryCreateItem(string id, out Item item) { item = ItemFactory?.Invoke(id) ?? new(); return id != FailItem; }
    }
    public static class Plugin { public static Logger Log = new(); }
    public class Logger
    {
        public readonly List<string> Errors = new();
        public void LogError(string error) => Errors.Add(error);
        public void LogWarning(string message) { }
    }
}
