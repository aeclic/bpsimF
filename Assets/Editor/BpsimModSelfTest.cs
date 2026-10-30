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
			TestZipRoundTrip();
			TestWorkshopIndex();
			TestVersionCompare();
			TestModSettings();

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

		private static void TestModSettings()
		{
			Section("设置持久化");
			var s = new ModSettings();
			s.Set("a", true);
			s.Set("b", false);
			Check("a 启用", s.IsEnabled("a"));
			Check("b 禁用", !s.IsEnabled("b"));
			Check("未知默认禁用", !s.IsEnabled("zzz"));
			string round = JsonConvert.SerializeObject(s, Formatting.Indented);
			ModSettings back = JsonConvert.DeserializeObject<ModSettings>(round, ModPackageStore.Lenient);
			Check("序列化往返保留 a", back.IsEnabled("a"));
			Check("序列化往返保留 b 禁用", !back.IsEnabled("b"));
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
