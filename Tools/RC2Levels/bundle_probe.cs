// Command/scope/invalidation: see probe_bundle.py. Native build/load, not source art parity.
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace Bpsim.RC2BundleProbe
{
    public static class Probe
    {
        [Serializable] public sealed class Config { public string old_bundle,output,report; }
        [Serializable] public sealed class Result { public bool pass,old_load_succeeded; public string unity,graphics,error;public List<string> old_errors=new List<string>(),shaders=new List<string>();public bool supported; }
        public static void Run()
        {
            Config config=JsonUtility.FromJson<Config>(File.ReadAllText("probe-config.json"));
            Result result=new Result { unity=Application.unityVersion,graphics=SystemInfo.graphicsDeviceType.ToString() };
            List<string> oldErrors=result.old_errors;
            Application.LogCallback callback=(message,stack,type)=>{if(type==LogType.Error||type==LogType.Exception)oldErrors.Add(message);};
            Application.logMessageReceived+=callback;
            try
            {
                AssetBundle old=AssetBundle.LoadFromMemory(File.ReadAllBytes(config.old_bundle));
                result.old_load_succeeded=old!=null;if(old!=null)old.Unload(true);
                Application.logMessageReceived-=callback;
                Directory.CreateDirectory(config.output);
                var build=new AssetBundleBuild { assetBundleName="rc2-windows.bundle",assetNames=new[]{"Assets/RC2/RC2Original.shader","Assets/RC2/RC2Waterfall.shader"} };
                AssetBundleManifest manifest=BuildPipeline.BuildAssetBundles(config.output,new[]{build},BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.ForceRebuildAssetBundle,BuildTarget.StandaloneWindows64);
                if(manifest==null)throw new Exception("Bundle build did not return a manifest");
                AssetBundle native=AssetBundle.LoadFromMemory(File.ReadAllBytes(Path.Combine(config.output,build.assetBundleName)));
                if(native==null)throw new Exception("Rebuilt native bundle failed to load");
                try
                {
                    bool supported=true;foreach(Shader shader in native.LoadAllAssets<Shader>()){result.shaders.Add(shader.name);supported&=shader.isSupported;}
                    result.supported=supported;
                    if(!result.shaders.Contains("Bpsim/RC2/OriginalUnlit")||!result.shaders.Contains("Bpsim/RC2/Waterfall")||!supported)throw new Exception("Expected supported shaders missing");
                }
                finally{native.Unload(true);}
                result.pass=true;
            }
            catch(Exception e){result.error=e.ToString();}
            Application.logMessageReceived-=callback;
            File.WriteAllText(config.report,JsonUtility.ToJson(result,true));Debug.Log("RC2_BUNDLE_PROBE "+JsonUtility.ToJson(result));EditorApplication.Exit(result.pass?0:1);
        }
    }
}
