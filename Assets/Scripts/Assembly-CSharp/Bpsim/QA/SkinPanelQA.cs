using Bpsim.Parts;
using Bpsim.Parts.Skin;
using Bpsim.UI;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.QA
{
	/// <summary>
	/// 蒙皮网页面板的运行时自检：不需要进关卡，直接造一个只有 <see cref="SkinSheet"/> 的实体，
	/// 把面板叫起来，然后靠 <see cref="SkinPanelHost.Describe"/> 判断网页是否真的加载完成。
	/// </summary>
	public static class SkinPanelQA
	{
		private static Entity s_part = Entity.Null;

		private static string s_log = string.Empty;

		public static string Open()
		{
			World world = World.DefaultGameObjectInjectionWorld;
			if (world == null)
			{
				return "no world";
			}
			EntityManager em = world.EntityManager;
			Close();
			s_part = em.CreateEntity();
			em.AddComponentData(s_part, SkinSheetDefaults.Create());
			em.AddComponentData(s_part, new SkinSheetMesh
			{
				Handle = 0,
				BuiltVersion = int.MinValue,
				BuiltScale = new float2(1f, 1f)
			});
			em.AddComponentData(s_part, LocalTransform.FromPosition(new float3(0f, 0f, 0f)));
			em.AddComponentData(s_part, new LocalToWorld
			{
				Value = float4x4.identity
			});
			SkinPanelHost.Instance.Open(s_part);
			s_log = "opened " + s_part.Index;
			Debug.Log("[SkinPanelQA] " + s_log);
			return s_log;
		}

		public static string ApplyCurve(float curve)
		{
			World world = World.DefaultGameObjectInjectionWorld;
			if (world == null || s_part == Entity.Null)
			{
				return "no part";
			}
			EntityManager em = world.EntityManager;
			if (!em.Exists(s_part) || !em.HasComponent<SkinSheet>(s_part))
			{
				return "no part";
			}
			SkinSheet sheet = em.GetComponentData<SkinSheet>(s_part);
			sheet.Curve = new float4(curve, curve, curve, curve);
			sheet.Color = new float4(1f, 0.72f, 0.35f, 1f);
			sheet.Version++;
			em.SetComponentData(s_part, sheet);
			return "curve=" + curve + " version=" + sheet.Version;
		}

		public static string Status()
		{
			string panel = SkinPanelHost.Instance.Describe();
			World world = World.DefaultGameObjectInjectionWorld;
			string entity = "none";
			string mesh = "none";
			if (world != null && s_part != Entity.Null && world.EntityManager.Exists(s_part))
			{
				EntityManager em = world.EntityManager;
				entity = s_part.Index.ToString();
				if (em.HasComponent<SkinSheetMesh>(s_part))
				{
					SkinSheetMesh data = em.GetComponentData<SkinSheetMesh>(s_part);
					Mesh built = SkinSheetRegistry.Instance.GetMesh(data.Handle);
					mesh = "handle=" + data.Handle + " built=" + data.BuiltVersion + " verts=" + (built == null ? 0 : built.vertexCount);
				}
			}
			return panel + " entity=" + entity + " mesh[" + mesh + "]";
		}

		/// <summary>读回组件，确认网页发来的数值真的落进了 <see cref="SkinSheet"/>。</summary>
		public static string Sheet()
		{
			World world = World.DefaultGameObjectInjectionWorld;
			if (world == null || s_part == Entity.Null || !world.EntityManager.Exists(s_part))
			{
				return "no part";
			}
			if (!world.EntityManager.HasComponent<SkinSheet>(s_part))
			{
				return "no sheet";
			}
			SkinSheet sheet = world.EntityManager.GetComponentData<SkinSheet>(s_part);
			return "version=" + sheet.Version
				+ " curve=(" + sheet.Curve.x.ToString("0.###") + "," + sheet.Curve.y.ToString("0.###") + "," + sheet.Curve.z.ToString("0.###") + "," + sheet.Curve.w.ToString("0.###") + ")"
				+ " tl=(" + sheet.TopLeft.x.ToString("0.###") + "," + sheet.TopLeft.y.ToString("0.###") + ")"
				+ " color=(" + sheet.Color.x.ToString("0.##") + "," + sheet.Color.z.ToString("0.##") + ")";
		}

		/// <summary>
		/// 像玩家在面板里那样改曲率：直接在网页里改输入框并触发 change，
		/// 于是走的是「网页 → vuplex.postMessage → SkinPanelHost.Apply → 组件」这条真实链路。
		/// </summary>
		public static string SimulateApply(string curve)
		{
			Vuplex.WebView.IWebView view = SkinPanelHost.Instance.WebView;
			if (view == null)
			{
				return "no webview";
			}
			string javaScript = "['cb-0','cb-1','cb-2','cb-3'].forEach(function(id){var e=document.getElementById(id);if(e){e.value='" + curve + "';e.dispatchEvent(new Event('change'));}});";
			view.ExecuteJavaScript(javaScript);
			return "sent";
		}

		public static void Close()
		{
			if (s_part != Entity.Null)
			{
				World world = World.DefaultGameObjectInjectionWorld;
				if (world != null && world.EntityManager.Exists(s_part))
				{
					world.EntityManager.DestroyEntity(s_part);
				}
				s_part = Entity.Null;
			}
			SkinPanelHost.Instance.Close();
		}
	}
}
