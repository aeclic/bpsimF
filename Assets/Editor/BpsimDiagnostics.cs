// Temporary diagnostics for the bpsim-main Unity 2022.2.23 migration.
//
//   bridge: invoke_static { "type": "BpsimDiagnostics", "method": "Probe" }
//           invoke_static { "type": "BpsimDiagnostics", "method": "ShaderReport" }
//
// No manual SetAtomicSafetyHandle anywhere: Probe exercises the real game paths.
using System;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using PhysBox = Unity.Physics.BoxCollider;
using PhysBoxGeometry = Unity.Physics.BoxGeometry;
using PhysCompound = Unity.Physics.CompoundCollider;
using PhysConvex = Unity.Physics.ConvexHullGenerationParameters;

public static unsafe class BpsimDiagnostics
{
    public static string Probe()
    {
        var sb = new StringBuilder();

        void Step(string name, Func<string> body)
        {
            try { sb.Append("PASS  ").Append(name).Append(": ").Append(body()).Append('\n'); }
            catch (Exception e)
            {
                sb.Append("FAIL  ").Append(name).Append(": ").Append(e.GetType().Name).Append(" - ").Append(e.Message).Append('\n');
                var st = e.StackTrace;
                if (st != null)
                {
                    var lines = st.Split('\n');
                    for (int i = 0; i < lines.Length && i < 2; i++) sb.Append("          ").Append(lines[i].Trim()).Append('\n');
                }
            }
        }

        Step("A. engine NativeArray<int> ctor + index", () =>
        {
            var a = new NativeArray<int>(4, Allocator.TempJob);
            a[0] = 42;
            int v = a[0];
            a.Dispose();
            return "a[0]=" + v;
        });

        Step("B. NativeList<int>.AsArray() + index", () =>
        {
            var l = new NativeList<int>(4, Allocator.TempJob);
            l.Add(7);
            NativeArray<int> arr = l.AsArray();
            int v = arr[0];
            l.Dispose();
            return "arr[0]=" + v;
        });

        Step("C. NativeList<int>.AsArray() + write + read", () =>
        {
            var l = new NativeList<int>(4, Allocator.TempJob);
            l.Add(1);
            NativeArray<int> arr = l.AsArray();
            arr[0] = 123;
            int v = arr[0];
            l.Dispose();
            return "arr[0]=" + v;
        });

        Step("D. GetUnsafeReadOnlyPtr on a NativeList-produced array", () =>
        {
            var l = new NativeList<int>(4, Allocator.TempJob);
            l.Add(3);
            NativeArray<int> arr = l.AsArray();
            void* p = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(arr);
            int v = *(int*)p;
            l.Dispose();
            return "ptr!=0=" + (p != null) + " *p=" + v;
        });

        Step("E. game collider path: NativeList<ColliderBlobInstance> -> CompoundCollider.Create", () =>
        {
            var box = PhysBox.Create(new PhysBoxGeometry
            {
                Center = float3.zero,
                Orientation = quaternion.identity,
                Size = new float3(1f, 1f, 1f),
                BevelRadius = math.min(PhysConvex.Default.BevelRadius, 0.5f)
            });
            var list = new NativeList<PhysCompound.ColliderBlobInstance>(4, Allocator.TempJob);
            list.Add(new PhysCompound.ColliderBlobInstance
            {
                CompoundFromChild = RigidTransform.identity,
                Collider = box,
                Entity = Entity.Null
            });
            NativeArray<PhysCompound.ColliderBlobInstance> arr = list.AsArray();
            bool elemOk = arr[0].Collider.IsCreated;
            var c = PhysCompound.Create(arr);
            string r = "elem0.Collider=" + elemOk + " compound.IsCreated=" + c.IsCreated;
            list.Dispose();
            return r;
        });

        Step("F. JobHandle.CombineDependencies(NativeArray<JobHandle>)", () =>
        {
            var jobs = new NativeArray<JobHandle>(2, Allocator.TempJob);
            jobs[0] = new JobHandle();
            jobs[1] = new JobHandle();
            JobHandle h = JobHandle.CombineDependencies(jobs);
            jobs.Dispose();
            return "combined ok";
        });

        Step("G. NativeParallelHashMap key/value arrays", () =>
        {
            var m = new NativeParallelHashMap<int, int>(4, Allocator.TempJob);
            m.TryAdd(1, 2);
            NativeArray<int> keys = m.GetKeyArray(Allocator.TempJob);
            int k = keys[0];
            keys.Dispose();
            m.Dispose();
            return "key=" + k;
        });

        Step("H. NativeSlice of an engine NativeArray", () =>
        {
            var a = new NativeArray<float3>(3, Allocator.TempJob);
            a[0] = new float3(1, 2, 3);
            var slice = new NativeSlice<float3>(a, 1, 2);
            float3 v = slice[0];
            a.Dispose();
            return "slice[0]=" + v;
        });

        return sb.ToString();
    }

    // Shader inventory: the migration restored 29 URP shaders from the Editor's built-in
    // URP 14.0.7 and vendors its HLSL as embedded packages. This reports whether they resolve
    // and how many passes they expose (a decompiler stub always reported exactly 1).
    public static string ShaderReport()
    {
        string[] names =
        {
            "Hidden/Universal/CoreBlit",
            "Hidden/Universal Render Pipeline/UberPost",
            "Hidden/Universal Render Pipeline/FinalPost",
            "Hidden/Universal Render Pipeline/Scaling Setup",
            "Hidden/Universal Render Pipeline/Sampling",
            "Hidden/Universal Render Pipeline/Bloom",
            "Hidden/Universal Render Pipeline/TemporalAA",
            "Hidden/Universal Render Pipeline/LutBuilderLdr",
            "Hidden/Light2D-Point",
            "Hidden/Light2D-Shape",
            "Hidden/Shadow2DUnshadowSprite",
            "Hidden/ShadowProjected2D",
            "Hidden/Core/FallbackError",
            "Hidden/Universal Render Pipeline/FallbackError",
            "Hidden/Shader Graph/FallbackError",
            "Sprites/Default",
            "Universal Render Pipeline/Lit",
            "Bpsim/Unlit_Texture",
            "Bpsim/URP/Unlit_Texture_Instancing",
            "Bpsim/RoundedRectangle",
            "Hidden/OccludeeScreenSpaceAABB",
            "Hidden/OcclusionDebugComposite",
            "Hidden/OcclusionDebugOccluders",
            "Shader Graphs/ShowOccluderMesh"
        };

        var sb = new StringBuilder();
        foreach (var n in names)
        {
            var sh = Shader.Find(n);
            if (sh == null) { sb.Append("MISSING  ").Append(n).Append('\n'); continue; }
            sb.Append(sh.passCount > 1 ? "OK       " : "1-PASS?  ")
              .Append(n).Append("  passes=").Append(sh.passCount)
              .Append(" supported=").Append(sh.isSupported)
              .Append(" instancing=").Append(sh.name)
              .Append('\n');
        }
        return sb.ToString();
    }

    // Reports how the occlusion shader assets actually imported, for the case where
    // Shader.Find cannot see one.
    public static string ShaderAssetReport()
    {
        var sb = new StringBuilder();
        string[] paths =
        {
            "Assets/Resources/occlusion/showoccludermesh.shader",
            "Assets/Resources/occlusion/occludeescreenspaceaabb.shader",
            "Assets/Resources/occlusion/occlusiondebugcomposite.shader",
            "Assets/Resources/occlusion/occlusiondebugoccluders.shader",
            "Assets/Shader/Bpsim_RoundedRectangle.shader",
            "Assets/Shader/Bpsim_Unlit_Texture.shader",
            "Assets/Shader/Bpsim_URP_Unlit_Texture_Instancing.shader"
        };

        foreach (var p in paths)
        {
            var sh = AssetDatabase.LoadAssetAtPath<Shader>(p);
            sb.Append(sh == null ? "NULL-ASSET  " : "ok          ").Append(p);
            if (sh != null)
            {
                sb.Append("  name='").Append(sh.name).Append("' passes=").Append(sh.passCount)
                  .Append(" supported=").Append(sh.isSupported)
                  .Append(" find=").Append(Shader.Find(sh.name) == null ? "NULL" : "ok");
            }
            sb.Append('\n');
        }

        sb.Append("--- every Shader asset under occlusion/ ---\n");
        foreach (var guid in AssetDatabase.FindAssets("t:Shader"))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.IndexOf("occlusion", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            var s = AssetDatabase.LoadAssetAtPath<Shader>(p);
            sb.Append("  ").Append(p).Append(" -> ").Append(s == null ? "<null>" : s.name + " passes=" + s.passCount).Append('\n');
        }
        return sb.ToString();
    }

    // Confirms the Input System package migration: the twelve repointed assets must load with the
    // package's script types (not Missing Script), and UserInterface.prefab must have no missing
    // MonoBehaviour slots.  Runnable headlessly:
    //   Unity.exe -batchmode -quit -projectPath <proj> -executeMethod BpsimDiagnostics.ValidateInputMigration
    public static void ValidateInputMigration()
    {
        var sb = new StringBuilder();
        string[] assets =
        {
            "Assets/MonoBehaviour/DefaultInputActions.asset",
            "Assets/MonoBehaviour/UI_Point.asset",
            "Assets/MonoBehaviour/UI_Click.asset",
            "Assets/MonoBehaviour/UI_MiddleClick.asset",
            "Assets/MonoBehaviour/UI_RightClick.asset",
            "Assets/MonoBehaviour/UI_ScrollWheel.asset",
            "Assets/MonoBehaviour/UI_Navigate.asset",
            "Assets/MonoBehaviour/UI_Submit.asset",
            "Assets/MonoBehaviour/UI_Cancel.asset",
            "Assets/MonoBehaviour/UI_TrackedDevicePosition.asset",
            "Assets/MonoBehaviour/UI_TrackedDeviceOrientation.asset"
        };
        foreach (var p in assets)
        {
            var obj = AssetDatabase.LoadMainAssetAtPath(p);
            // InputActionAsset / InputActionReference are ScriptableObjects, not MonoBehaviours -
            // a failed script reference shows up as a null main asset (or a MonoBehaviour with a
            // null m_Script, which LoadMainAssetAtPath returns as null too).
            sb.Append(obj != null ? "ok       " : "MISSING  ").Append(p)
              .Append("  type=").Append(obj == null ? "<null>" : obj.GetType().FullName).Append('\n');
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObject/UserInterface.prefab");
        if (prefab == null) { sb.Append("UserInterface.prefab: <null>\n"); }
        else
        {
            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab);
            sb.Append("UserInterface.prefab: missingScripts=").Append(missing)
              .Append(" (expect 0)\n");
        }

        sb.Append("InputSystem.devices=").Append(UnityEngine.InputSystem.InputSystem.devices.Count)
          .Append(" Mouse.current=").Append(UnityEngine.InputSystem.Mouse.current == null ? "<null>" : "present")
          .Append('\n');

        Debug.Log("VALIDATE-INPUT\n" + sb.ToString());
        EditorApplication.Exit(sb.ToString().Contains("MISSING") ? 1 : 0);
    }

    // Drives and reports the whole "get to a place where parts can be drawn" flow:
    //   PartManager.HasActiveScene  <- only SelectScene() ever sets m_activeScene, and that runs
    //                                  from SceneTabView's tab toggle firing onValueChanged when
    //                                  SceneLoaded creates the tab
    //   PartEntitySpawner.Collection <- the prefab entities PlaceOrEraseParts looks up
    //   PartOperator.Mode           <- derived from the ActionBar toggle group
    // Call it twice a few seconds apart: the first call creates the scene and flips the toggle,
    // the second one shows the mode the game picked up on the next frame.
    public static string PlacementFlowProbe()
    {
        var sb = new StringBuilder();

        Type T(string full)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(full);
                if (t != null) return t;
            }
            return null;
        }

        object StaticProp(Type t, string name)
        {
            if (t == null) return null;
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy;
            System.Reflection.PropertyInfo best = null;
            foreach (var p in t.GetProperties(flags))
            {
                if (p.Name != name) continue;
                if (best == null) { best = p; continue; }
                if (best.DeclaringType != null && p.DeclaringType != null && p.DeclaringType.IsSubclassOf(best.DeclaringType)) best = p;
            }
            try { return best == null ? null : best.GetValue(null); } catch { return null; }
        }

        object Prop(object o, string name)
        {
            if (o == null) return null;
            var t = o.GetType();
            var p = t.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.FlattenHierarchy);
            if (p != null) { try { return p.GetValue(o); } catch { } }
            var f = t.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return f == null ? null : f.GetValue(o);
        }

        object Call(object o, string name, params object[] args)
        {
            if (o == null) return null;
            var m = o.GetType().GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (m == null) return "no method " + name;
            try { return m.Invoke(o, args); }
            catch (System.Reflection.TargetInvocationException e) { return "THREW " + (e.InnerException ?? e).GetType().Name + ": " + (e.InnerException ?? e).Message; }
        }

        var pmType = T("Bpsim.Parts.PartManager");
        var pm = StaticProp(pmType, "Instance");
        sb.Append("PartManager = ").Append(pm == null ? "<null>" : "present").Append('\n');
        if (pm == null) return sb.ToString();

        var hasScene = Call(pm, "HasActiveScene");
        sb.Append("HasActiveScene (before) = ").Append(hasScene).Append('\n');

        if (!(hasScene is bool b && b))
        {
            var res = Call(pm, "LoadEmptyScene", "Empty");
            sb.Append("LoadEmptyScene(\"Empty\") -> ").Append(res == null ? "ok" : res.ToString()).Append('\n');
            sb.Append("HasActiveScene (after LoadEmptyScene) = ").Append(Call(pm, "HasActiveScene")).Append('\n');
        }

        // prefab entities that PlaceOrEraseParts looks up
        var spawner = Prop(pm, "EntitySpawner");
        var collection = Prop(spawner, "Collection");
        if (collection != null)
        {
            var cnt = Call(collection, "Count");
            sb.Append("EntitySpawner.Collection.Count = ").Append(cnt == null ? "?" : cnt.ToString()).Append('\n');
        }
        else
        {
            sb.Append("EntitySpawner.Collection = <null>\n");
        }

        // flip the ActionBar to Place
        var ui = StaticProp(T("Bpsim.UI.UserInterface"), "Instance");
        var op = Prop(ui, "PartOperator");
        if (op != null)
        {
            sb.Append("PartOperator.Mode (now) = ").Append(Prop(op, "Mode")).Append('\n');
            var tg = Prop(op, "m_toggleGroup");
            if (tg is Component comp)
            {
                sb.Append("toggleGroup go=").Append(comp.gameObject.name).Append(" allowSwitchOff=").Append(Prop(tg, "m_AllowSwitchOff")).Append('\n');
                foreach (var t2 in comp.gameObject.GetComponentsInChildren<UnityEngine.UI.Toggle>(true))
                {
                    sb.Append("  toggle ").Append(t2.name).Append(" isOn=").Append(t2.isOn)
                      .Append(" group=").Append(t2.group == null ? "<null>" : t2.group.gameObject.name)
                      .Append(" interactable=").Append(t2.interactable).Append('\n');
                    if (t2.name == "PlaceButton" && !t2.isOn) { t2.isOn = true; sb.Append("  -> turned PlaceButton on\n"); }
                }
                var active = comp.gameObject.GetComponent<UnityEngine.UI.ToggleGroup>();
                if (active != null)
                {
                    int n = 0;
                    foreach (var a in active.ActiveToggles()) n++;
                    sb.Append("ActiveToggles().Count = ").Append(n).Append('\n');
                }
            }
        }
        return sb.ToString();
    }

    // Reports the whole click-to-place chain: EventSystem + InputSystemUIInputModule, the
    // Input System mouse, SceneScreen's pointer state, PartOperator's mode and whether a scene
    // is loaded.  Uses reflection because SceneScreen / PartOperator / PartManager are internal
    // to Assembly-CSharp, which does not expose InternalsVisibleTo.
    public static string UiProbe()
    {
        var sb = new StringBuilder();

        Type T(string full)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(full);
                if (t != null) return t;
            }
            return null;
        }

        object StaticProp(Type t, string name)
        {
            if (t == null) return null;
            // FlattenHierarchy is required (Mouse.current etc. are inherited), but it makes
            // `public new static Instance` plus the base `Instance` an ambiguous match - so pick
            // the one declared on the most derived type by hand.
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy;
            System.Reflection.PropertyInfo best = null;
            foreach (var p in t.GetProperties(flags))
            {
                if (p.Name != name) continue;
                if (best == null) { best = p; continue; }
                if (best.DeclaringType != null && p.DeclaringType != null && p.DeclaringType.IsSubclassOf(best.DeclaringType)) best = p;
            }
            if (best == null) best = t.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            try { return best == null ? null : best.GetValue(null); } catch { return null; }
        }

        string DescribeType(string typeName, string instanceName)
        {
            var t = T(typeName);
            if (t == null) return typeName + " : TYPE NOT FOUND";
            var v = StaticProp(t, instanceName);
            return typeName + "." + instanceName + " = " + (v == null ? "<null>" : v.GetType().Name);
        }

        object Prop(object o, string name)
        {
            if (o == null) return null;
            var p = o.GetType().GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return p == null ? null : p.GetValue(o);
        }

        object Field(object o, string name)
        {
            if (o == null) return null;
            var f = o.GetType().GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return f == null ? null : f.GetValue(o);
        }

        // --- EventSystem / input module ---------------------------------------------------
        var es = UnityEngine.EventSystems.EventSystem.current;
        sb.Append("EventSystem.current = ").Append(es == null ? "<null>" : es.name);
        if (es != null)
        {
            sb.Append("  enabled=").Append(es.enabled).Append(" active=").Append(es.gameObject.activeInHierarchy);
            sb.Append("  selected=").Append(es.currentSelectedGameObject == null ? "<null>" : es.currentSelectedGameObject.name);
        }
        sb.Append('\n');

        var moduleTypes = new[] { "UnityEngine.InputSystem.UI.InputSystemUIInputModule", "UnityEngine.EventSystems.StandaloneInputModule" };
        foreach (var mt in moduleTypes)
        {
            var t = T(mt);
            if (t == null) { sb.Append("module ").Append(mt).Append(" : type not loaded\n"); continue; }
            var comp = es == null ? null : es.gameObject.GetComponent(t);
            sb.Append("module ").Append(mt).Append(" : ").Append(comp == null ? "ABSENT" : "present");
            if (comp != null)
            {
                var actionsAsset = Prop(comp, "actionsAsset");
                sb.Append(" actionsAsset=").Append(actionsAsset == null ? "<null>" : actionsAsset.ToString());
                var point = Prop(comp, "point");
                sb.Append(" pointAction=").Append(point == null ? "<null>" : (Prop(point, "action") == null ? "no-action" : Prop(point, "action").ToString()));
            }
            sb.Append('\n');
        }

        // --- Input System mouse ------------------------------------------------------------
        var mouseType = T("UnityEngine.InputSystem.Mouse");
        var mouse = StaticProp(mouseType, "current");
        sb.Append("Mouse.current = ").Append(mouse == null ? "<null>" : "present");
        if (mouse != null)
        {
            var posCtl = Prop(mouse, "position");
            var btn = Prop(mouse, "leftButton");
            sb.Append(" pos=").Append(posCtl == null ? "?" : Prop(posCtl, "ReadValue") == null ? posCtl.ToString() : "ctrl")
              .Append(" leftPressed=");
            var pressed = btn == null ? null : Prop(btn, "isPressed");
            sb.Append(pressed == null ? "?" : pressed.ToString());
        }
        sb.Append('\n');

        // --- Input System device list ------------------------------------------------------
        var inputSystemType = T("UnityEngine.InputSystem.InputSystem");
        if (inputSystemType != null)
        {
            var devices = StaticProp(inputSystemType, "devices");
            sb.Append("InputSystem.devices = ");
            var enumerable = devices as System.Collections.IEnumerable;
            if (enumerable == null) { sb.Append("<null>\n"); }
            else
            {
                int n = 0;
                var names = new StringBuilder();
                foreach (var d in enumerable)
                {
                    n++;
                    if (n <= 8) names.Append(Prop(d, "displayName")).Append('/').Append(Prop(d, "layout")).Append("  ");
                }
                sb.Append(n).Append(" : ").Append(names).Append('\n');
            }
            var settings = StaticProp(inputSystemType, "settings");
            sb.Append("InputSystem.settings = ").Append(settings == null ? "<null>" : settings.GetType().Name)
              .Append(" updateMode=").Append(Prop(settings, "updateMode"))
              .Append(" backgroundBehavior=").Append(Prop(settings, "backgroundBehavior")).Append('\n');
            var isProcessing = StaticProp(inputSystemType, "isProcessingEvents");
            sb.Append("InputSystem.isProcessingEvents = ").Append(isProcessing).Append('\n');
        }
        else
        {
            sb.Append("InputSystem type NOT LOADED\n");
        }

        // --- game objects ------------------------------------------------------------------
        var uiType = T("Bpsim.UI.UserInterface");
        var ui = StaticProp(uiType, "Instance");
        sb.Append(DescribeType("Bpsim.UI.UserInterface", "Instance")).Append('\n');
        sb.Append(DescribeType("Bpsim.Parts.PartManager", "Instance")).Append('\n');
        sb.Append(DescribeType("Bpsim.CoreManager", "Instance")).Append('\n');
        sb.Append(DescribeType("Bpsim.Rendering.SpriteManager", "Instance")).Append('\n');
        sb.Append(DescribeType("Bpsim.SceneCamera", "Instance")).Append('\n');
        sb.Append("loaded assemblies = ").Append(AppDomain.CurrentDomain.GetAssemblies().Length).Append('\n');

        var sceneScreen = Prop(ui, "SceneScreen");
        sb.Append("  SceneScreen = ").Append(sceneScreen == null ? "<null>" : "present");
        if (sceneScreen != null)
        {
            var pointer = Prop(sceneScreen, "Pointer");
            sb.Append(" pointerHasValue=").Append(Prop(pointer, "HasValue"))
              .Append(" isPressed=").Append(Prop(pointer, "IsPressed"))
              .Append(" point=").Append(Prop(pointer, "Point"));
            var ssGo = sceneScreen is Component c ? c.gameObject : null;
            sb.Append(" go=").Append(ssGo == null ? "?" : ssGo.name + " active=" + ssGo.activeInHierarchy);
        }
        sb.Append('\n');

        var op = Prop(ui, "PartOperator");
        sb.Append("  PartOperator = ").Append(op == null ? "<null>" : "present");
        if (op != null)
        {
            sb.Append(" Mode=").Append(Prop(op, "Mode"));
            var tg = Field(op, "m_toggleGroup");
            if (tg != null)
            {
                var anyActive = Prop(tg, "anyTogglesOn");
                sb.Append(" toggleGroup=").Append(((Component)tg).gameObject.name).Append(" anyTogglesOn=").Append(anyActive);
                var go = ((Component)tg).gameObject;
                var toggles = go.GetComponentsInChildren<UnityEngine.UI.Toggle>(true);
                sb.Append(" toggles=").Append(toggles.Length).Append(" [");
                foreach (var t2 in toggles) sb.Append(t2.name).Append("=").Append(t2.isOn).Append(t2.enabled ? "" : "(disabled)").Append(" ");
                sb.Append(']');
            }
        }
        sb.Append('\n');

        var sel = Prop(ui, "PartSelector");
        if (sel != null)
        {
            sb.Append("  PartSelector PartType=").Append(Prop(sel, "PartType"))
              .Append(" PartIndex=").Append(Prop(sel, "PartIndex"))
              .Append(" Rotation=").Append(Prop(sel, "Rotation")).Append('\n');
        }

        var pmType = T("Bpsim.Parts.PartManager");
        var pm = StaticProp(pmType, "Instance");
        sb.Append("PartManager.Instance = ").Append(pm == null ? "<null>" : "present");
        if (pm != null)
        {
            var hasScene = pm.GetType().GetMethod("HasActiveScene", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            sb.Append(" HasActiveScene=").Append(hasScene == null ? "?" : hasScene.Invoke(pm, null).ToString());
        }
        sb.Append('\n');

        return sb.ToString();
    }

    // Renders a camera to a RenderTexture and reports pixel statistics, so "does it actually
    // draw anything" can be answered without looking at an image.
    public static string RenderProbe()
    {
        var sb = new StringBuilder();

        Camera sceneCam = null;
        foreach (var c in UnityEngine.Object.FindObjectsOfType<Camera>())
        {
            if (c.name == "SceneCamera") sceneCam = c;
        }

        // A camera aimed at the part-prefab grid (BuildPartGroups lays it out from x=0 upward,
        // Part_PointLight_* sit around x=88) so the probe sees real content rather than the
        // empty origin the scene camera starts on.
        var go = new GameObject("BpsimRenderProbeCam");
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 14f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        cam.transform.position = new Vector3(90f, -6f, -30f);
        cam.transform.rotation = Quaternion.identity;
        cam.cullingMask = ~0;

        sb.Append(Describe("scene @ part grid (x=90)", cam, 512, 512)).Append('\n');

        // Where is the geometry actually?  Tells "camera aimed wrong" apart from
        // "renderers exist but are not drawn".
        var renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
        int enabled = 0, withBounds = 0;
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        int visibleInCamera = 0;
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        foreach (var mr in renderers)
        {
            if (mr.enabled && mr.gameObject.activeInHierarchy) enabled++;
            var b = mr.bounds;
            if (b.size.sqrMagnitude <= 0f) continue;
            withBounds++;
            min = Vector3.Min(min, b.min);
            max = Vector3.Max(max, b.max);
            if (GeometryUtility.TestPlanesAABB(planes, b)) visibleInCamera++;
        }
        sb.Append("MeshRenderers: total=").Append(renderers.Length)
          .Append(" enabledAndActive=").Append(enabled)
          .Append(" withNonZeroBounds=").Append(withBounds)
          .Append(" insideProbeFrustum=").Append(visibleInCamera).Append('\n');
        if (withBounds > 0)
        {
            sb.Append("  bounds min=").Append(min.ToString("F1")).Append(" max=").Append(max.ToString("F1")).Append('\n');
        }
        sb.Append("  probe camera pos=").Append(cam.transform.position.ToString("F1"))
          .Append(" orthoSize=").Append(cam.orthographicSize).Append('\n');

        // Self-test: put a quad right in front of the probe camera with a known-good shader and
        // then with the game's shader, so "camera does not render" can be told apart from
        // "this shader does not render".
        string[] shaders = { "Sprites/Default", "Bpsim/URP/Unlit_Texture_Instancing", "Bpsim/Unlit_Texture", "Universal Render Pipeline/2D/Sprite-Unlit-Default" };
        foreach (var shaderName in shaders)
        {
            var sh = Shader.Find(shaderName);
            if (sh == null) { sb.Append("selftest ").Append(shaderName).Append(": SHADER NULL\n"); continue; }

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "BpsimRenderProbeQuad";
            UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.position = new Vector3(90f, -6f, 0f);
            quad.transform.localScale = new Vector3(20f, 20f, 1f);
            var mat = new Material(sh);
            mat.mainTexture = Texture2D.whiteTexture;
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.red);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.red);
            quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (quad.GetComponent<Bpsim.Rendering.MaterialSprite>() == null) { }

            sb.Append(Describe("selftest " + shaderName, cam, 256, 256)).Append('\n');
            UnityEngine.Object.DestroyImmediate(quad);
            UnityEngine.Object.DestroyImmediate(mat);
        }

        UnityEngine.Object.DestroyImmediate(go);
        return sb.ToString();
    }

    private static string Describe(string label, Camera cam, int w, int h)
    {
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevTarget;

        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;
        RenderTexture.ReleaseTemporary(rt);

        var px = tex.GetPixels32();
        UnityEngine.Object.DestroyImmediate(tex);

        int lit = 0;
        long r = 0, g = 0, b = 0;
        var colors = new System.Collections.Generic.HashSet<int>();
        foreach (var p in px)
        {
            if (p.r > 12 || p.g > 12 || p.b > 12) lit++;
            r += p.r; g += p.g; b += p.b;
            colors.Add((p.r << 16) | (p.g << 8) | p.b);
        }
        float n = px.Length;
        return string.Concat(label,
            ": lit=", (100f * lit / n).ToString("F1"), "%",
            " meanRGB=(", (r / n).ToString("F0"), ",", (g / n).ToString("F0"), ",", (b / n).ToString("F0"), ")",
            " distinctColors=", colors.Count,
            " enabled=", cam.enabled, " activeInHierarchy=", cam.gameObject.activeInHierarchy);
    }
}
