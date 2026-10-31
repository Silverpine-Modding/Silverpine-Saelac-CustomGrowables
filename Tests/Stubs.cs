namespace UnityEngine
{
    public class Sprite { }
    public class Texture2D { }
    public class GameObject
    {
        public Transform transform = new();
        public HideFlags hideFlags;
        public bool activeInHierarchy = true;
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new(1, 1, 1);
        public static Vector3 operator *(Vector3 v, float s) => new(v.x * s, v.y * s, v.z * s);
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
