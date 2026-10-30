using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bpsim.Parts;
using Bpsim.Parts.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.QA
{
    /// <summary>
    /// 打包产物自检：只有在命令行带 <c>-bpsimSelfTest</c> 时才启动，跑「放置部件 → 开始模拟 →
    /// 读模拟实体位置」这一条最小闭环，把结果写进日志文件后退出。
    /// <para>
    /// 为什么需要它：编辑器里 Havok 系统没有 Burst 编译（走托管回退），只测编辑器无法判断
    /// Havok 后端在真实 Player（Burst AOT）里是否正常。用法：
    /// <code>
    /// bpsim.exe -batchmode -nographics -bpsimSelfTest -bpsimSelfTestLog "D:/out/selftest.log"
    /// </code>
    /// 退出码 0 = 通过，1 = 失败。
    /// </para>
    /// </summary>
    public sealed class PlayerSelfTest : MonoBehaviour
    {
        private const string FlagArg = "-bpsimSelfTest";
        private const string LogArg = "-bpsimSelfTestLog";

        private static PlayerSelfTest s_instance;
        private static readonly StringBuilder s_report = new StringBuilder();
        private static readonly List<string> s_errors = new List<string>();
        private static int s_errorCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool enabled = false;
            foreach (string a in args)
            {
                if (string.Equals(a, FlagArg, StringComparison.OrdinalIgnoreCase))
                {
                    enabled = true;
                    break;
                }
            }
            if (!enabled || s_instance != null)
            {
                return;
            }
            GameObject go = new GameObject("[PlayerSelfTest]");
            go.hideFlags = HideFlags.DontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            s_instance = go.AddComponent<PlayerSelfTest>();
        }

        private void Awake()
        {
            Application.logMessageReceived += OnLog;
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
            {
                s_errorCount++;
                if (s_errors.Count < 20)
                {
                    s_errors.Add("[" + type + "] " + message);
                }
            }
        }

        private static string LogPath()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], LogArg, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return Path.Combine(Application.persistentDataPath, "bpsim-selftest.log");
        }

        private static void Line(string text)
        {
            s_report.AppendLine(text);
            Debug.Log("[SelfTest] " + text);
        }

        private IEnumerator Start()
        {
            Line("=== bpsim player self test @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            Line("unity=" + Application.unityVersion + " platform=" + Application.platform
                + " product=" + Application.productName + " version=" + Application.version
                + " graphics=" + SystemInfo.graphicsDeviceType);
            Line("persistentDataPath=" + Application.persistentDataPath);

            // PartManager.Awake only creates the dictionaries; the ECS world is resolved in its
            // Start(), so both the singleton and IsReady have to be up before anything is loaded.
            float waited = 0f;
            while (waited < 60f && (PartManager.Instance == null || UserSettings.Instance == null || !PartManager.Instance.IsReady))
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (PartManager.Instance == null || !PartManager.Instance.IsReady)
            {
                Line("FAIL: PartManager/ECS world never became ready after 60s");
                Finish(1);
                yield break;
            }
            Line("boot: waited=" + waited.ToString("0.0") + "s simulatorType="
                + UserSettings.Instance.SimulationSettings.SimulationType);

            PartManager pm = PartManager.Instance;
            Line("diag: " + DescribePartManager(pm));
            Line("boot: havokNative=" + DescribeHavokNative());
            if (pm.HasActiveScene())
            {
                pm.UnloadScene(pm.ActiveScene.SceneID);
                yield return null;
                yield return null;
            }
            pm.LoadEmptyScene("SelfTest");
            yield return null;
            yield return null;
            PartScene scene = pm.ActiveScene;
            if (scene == null)
            {
                Line("FAIL: LoadEmptyScene produced no active scene");
                Finish(1);
                yield break;
            }
            Line("scene: id=" + scene.SceneID + " name='" + scene.SceneName + "' parts=" + scene.Unmanaged.Parts.Length);

            int placed = PlaceParts(pm, scene);
            scene.Unmanaged.Update();
            yield return null;
            yield return null;
            Line("place: placed=" + placed + " parts=" + scene.Unmanaged.Parts.Length);
            if (placed < 4)
            {
                Line("FAIL: could not place a usable contraption");
                Finish(1);
                yield break;
            }

            pm.StartSimulation();
            yield return null;
            Reference<PartSimulatorUnmanaged> simRef = pm.PartSimulator.UnmanagedRef;
            NativeList<Entity> simParts = simRef.Value.Parts;
            EntityManager em = scene.Unmanaged.EntityManager;
            Line("sim: IsSimulating=" + pm.IsSimulating + " simulatorParts=" + simParts.Length
                + " joints=" + simRef.Value.Joints.Length);
            Line("sim: world " + DescribeWorld());
            Line("sim: t=0.0 " + DescribeSimParts(em, simParts, 4));
            Line("sim: backend@0 " + DescribeBackend() + "  world@0 " + DescribeWorld());

            yield return new WaitForSeconds(1.0f);
            Line("sim: t=1.0 " + DescribeSimParts(em, simParts, 4));
            Line("sim: backend@1 " + DescribeBackend() + "  world@1 " + DescribeWorld());
            yield return new WaitForSeconds(1.0f);
            Line("sim: t=2.0 " + DescribeSimParts(em, simParts, 4));
            Line("sim: backend@2 " + DescribeBackend() + "  world@2 " + DescribeWorld());

            float maxDelta = MaxDrop(em, simParts);
            Line("sim: maxDrop=" + maxDelta.ToString("0.###") + " m (expect > 1.0 with gravity on)");
            pm.EndSimulation();
            Line("sim: ended IsSimulating=" + pm.IsSimulating);

            bool moved = maxDelta > 1.0f;
            Line("errors: count=" + s_errorCount);
            foreach (string e in s_errors)
            {
                Line("  " + e);
            }
            Line(moved ? "PASS: physics integrated positions in the player build"
                       : "FAIL: parts never moved (physics backend did not integrate)");
            if (!moved)
            {
                Finish(1);
                yield break;
            }
            Finish(0);
        }

        // 反射读 PartManager 的私有字段：LoadEmptyScene 在 Player 里 NRE 过，先判断是
        // 场景预制体丢了（m_scenePrefab == null）还是 world/entityManager 没就绪。
        private static string DescribePartManager(PartManager pm)
        {
            StringBuilder sb = new StringBuilder("PartManager ready=").Append(pm.IsReady);
            const System.Reflection.BindingFlags Flags =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            string[] names = { "m_scenePrefab", "m_factory", "m_world", "m_entityManager", "m_sceneMap" };
            foreach (string name in names)
            {
                System.Reflection.FieldInfo f = typeof(PartManager).GetField(name, Flags);
                if (f == null)
                {
                    sb.Append(' ').Append(name).Append("=<no field>");
                    continue;
                }
                object v = f.GetValue(pm);
                sb.Append(' ').Append(name).Append('=')
                  .Append(v == null ? "null" : (v is UnityEngine.Object o ? (o == null ? "null(destroyed)" : o.name) : v.ToString()));
            }
            return sb.ToString();
        }

        private static int PlaceParts(PartManager pm, PartScene scene)
        {
            (PartType type, int index, int x, int y)[] specs =
            {
                (PartType.WoodenFrame, 0, 0, 0),
                (PartType.WoodenFrame, 0, 1, 0),
                (PartType.WoodenFrame, 0, 2, 0),
                (PartType.MetalFrame, 0, 3, 0),
                (PartType.NormalWheel, 0, 0, -1),
                (PartType.NormalWheel, 0, 3, -1)
            };
            int placed = 0;
            foreach ((PartType type, int index, int x, int y) in specs)
            {
                PartTypeInfo info = new PartTypeInfo(type, index);
                Entity prefab = pm.EntitySpawner.FindPrefab(info);
                if (prefab == Entity.Null)
                {
                    Line("place: no prefab for " + type + "#" + index);
                    continue;
                }
                PartCreationRequest request = new PartCreationRequest(prefab, scene.SceneID, info, x, y, 0, false);
                try
                {
                    if (scene.Unmanaged.PlacePartAt(request, overlay: false))
                    {
                        placed++;
                    }
                    else
                    {
                        Line("place: rejected " + type + " at (" + x + "," + y + ")");
                    }
                }
                catch (Exception ex)
                {
                    Line("place: EXCEPTION " + type + " " + ex.GetType().Name + " " + ex.Message);
                }
            }
            return placed;
        }

        private static string DescribeWorld()
        {
            EntityManager em = World.DefaultGameObjectInjectionWorld.EntityManager;
            StringBuilder sb = new StringBuilder();
            EntityQuery simQuery = em.CreateEntityQuery(ComponentType.ReadOnly<SimulationSingleton>());
            if (simQuery.CalculateEntityCount() == 1)
            {
                sb.Append("simSingleton=").Append(simQuery.GetSingleton<SimulationSingleton>().Type);
            }
            else
            {
                sb.Append("simSingleton=?");
            }
            simQuery.Dispose();
            EntityQuery stepQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PhysicsStep>());
            if (stepQuery.CalculateEntityCount() == 1)
            {
                PhysicsStep step = stepQuery.GetSingleton<PhysicsStep>();
                sb.Append(" step=").Append(step.SimulationType).Append(" gravity=").Append(step.Gravity);
            }
            else
            {
                sb.Append(" step=?");
            }
            stepQuery.Dispose();
            EntityQuery worldQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PhysicsWorldSingleton>());
            if (worldQuery.CalculateEntityCount() == 1)
            {
                PhysicsWorld pw = worldQuery.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
                NativeArray<MotionData> motions = pw.MotionDatas;
                sb.Append(" motionDatas=").Append(motions.Length).Append(" dynamic=").Append(pw.NumDynamicBodies);
                for (int i = 0; i < motions.Length && i < 3; i++)
                {
                    sb.Append(" m").Append(i).Append('=').Append(motions[i].WorldFromMotion.pos);
                }
            }
            else
            {
                sb.Append(" physicsWorld=?");
            }
            worldQuery.Dispose();
            return sb.ToString();
        }

        // 后端到底是谁在跑：Havok 与 Unity.Physics 各自在系统实体上挂一个 state 组件
        // （HavokBroadphaseData / BroadphaseData …），数一下哪种存在就能判定实际启用的链，
        // 不能只看 SimulationSingleton.Type（那只是某一帧的快照）。
        private static string DescribeBackend()
        {
            string[] probes =
            {
                "Havok.Physics.HavokBroadphaseData, Havok.Physics",
                "Havok.Physics.HavokNarrowphaseData, Havok.Physics",
                "Unity.Physics.Systems.BroadphaseData, Unity.Physics",
                "Unity.Physics.Systems.DummySimulationData, Unity.Physics"
            };
            StringBuilder sb = new StringBuilder();
            EntityManager em = World.DefaultGameObjectInjectionWorld.EntityManager;
            foreach (string spec in probes)
            {
                string name = spec.Substring(spec.LastIndexOf('.') + 1).Replace("Data, ", "").Replace("Data,", "");
                name = spec.Split(',')[0];
                name = name.Substring(name.LastIndexOf('.') + 1);
                Type t = Type.GetType(spec);
                if (t == null)
                {
                    sb.Append(name).Append("=NOTYPE ");
                    continue;
                }
                try
                {
                    EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly(t));
                    sb.Append(name).Append('=').Append(q.CalculateEntityCount()).Append(' ');
                    q.Dispose();
                }
                catch (Exception ex)
                {
                    sb.Append(name).Append("=ERR(").Append(ex.GetType().Name).Append(") ");
                }
            }
            return sb.ToString().TrimEnd();
        }

        // Havok 原生插件在 Player 里到底有没有加载（HavokNative.dll 进包 ≠ 加载成功）。
        private static string DescribeHavokNative()
        {
            try
            {
                System.Reflection.Assembly asm = System.Reflection.Assembly.Load("Havok.Physics");
                Type plugin = asm.GetType("Havok.Physics.Plugin", true);
                System.Reflection.MethodInfo ver = plugin.GetMethod("HP_GetPluginVersionString",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                IntPtr ip = (IntPtr)ver.Invoke(null, null);
                string text = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(ip);
                if (text != null && text.Length > 60)
                {
                    text = text.Substring(0, 60);
                }
                return text;
            }
            catch (Exception ex)
            {
                return "EXCEPTION " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static string DescribeSimParts(EntityManager em, NativeList<Entity> parts, int count)
        {
            StringBuilder sb = new StringBuilder();
            int n = math.min(count, parts.Length);
            for (int i = 0; i < n; i++)
            {
                Entity e = parts[i];
                string pos = "?";
                string vel = "?";
                try
                {
                    pos = em.GetComponentData<LocalTransform>(e).Position.ToString();
                }
                catch
                {
                }
                try
                {
                    if (em.HasComponent<PhysicsVelocity>(e))
                    {
                        vel = em.GetComponentData<PhysicsVelocity>(e).Linear.ToString();
                    }
                }
                catch
                {
                }
                if (i > 0)
                {
                    sb.Append("; ");
                }
                sb.Append('[').Append(i).Append("] p=").Append(pos).Append(" v=").Append(vel)
                    .Append(" world=").Append(em.HasComponent<PhysicsWorldIndex>(e));
            }
            return sb.ToString();
        }

        private static float MaxDrop(EntityManager em, NativeList<Entity> parts)
        {
            float max = 0f;
            for (int i = 0; i < parts.Length; i++)
            {
                try
                {
                    float3 p = em.GetComponentData<LocalTransform>(parts[i]).Position;
                    float d = math.abs(p.y);
                    if (d > max)
                    {
                        max = d;
                    }
                }
                catch
                {
                }
            }
            return max;
        }

        private void Finish(int exitCode)
        {
            s_report.AppendLine("exit=" + exitCode);
            string text = s_report.ToString();
            // 指定路径可能不可写（例如 C 盘根目录受 UAC/沙箱限制），写不进去就退回
            // persistentDataPath —— 自检必须留下证据，否则排在 CI 里等于没有。
            string requested = LogPath();
            if (TryWrite(requested, text))
            {
                Debug.Log("[SelfTest] wrote " + requested + " (exit " + exitCode + ")");
                Application.Quit(exitCode);
                return;
            }
            string fallback = Path.Combine(Application.persistentDataPath, "bpsim-selftest.log");
            if (TryWrite(fallback, text))
            {
                Debug.Log("[SelfTest] wrote " + fallback + " (exit " + exitCode + ")");
            }
            Application.Quit(exitCode);
        }

        private static bool TryWrite(string path, string text)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(path, text);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SelfTest] cannot write log to " + path + ": " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }
    }
}
