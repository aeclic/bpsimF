using System;
using System.Collections.Generic;
using System.Text;
using Bpsim.Parts;
using UnityEngine;

namespace Bpsim.Levels
{
    /// <summary>
    /// Small public surface used by the editor bridge (DshBridge.invoke_static) and by
    /// hand-written smoke tests. Everything here is a thin wrapper over LevelCatalog /
    /// LevelManager so that verification does not need a UI.
    /// </summary>
    public static class LevelQa
    {
        public static string Info()
        {
            if (!LevelCatalog.IsAvailable)
            {
                return "unavailable: " + LevelCatalog.Root;
            }

            List<LevelEntryJson> levels = LevelCatalog.Levels;
            return string.Format("root={0}\nlevels={1}\nfirst={2}\nlast={3}",
                LevelCatalog.Root,
                levels.Count,
                levels.Count > 0 ? levels[0].scene + " (" + levels[0].title + ")" : "-",
                levels.Count > 0 ? levels[levels.Count - 1].scene : "-");
        }

        public static string ListLevels(int max)
        {
            if (!LevelCatalog.IsAvailable)
            {
                return "unavailable";
            }

            List<LevelEntryJson> levels = LevelCatalog.Levels;
            StringBuilder sb = new StringBuilder();
            int count = Mathf.Min(max, levels.Count);
            for (int i = 0; i < count; i++)
            {
                LevelEntryJson level = levels[i];
                sb.Append(i).Append('\t')
                    .Append(level.episode).Append('\t')
                    .Append(level.scene).Append('\t')
                    .Append(level.title).Append('\t')
                    .Append(level.contraption.Length > 0 ? "contraption" : "no-contraption")
                    .Append('\n');
            }

            return sb.ToString();
        }

        public static string Find(string scene)
        {
            if (!LevelCatalog.IsAvailable)
            {
                return "unavailable";
            }

            LevelEntryJson entry = LevelCatalog.FindLevel(scene);
            if (entry == null)
            {
                return "not found: " + scene;
            }

            return entry.scene + " | " + entry.title + " | data=" + entry.data
                   + " | contraption=" + entry.contraption
                   + " | prefabs=" + entry.prefabs.Length
                   + " | references=" + entry.references.Length;
        }

        public static string Load(string scene)
        {
            if (!LevelCatalog.IsAvailable)
            {
                return "unavailable";
            }

            LevelEntryJson entry = LevelCatalog.FindLevel(scene);
            if (entry == null)
            {
                return "not found: " + scene;
            }

            return LoadEntry(entry);
        }

        public static string LoadIndex(int index)
        {
            if (!LevelCatalog.IsAvailable)
            {
                return "unavailable";
            }

            List<LevelEntryJson> levels = LevelCatalog.Levels;
            if (index < 0 || index >= levels.Count)
            {
                return "index out of range: " + index;
            }

            return LoadEntry(levels[index]);
        }

        public static string Unload()
        {
            LevelManager manager = LevelManager.Instance;
            if (manager == null)
            {
                return "no LevelManager";
            }

            manager.Unload();
            return "unloaded";
        }

        public static string Stats()
        {
            LevelManager manager = LevelManager.Instance;
            if (manager == null)
            {
                return "no LevelManager";
            }

            if (!manager.HasLevel)
            {
                return "no level loaded";
            }

            LevelImportResult result = manager.Current;
            StringBuilder sb = new StringBuilder();
            sb.Append("scene=").Append(manager.Entry.scene).Append('\n');
            sb.Append("title=").Append(manager.Entry.title).Append('\n');
            sb.Append("terrains=").Append(result.TerrainCount).Append('\n');
            sb.Append("props=").Append(result.PropCount).Append('\n');
            sb.Append("colliders=").Append(result.Colliders.Count).Append('\n');
            sb.Append("start=").Append(result.Start.x.ToString("0.##")).Append(',')
                .Append(result.Start.y.ToString("0.##")).Append('\n');
            if (result.HasTerrainBounds)
            {
                Bounds b = result.TerrainBounds;
                sb.Append("bounds.center=").Append(b.center.x.ToString("0.##")).Append(',')
                    .Append(b.center.y.ToString("0.##")).Append('\n');
                sb.Append("bounds.size=").Append(b.size.x.ToString("0.##")).Append(',')
                    .Append(b.size.y.ToString("0.##")).Append('\n');
            }

            PartScene scene = manager.Scene;
            sb.Append("partScene=").Append(scene != null ? scene.SceneID.ToString() : "none").Append('\n');
            if (scene != null)
            {
                sb.Append("parts=").Append(scene.Unmanaged.Parts.Length).Append('\n');
                sb.Append("camera=").Append(scene.State.CameraPosition.x.ToString("0.##")).Append(',')
                    .Append(scene.State.CameraPosition.y.ToString("0.##")).Append(" size=")
                    .Append(scene.State.CameraSize.ToString("0.##")).Append('\n');
            }

            if (result.Root != null)
            {
                Transform root = result.Root.transform;
                sb.Append("renderers=").Append(root.GetComponentsInChildren<Renderer>(true).Length).Append('\n');
                sb.Append("meshes=").Append(root.GetComponentsInChildren<MeshFilter>(true).Length).Append('\n');
            }

            return sb.ToString();
        }

        public static string Dump()
        {
            LevelManager manager = LevelManager.Instance;
            if (manager == null || !manager.HasLevel)
            {
                return "no level loaded";
            }

            StringBuilder sb = new StringBuilder();
            Walk(manager.Current.Root.transform, 0, sb);
            return sb.ToString();
        }

        static void Walk(Transform t, int depth, StringBuilder sb)
        {
            MeshFilter filter = t.GetComponent<MeshFilter>();
            MeshRenderer renderer = t.GetComponent<MeshRenderer>();
            sb.Append(' ', depth * 2).Append(t.name)
                .Append(" @").Append(t.position.x.ToString("0.##")).Append(',')
                .Append(t.position.y.ToString("0.##")).Append(',')
                .Append(t.position.z.ToString("0.##"));
            if (filter != null && filter.sharedMesh != null)
            {
                Mesh mesh = filter.sharedMesh;
                sb.Append(" mesh=").Append(mesh.vertexCount).Append('v')
                    .Append(mesh.triangles.Length / 3).Append('t')
                    .Append(" size=").Append(mesh.bounds.size.x.ToString("0.##")).Append('x')
                    .Append(mesh.bounds.size.y.ToString("0.##"));
                if (mesh.colors != null && mesh.colors.Length > 0)
                {
                    sb.Append(" colors");
                }
            }

            if (renderer != null && renderer.sharedMaterial != null)
            {
                Material mat = renderer.sharedMaterial;
                Texture tex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                Texture splat0 = mat.HasProperty("_Splat0") ? mat.GetTexture("_Splat0") : null;
                Texture splat1 = mat.HasProperty("_Splat1") ? mat.GetTexture("_Splat1") : null;
                Texture control = mat.HasProperty("_Control") ? mat.GetTexture("_Control") : null;
                sb.Append(" shader=").Append(mat.shader != null ? mat.shader.name : "null");
                if (tex != null) sb.Append(" tex=").Append(tex.name).Append('(').Append(tex.width).Append('x').Append(tex.height).Append(')');
                if (splat0 != null) sb.Append(" splat0=").Append(splat0.name);
                if (splat1 != null) sb.Append(" splat1=").Append(splat1.name);
                if (control != null) sb.Append(" control=").Append(control.width);
            }

            sb.Append('\n');
            if (depth >= 3)
            {
                return;
            }

            for (int i = 0; i < t.childCount; i++)
            {
                Walk(t.GetChild(i), depth + 1, sb);
            }
        }

        public static string WorldBounds()
        {
            LevelManager manager = LevelManager.Instance;
            if (manager == null || !manager.HasLevel)
            {
                return "no level loaded";
            }

            StringBuilder sb = new StringBuilder();
            Renderer[] renderers = manager.Current.Root.GetComponentsInChildren<Renderer>(true);
            Bounds union = default(Bounds);
            bool first = true;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Bounds b = renderer.bounds;
                if (first)
                {
                    union = b;
                    first = false;
                }
                else
                {
                    union.Encapsulate(b);
                }

                if (i < 12)
                {
                    sb.Append(renderer.name).Append(" z=").Append(renderer.transform.position.z.ToString("0.##"))
                        .Append(" scale=").Append(renderer.transform.lossyScale.x.ToString("0.##"))
                        .Append(" centre=").Append(b.center.x.ToString("0.#")).Append(',')
                        .Append(b.center.y.ToString("0.#"))
                        .Append(" size=").Append(b.size.x.ToString("0.#")).Append('x')
                        .Append(b.size.y.ToString("0.#"))
                        .Append('\n');
                }
            }

            sb.Append("union centre=").Append(union.center.x.ToString("0.#")).Append(',')
                .Append(union.center.y.ToString("0.#"))
                .Append(" size=").Append(union.size.x.ToString("0.#")).Append('x')
                .Append(union.size.y.ToString("0.#")).Append('\n');
            Camera cam = SceneCamera.Instance != null ? SceneCamera.Instance.Camera : null;
            if (cam != null)
            {
                sb.Append("camera at ").Append(cam.transform.position.x.ToString("0.#")).Append(',')
                    .Append(cam.transform.position.y.ToString("0.#")).Append(',')
                    .Append(cam.transform.position.z.ToString("0.#"))
                    .Append(" size=").Append(cam.orthographicSize.ToString("0.#"))
                    .Append(" near=").Append(cam.nearClipPlane.ToString("0.#"))
                    .Append(" far=").Append(cam.farClipPlane.ToString("0.#"))
                    .Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>
        /// Reports the world-space AABB of every terrain collider that was baked into the
        /// physics world, so a test can confirm the slabs actually straddle the part plane
        /// (z = 0) instead of floating behind it at <see cref="LevelImporter.DepthOffset"/>.
        /// </summary>
        public static string ColliderBounds()
        {
            LevelManager manager = LevelManager.Instance;
            if (manager == null || !manager.HasLevel)
            {
                return "no level loaded";
            }

            LevelImportResult result = manager.Current;
            StringBuilder sb = new StringBuilder();
            sb.Append("colliders=").Append(result.Colliders.Count).Append('\n');
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;
            for (int i = 0; i < result.Colliders.Count; i++)
            {
                // Must stay a ref: a Collider is only the header, the shape data follows it
                // inside the blob, so copying the struct would read past the end of the copy.
                ref Unity.Physics.Collider collider = ref result.Colliders[i].Value;
                Unity.Physics.Aabb aabb = collider.CalculateAabb();
                sb.Append(i)
                    .Append(" z=").Append(aabb.Min.z.ToString("0.##")).Append("..").Append(aabb.Max.z.ToString("0.##"))
                    .Append(" x=").Append(aabb.Min.x.ToString("0.#")).Append("..").Append(aabb.Max.x.ToString("0.#"))
                    .Append(" y=").Append(aabb.Min.y.ToString("0.#")).Append("..").Append(aabb.Max.y.ToString("0.#"))
                    .Append(" type=").Append(collider.CollisionType)
                    .Append('\n');
                if (aabb.Min.z < minZ) minZ = aabb.Min.z;
                if (aabb.Max.z > maxZ) maxZ = aabb.Max.z;
            }

            sb.Append("union z=").Append(minZ.ToString("0.##")).Append("..").Append(maxZ.ToString("0.##")).Append('\n');
            sb.Append("straddles-part-plane=").Append(minZ <= 0f && maxZ >= 0f ? "yes" : "NO").Append('\n');
            return sb.ToString();
        }

        /// <summary>
        /// Casts a straight down ray through the part plane (z = 0) at the given XY and
        /// reports whether it reaches a terrain collider. A part living at z = 0 must be
        /// able to touch the terrain, so this is the end-to-end proof that the extruded
        /// slab really covers the plane instead of hovering behind it.
        /// </summary>
        public static string RaycastTerrain(float x, float y)
        {
            LevelManager manager = LevelManager.Instance;
            if (manager == null || !manager.HasLevel)
            {
                return "no level loaded";
            }

            LevelImportResult result = manager.Current;
            StringBuilder sb = new StringBuilder();
            int hits = 0;
            for (int i = 0; i < result.Colliders.Count; i++)
            {
                ref Unity.Physics.Collider collider = ref result.Colliders[i].Value;
                Unity.Physics.RaycastInput input = new Unity.Physics.RaycastInput
                {
                    Start = new Unity.Mathematics.float3(x, y + 200f, 0f),
                    End = new Unity.Mathematics.float3(x, y - 200f, 0f),
                    Filter = Unity.Physics.CollisionFilter.Default
                };
                if (collider.CastRay(input, out Unity.Physics.RaycastHit hit))
                {
                    hits++;
                    sb.Append("hit[").Append(i).Append("] at y=").Append(hit.Position.y.ToString("0.##"))
                        .Append(" z=").Append(hit.Position.z.ToString("0.##")).Append('\n');
                }
            }

            sb.Append("ray (").Append(x.ToString("0.#")).Append(',').Append(y.ToString("0.#")).Append(") z=0 hits=").Append(hits);
            return sb.ToString();
        }

        private static Unity.Entities.Entity s_dropBody;

        private static bool s_hasDropBody;

        private static Unity.Entities.BlobAssetReference<Unity.Physics.Collider> s_dropCollider;

        /// <summary>
        /// Drops a dynamic sphere into the live physics world at the given XY, high above
        /// the terrain. Combined with <see cref="DropReport"/> this proves that a body
        /// living at z = 0 is actually stopped by the terrain instead of falling through.
        /// </summary>
        public static string DropProbe(float x, float y)
        {
            Unity.Entities.World world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return "no physics world";
            }

            Unity.Entities.EntityManager entityManager = world.EntityManager;
            if (s_hasDropBody && entityManager.Exists(s_dropBody))
            {
                entityManager.DestroyEntity(s_dropBody);
            }
            s_hasDropBody = false;
            if (s_dropCollider.IsCreated)
            {
                s_dropCollider.Dispose();
            }

            s_dropCollider = Bpsim.Parts.Simulation.PartPhysics.CreateSphere(Unity.Mathematics.float3.zero, 1f);
            s_dropBody = entityManager.CreateEntity();
            entityManager.AddComponentData(s_dropBody, new Unity.Transforms.LocalTransform
            {
                Position = new Unity.Mathematics.float3(x, y + 60f, 0f),
                Rotation = Unity.Mathematics.quaternion.identity,
                Scale = 1f
            });
            entityManager.AddComponentData(s_dropBody, new Unity.Transforms.LocalToWorld
            {
                Value = Unity.Mathematics.float4x4.identity
            });
            entityManager.AddComponentData(s_dropBody, new Unity.Physics.PhysicsCollider
            {
                Value = s_dropCollider
            });
            entityManager.AddSharedComponent(s_dropBody, new Unity.Physics.PhysicsWorldIndex(0u));
            entityManager.AddComponentData(s_dropBody, new Unity.Physics.PhysicsGravityFactor
            {
                Value = 1f
            });
            Bpsim.Parts.Simulation.PartPhysics.AddRigidbody(entityManager, s_dropBody, false,
                s_dropCollider.Value.MassProperties, 1f, 0f, 0f);
            s_hasDropBody = true;
            return string.Format("dropped at {0:0.#},{1:0.#} z=0 start y={2:0.#}", x, y, y + 60f);
        }

        /// <summary>
        /// Reports where the body from <see cref="DropProbe"/> is now, next to the terrain
        /// surface under it. A resting body means the terrain collider really is solid.
        /// </summary>
        public static string DropReport()
        {
            Unity.Entities.World world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || !s_hasDropBody || !world.EntityManager.Exists(s_dropBody))
            {
                return "no dropped body";
            }

            Unity.Transforms.LocalTransform transform = world.EntityManager.GetComponentData<Unity.Transforms.LocalTransform>(s_dropBody);
            Unity.Physics.PhysicsVelocity velocity = world.EntityManager.GetComponentData<Unity.Physics.PhysicsVelocity>(s_dropBody);
            StringBuilder sb = new StringBuilder();
            sb.Append("body y=").Append(transform.Position.y.ToString("0.##"))
                .Append(" x=").Append(transform.Position.x.ToString("0.##"))
                .Append(" z=").Append(transform.Position.z.ToString("0.##"))
                .Append(" v=").Append(velocity.Linear.y.ToString("0.##")).Append('\n');
            sb.Append(RaycastTerrain(transform.Position.x, transform.Position.y + 4f));
            return sb.ToString();
        }

        /// <summary>Dumps what the session restore would replay on the next launch.</summary>
        public static string Session()        {
            return string.Format("level={0}\nscene={1}\nformat={2}\nautoRestore={3}",
                string.IsNullOrEmpty(LevelSession.LevelScene) ? "-" : LevelSession.LevelScene,
                string.IsNullOrEmpty(LevelSession.ScenePath) ? "-" : LevelSession.ScenePath,
                LevelSession.SceneFormat,
                LevelSession.AutoRestore);
        }

        /// <summary>
        /// Loads a schematics file through the same code path the 加载场景 dialog uses,
        /// including recording it for the session restore.
        /// </summary>
        public static string LoadSceneFile(string path)
        {
            try
            {
                Bpsim.UI.SceneLoader.LoadFromPath(path, Bpsim.Parts.SchematicsFormat.Xml);
                return Session();
            }
            catch (Exception exception)
            {
                return "failed: " + exception.Message;
            }
        }

        /// <summary>Runs the session restore now (the bootstrap already does this once).</summary>
        public static string RestoreSession()
        {
            return LevelSession.Restore();
        }

        /// <summary>Clears the recorded session so the next launch starts empty.</summary>
        public static string ForgetSession()
        {
            LevelSession.Forget();
            return Session();
        }

        public static string Capture(int width, int height)
        {
            SceneCamera sceneCamera = SceneCamera.Instance;
            if (sceneCamera == null || sceneCamera.Camera == null)
            {
                return "no camera";
            }

            Camera cam = sceneCamera.Camera;
            RenderTexture previousTarget = cam.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            tex.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            RenderTexture.active = previousActive;
            cam.targetTexture = previousTarget;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(tex);

            string dir = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "DshBridge");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "level_capture.png");
            System.IO.File.WriteAllBytes(path, png);
            return path;
        }

        static string LoadEntry(LevelEntryJson entry)
        {
            LevelManager manager = LevelManager.Instance;
            if (manager == null)
            {
                return "no LevelManager (LevelBootstrap did not run)";
            }

            string error;
            if (!manager.Load(entry, out error))
            {
                return "failed: " + error;
            }

            return Stats();
        }
    }
}
