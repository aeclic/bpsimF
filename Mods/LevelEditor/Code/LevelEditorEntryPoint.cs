using System;
using System.Collections.Generic;
using Bpsim.Modding;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Bpsim.LevelEditor
{
	public sealed class LevelEditorEntryPoint : ModEntryPoint
	{
		private ModDataStore m_store;
		private ModWebService m_server;
		private LevelDocument m_document;
		private EditorWindow m_window;
		private ModFluidSession m_fluids;
		private WaterAssets m_waterAssets;
		private readonly List<GameObject> m_waterRoots = new List<GameObject>();
		public string EditorUrl { get { return m_server == null ? null : m_server.PageUrl.Replace("/index.html#", "/badpiggies-editor/index.html#"); } }

		public override void OnLoad()
		{
			m_store = ModDataStore.Open(this, "Levels");
			m_fluids = ModFluidSession.Open(this);
			ModLevels.RegisterCatalog(this, ReadCatalog);
			ModLevels.SubscribeLoaded(this, OnLevelLoaded);
			ModLevelMenu.AddButton(this, "关卡编辑器", OpenEditor);
			Context.Ui.AddQuickButton("打开关卡编辑器", OpenEditor);
			ModHostResources.Own(this, new Cleanup(CloseResources));
			Log.Info("关卡编辑器已启用；未打开编辑器时不启动网页服务。");
		}

		public override void OnUnload() { CloseResources(); }
		public override bool IsRequiredForLevel(string levelId)
		{
			return levelId != null && levelId.StartsWith("mod:" + Id.ToString("N") + ":", StringComparison.Ordinal);
		}

		private IEnumerable<ModLevelDefinition> ReadCatalog()
		{
			List<ModLevelDefinition> definitions = new List<ModLevelDefinition>();
			foreach (string name in m_store.Files(".bpslevel"))
			{
				try { definitions.Add(LevelDocument.Parse(m_store.ReadText(name)).Definition()); }
				catch (Exception ex) { Log.Warn("保留但跳过无法读取的关卡包 " + name + "：" + ex.Message); }
			}
			return definitions;
		}

		public void OpenEditor()
		{
			try
			{
				if (m_document == null)
				{
					ModLevelDefinition current = ModLevels.CaptureCurrent();
					if (current == null)
					{
						ModLevelDefinition[] builtIn = ModLevels.BuiltInLevels();
						if (builtIn.Length == 0) throw new InvalidOperationException("没有可编辑的关卡。请先准备 BPLE 关卡资源。");
						current = ModLevels.ReadBuiltIn(builtIn[0].Key);
					}
					m_document = LevelDocument.FromLevel(current);
				}
				if (m_server == null) m_server = ModWebService.Start(this, "Web", Handle);
				if (m_window == null) m_window = EditorWindow.Create(this, EditorUrl);
				else m_window.Show();
			}
			catch (Exception ex) { Context.Ui.Toast("编辑器打开失败：" + ex.Message); Log.Warn(ex.Message); }
		}

		public void OpenBrowser()
		{
			if (m_server != null) Application.OpenURL(EditorUrl);
		}

		public ModWebResponse Handle(ModWebRequest request)
		{
			try
			{
				if (request.Method == "GET" && request.Path == "/api/session")
				{
					JArray builtIn = new JArray();
					foreach (ModLevelDefinition level in ModLevels.BuiltInLevels())
						builtIn.Add(new JObject { ["scene"] = level.Key, ["title"] = level.Title });
					JArray saved = new JArray();
					foreach (ModLevelDefinition level in ReadCatalog())
						saved.Add(new JObject { ["id"] = level.Key, ["title"] = level.Title });
					return Reply(new JObject
					{
						["ok"] = true, ["document"] = m_document.Data, ["built_in"] = builtIn, ["saved"] = saved,
						["capabilities"] = new JObject
						{
							["water_preview"] = true, ["water_physics"] = true, ["water_schema_version"] = 2,
							["integrated_canvas"] = true, ["ripples"] = true, ["refraction"] = true,
							["caustics"] = true, ["planar_reflection"] = "screen_space_2d", ["parallax"] = true,
							["interactive_water_system"] = false
						}
					});
				}
				if (request.Method != "POST") return Error("未知编辑器接口。", 404);
				if (request.Path == "/api/open")
				{
					JObject command = JObject.Parse(request.Text);
					if (command["id"] != null)
					{
						Guid id = Guid.Parse((string)command["id"]);
						m_document = LevelDocument.Parse(m_store.ReadText(id.ToString("N") + ".bpslevel"));
					}
					else if ((bool?)command["current"] == true)
						m_document = LevelDocument.FromLevel(ModLevels.CaptureCurrent());
					else m_document = LevelDocument.FromLevel(ModLevels.ReadBuiltIn((string)command["scene"]));
					return Reply(new JObject { ["ok"] = true, ["document"] = m_document.Data });
				}
				if (request.Path == "/api/import")
				{
					m_document = LevelDocument.Parse(request.Text);
					return Reply(new JObject { ["ok"] = true, ["document"] = m_document.Data });
				}
				if (request.Path == "/api/save" || request.Path == "/api/play")
				{
					LevelDocument document = LevelDocument.Parse(request.Text);
					if (m_document != null && m_document.Id != document.Id) return Error("文档 ID 已改变，请先打开或导入对应关卡包。", 409);
					m_store.WriteText(document.FileName, document.Data.ToString(Formatting.Indented));
					m_document = document;
					ModLevels.Refresh();
					if (request.Path == "/api/play")
					{
						string error;
						if (!ModLevels.Load(this, document.Definition(), out error))
							return Reply(new JObject { ["ok"] = false, ["saved"] = true, ["error"] = error });
						if (m_window != null) m_window.RequestClose();
					}
					return Reply(new JObject { ["ok"] = true, ["saved"] = true, ["file"] = document.FileName, ["document"] = document.Data });
				}
				if (request.Path == "/api/browser") { OpenBrowser(); return Reply(new JObject { ["ok"] = true }); }
				if (request.Path == "/api/close")
				{
					if (m_window != null) m_window.RequestClose();
					return Reply(new JObject { ["ok"] = true });
				}
				return Error("未知编辑器接口。", 404);
			}
			catch (Exception ex) { return Error(ex.Message, 400); }
		}

		private void OnLevelLoaded(ModLevelLoaded loaded)
		{
			if (m_window != null) { m_window.Close(); m_window = null; }
			ClearLevelResources();
			if (string.IsNullOrEmpty(loaded.Definition.ExtensionData)) return;
			JObject extension = JObject.Parse(loaded.Definition.ExtensionData);
			JArray bodies = extension["water_bodies"] as JArray;
			GameObject root = new GameObject("LevelEditorWater");
			root.transform.SetParent(loaded.Root.transform, false);
			m_waterRoots.Add(root);
			if ((bool?)extension["parallax_enabled"] != false)
				root.AddComponent<LevelParallax>().Initialize(loaded.Root.transform, loaded.Anchor);
			if (bodies == null || bodies.Count == 0) return;
			if (m_waterAssets == null) m_waterAssets = new WaterAssets(this);
			WaterWorld world = root.AddComponent<WaterWorld>();
			world.Initialize(loaded.Root.transform, m_waterAssets);
			foreach (JObject body in bodies)
				WaterPreview.Create(root.transform, body, loaded.Anchor, loaded.TerrainScale, m_waterAssets, m_fluids, world);
		}

		private void CloseResources()
		{
			if (m_window != null) { m_window.Close(); m_window = null; }
			if (m_server != null) { m_server.Dispose(); m_server = null; }
			ClearLevelResources();
			if (m_waterAssets != null) { m_waterAssets.Dispose(); m_waterAssets = null; }
			if (m_fluids != null) { m_fluids.Dispose(); m_fluids = null; }
			m_document = null;
		}

		private void ClearLevelResources()
		{
			foreach (GameObject root in m_waterRoots)
				if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
			m_waterRoots.Clear();
		}

		private static ModWebResponse Reply(JObject value) { return ModWebResponse.Json(value.ToString(Formatting.None)); }
		private static ModWebResponse Error(string error, int status)
		{
			return ModWebResponse.Json(new JObject { ["ok"] = false, ["error"] = error }.ToString(Formatting.None), status);
		}
	}

	internal sealed class Cleanup : IDisposable
	{
		private Action m_action;
		internal Cleanup(Action action) { m_action = action; }
		public void Dispose() { Action action = m_action; m_action = null; if (action != null) action(); }
	}
}
