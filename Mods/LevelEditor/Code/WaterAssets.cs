using System;
using System.IO;
using Bpsim.Modding;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Bpsim.LevelEditor
{
	public sealed class WaterAssets : IDisposable
	{
		private AssetBundle m_bundle;
		public Shader WaterShader { get; private set; }
		public Shader MaskShader { get; private set; }
		public Shader SurfaceShader { get; private set; }
		public Shader UnderwaterShader { get; private set; }
		public Shader MirrorShader { get; private set; }
		public Shader LightRaysShader { get; private set; }
		public JObject OriginalProfile { get; private set; }
		private JObject m_runtimeProfile;
		private byte[] m_surfaceGeometry;
		private JObject m_surfaceInfo;
		private JObject m_lightingProfile;
		private readonly System.Collections.Generic.Dictionary<string, Texture2D> m_dataTextures = new System.Collections.Generic.Dictionary<string, Texture2D>();
		private readonly System.Collections.Generic.Dictionary<string, Texture2D> m_lighting = new System.Collections.Generic.Dictionary<string, Texture2D>();
		public JObject LightRaysGeometry { get; private set; }
		public bool HasOriginalPrograms { get { return SurfaceShader != null && SurfaceShader.isSupported && UnderwaterShader != null && UnderwaterShader.isSupported && OriginalProfile != null; } }
		public Texture2D NormalTexture { get; private set; }
		public Texture2D FoamTexture { get; private set; }
		public Texture2D CausticTexture { get; private set; }
		public Texture2D SurfaceFoamTexture { get; private set; }
		public JObject Style { get; private set; }
		public bool HasAdvancedEffects
		{
			get
			{
				return WaterShader != null && MaskShader != null && Style != null && NormalTexture != null &&
					FoamTexture != null && CausticTexture != null && SurfaceFoamTexture != null;
			}
		}

		public WaterAssets(ModEntryPoint owner)
		{
			// Shader code is carried by the optional Mod, not a permanent host renderer feature.
			m_bundle = AssetBundle.LoadFromFile(owner.Path("Assets/water-windows.bundle"));
			if (m_bundle != null)
			{
				WaterShader = m_bundle.LoadAsset<Shader>("assets/editor/leveleditormodassets/water.shader");
				MaskShader = m_bundle.LoadAsset<Shader>("assets/editor/leveleditormodassets/terrainmask.shader");
				SurfaceShader = m_bundle.LoadAsset<Shader>("assets/editor/leveleditormodassets/ultimatewatersurface.shader");
				UnderwaterShader = m_bundle.LoadAsset<Shader>("assets/editor/leveleditormodassets/ultimateunderwater.shader");
				MirrorShader = m_bundle.LoadAsset<Shader>("assets/editor/leveleditormodassets/watermirror.shader");
				LightRaysShader = m_bundle.LoadAsset<Shader>("assets/editor/leveleditormodassets/ultimatelightrays.shader");
				Texture2D[] textures = m_bundle.LoadAllAssets<Texture2D>();
				NormalTexture = FindTexture(textures, "WaterNormals_01");
				FoamTexture = FindTexture(textures, "Foam_01");
				CausticTexture = FindTexture(textures, "Foam_02");
				SurfaceFoamTexture = FindTexture(textures, "Foam_03");
			}
			string profilePath = owner.Path("Web/bpsim/ultimate-water/style.json");
			if (File.Exists(profilePath)) Style = JObject.Parse(File.ReadAllText(profilePath));
			else owner.Log.Warn("缺少 Ultimate Water 2D 样式档案：" + profilePath);
			string originalPath = owner.Path("Web/bpsim/ultimate-water/demo-profile.json");
			if (File.Exists(originalPath)) OriginalProfile = JObject.Parse(File.ReadAllText(originalPath));
			string surfacePath=owner.Path("Web/bpsim/ultimate-water/surface-mesh.bin");
			if(File.Exists(surfacePath)) { m_surfaceGeometry=File.ReadAllBytes(surfacePath);m_surfaceInfo=JObject.Parse(File.ReadAllText(owner.Path("Web/bpsim/ultimate-water/surface-mesh.json"))); }
			if (OriginalProfile != null) foreach (JProperty item in ((JObject)OriginalProfile["textures"]).Properties())
			{
				string path = owner.Path("Web/bpsim/ultimate-water/" + item.Name + ".mips.bin");
				if (!File.Exists(path)) continue;
				m_dataTextures[item.Name] = LoadDataTexture(path,(int)item.Value["width"],(int)item.Value["height"],(int)item.Value["mip_count"],(int)item.Value["color_space"] == 1);
			}
			string lightingPath = owner.Path("Web/bpsim/ultimate-water/lighting-profile.json");
			if (File.Exists(lightingPath)) m_lightingProfile=JObject.Parse(File.ReadAllText(lightingPath));
			if (m_lightingProfile != null) foreach (JProperty item in m_lightingProfile.Properties())
			{
				Texture2D texture = new Texture2D((int)item.Value["width"],(int)item.Value["height"],TextureFormat.RGBAFloat,false,true) { filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp };
				texture.LoadRawTextureData(File.ReadAllBytes(owner.Path("Web/bpsim/ultimate-water/"+(string)item.Value["file"])));texture.Apply(false,true);m_lighting[item.Name]=texture;
			}
			string runtimePath = owner.Path("Web/bpsim/ultimate-water/runtime-profile.json");
			if (File.Exists(runtimePath)) m_runtimeProfile = JObject.Parse(File.ReadAllText(runtimePath));
			string raysPath = owner.Path("Web/bpsim/ultimate-water/light-rays-mesh.json");
			if (File.Exists(raysPath)) LightRaysGeometry = JObject.Parse(File.ReadAllText(raysPath));
			if (WaterShader == null || !WaterShader.isSupported)
			{
				WaterShader = null;
				owner.Log.Warn("Ultimate Water 2D 水体着色器不可用；请重新安装当前平台的完整 Mod 包。");
			}
			if (WaterShader != null && (NormalTexture == null || FoamTexture == null || CausticTexture == null || SurfaceFoamTexture == null))
				owner.Log.Warn("Ultimate Water 2D 原始纹理不完整；请重新安装当前平台的完整 Mod 包。");
		}

		public Material CreateOriginalMaterial(bool surface)
		{
			return CreateOriginalMaterial(surface ? "WaterSurface" : "Underwater");
		}

		public Material CreateOriginalMaterial(string program)
		{
			bool surface = program == "WaterSurface";
			Material material = new Material(surface ? SurfaceShader : program == "LightRays" ? LightRaysShader : UnderwaterShader) { name = "Ultimate " + program };
			JObject defaults = (JObject)OriginalProfile["shaders"][program]["properties"];
			foreach (JProperty value in defaults.Properties())
			{
				if (!material.HasProperty(value.Name)) continue;
				JArray vector = value.Value as JArray;
				if (vector == null) material.SetFloat(value.Name, (float)value.Value);
				else material.SetVector(value.Name, new Vector4((float)vector[0], (float)vector[1], (float)vector[2], (float)vector[3]));
			}
			JObject values = (JObject)OriginalProfile["materials"][surface ? "SurfaceWater" : program] ?? new JObject { ["floats"] = new JObject(), ["vectors"] = new JObject(), ["textures"] = new JObject() };
			foreach (JProperty value in ((JObject)values["floats"]).Properties())
				if (material.HasProperty(value.Name)) material.SetFloat(value.Name, (float)value.Value);
			foreach (JProperty value in ((JObject)values["vectors"]).Properties())
			{
				JArray v = (JArray)value.Value;
				if (material.HasProperty(value.Name)) material.SetVector(value.Name, new Vector4((float)v[0], (float)v[1], (float)v[2], (float)v[3]));
			}
			foreach (JProperty value in ((JObject)values["textures"]).Properties())
			{
				if (!material.HasProperty(value.Name)) continue;
				string name = (string)value.Value["name"];
				Texture texture = name == "WaterNormals_01" ? NormalTexture : name == "Foam_01" ? FoamTexture :
					name == "Foam_02" ? CausticTexture : name == "Foam_03" ? SurfaceFoamTexture : Texture2D.whiteTexture;
				Texture2D original;
				if (name != null && m_dataTextures.TryGetValue(name,out original)) texture=original;
				material.SetTexture(value.Name, texture);
			}
			foreach (string key in new[] { "_WaterInteractionsEnabled", "_ImpactRippleVisualEnabled", "_WakeRippleVisualEnabled" })
				if (material.HasProperty(key)) material.SetFloat(key, 1);
			JObject runtime = m_runtimeProfile == null ? null : m_runtimeProfile[program] as JObject;
			if (runtime != null) foreach (JProperty value in runtime.Properties())
			{
				if (!material.HasProperty(value.Name)) continue;
				if (material.shader.GetPropertyType(material.shader.FindPropertyIndex(value.Name)) == UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
				JArray vector = value.Value as JArray;
				if (vector == null) material.SetFloat(value.Name, (float)value.Value);
				else material.SetVector(value.Name, new Vector4((float)vector[0], vector.Count > 1 ? (float)vector[1] : 0, vector.Count > 2 ? (float)vector[2] : 0, vector.Count > 3 ? (float)vector[3] : 0));
			}
			Texture2D lighting;
			if (material.HasProperty("_ShapeLightTexture0") && m_lighting.TryGetValue(program,out lighting)) material.SetTexture("_ShapeLightTexture0",lighting);
			return material;
		}

		public static Texture2D LoadDataTexture(string path,int width,int height,int mipCount,bool srgb)
		{
			Texture2D texture=new Texture2D(width,height,TextureFormat.RGBAFloat,mipCount,true) { name=Path.GetFileName(path).Replace(".mips.bin",""),filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Repeat,anisoLevel=1 };
			byte[] bytes=File.ReadAllBytes(path);int offset=0;
			for(int level=0;level<mipCount;level++)
			{
				int count=Mathf.Max(1,width>>level)*Mathf.Max(1,height>>level);Color[] colors=new Color[count];
				for(int i=0;i<count;i++,offset+=4)
				{
					Color color=new Color(bytes[offset]/255f,bytes[offset+1]/255f,bytes[offset+2]/255f,bytes[offset+3]/255f);
					if(srgb) color=new Color(Mathf.GammaToLinearSpace(color.r),Mathf.GammaToLinearSpace(color.g),Mathf.GammaToLinearSpace(color.b),color.a);
					colors[i]=color;
				}
				texture.SetPixels(colors,level);
			}
			texture.Apply(false,true);return texture;
		}

		public Mesh CreateSurfaceMesh(float width,float scale)
		{
			int count=(int)m_surfaceInfo["vertices"],indexCount=(int)m_surfaceInfo["indices"];
			float[] data=new float[count*4];Buffer.BlockCopy(m_surfaceGeometry,0,data,0,count*16);
			Vector3[] vertices=new Vector3[count];Vector2[] uv=new Vector2[count],local=new Vector2[count];int[] indices=new int[indexCount];
			for(int i=0;i<count;i++)
			{
				float x=data[i*4]*width/scale/303.75f,z=data[i*4+1],p=19.4f/(19.4f+z);
				vertices[i]=new Vector3(width*.5f+x*p*scale,3.8f*(1-p)*scale,-.001f);
				uv[i]=new Vector2(data[i*4+2],data[i*4+3]);local[i]=new Vector2(x,z);
			}
			for(int i=0;i<indexCount;i++)indices[i]=BitConverter.ToUInt16(m_surfaceGeometry,count*16+i*2);
			Mesh mesh=new Mesh { name="Ultimate Water Original Surface",vertices=vertices,uv=uv,uv2=local,triangles=indices };
			mesh.bounds=new Bounds(new Vector3(width*.5f,scale*1.9f,0),new Vector3(width*3,scale*8,1));return mesh;
		}

		public void SetLightingBounds(Material material,string program,Vector4 bounds,float scale)
		{
			JArray rect=m_lightingProfile == null ? null : m_lightingProfile[program]?["rect"] as JArray;
			if(rect==null)return;
			material.SetVector("uw_LightRect",new Vector4(bounds.x+bounds.z*.5f+(float)rect[0]*scale,bounds.y+(float)rect[1]*scale,(float)rect[2]*scale,(float)rect[3]*scale));
		}

		private static Texture2D FindTexture(Texture2D[] textures, string name)
		{
			for (int i = 0; i < textures.Length; i++)
				if (textures[i] != null && textures[i].name == name) return textures[i];
			return null;
		}

		public void Dispose()
		{
			foreach(Texture2D texture in m_dataTextures.Values) UnityEngine.Object.Destroy(texture);m_dataTextures.Clear();
			foreach(Texture2D texture in m_lighting.Values) UnityEngine.Object.Destroy(texture);m_lighting.Clear();
			WaterShader = null; MaskShader = null;
			SurfaceShader = null; UnderwaterShader = null; OriginalProfile = null; m_runtimeProfile = null;
			MirrorShader = null; LightRaysShader = null; LightRaysGeometry = null;
			NormalTexture = null; FoamTexture = null; CausticTexture = null; SurfaceFoamTexture = null; Style = null;
			if (m_bundle != null) { m_bundle.Unload(true); m_bundle = null; }
		}
	}
}
