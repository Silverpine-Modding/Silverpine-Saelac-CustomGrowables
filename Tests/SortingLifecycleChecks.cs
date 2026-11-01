using System.Reflection;
using SilverpineMods.CustomGrowables;
using UnityEngine;

internal static class SortingLifecycleChecks
{
    private static void Invoke(CustomGrowable plant, string method) =>
        typeof(CustomGrowable).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(plant, null);

    internal static void Run(Action<bool, string> check, string file)
    {
        var definition = new GrowableDefinition
        {
            clock = "GameTurns", growthSeasons = new[] { "Spring", "Summer", "Autumn", "Winter" },
            scale = 2,
            stages = new()
            {
                new() { sprite = "seed", duration = 1, RuntimeSprite = new Sprite() },
                new() { sprite = "tree", scale = 3, offsetY = 2, RuntimeSprite = new Sprite(), light = GrowableLightDefinition.MorningDew() }
            },
            harvest = new() { new() { item = "Apple" } }
        };
        definition.Validate(file, "pack:sorting-lifecycle");
        definition.Template = new GameObject(); definition.Template.SetActive(false);
        definition.Template.transform.position = new Vector3(0, 0, -.4f);
        GrowableRegistry.Definitions[definition.QualifiedId] = definition;
        SerializationManager.loadingSave = false;

        CustomGrowable CreateInactive(float y)
        {
            var plant = new CustomGrowable { isActiveAndEnabled = false };
            plant.gameObject.SetActive(false);
            plant.transform.position = new Vector3(3, y, 0);
            plant.gameObject.AddComponent<SpriteRenderer>();
            plant.gameObject.AddComponent<YSorter>().offset = 7;
            plant.Configure(definition.QualifiedId);
            return plant;
        }

        foreach (bool sorterFirst in new[] { false, true })
        {
            ActionQueue.Instance = new();
            var plant = CreateInactive(12);
            var renderer = plant.GetComponent<SpriteRenderer>();
            var sorter = plant.GetComponent<YSorter>();
            check(!sorter.HasAwakened && renderer.sortingOrder == 0,
                "inactive plant setup does not require or initialize YSorter.Awake");
            plant.gameObject.SetActive(true); plant.isActiveAndEnabled = true;
            if (sorterFirst) sorter.Awake();
            Invoke(plant, "OnEnable");
            check(renderer.sortingOrder == -113 && sorter.HasAwakened == sorterFirst && ActionQueue.Instance.Subscribers == 1,
                "plant activation completes sorting and turn subscription when YSorter Awake runs " + (sorterFirst ? "first" : "later"));
            sorter.Awake(); sorter.SetSortingOrder(); Invoke(plant, "Start");
            check(renderer.sortingOrder == -113 && plant.transform.position.z == -.4f,
                "native sorting after Awake agrees with activation sorting and preserves tree depth");
            ActionQueue.Instance.Turn();
            check(renderer.sortingOrder == -113 && renderer.sprite == definition.stages[1].RuntimeSprite &&
                    plant.transform.localScale.y == 6 && ActionQueue.Instance.Subscribers == 0,
                "growth keeps planted-tile sorting despite stage art offsets and scale");
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) plant.Serialize(writer);
            stream.Position = 0;
            var restored = CreateInactive(-4);
            // Restore hooks or asynchronous art callbacks may run before the sorter's Awake too.
            restored.gameObject.SetActive(true); restored.isActiveAndEnabled = true;
            using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true)) restored.Deserialize(reader);
            restored.RestoreInstance(); restored.RefreshArt(definition.QualifiedId);
            check(!restored.GetComponent<YSorter>().HasAwakened && restored.GetComponent<SpriteRenderer>().sortingOrder == 47 &&
                    restored.GetComponent<SpriteRenderer>().sprite == definition.stages[1].RuntimeSprite,
                "save restoration and art refresh sort correctly before YSorter Awake");
            Invoke(plant, "OnDisable"); plant.gameObject.SetActive(false); plant.isActiveAndEnabled = false;
            plant.transform.position = new Vector3(3, -2, -.4f);
            plant.gameObject.SetActive(true); plant.isActiveAndEnabled = true; Invoke(plant, "OnEnable");
            check(renderer.sortingOrder == 27, "reenabling an existing plant updates sorting for its current tile");
            Invoke(plant, "OnDisable"); Invoke(restored, "OnDisable");
        }

        // Include half-way rounding and both sides of the world origin.
        var sample = CreateInactive(0);
        sample.gameObject.SetActive(true); sample.isActiveAndEnabled = true;
        var sampleSorter = sample.GetComponent<YSorter>(); sampleSorter.Awake();
        foreach (float y in new[] { -12f, -1.25f, -.05f, 0f, .05f, 1.25f, 12f })
        {
            sample.transform.position = new Vector3(3, y, -.4f);
            sample.RefreshArt(definition.QualifiedId);
            int actual = sample.GetComponent<SpriteRenderer>().sortingOrder;
            sampleSorter.SetSortingOrder();
            check(actual == sample.GetComponent<SpriteRenderer>().sortingOrder,
                "stage sorting matches native YSorter at Y=" + y);
        }
        Invoke(sample, "OnDisable");
        GrowableRegistry.Definitions.Remove(definition.QualifiedId);
    }
}
