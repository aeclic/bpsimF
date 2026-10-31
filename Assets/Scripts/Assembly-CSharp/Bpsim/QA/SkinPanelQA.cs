using System;
using System.IO;
using Bpsim.Parts;
using Bpsim.Parts.Skin;
using Bpsim.UI;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.QA
{
	/// <summary>蒙皮面板的运行时自检：直接造一个只有 <see cref="SkinSheet"/> 的实体并打开 UGUI 面板。</summary>
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

		/// <summary>读回组件，确认面板输入的数值真的落进了 <see cref="SkinSheet"/>。</summary>
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

		/// <summary>像玩家在面板里一样改曲率，走 SkinPanelHost 的原生写回路径。</summary>
		public static string SimulateApply(string curve)
		{
			if (!float.TryParse(curve, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
			{
				return "bad curve";
			}
			return SkinPanelHost.Instance.ApplyCurveForQa(value);
		}

		/// <summary>用程序生成一张棋盘贴图并打开裁剪窗，验证遮罩、缩放与拖选。</summary>
		public static string OpenCropDemo()
		{
			const int size = 256;
			Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
			{
				name = "qa-skin-atlas"
			};
			Color32[] pixels = new Color32[size * size];
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					int cell = ((x / 32) + (y / 32)) & 1;
					Color32 color = cell == 0 ? new Color32(58, 74, 96, 255) : new Color32(96, 150, 176, 255);
					if (x > 150 && y > 150 && x < 210 && y < 210) color = new Color32(230, 170, 64, 255);
					pixels[y * size + x] = color;
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply();
			SkinAtlasCropDialog.Instance.Open(texture, id =>
			{
				s_log = "crop=" + id;
				// 和 SkinPanelHost.OpenCrop 走同一条回写路径，确保裁剪结果真的进组件。
				System.Reflection.MethodInfo apply = typeof(SkinPanelHost).GetMethod(
					"ApplyTextureId",
					System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
				apply?.Invoke(SkinPanelHost.Instance, new object[] { id });
			});
			s_log = "crop demo open";
			return s_log;
		}

		/// <summary>把面板滚动到指定归一化位置，便于截图检查底部卡片。</summary>
		public static string ScrollTo(string value)
		{
			if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float position))
			{
				return "bad value";
			}
			System.Reflection.FieldInfo field = typeof(SkinPanelHost).GetField("m_scroll", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			ScrollRect scroll = field?.GetValue(SkinPanelHost.Instance) as ScrollRect;
			if (scroll == null)
			{
				return "no scroll";
			}
			scroll.verticalNormalizedPosition = position;
			Canvas.ForceUpdateCanvases();
			return "scroll=" + scroll.verticalNormalizedPosition.ToString("0.###");
		}

		/// <summary>设一个落在橙色块上的选区并走「使用裁剪」，确认尺寸与 y 朝向。</summary>
		public static string CropApplyTest()
		{
			SkinAtlasCropDialog dialog = SkinAtlasCropDialog.Instance;
			if (!dialog.IsOpen)
			{
				return "crop dialog not open";
			}
			Type type = typeof(SkinAtlasCropDialog);
			const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
			// ApplyCrop 会先 ApplyFields()，所以这里改写四个输入框而不是内部字段。
			((InputField)type.GetField("m_x", flags)?.GetValue(dialog))?.SetTextWithoutNotify("150");
			((InputField)type.GetField("m_y", flags)?.GetValue(dialog))?.SetTextWithoutNotify("46");
			((InputField)type.GetField("m_width", flags)?.GetValue(dialog))?.SetTextWithoutNotify("60");
			((InputField)type.GetField("m_height", flags)?.GetValue(dialog))?.SetTextWithoutNotify("60");
			type.GetMethod("ApplyCrop", flags)?.Invoke(dialog, null);
			if (dialog.IsOpen)
			{
				return "crop dialog still open";
			}

			World world = World.DefaultGameObjectInjectionWorld;
			if (world == null || s_part == Entity.Null || !world.EntityManager.Exists(s_part))
			{
				return "no part";
			}
			SkinSheet sheet = world.EntityManager.GetComponentData<SkinSheet>(s_part);
			Texture2D texture = SkinSheetRegistry.Instance.GetTexture(sheet.TextureHandle);
			if (texture == null)
			{
				return "no texture";
			}
			string pixel;
			try
			{
				Color center = texture.GetPixel(texture.width / 2, texture.height / 2);
				pixel = "center=(" + Mathf.RoundToInt(center.r * 255f) + "," + Mathf.RoundToInt(center.g * 255f) + "," + Mathf.RoundToInt(center.b * 255f) + ")";
			}
			catch (Exception ex)
			{
				pixel = "unreadable:" + ex.GetType().Name;
			}
			return "cropped " + texture.width + "x" + texture.height + " " + pixel + " version=" + sheet.Version;
		}

		/// <summary>把 Game 视图（含 Screen Space Overlay 的 UGUI）写成工程内 PNG，供桥接读回检查。</summary>
		public static string Shot(string relativePath)
		{
			try
			{
				Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
				if (shot == null) return "shot=null";
				byte[] png = shot.EncodeToPNG();
				int width = shot.width;
				int height = shot.height;
				UnityEngine.Object.Destroy(shot);
				string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));
				Directory.CreateDirectory(Path.GetDirectoryName(full));
				File.WriteAllBytes(full, png);
				s_log = "shot " + relativePath + " " + width + "x" + height;
				Debug.Log("[SkinPanelQA] " + s_log);
				return s_log;
			}
			catch (Exception exception)
			{
				return "shot failed: " + exception.Message;
			}
		}

		/// <summary>打印蒙皮面板的 RectTransform 几何，用于定位布局偏差（坐标是 Canvas 局部坐标）。</summary>
		public static string Layout()
		{
			GameObject root = GameObject.Find("SkinPanelCanvas");
			if (root == null) return "no canvas";
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			DumpRect(root.transform, 0, sb);
			return sb.ToString();
		}

		private static void DumpRect(Transform t, int depth, System.Text.StringBuilder sb)
		{
			if (depth > 4) return;
			RectTransform rt = t as RectTransform;
			if (rt != null)
			{
				Vector3[] corners = new Vector3[4];
				rt.GetWorldCorners(corners);
				sb.Append(new string(' ', depth * 2)).Append(t.name)
					.Append(" r=").Append(rt.rect.x.ToString("0.")).Append(',').Append(rt.rect.y.ToString("0.")).Append(',').Append(rt.rect.width.ToString("0.")).Append('x').Append(rt.rect.height.ToString("0."))
					.Append(" a=").Append(rt.anchorMin.x.ToString("0.##")).Append(',').Append(rt.anchorMin.y.ToString("0.##")).Append('-').Append(rt.anchorMax.x.ToString("0.##")).Append(',').Append(rt.anchorMax.y.ToString("0.##"))
					.Append(" p=").Append(rt.pivot.x.ToString("0.##")).Append(',').Append(rt.pivot.y.ToString("0.##"))
					.Append(" pos=").Append(rt.anchoredPosition.x.ToString("0.")).Append(',').Append(rt.anchoredPosition.y.ToString("0."))
					.Append(" sd=").Append(rt.sizeDelta.x.ToString("0.")).Append(',').Append(rt.sizeDelta.y.ToString("0."))
					.Append(" wx=").Append(corners[0].x.ToString("0.")).Append("..").Append(corners[2].x.ToString("0."))
					.Append('\n');
			}
			for (int i = 0; i < t.childCount; i++) DumpRect(t.GetChild(i), depth + 1, sb);
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
