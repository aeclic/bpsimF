// See verify_unity.py for command, criteria, scope and invalidation boundaries.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Bpsim.RC2Qa
{
    public static class Probe
    {
        private static JObject config, catalog, report;
        private static Assembly mod, host;
        private static object owner, world;
        private static Type runtimeType, documentType;
        private static GameObject root, cameraObject;
        private static Camera camera;
        private static RenderTexture target;
        private static int index, phase;
        private static double started, next;
        private static readonly List<string> errors = new List<string>();
        private static readonly BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static string Output { get { return (string)config["output"]; } }
        public static void Run()
        {
            config=JObject.Parse(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),"rc2-qa-config.json")));
            report=new JObject { ["pass"]=false,["levels"]=new JArray(),["boundary"]="real mod/host DLL runtime admission and URP renderability, not APK dynamic/frame equivalence",["configuration"]=config };
            Application.logMessageReceived += Log;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                UniversalRenderPipelineAsset pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/MonoBehaviour/URP_2D_Asset.asset");
                if(pipeline==null) throw new Exception("Frozen host URP asset did not import");
                GraphicsSettings.renderPipelineAsset=pipeline; QualitySettings.renderPipeline=pipeline;
                EditorSettings.enterPlayModeOptionsEnabled=true; EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
                EditorApplication.playModeStateChanged += Mode;
                started=EditorApplication.timeSinceStartup; EditorApplication.update += Tick;
                EditorApplication.EnterPlaymode();
            }
            catch(Exception e) { Fail(e); }
        }
        private static void Log(string message,string stack,LogType type)
        { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert) errors.Add(message+"\n"+stack); }
        private static Assembly Resolve(object sender,ResolveEventArgs args)
        {
            string name=new AssemblyName(args.Name).Name;
            Assembly loaded=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name==name);
            if(loaded!=null) return loaded;
            string path=Path.Combine((string)config["snapshot"],name+".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }
        private static void Mode(PlayModeStateChange mode)
        {
            if(mode!=PlayModeStateChange.EnteredPlayMode) return;
            try
            {
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                host=Assembly.LoadFrom(Path.Combine((string)config["snapshot"],"Assembly-CSharp.dll"));
                mod=Assembly.LoadFrom(Path.Combine((string)config["snapshot"],"BpsimRC2Levels.dll"));
                Type entry=mod.GetType("Bpsim.RC2Levels.Rc2EntryPoint",true);
                owner=Activator.CreateInstance(entry); entry.GetProperty("ModDirectory").SetValue(owner,(string)config["mod"]);
                entry.GetProperty("Id").SetValue(owner,Guid.Parse("683178f5-4b60-5fd7-8ab4-f885409b48ca"));
                catalog=JObject.Parse(File.ReadAllText(Path.Combine((string)config["mod"],"catalog.json")));
                entry.GetProperty("Catalog").SetValue(owner,catalog);
                documentType=mod.GetType("Bpsim.RC2Levels.Rc2Document",true); runtimeType=mod.GetType("Bpsim.RC2Levels.Rc2SceneRuntime",true);
                // Reflected DOTS World creation caused a native failure in the first
                // probe. Do not repeat it. Collision testing requires a separately
                // compiled DOTS package/asmdef environment, not this render probe.
                report["collision_scope"]="not tested; unsafe reflected World creation is disabled";
                cameraObject=new GameObject("RC2_QA_Camera");camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.09f,.12f);camera.nearClipPlane=.1f;camera.farClipPlane=2000;
                cameraObject.AddComponent<UniversalAdditionalCameraData>();
                target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);target.Create();
                phase=1;next=EditorApplication.timeSinceStartup+.5;
            }
            catch(Exception e) { Fail(e); }
        }
        private static void Tick()
        {
            if(EditorApplication.timeSinceStartup-started>700){Fail(new Exception("Isolated scene probe timeout"));return;}
            if(phase==0||EditorApplication.timeSinceStartup<next) return;
            try
            {
                if(phase==1)
                {
                    if(index>=((JArray)catalog["levels"]).Count){Finish();return;}
                    Load((JObject)catalog["levels"][index]);phase=2;next=EditorApplication.timeSinceStartup+.1;
                }
                else
                {
                    Inspect((JObject)catalog["levels"][index]);Object.Destroy(root);root=null;index++;phase=1;next=EditorApplication.timeSinceStartup+.1;
                }
            }
            catch(Exception e){Fail(e);}
        }
        private static void Load(JObject level)
        {
            string text=File.ReadAllText(Path.Combine((string)config["mod"],(string)level["path"]));
            object document=documentType.GetMethod("Parse",All).Invoke(null,new object[]{text,catalog});
            root=new GameObject("RC2_QA_"+(string)level["mission"]);Component component=root.AddComponent(runtimeType);
            if(component==null)throw new Exception("Dynamic RC2 scene component could not be attached");
            runtimeType.GetMethod("Initialize",All).Invoke(component,new object[]{owner,document});
            if((int)runtimeType.GetProperty("SourceNodes").GetValue(component)!=(int)level["nodes"]||(int)runtimeType.GetProperty("SourceRenderers").GetValue(component)!=(int)level["renderers"])
                throw new Exception("Runtime source node/renderer counts differ: "+level["mission"]);
            JObject scene=JObject.Parse(text)["scene"] as JObject;float scale=(float)scene["unit_scale"];
            JArray min=(JArray)scene["bounds"][0],max=(JArray)scene["bounds"][1];
            Vector3 center=new Vector3(((float)min[0]+(float)max[0])*scale*.5f,((float)min[1]+(float)max[1])*scale*.5f,0)+root.transform.position;
            camera.transform.position=new Vector3(center.x,center.y,-30);camera.orthographicSize=Mathf.Max(4,((float)max[1]-(float)min[1])*scale*.55f,((float)max[0]-(float)min[0])*scale/(1280f/720)*.55f);
            foreach(ParticleSystem particle in root.GetComponentsInChildren<ParticleSystem>(true)) if(particle.gameObject.activeInHierarchy)particle.Simulate(.25f,true,true,true);
            // Hold mod animation time at zero: the oracle here is static geometry.
            runtimeType.GetField("m_animate",All).SetValue(component,false);
        }
        private static void Inspect(JObject level)
        {
            int meshes=0,vertices=0,skins=0,particles=root.GetComponentsInChildren<ParticleSystem>(true).Length;
            foreach(MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true)){InspectMesh(filter.sharedMesh);meshes++;vertices+=filter.sharedMesh==null?0:filter.sharedMesh.vertexCount;}
            foreach(SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)){InspectMesh(skin.sharedMesh);if(skin.bones.Length!=skin.sharedMesh.bindposes.Length)throw new Exception("Native bone/bindpose count differs");meshes++;skins++;vertices+=skin.sharedMesh.vertexCount;}
            foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))foreach(Material material in renderer.sharedMaterials)
                if(material==null||material.shader==null||!material.shader.isSupported)throw new Exception("Missing/unsupported native material: "+renderer.name);
            var request=new UniversalRenderPipeline.SingleCameraRequest { destination=target };
            if(!RenderPipeline.SupportsRenderRequest(camera,request))throw new Exception("Host URP does not support the camera render request");
            RenderPipeline.SubmitRenderRequest(camera,request);
            RenderTexture old=RenderTexture.active;RenderTexture.active=target;Texture2D image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();RenderTexture.active=old;
            Color32[] pixels=image.GetPixels32();int nonBackground=0,magenta=0;foreach(Color32 pixel in pixels){if(Math.Abs(pixel.r-15)+Math.Abs(pixel.g-23)+Math.Abs(pixel.b-31)>10)nonBackground++;if(pixel.r>245&&pixel.g<10&&pixel.b>245)magenta++;}
            if(nonBackground<100)throw new Exception("Native scene render is empty");if(magenta>10)throw new Exception("Native scene has missing-shader magenta pixels: "+magenta);
            string name=(string)level["mission"];
            if(name=="Mission_1"||name=="Mission_4"||name=="Mission_Sand_1")File.WriteAllBytes(Path.Combine(Output,"unity-"+name+".png"),image.EncodeToPNG());
            Object.Destroy(image);
            Component component=root.GetComponent(runtimeType);
            IList colliders=(IList)runtimeType.GetField("m_colliders",All).GetValue(component);
            ((JArray)report["levels"]).Add(new JObject { ["mission"]=name,["source_nodes"]=runtimeType.GetProperty("SourceNodes").GetValue(component).ToString(),["source_renderers"]=runtimeType.GetProperty("SourceRenderers").GetValue(component).ToString(),["mesh_components"]=meshes,["vertices"]=vertices,["skin_renderers"]=skins,["particle_systems"]=particles,["static_colliders"]=colliders.Count,["non_background_pixels"]=nonBackground,["magenta_pixels"]=magenta });
            if(errors.Count>0)throw new Exception("Unity error log: "+errors[0]);
            Debug.Log("RC2_UNITY_LEVEL_PASS "+name);
        }
        private static void InspectMesh(Mesh mesh)
        {
            if(mesh==null||mesh.vertexCount==0)throw new Exception("Native mesh is missing/empty");
            foreach(Vector3 v in mesh.vertices)if(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.x)||float.IsInfinity(v.y)||float.IsInfinity(v.z))throw new Exception("Native mesh has non-finite position");
            if(mesh.uv.Length!=mesh.vertexCount)throw new Exception("Native mesh UV count differs");
            for(int s=0;s<mesh.subMeshCount;s++)foreach(int i in mesh.GetTriangles(s))if(i<0||i>=mesh.vertexCount)throw new Exception("Native mesh index out of range");
        }
        private static void Finish()
        {
            try
            {
                if(errors.Count>0)throw new Exception(errors[0]);
                // A source document with a cyclic parent must fail the real parser.
                JObject level=(JObject)catalog["levels"][0];JObject invalid=JObject.Parse(File.ReadAllText(Path.Combine((string)config["mod"],(string)level["path"])));invalid["scene"]["nodes"][0]["parent"]=0;
                bool rejected=false;try{documentType.GetMethod("Parse",All).Invoke(null,new object[]{invalid.ToString(),catalog});}catch(TargetInvocationException){rejected=true;}
                if(!rejected)throw new Exception("Cyclic negative document was accepted");
                report["negative_controls"]=1;report["pass"]=true;report["urp"]=GraphicsSettings.currentRenderPipeline.GetType().FullName;
                Cleanup();File.WriteAllText(Path.Combine(Output,"unity-verification.json"),report.ToString());Debug.Log("RC2_UNITY_QA_PASS");EditorApplication.Exit(0);
            }catch(Exception e){Fail(e);}
        }
        private static void Cleanup()
        {
            EditorApplication.update-=Tick;EditorApplication.playModeStateChanged-=Mode;
            if(world!=null){world.GetType().GetProperty("DefaultGameObjectInjectionWorld",All).SetValue(null,null);((IDisposable)world).Dispose();world=null;}
            if(target!=null){target.Release();Object.Destroy(target);target=null;}
        }
        private static void Fail(Exception exception)
        {
            phase=0;Exception e=exception;while(e is TargetInvocationException&&e.InnerException!=null)e=e.InnerException;
            report["first_failure"]=e.ToString();report["errors"]=new JArray(errors);
            try{Cleanup();}catch(Exception cleanup){report["cleanup_error"]=cleanup.ToString();}
            File.WriteAllText(Path.Combine(Output,"unity-verification.json"),report.ToString());Debug.Log("RC2_UNITY_QA_FAIL "+e.Message);EditorApplication.Exit(1);
        }
    }
}
