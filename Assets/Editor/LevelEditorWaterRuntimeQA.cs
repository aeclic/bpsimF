using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Bpsim.Modding;
using Bpsim.Mods;
using Bpsim.Parts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Bpsim.EditorTools
{
    // Preserve the user's active level and construction across the Edit Mode
    // required by BuildAssetBundles. Nothing is written to the level catalogue.
    public static class LevelEditorWaterRuntimeQA
    {
        private static string SessionPath => Path.Combine(Path.GetDirectoryName(Application.dataPath), "_codex_work", "water-comparison", "runtime-session.json");

        public static string SaveSession()
        {
            if (!Application.isPlaying || PartManager.Instance == null || PartManager.Instance.IsSimulating)
                throw new InvalidOperationException("Capture the water fixture in construction mode.");
            ModLevelDefinition level = ModLevels.CaptureCurrent();
            if (level == null) throw new InvalidOperationException("No loaded level to preserve.");
            // A level without a starter may have no LevelManager.Scene. Always
            // capture the active construction explicitly instead of trusting it.
            using (var stream = new MemoryStream())
            {
                PartManager.Instance.SaveScene(stream, PartManager.Instance.ActiveScene.SceneID, SchematicsFormat.Csv);
                level.Starter = Encoding.UTF8.GetString(stream.ToArray());
                level.StarterFormat = SchematicsFormat.Csv;
            }
            int count = PartManager.Instance.ActiveScene.Unmanaged.Parts.Length;
            int savedCount = level.Starter.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"));
            if (savedCount != count) throw new InvalidOperationException("The construction capture is incomplete; keep Play Mode running.");
            Camera camera = SceneCamera.Instance.Camera;
            JObject data = new JObject {
                ["level"] = JObject.FromObject(level),
                ["camera"] = new JArray(camera.transform.position.x, camera.transform.position.y, camera.orthographicSize),
                ["parts"] = count
            };
            File.WriteAllText(SessionPath, data.ToString(Formatting.Indented));
            return new JObject { ["path"] = SessionPath, ["parts"] = data["parts"], ["camera"] = data["camera"] }.ToString(Formatting.None);
        }

        public static string RestoreSession()
        {
            JObject data = JObject.Parse(File.ReadAllText(SessionPath));
            if ((int)data["parts"] > 0 && string.IsNullOrWhiteSpace((string)data["level"]["Starter"]))
                throw new InvalidOperationException("Session has no construction data; do not replace the current level.");
            ModPackage package = ModManager.Instance.Packages.First(p => p.Guid == new Guid("e47b018a-61a6-4e79-a789-c5f627a00aa4") && p.IsLoaded);
            string error;
            if (!ModLevels.Load(package.EntryPoint, data["level"].ToObject<ModLevelDefinition>(), out error))
                throw new InvalidOperationException(error);
            SceneCamera.Instance.MoveTo(new Vector2((float)data["camera"][0], (float)data["camera"][1]));
            SceneCamera.Instance.ScaleTo((float)data["camera"][2]);
            int parts = PartManager.Instance.ActiveScene.Unmanaged.Parts.Length;
            if (parts != (int)data["parts"]) throw new InvalidOperationException("Construction part count changed during reload.");
            return "Restored running water level, camera and " + parts + " parts.";
        }

        public static string Inspect()
        {
            Camera camera = SceneCamera.Instance.Camera;
            var bodies = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .Where(c => c != null && c.isActiveAndEnabled && c.GetType().Name == "WaterPreview");
            JArray report = new JArray();
            foreach (MonoBehaviour water in bodies)
            {
                var surface = (MeshRenderer)water.GetType().GetProperty("SurfaceRenderer").GetValue(water);
                report.Add(new JObject {
                    ["bankY"] = water.transform.position.y,
                    ["cameraY"] = camera.transform.position.y,
                    ["surfaceVisible"] = surface.isVisible,
                    ["surfaceOrder"] = surface.sortingOrder,
                    ["surfaceQueue"] = surface.sharedMaterial.renderQueue,
                    ["surfaceZ"] = surface.transform.position.z,
                    ["boundsMinY"] = surface.bounds.min.y, ["boundsMaxY"] = surface.bounds.max.y
                });
            }
            return report.ToString(Formatting.None);
        }

        public static string MoveCamera(float offsetFromBank)
        {
            MonoBehaviour water = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .First(c => c != null && c.isActiveAndEnabled && c.GetType().Name == "WaterPreview");
            SceneCamera.Instance.MoveTo(new Vector2(SceneCamera.Instance.Camera.transform.position.x, water.transform.position.y + offsetFromBank));
            return Inspect();
        }

        public static string RestoreCamera()
        {
            JObject data = JObject.Parse(File.ReadAllText(SessionPath));
            SceneCamera.Instance.MoveTo(new Vector2((float)data["camera"][0], (float)data["camera"][1]));
            SceneCamera.Instance.ScaleTo((float)data["camera"][2]);
            return Inspect();
        }

    }
}
