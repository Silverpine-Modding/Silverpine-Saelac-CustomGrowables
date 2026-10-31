namespace UnityEngine
{
    public class Sprite { }
    public class Texture2D { }
    public class GameObject
    {
        public string name;
        public GameObject(string name = "") { this.name = name; }
        public Transform transform = new();
        public HideFlags hideFlags;
        public bool activeInHierarchy = true;
        public bool Destroyed;
        public readonly Dictionary<Type, object> Components = new();
        public void SetActive(bool value) => activeInHierarchy = value;
        public T AddComponent<T>() where T : new()
        {
            var component = new T();
            Components[typeof(T)] = component;
            if (component is Light light) { light.gameObject = this; Light.Created.Add(light); }
            return component;
        }
        public T GetComponent<T>() where T : new() => Components.TryGetValue(typeof(T), out var component) ? (T)component : AddComponent<T>();
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new(1, 1, 1);
        public static Vector3 operator *(Vector3 v, float s) => new(v.x * s, v.y * s, v.z * s);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
    public enum LightType { Point }
    public enum LightShadows { None }
    public class Light
    {
        public static readonly List<Light> Created = new();
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public LightType type;
        public LightShadows shadows;
        public Color color;
        public float range, intensity;
    }
    public static class Mathf
    {
        public static float Round(float x) => (float)System.Math.Round(x);
        public static int Clamp(int v, int min, int max) => System.Math.Clamp(v, min, max);
    }
}
namespace SilverpineMods.CustomItemLoader
{
    public static class CustomItemApi
    {
        public static string ResolveGlbModelSourcePath(string folder, string model, bool requireExists) => System.IO.Path.Combine(folder, model);
        public static string GetGlbSpriteCachePath(string folder, string pack, string item, string suffix) => System.IO.Path.Combine(folder, suffix + ".png");
    }
    public static class ItemPackLoader
    {
        public static string FindReadableGlbCachePath(string folder, string pack, string item, string suffix) => CustomItemApi.GetGlbSpriteCachePath(folder, pack, item, suffix);
    }
}
namespace Silverpine.ModdingTools
{
    public static class Plugin { public static readonly Logger Log = new(); }
    public class Logger { public void LogWarning(string message) { } }
}
