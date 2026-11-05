#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bpsim.EditorTools
{
    public static class LevelEditorWaterCaptureQA
    {
        public static string ShaderStatus()
        {
            JObject result = new JObject();
            foreach (string name in new[] { "WaterSurface", "Underwater", "LightRays" })
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/LevelEditorModAssets/Ultimate" + name + ".shader");
                if (shader == null) continue;
                result[name] = new JObject {
                    ["supported"] = shader.isSupported,
                    ["messages"] = JArray.FromObject(ShaderUtil.GetShaderMessages(shader).Select(message => new {
                        message.message, message.line, severity = message.severity.ToString(), message.platform
                    }))
                };
            }
            return result.ToString(Formatting.None);
        }

        // Called through the existing DshBridge. This captures the game's actual
        // camera including URP water captures, instead of creating a perspective camera.
        public static string CaptureGame()
        {
            return CaptureGame("unity-game");
        }

        public static string CaptureGame(string name)
        {
            if (!Application.isPlaying || SceneCamera.Instance == null) throw new InvalidOperationException("Game camera is not running.");
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_codex_work", "water-comparison");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            MonoBehaviour world = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .FirstOrDefault(component => component != null && component.gameObject.activeInHierarchy && component.GetType().Name == "WaterWorld");
            PropertyInfo sceneTextureProperty = world == null ? null : world.GetType().GetProperty("SceneTexture");
            RenderTexture scene = sceneTextureProperty == null ? null : sceneTextureProperty.GetValue(world) as RenderTexture;
            if (scene != null) SaveTexture(scene, Path.Combine(dir, name + "-scene.png"));
            MonoBehaviour water = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .FirstOrDefault(component => component != null && component.gameObject.activeInHierarchy && component.GetType().Name == "WaterPreview");
            if (water != null)
            {
                FieldInfo reflectionField = water.GetType().GetField("m_reflection", BindingFlags.NonPublic | BindingFlags.Instance);
                RenderTexture reflection = reflectionField == null ? null : reflectionField.GetValue(water) as RenderTexture;
                if (reflection != null) SaveTexture(reflection, Path.Combine(dir, name + "-reflection.png"));
            }
            return path;
        }

        public static JObject MeasureReflectionContent(RenderTexture scene, RenderTexture reflection, float horizon)
        {
            if (scene == null || reflection == null || !scene.IsCreated() || !reflection.IsCreated())
                throw new InvalidOperationException("Water reflection capture is unavailable.");
            Texture2D sceneImage = ReadTexture(scene);
            Texture2D reflectionImage = ReadTexture(reflection);
            try
            {
                int scenePixels, reflectionPixels;
                float sceneV = WarmCentroidV(sceneImage, out scenePixels);
                float reflectionV = WarmCentroidV(reflectionImage, out reflectionPixels);
                if (scenePixels == 0 || reflectionPixels == 0)
                    throw new InvalidOperationException("The water capture fixture's warm marker is missing.");
                float expectedV = 2f * (1f - horizon) - sceneV;
                return new JObject
                {
                    ["sourcePixels"] = scenePixels,
                    ["reflectionPixels"] = reflectionPixels,
                    ["sourceV"] = sceneV,
                    ["reflectionV"] = reflectionV,
                    ["expectedV"] = expectedV,
                    ["errorTexels"] = (reflectionV - expectedV) * reflection.height
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sceneImage);
                UnityEngine.Object.DestroyImmediate(reflectionImage);
            }
        }

        private static float WarmCentroidV(Texture2D image, out int count)
        {
            Color32[] pixels = image.GetPixels32();
            double sum = 0;
            count = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 color = pixels[i];
                if (color.r <= 130 || color.g <= 120 || color.b >= 100) continue;
                sum += i / image.width + .5;
                count++;
            }
            return count == 0 ? 0f : 1f - (float)(sum / count / image.height);
        }

        private static Texture2D ReadTexture(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active;
            Texture2D image = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = source;
                image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                image.Apply(false, false);
                return image;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(image);
                throw;
            }
            finally { RenderTexture.active = previous; }
        }

        private static void SaveTexture(RenderTexture source, string path)
        {
            Texture2D image = ReadTexture(source);
            try
            {
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        public static string RenderFixture() { return RenderFixtureCore(float.NaN); }

        public static string RenderFixtureAtCameraY(float cameraY) { return RenderFixtureCore(cameraY); }

        private static string RenderFixtureCore(float cameraYOverride)
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string directory = Path.Combine(root, "_codex_work", "water-comparison");
            JObject fixture = JObject.Parse(File.ReadAllText(Path.Combine(directory, "web-report.json")))["fixture"] as JObject;
            JObject body = (JObject)fixture["body"], cameraData = (JObject)fixture["camera"];
            if (!float.IsNaN(cameraYOverride)) cameraData["y"] = cameraYOverride;
            string web = Path.Combine(root, "Mods", "LevelEditor", "Web", "bpsim", "ultimate-water");
            JObject profile = JObject.Parse(File.ReadAllText(Path.Combine(web, "demo-profile.json")));
            JObject runtime = JObject.Parse(File.ReadAllText(Path.Combine(web, "runtime-profile.json")));
            JObject programs = JObject.Parse(File.ReadAllText(Path.Combine(web, "programs.json")));
            string overridesPath=Path.Combine(directory,"diagnostic-overrides.json");
            JObject uniformOverrides=File.Exists(overridesPath)?JObject.Parse(File.ReadAllText(overridesPath)):new JObject();
            float fixtureTime=(float)fixture["time"];
            Type waterAssets=System.Reflection.Assembly.Load(File.ReadAllBytes(Path.Combine(root,"Mods","LevelEditor","BpsimLevelEditor.dll"))).GetType("Bpsim.LevelEditor.WaterAssets");
            JObject lighting=JObject.Parse(File.ReadAllText(Path.Combine(web,"lighting-profile.json")));
            int width = 1280, height = 762;
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            source.LoadImage(File.ReadAllBytes(Path.Combine(directory, "web-source.png")));
            RenderTexture previousActive = RenderTexture.active;
            if (previousActive != null && !previousActive.IsCreated()) previousActive = null;
            Texture2D halfSource=new Texture2D(width/2,height/2,TextureFormat.RGBA32,false,true){wrapMode=TextureWrapMode.Clamp};
            RenderTexture downsample=new RenderTexture(width/2,height/2,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);downsample.Create();Graphics.Blit(source,downsample);
            try { RenderTexture.active=downsample;halfSource.ReadPixels(new Rect(0,0,width/2,height/2),0,0);halfSource.Apply(); }
            finally { RenderTexture.active=previousActive; }
            File.WriteAllBytes(Path.Combine(directory,"unity-half-source.png"),halfSource.EncodeToPNG());
            GameObject cameraObject = new GameObject("WaterParityCamera");
            Camera camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
            camera.orthographicSize = height / (float)cameraData["zoom"] * 4f; camera.aspect = (float)width / height;
            camera.transform.position = new Vector3((float)cameraData["x"] * 8, (float)cameraData["y"] * 8, -20);
            RenderTexture target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.Create(); Graphics.Blit(source, target);
            RenderTexture reflection = new RenderTexture(width/2,height/2,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear)
                { name="Water Parity Reflection",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp };
            reflection.Create();
            Material mirrorMaterial = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/LevelEditorModAssets/WaterMirror.shader"));
            float cameraBottom = camera.transform.position.y - camera.orthographicSize;
            float cameraHeight = camera.orthographicSize * 2f;
            float horizon = ((float)body["surface_y"] * 8f - cameraBottom) / cameraHeight;
            mirrorMaterial.SetFloat("_Horizon", horizon);
            Graphics.Blit(target, reflection, mirrorMaterial);
            Texture2D reflectionImage = new Texture2D(width/2,height/2,TextureFormat.RGBA32,false,true);
            try { RenderTexture.active=reflection; reflectionImage.ReadPixels(new Rect(0,0,width/2,height/2),0,0); reflectionImage.Apply(); }
            finally { RenderTexture.active=previousActive; }
            File.WriteAllBytes(Path.Combine(directory,"unity-reflection.png"),reflectionImage.EncodeToPNG());
            RenderTexture waterOnly = new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            waterOnly.Create();
            RenderTexture underwaterOnly = new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            underwaterOnly.Create();
            RenderTexture surfaceOnly = new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            surfaceOnly.Create();
            RenderTexture rays = new RenderTexture(width/2,height/2,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear); rays.Create();
            var created = new System.Collections.Generic.List<UnityEngine.Object> { source,halfSource,reflection,reflectionImage,mirrorMaterial,cameraObject,target,waterOnly,underwaterOnly,surfaceOnly,rays,downsample };
            CommandBuffer commands = new CommandBuffer();
            try
            {
                commands.SetRenderTarget(target);
                commands.SetGlobalVector("_ScreenParams",new Vector4(width,height,1f+1f/width,1f+1f/height));
                commands.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, false));
                foreach (string name in new[] { "LightRays", "Underwater", "WaterSurface" })
                {
                    commands.SetRenderTarget(name == "LightRays" ? rays : target);
                    commands.SetViewport(new Rect(0,0,name == "LightRays" ? width/2 : width,name == "LightRays" ? height/2 : height));
                    if(name == "LightRays") commands.ClearRenderTarget(false,true,Color.clear);
                    Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/LevelEditorModAssets/Ultimate" + name + ".shader");
                    if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Shader has compilation errors: " + name);
                    Material material = new Material(shader); created.Add(material);
                    JObject values = (JObject)profile["materials"][name == "WaterSurface" ? "SurfaceWater" : name] ?? new JObject();
                    foreach (JProperty prop in ((JObject)programs[name]["uniforms"]).Properties())
                    {
                        if (!material.HasProperty(prop.Name)) continue;
                        string type = (string)prop.Value["type"];
                        if (type == "sampler2D")
                        {
                            string textureName = (string)values["textures"]?[prop.Name]?["name"];
                            Texture texture = string.IsNullOrEmpty(textureName) ? Texture2D.whiteTexture :
                                AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/LevelEditorModAssets/Textures/UltimateWater/" + textureName + ".png");
                            if(!string.IsNullOrEmpty(textureName))
                            {
                                JToken info=profile["textures"][textureName];
                                texture=(Texture)waterAssets.GetMethod("LoadDataTexture").Invoke(null,new object[]{Path.Combine(web,textureName+".mips.bin"),(int)info["width"],(int)info["height"],(int)info["mip_count"],(int)info["color_space"]==1});created.Add(texture);
                            }
                            if(prop.Name=="_ShapeLightTexture0")
                            {
                                JToken info=lighting[name];Texture2D light=new Texture2D((int)info["width"],(int)info["height"],TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                                light.LoadRawTextureData(File.ReadAllBytes(Path.Combine(web,(string)info["file"])));light.Apply(false,true);texture=light;created.Add(light);
                            }
                            if (prop.Name.Contains("Ripple") && prop.Name.EndsWith("Tex")) texture = Texture2D.blackTexture;
                            if (prop.Name == "_LocalReflectionMap") texture = reflection;
                            if (prop.Name == "_CameraSortingLayerTexture") texture = target;
                            if (prop.Name == "_LocalUnderwaterSurfaceMap") texture = halfSource;
                            material.SetTexture(prop.Name, texture);
                            continue;
                        }
                        JToken value = uniformOverrides[name]?[prop.Name] ?? runtime[name]?[prop.Name] ?? values["floats"]?[prop.Name] ?? values["vectors"]?[prop.Name] ?? profile["shaders"][name]["properties"]?[prop.Name];
                        if(prop.Name=="_Time")value=new JArray(fixtureTime/20f,fixtureTime,fixtureTime*2f,fixtureTime*3f);
                        else if(prop.Name=="_TimeParameters")value=new JArray(fixtureTime,Mathf.Sin(fixtureTime),Mathf.Cos(fixtureTime),0);
                        else if(prop.Name=="_ProjectionParams")value=new JArray(1,0,0,0);
                        else if(prop.Name=="_ScaledScreenParams")value=new JArray(width,height,0,0);
                        else if(prop.Name=="_GlobalMipBias")value=new JArray(0,0);
                        else if(prop.Name=="_RippleCount")value=0;
                        else if(prop.Name=="_WaveAmplitude")value=(float)body["wave_amplitude"]*8f;
                        else if(prop.Name=="_WaveFrequency")value=Mathf.PI*2f/((float)body["wave_length"]*8f);
                        else if(prop.Name=="_WaveSpeed")value=(float)body["wave_speed"];
                        else if(prop.Name=="_LocalXYBounds")value=new JArray(-(float)body["width"]*4f,(float)body["width"]*4f,-(float)body["depth"]*8f,0);
                        if (value == null) continue;
                        JArray v = value as JArray;
                        if (v == null) material.SetFloat(prop.Name, (float)value);
                        else material.SetVector(prop.Name, new Vector4((float)v[0], v.Count > 1 ? (float)v[1] : 0, v.Count > 2 ? (float)v[2] : 0, v.Count > 3 ? (float)v[3] : 0));
                    }
                    float left = (float)body["x"] * 8, right = left + (float)body["width"] * 8;
                    float surface = (float)body["surface_y"] * 8;
                    float top = name == "WaterSurface" ? surface + .445625f * 8 : surface;
                    float bottom = name == "WaterSurface" ? surface : surface - (float)body["depth"] * 8;
                    material.SetVector("uw_Bounds", new Vector4(left, surface, right - left, (float)body["depth"] * 8));
                    JArray lightRect=lighting[name]?["rect"] as JArray;
                    if(lightRect!=null)material.SetVector("uw_LightRect",new Vector4((float)lightRect[0],(float)lightRect[1],(float)lightRect[2],(float)lightRect[3]));
                    material.SetVector("uw_CaptureRect", new Vector4(camera.transform.position.x - camera.orthographicSize * camera.aspect,
                        camera.transform.position.y - camera.orthographicSize, camera.orthographicSize * camera.aspect * 2, camera.orthographicSize * 2));
                    material.SetFloat("uw_Scale", 1); material.SetFloat("uw_CapturesLinear", 0);
                    material.SetFloat("uw_Clock", fixtureTime); material.SetTexture("uw_Terrain", Texture2D.blackTexture);
                    material.SetTexture("uw_Rays",name == "LightRays" ? (Texture)Texture2D.blackTexture : rays);
                    if(name == "LightRays")
                    {
                        JArray rayData=JArray.Parse(File.ReadAllText(Path.Combine(directory,"ray-frame.json")));
                        int count=rayData.Count/13; Vector3[] positions=new Vector3[count]; Vector2[] uv=new Vector2[count],seed=new Vector2[count];
                        Color[] colors=new Color[count]; var extra=new System.Collections.Generic.List<Vector2>(count); int[] indices=new int[count];
                        for(int i=0;i<count;i++)
                        {
                            int n=i*13; positions[i]=new Vector3((float)rayData[n],(float)rayData[n+1],(float)rayData[n+2]);
                            uv[i]=new Vector2((float)rayData[n+3],(float)rayData[n+4]); seed[i]=new Vector2((float)rayData[n+5],(float)rayData[n+6]);
                            extra.Add(new Vector2((float)rayData[n+7],(float)rayData[n+8]));
                            colors[i]=new Color((float)rayData[n+9],(float)rayData[n+10],(float)rayData[n+11],(float)rayData[n+12]); indices[i]=i;
                        }
                        Mesh rayMesh=new Mesh { vertices=positions,uv=uv,uv2=seed,colors=colors,triangles=indices };
                        rayMesh.SetUVs(2,extra); created.Add(rayMesh); commands.DrawMesh(rayMesh,Matrix4x4.identity,material); continue;
                    }
                    Mesh mesh = new Mesh { vertices = new[] { new Vector3(left,bottom),new Vector3(right,bottom),new Vector3(left,top),new Vector3(right,top) },
                        uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one }, triangles = new[] { 0,1,2,2,1,3 } };
                    if(name=="WaterSurface")
                    {
                        JObject info=JObject.Parse(File.ReadAllText(Path.Combine(web,"surface-mesh.json")));int count=(int)info["vertices"],indexCount=(int)info["indices"];
                        byte[] bytes=File.ReadAllBytes(Path.Combine(web,"surface-mesh.bin"));float[] data=new float[count*4];Buffer.BlockCopy(bytes,0,data,0,count*16);
                        Vector3[] positions=new Vector3[count];Vector2[] uv=new Vector2[count],local=new Vector2[count];int[] indices=new int[indexCount];
                        for(int i=0;i<count;i++){float x=data[i*4],z=data[i*4+1],p=19.4f/(19.4f+z);positions[i]=new Vector3((left+right)*.5f+x*p,surface+3.8f*(1-p),0);uv[i]=new Vector2(data[i*4+2],data[i*4+3]);local[i]=new Vector2(x,z);}
                        for(int i=0;i<indexCount;i++)indices[i]=BitConverter.ToUInt16(bytes,count*16+i*2);
                        mesh.Clear();mesh.vertices=positions;mesh.uv=uv;mesh.uv2=local;mesh.triangles=indices;
                    }
                    created.Add(mesh);
                    if(name!="LightRays")
                    {
                        commands.SetRenderTarget(waterOnly);commands.SetViewport(new Rect(0,0,width,height));
                        if(name=="Underwater")commands.ClearRenderTarget(false,true,Color.clear);
                        commands.DrawMesh(mesh,Matrix4x4.identity,material);
                        RenderTexture passTarget=name=="Underwater"?underwaterOnly:surfaceOnly;
                        commands.SetRenderTarget(passTarget);commands.SetViewport(new Rect(0,0,width,height));
                        commands.ClearRenderTarget(false,true,Color.clear);
                        commands.DrawMesh(mesh,Matrix4x4.identity,material);
                        commands.SetRenderTarget(target);commands.SetViewport(new Rect(0,0,width,height));
                    }
                    commands.DrawMesh(mesh, Matrix4x4.identity, material);
                }
                Graphics.ExecuteCommandBuffer(commands);
                RenderTexture.active = target;
                Texture2D image = new Texture2D(width,height,TextureFormat.RGBA32,false,true); created.Add(image);
                image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply(); RenderTexture.active = previousActive;
                string output = Path.Combine(directory,"unity-composite.png"); File.WriteAllBytes(output,image.EncodeToPNG());
                RenderTexture.active=waterOnly;
                Texture2D waterImage=new Texture2D(width,height,TextureFormat.RGBA32,false,true);created.Add(waterImage);
                waterImage.ReadPixels(new Rect(0,0,width,height),0,0);waterImage.Apply();RenderTexture.active=previousActive;
                File.WriteAllBytes(Path.Combine(directory,"unity-water.png"),waterImage.EncodeToPNG());
                Texture2D underwaterImage=new Texture2D(width,height,TextureFormat.RGBA32,false,true);created.Add(underwaterImage);
                RenderTexture.active=underwaterOnly;underwaterImage.ReadPixels(new Rect(0,0,width,height),0,0);underwaterImage.Apply();RenderTexture.active=previousActive;
                File.WriteAllBytes(Path.Combine(directory,"unity-underwater.png"),underwaterImage.EncodeToPNG());
                Texture2D surfaceImage=new Texture2D(width,height,TextureFormat.RGBA32,false,true);created.Add(surfaceImage);
                RenderTexture.active=surfaceOnly;surfaceImage.ReadPixels(new Rect(0,0,width,height),0,0);surfaceImage.Apply();RenderTexture.active=previousActive;
                File.WriteAllBytes(Path.Combine(directory,"unity-surface.png"),surfaceImage.EncodeToPNG());return output;
            }
            finally
            {
                RenderTexture.active=previousActive;
                commands.Release(); target.Release(); waterOnly.Release(); underwaterOnly.Release(); surfaceOnly.Release(); rays.Release(); downsample.Release();
                foreach (UnityEngine.Object item in created) UnityEngine.Object.DestroyImmediate(item);
            }
        }
    }
}
#endif
