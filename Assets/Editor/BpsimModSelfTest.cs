using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Bpsim.Modding;
using Bpsim.Mods;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Bpsim.Mods.EditorTools
{
	/// <summary>
	/// Mod 系统的无头自测。用菜单或编辑器批处理跑，验证：
	/// 清单解析、内置样板扫描、zip 安装/卸载往返、工坊索引反序列化、版本比较。
	/// 结果写到 <c>Builds/QA/mods-selftest.txt</c>，并返回 <c>FAILURES: n</c>。
	/// </summary>
	public static class BpsimModSelfTest
	{
		private static readonly StringBuilder s_log = new StringBuilder();
		private static int s_failures;

		[MenuItem("Tools/BPSim Mod/Self Test")]
		public static void RunMenu()
		{
			Run();
			EditorUtility.DisplayDialog("BPSim Mod 自测", s_log.ToString(), "好");
		}

		/// <summary>桥/批处理入口：返回报告文本，同时落盘。</summary>
		public static string Run()
		{
			s_log.Clear();
			s_failures = 0;

			s_log.AppendLine("=== BPSim Mod 自测 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");

			TestPaths();
			TestSampleManifest();
			TestModXml();
			TestZipRoundTrip();
			TestWorkshopIndex();
			TestVersionCompare();
			TestModdingConfig();

			s_log.AppendLine();
			s_log.AppendLine("FAILURES: " + s_failures);

			WriteReport();
			return s_log.ToString();
		}

		// ── 用例 ──

		private static void TestPaths()
		{
			Section("路径");
			Check("persistentDataPath 非空", !string.IsNullOrEmpty(Application.persistentDataPath));
			Check("Mods 根以 /Mods 结尾", ModPaths.Root.Replace('\\', '/').EndsWith("/Mods"));
			Check("内置根包含 StreamingAssets", ModPaths.BuiltInRoot.Contains("StreamingAssets"));
		}

		private static void TestSampleManifest()
		{
			Section("内置样板 manifest");
			string dir = Path.Combine(Application.streamingAssetsPath, "Mods", "netease-music");
			if (!Directory.Exists(dir))
			{
				Fail("找不到样板目录：" + dir);
				return;
			}
			ModPackage pkg = ModPackageStore.TryLoadPackage(dir, ModSource.BuiltIn);
			Check("样板能解析出 ModPackage", pkg != null);
			if (pkg == null)
			{
				return;
			}
			Check("id = netease-music", pkg.Id == "netease-music");
			Check("type = panel", pkg.Manifest.type == ModPackageType.Panel);
			Check("面板数量 = 1", pkg.Manifest.panels.Count == 1);
			if (pkg.Manifest.panels.Count > 0)
			{
				ModPanelSpec spec = pkg.Manifest.panels[0];
				Check("面板 id = player", spec.id == "player");
				Check("面板 html 文件存在", File.Exists(Path.Combine(dir, spec.html)));
				Check("面板 css 文件存在", string.IsNullOrEmpty(spec.css) || File.Exists(Path.Combine(dir, spec.css)));
				Check("面板 js 文件存在", string.IsNullOrEmpty(spec.js) || File.Exists(Path.Combine(dir, spec.js)));
			}
		}

		private static void TestZipRoundTrip()
		{
			Section("zip 安装 / 卸载往返");
			string tmp = Path.Combine(Path.GetTempPath(), "bpsim-modtest-" + Guid.NewGuid().ToString("N"));
			string src = Path.Combine(tmp, "src");
			string id = "selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8);
			string zip = Path.Combine(tmp, "pack.zip");
			Directory.CreateDirectory(src);
			try
			{
				string manifest = "{\"schema\":1,\"id\":\"" + id + "\",\"name\":\"自测包\",\"version\":\"1.2.3\"," +
					"\"type\":\"panel\",\"panels\":[{\"id\":\"p\",\"title\":\"P\",\"html\":\"p.html\"}]}";
				File.WriteAllText(Path.Combine(src, "manifest.json"), manifest, Encoding.UTF8);
				File.WriteAllText(Path.Combine(src, "p.html"), "<html><body>hi</body></html>", Encoding.UTF8);

				ZipFile.CreateFromDirectory(src, zip);
				Check("zip 已生成", File.Exists(zip));

				ModPackage installed = ModPackageStore.InstallZip(zip);
				Check("安装成功", installed != null && installed.Id == id);
				Check("安装目录存在", installed != null && Directory.Exists(installed.Directory));
				Check("安装版本 = 1.2.3", installed != null && installed.Version == "1.2.3");
				if (installed != null)
				{
					Check("html 落盘", File.Exists(Path.Combine(installed.Directory, "p.html")));
					bool removed = ModPackageStore.Uninstall(installed);
					Check("卸载成功", removed);
					Check("卸载后目录不存在", !Directory.Exists(installed.Directory));
				}
			}
			catch (Exception ex)
			{
				Fail("zip 往返异常：" + ex.Message);
			}
			finally
			{
				try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
			}
		}

		private static void TestWorkshopIndex()
		{
			Section("工坊索引反序列化");
			string json = @"{""schema"":1,""baseUrl"":""https://example.com/ws/"",""packs"":[
				{""id"":""demo"",""name"":""演示"",""version"":""2.0.0"",""url"":""demo.zip"",""sha256"":""abcd"",""tags"":[""a""]}
			]}";
			try
			{
				WorkshopIndex idx = JsonConvert.DeserializeObject<WorkshopIndex>(json, ModPackageStore.Lenient);
				Check("索引非空", idx != null);
				Check("packs 有 1 条", idx != null && idx.packs.Count == 1);
				if (idx != null && idx.packs.Count == 1)
				{
					WorkshopPack p = idx.packs[0];
					Check("id = demo", p.id == "demo");
					Check("url 原样", p.url == "demo.zip");
					Check("AllUrls 至少 1 个", Count(p.AllUrls()) >= 1);
					Check("IsAddon（默认 addon）", p.IsAddon);
				}
			}
			catch (Exception ex)
			{
				Fail("索引解析异常：" + ex.Message);
			}
		}

		private static void TestVersionCompare()
		{
			Section("版本比较");
			Check("1.0.0 < 1.0.1", ModManager.CompareVersion("1.0.0", "1.0.1") < 0);
			Check("1.2 = 1.2.0", ModManager.CompareVersion("1.2", "1.2.0") == 0);
			Check("2.0.0 > 1.9.9", ModManager.CompareVersion("2.0.0", "1.9.9") > 0);
		}

		private static void TestModdingConfig()
		{
			Section("Modding.xml 配置");
			string path = Path.Combine(Path.GetTempPath(), "bpsim-modding-selftest.xml");
			try
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
				Guid a = ModGuid.Derive("mod-a");
				Guid b = ModGuid.Derive("mod-b");
				ModdingConfig cfg = new ModdingConfig();
				cfg.SetEnabled(a, true);
				cfg.SetEnabled(b, false);
				cfg.LastGameVersion = "0.0.12";
				cfg.Save(path);
				Check("配置文件已写入", File.Exists(path));

				ModdingConfig back = ModdingConfig.Load(path);
				Check("a 启用", !back.IsDisabled(a, "mod-a"));
				Check("b 禁用", back.IsDisabled(b, "mod-b"));
				Check("版本号保留", back.LastGameVersion == "0.0.12");
				Check("严格扫描默认关", !back.StrictScan);
			}
			catch (Exception ex)
			{
				Fail("配置读写异常：" + ex.Message);
			}
			finally
			{
				try { if (File.Exists(path)) File.Delete(path); } catch { }
			}
		}

		private static void TestModXml()
		{
			Section("Mod.xml 解析");
			string dir = Path.Combine(Path.GetTempPath(), "bpsim-modxml-selftest");
			string file = Path.Combine(dir, ModXml.FileName);
			try
			{
				Directory.CreateDirectory(dir);
				File.WriteAllText(file,
					"<Mod>\n"
					+ "  <Name>Demo Addon</Name>\n"
					+ "  <Author>tester</Author>\n"
					+ "  <Version>1.2.3</Version>\n"
					+ "  <MultiplayerCompatible>false</MultiplayerCompatible>\n"
					+ "  <ID>bb7ba333-e72b-49a8-8c0d-011a3fcadaf3</ID>\n"
					+ "  <EntryPoint type=\"Demo.Entry\" />\n"
					+ "  <Assemblies><Assembly path=\"Demo.dll\" /></Assemblies>\n"
					+ "  <Resources>\n"
					+ "    <Panel id=\"p\" title=\"面板\" html=\"panel.html\" width=\"300\" />\n"
					+ "  </Resources>\n"
					+ "</Mod>\n");

				bool ok = ModXml.TryParse(file, "demo-folder", out ModManifest m, out string error);
				Check("解析成功", ok);
				if (!ok)
				{
					Fail(error);
					return;
				}
				Check("名字", m.name == "Demo Addon");
				Check("版本", m.version == "1.2.3");
				Check("GUID", m.guid == Guid.Parse("bb7ba333-e72b-49a8-8c0d-011a3fcadaf3"));
				Check("入口类型", m.entryType == "Demo.Entry");
				Check("入口程序集", m.entryAssembly == "Demo.dll");
				Check("是插件类型", m.IsPlugin);
				Check("有一个面板", m.panels.Count == 1 && m.panels[0].width == 300f);
				Check("联机不安全", !m.multiplayerSafe);
			}
			catch (Exception ex)
			{
				Fail("Mod.xml 解析异常：" + ex.Message);
			}
			finally
			{
				try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
			}
		}

		// ── 辅助 ──

		private static int Count(IEnumerable<string> e)
		{
			int n = 0;
			foreach (string _ in e)
			{
				n++;
			}
			return n;
		}

		private static void Section(string name)
		{
			s_log.AppendLine();
			s_log.AppendLine("-- " + name + " --");
		}

		private static void Check(string what, bool ok)
		{
			if (ok)
			{
				s_log.AppendLine("  [OK]   " + what);
			}
			else
			{
				s_log.AppendLine("  [FAIL] " + what);
				s_failures++;
			}
		}

		private static void Fail(string msg)
		{
			s_log.AppendLine("  [FAIL] " + msg);
			s_failures++;
		}

		private static void WriteReport()
		{
			try
			{
				string qa = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "QA"));
				Directory.CreateDirectory(qa);
				File.WriteAllText(Path.Combine(qa, "mods-selftest.txt"), s_log.ToString(), Encoding.UTF8);
				Debug.Log("[BPSimModSelfTest] 报告写入 " + qa + "  (FAILURES: " + s_failures + ")");
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[BPSimModSelfTest] 写报告失败：" + ex.Message);
			}
		}
	}
}
