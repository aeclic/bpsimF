// Command: run through verify_managed.py. Tests the merged 关卡编辑器 DLL's RC2 document and
// mesh validators against the full corpus and bad inputs. No GameObject, World,
// render pipeline or target gameplay methods are constructed/invoked. This is not
// Unity scene/render acceptance. Invalidated by SDK/host/mod/catalog changes.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
public static class RC2ManagedValidation
{
    private static MethodInfo parse,validate;
    private static JObject catalog,report;
    private static int negatives;
    private static void Reject(Action action,string label)
    {
        bool rejected=false;try{action();}catch(TargetInvocationException e){if(e.InnerException is FormatException)rejected=true;else throw;}
        if(!rejected)throw new Exception("Negative control was accepted: "+label);negatives++;
    }
    public static int Main(string[] args)
    {
        string pack=args[0],assemblyPath=args[1],output=args[2];
        AppDomain.CurrentDomain.AssemblyResolve+=(sender,eventArgs)=>{string name=new AssemblyName(eventArgs.Name).Name;Assembly loaded=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name==name);if(loaded!=null)return loaded;foreach(string directory in args.Skip(3)){string file=Path.Combine(directory,name+".dll");if(File.Exists(file))return Assembly.LoadFrom(file);}return null;};
        report=new JObject{["pass"]=false,["levels"]=0,["meshes"]=0,["negative_controls"]=0,["boundary"]="actual C# validators in Mono; not Unity scene/physics/render acceptance"};
        try
        {
            catalog=JObject.Parse(File.ReadAllText(Path.Combine(pack,"catalog.json")));Assembly assembly=Assembly.LoadFrom(assemblyPath);Type type=assembly.GetType("Bpsim.LevelEditor.Rc2.Rc2Document",true);
            parse=type.GetMethod("Parse");validate=type.GetMethod("ValidateMesh");
            JObject first=null,skin=null;
            foreach(JObject level in (JArray)catalog["levels"])
            {
                string text=File.ReadAllText(Path.Combine(pack,(string)level["path"]));object document=parse.Invoke(null,new object[]{text,catalog});
                JObject data=(JObject)type.GetField("Data").GetValue(document);if((string)data["id"]!=(string)level["id"])throw new Exception("Document id mismatch");
                if(((JArray)data["scene"]["nodes"]).Count!=(int)level["nodes"])throw new Exception("Document node count mismatch");
                report["levels"]=(int)report["levels"]+1;report["max_document_characters"]=Math.Max((int?)report["max_document_characters"]??0,text.Length);if(first==null)first=data;
            }
            foreach(JProperty property in ((JObject)catalog["assets"]["meshes"]).Properties())
            {
                JObject mesh=JObject.Parse(File.ReadAllText(Path.Combine(pack,(string)property.Value["path"])));validate.Invoke(null,new object[]{mesh});report["meshes"]=(int)report["meshes"]+1;if(skin==null&&mesh["weights"]!=null)skin=mesh;
            }
            JObject bad=(JObject)first.DeepClone();bad["scene"]["nodes"][0]["parent"]=0;Reject(()=>parse.Invoke(null,new object[]{bad.ToString(),catalog}),"cyclic parent");
            bad=(JObject)first.DeepClone();bad["scene"]["nodes"][0]["q"]=new JArray(0,0,0,0);Reject(()=>parse.Invoke(null,new object[]{bad.ToString(),catalog}),"zero quaternion");
            bad=(JObject)first.DeepClone();JObject prefab=(JObject)((JObject)bad["scene"]["prefabs"]).Properties().First().Value;prefab["mesh"]="not_an_original_mesh";Reject(()=>parse.Invoke(null,new object[]{bad.ToString(),catalog}),"missing source mesh");
            JObject badSkin=(JObject)skin.DeepClone();badSkin["weights"]=new JArray(1,0);Reject(()=>validate.Invoke(null,new object[]{badSkin}),"short weights");
            badSkin=(JObject)skin.DeepClone();badSkin["bone_indices"][0]=((JArray)skin["bindposes"]).Count;Reject(()=>validate.Invoke(null,new object[]{badSkin}),"out of range bone");
            badSkin=(JObject)skin.DeepClone();badSkin["weights"][0]=-1;Reject(()=>validate.Invoke(null,new object[]{badSkin}),"negative weight");
            badSkin=(JObject)skin.DeepClone();badSkin["submeshes"][0][0]=.5;Reject(()=>validate.Invoke(null,new object[]{badSkin}),"fractional triangle index");
            JObject wrongApk=(JObject)first.DeepClone();wrongApk["source"]["apk_sha256"]=new string('0',64);Reject(()=>parse.Invoke(null,new object[]{wrongApk.ToString(),catalog}),"foreign APK sha256");
            JObject wrongPack=(JObject)first.DeepClone();wrongPack["pack_id"]=Guid.NewGuid().ToString();Reject(()=>parse.Invoke(null,new object[]{wrongPack.ToString(),catalog}),"foreign pack id");
            if((int)report["levels"]!=17||(int)report["meshes"]!=433)throw new Exception("Unexpected original corpus coverage");
            report["negative_controls"]=negatives;report["apk_sha256"]=catalog["apk_sha256"];
            // Definition must supply a fresh source-level starter without changing
            // source JSON. Otherwise the host reuses an unrelated vehicle scene.
            object parsed=parse.Invoke(null,new object[]{first.ToString(Newtonsoft.Json.Formatting.None),catalog});string original=first.ToString(Newtonsoft.Json.Formatting.None);
            object def=type.GetMethod("Definition").Invoke(parsed,null);string starter=(string)def.GetType().GetField("Starter").GetValue(def);
            if(string.IsNullOrWhiteSpace(starter)||((JObject)type.GetField("Data").GetValue(parsed)).ToString(Newtonsoft.Json.Formatting.None)!=original)throw new Exception("Runtime starter changed the source document or is empty");
            report["runtime_starter_default"]=starter;report["runtime_starter_document_unchanged"]=true;
            report["pass"]=true;
        }
        catch(Exception exception){Exception e=exception;while(e is TargetInvocationException&&e.InnerException!=null)e=e.InnerException;report["first_failure"]=e.ToString();}
        File.WriteAllText(output,report.ToString());Console.WriteLine(report.ToString(Newtonsoft.Json.Formatting.None));return (bool)report["pass"]?0:1;
    }
}
