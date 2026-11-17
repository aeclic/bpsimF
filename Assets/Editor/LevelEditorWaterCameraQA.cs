#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Bpsim.EditorTools
{
    public static class LevelEditorWaterCameraQA
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Builds the production WaterPreview, tests its real Renderer bounds, then
        // submits its surface and a normal part-material marker through native URP.
        public static string Run()
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string mod = Path.Combine(root, "Mods", "LevelEditor");
            string web = Path.Combine(mod, "Web", "bpsim", "ultimate-water");
            string output = Path.Combine(root, "_codex_work", "water-comparison");
            Directory.CreateDirectory(output);
            Assembly assembly = Assembly.Load(File.ReadAllBytes(Path.Combine(mod, "BpsimLevelEditor_v050.dll")));
            Type waterType = assembly.GetType("Bpsim.LevelEditor.WaterPreview", true);
            Type assetsType = assembly.GetType("Bpsim.LevelEditor.WaterAssets", true);
            Type settingsType = assembly.GetType("Bpsim.LevelEditor.WaterSettings", true);
            var owned = new List<UnityEngine.Object>();
            var checks = new JArray();
            var report = new JObject { ["passed"] = false, ["checks"] = checks };
            Action<bool, string> check = (ok, name) => { if (!ok) throw new InvalidOperationException(name); checks.Add(name); };
            RenderTexture previous = RenderTexture.active;
            MonoBehaviour water = null;
            GameObject waterObject = null;
            try
            {
                // Load the same source profile and shader assets without taking
                // ownership of an AssetBundle already used by the running game.
                object assets = FormatterServices.GetUninitializedObject(assetsType);
                Set(assets, "m_dataTextures", new Dictionary<string, Texture2D>());
                Set(assets, "m_lighting", new Dictionary<string, Texture2D>());
                Set(assets, "m_surfaceGeometry", File.ReadAllBytes(Path.Combine(web, "surface-mesh.bin")));
                Set(assets, "m_surfaceInfo", JObject.Parse(File.ReadAllText(Path.Combine(web, "surface-mesh.json"))));
                Set(assets, "m_runtimeProfile", JObject.Parse(File.ReadAllText(Path.Combine(web, "runtime-profile.json"))));
                Auto(assets, "OriginalProfile", JObject.Parse(File.ReadAllText(Path.Combine(web, "demo-profile.json"))));
                Auto(assets, "Style", JObject.Parse(File.ReadAllText(Path.Combine(web, "style.json"))));
                foreach (var pair in new[] { new[] { "WaterShader", "Water" }, new[] { "MaskShader", "TerrainMask" },
                    new[] { "SurfaceShader", "UltimateWaterSurface" }, new[] { "UnderwaterShader", "UltimateUnderwater" } })
                {
                    Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/LevelEditorModAssets/" + pair[1] + ".shader");
                    check(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), pair[1] + " shader supported");
                    Auto(assets, pair[0], shader);
                }
                foreach (string name in new[] { "NormalTexture", "FoamTexture", "CausticTexture", "SurfaceFoamTexture" })
                    Auto(assets, name, Texture2D.whiteTexture);
                var body = new JObject { ["id"] = "camera-qa", ["name"] = "Camera QA", ["x"] = 0,
                    ["surface_y"] = 0, ["width"] = 2, ["depth"] = 2, ["opacity"] = 1,
                    ["wave_amplitude"] = 0, ["wave_speed"] = 0, ["color"] = "#00a9fd",
                    ["physics_enabled"] = false, ["ripples_enabled"] = false };
                object settings = settingsType.GetMethod("Read").Invoke(null, new object[] { body });
                waterObject = new GameObject("[Water camera QA]") { hideFlags = HideFlags.HideAndDontSave, layer = 31 };
                waterObject.transform.position = new Vector3(10000, 0, -.2f);
                water = (MonoBehaviour)waterObject.AddComponent(waterType);
                waterType.GetMethod("Build", Fields).Invoke(water, new[] { settings, (object)8f, assets, null });
                water.enabled = false;
                MeshRenderer underwater = (MeshRenderer)waterType.GetProperty("Renderer").GetValue(water);
                MeshRenderer surface = (MeshRenderer)waterType.GetProperty("SurfaceRenderer").GetValue(water);
                MeshRenderer meniscus = (MeshRenderer)waterType.GetProperty("MeniscusRenderer").GetValue(water);
                check(surface != null, "Production Build creates the original surface renderer");
                surface.gameObject.layer = 31;
                meniscus.gameObject.layer = 31;
                surface.enabled = true;
                underwater.enabled = false;
                check(surface.sortingLayerID == underwater.sortingLayerID && surface.sortingOrder == 0 && surface.sharedMaterial.renderQueue == 3000,
                    "Production surface shares transparent depth sorting with scenery and parts");
                var cameraObject = new GameObject("[Water camera QA camera]") { hideFlags = HideFlags.HideAndDontSave };
                owned.Add(cameraObject);
                Camera camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                camera.orthographic = true; camera.aspect = 1; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.05f, .05f, .05f, 1); camera.allowHDR = false; camera.allowMSAA = false;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                MethodInfo prepare = waterType.GetMethod("PrepareForCamera");
                float span = (float)assetsType.GetField("SurfaceProjectionSpan").GetRawConstantValue();
                foreach (float y in new[] { -128f, -16f, -4f, 4f, 16f, 128f })
                {
                    camera.transform.position = new Vector3(10008, y, -20);
                    prepare.Invoke(water, new object[] { camera });
                    float edge = Mathf.Clamp(y * span, -16, 16);
                    check(surface.bounds.Contains(new Vector3(10000, edge, .25f)) &&
                        surface.bounds.Contains(new Vector3(10016, 0, .25f)), "Fixed wave line and projected far edge survive CPU bounds at camera Y=" + y);
                    check(underwater.bounds.min.y <= -16 && underwater.bounds.max.y >= Mathf.Min(edge, 0),
                        "The underwater layer below the surface band survives CPU bounds at camera Y=" + y);
                }
                camera.transform.position = new Vector3(10008, 4, -20); camera.orthographicSize = 3;
                prepare.Invoke(water, new object[] { camera });
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
                bool bodyVisible = GeometryUtility.TestPlanesAABB(planes, underwater.bounds);
                bool surfaceVisible = GeometryUtility.TestPlanesAABB(planes, surface.bounds);
                check(!bodyVisible && surfaceVisible, "A surface-only view is visible even when the underwater volume is offscreen");
                report["surfaceOnly"] = new JObject { ["bodyVisible"] = bodyVisible, ["surfaceVisible"] = surfaceVisible };

                // The real surface shader uses neutral fixture captures/textures;
                // this probe checks rasterization and ordering, not visual parity.
                camera.transform.position = new Vector3(10008, 4, -20); camera.orthographicSize = 8;
                prepare.Invoke(water, new object[] { camera });
                Vector4 rect = new Vector4(10000, -4, 16, 16);
                Material waterMaterial = surface.sharedMaterial;
                waterMaterial.SetVector("uw_CaptureRect", rect);
                waterMaterial.SetTexture("uw_Terrain", Texture2D.blackTexture);
                waterMaterial.SetTexture("_ShapeLightTexture0", Texture2D.whiteTexture);
                waterMaterial.SetTexture("_CameraSortingLayerTexture", Texture2D.blackTexture);
                waterMaterial.SetTexture("_LocalUnderwaterSurfaceMap", Texture2D.blackTexture);
                waterMaterial.SetTexture("_LocalReflectionMap", Texture2D.blackTexture);
                waterMaterial.SetFloat("uw_Clock", 2f);
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Quad); owned.Add(marker);
                marker.name = "[Water QA part texture]"; marker.hideFlags = HideFlags.HideAndDontSave; marker.layer = 31;
                marker.transform.position = new Vector3(10008, 2, 0); marker.transform.localScale = Vector3.one;
                Shader partShader = Shader.Find("Bpsim/Unlit_Texture");
                check(partShader != null && partShader.isSupported, "Host part texture shader supported");
                Material markerMaterial = new Material(partShader); owned.Add(markerMaterial);
                markerMaterial.SetTexture("_MainTex", Texture2D.whiteTexture); markerMaterial.SetColor("_Color", Color.magenta);
                marker.GetComponent<MeshRenderer>().sharedMaterial = markerMaterial;
                // Imported scenery uses the same transparent queue/order as parts.
                // A clear-color-only fixture misses water sorted behind this quad.
                GameObject background = GameObject.CreatePrimitive(PrimitiveType.Quad); owned.Add(background);
                background.hideFlags = HideFlags.HideAndDontSave; background.layer = 31;
                background.transform.position = new Vector3(10008, 4, 14);
                background.transform.localScale = new Vector3(20, 20, 1);
                Material backgroundMaterial = new Material(partShader); owned.Add(backgroundMaterial);
                backgroundMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
                backgroundMaterial.SetColor("_Color", Color.red);
                background.GetComponent<MeshRenderer>().sharedMaterial = backgroundMaterial;
                var target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32); target.Create(); owned.Add(target);
                var image = new Texture2D(512, 512, TextureFormat.RGBA32, false); owned.Add(image);
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                check(RenderPipeline.SupportsRenderRequest(camera, request), "Native URP single-camera render request supported");
                marker.SetActive(false);
                RenderPipeline.SubmitRenderRequest(camera, request);
                Read(target, image);
                Color waterPixel = image.GetPixel(256, 192);
                check(waterPixel.g > .1f || waterPixel.b > .1f, "Surface stays visible in front of an opaque-alpha imported background");
                File.WriteAllBytes(Path.Combine(output, "unity-camera-surface.png"), image.EncodeToPNG());
                marker.SetActive(true);
                RenderPipeline.SubmitRenderRequest(camera, request);
                Read(target, image);
                Color markerPixel = image.GetPixel(256, 192);
                check(markerPixel.r > .8f && markerPixel.g < .15f && markerPixel.b > .8f,
                    "Native URP draws the part texture in front of the water surface");
                string screenshot = Path.Combine(output, "unity-camera-part-order.png");
                File.WriteAllBytes(screenshot, image.EncodeToPNG());
                report["screenshot"] = screenshot;
                report["waterPixel"] = JArray.FromObject(new[] { waterPixel.r, waterPixel.g, waterPixel.b });
                report["markerPixel"] = JArray.FromObject(new[] { markerPixel.r, markerPixel.g, markerPixel.b });
                camera.transform.position = new Vector3(10008, -4, -20);
                prepare.Invoke(water, new object[] { camera });
                Material line = meniscus.sharedMaterial;
                line.SetVector("uw_CaptureRect", new Vector4(10000, -12, 16, 16));
                line.SetTexture("uw_Terrain", Texture2D.blackTexture);
                line.SetTexture("_ShapeLightTexture0", Texture2D.whiteTexture);
                line.SetTexture("_CameraSortingLayerTexture", Texture2D.blackTexture);
                line.SetFloat("uw_Clock", 2);
                surface.enabled = false; marker.SetActive(false);
                RenderPipeline.SubmitRenderRequest(camera, request); Read(target, image);
                Color cyanPixel = image.GetPixel(256, 383);
                check(cyanPixel.g > .4f && cyanPixel.b > .4f,
                    "Underwater cyan meniscus renders at the fixed near bank");
                marker.transform.position = new Vector3(10008, 0, 0); marker.SetActive(true);
                RenderPipeline.SubmitRenderRequest(camera, request); Read(target, image);
                Color lineMarkerPixel = image.GetPixel(256, 383);
                check(lineMarkerPixel.r > .8f && lineMarkerPixel.g < .15f && lineMarkerPixel.b > .8f,
                    "Parts remain in front of the anchored cyan meniscus");
                report["meniscusPixel"] = JArray.FromObject(new[] { cyanPixel.r, cyanPixel.g, cyanPixel.b });
                report["passed"] = true;
            }
            catch (Exception ex) { report["error"] = (ex.InnerException ?? ex).ToString(); }
            finally
            {
                RenderTexture.active = previous;
                if (water != null)
                {
                    object ripples = waterType.GetField("m_ripples", Fields).GetValue(water);
                    if (ripples != null) foreach (FieldInfo field in ripples.GetType().GetFields(Fields))
                        if (field.GetValue(ripples) is UnityEngine.Object texture) owned.Add(texture);
                    Set(water, "m_ripples", null);
                    foreach (FieldInfo field in waterType.GetFields(Fields))
                        if (field.GetValue(water) is Mesh || field.GetValue(water) is Material || field.GetValue(water) is RenderTexture)
                        { owned.Add((UnityEngine.Object)field.GetValue(water)); field.SetValue(water, null); }
                }
                if (waterObject != null) UnityEngine.Object.DestroyImmediate(waterObject);
                foreach (UnityEngine.Object item in owned)
                { if (item is RenderTexture texture) texture.Release(); if (item != null) UnityEngine.Object.DestroyImmediate(item); }
            }
            string json = report.ToString(Formatting.Indented);
            File.WriteAllText(Path.Combine(output, "unity-camera-qa.json"), json);
            return json;
        }

        private static void Read(RenderTexture source, Texture2D image)
        { RenderTexture.active = source; image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); image.Apply(); }
        private static void Set(object instance, string field, object value)
        { instance.GetType().GetField(field, Fields).SetValue(instance, value); }
        private static void Auto(object instance, string property, object value)
        { Set(instance, "<" + property + ">k__BackingField", value); }
    }
}
#endif
