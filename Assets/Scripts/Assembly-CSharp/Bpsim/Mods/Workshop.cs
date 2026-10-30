using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Bpsim.Modding;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Bpsim.Mods
{
	// ───────────────────────────── 模型（移植自 BPSS Workshop） ─────────────────────────────

	/// <summary>工坊索引根。</summary>
	[Serializable]
	public sealed class WorkshopIndex
	{
		public int schema = 1;
		public string generatedAt;
		public string baseUrl;
		public string generator;
		public List<WorkshopPack> packs = new List<WorkshopPack>();
	}

	/// <summary>一条工坊内容。字段命名与 BPSS 工坊 index.json 对齐，便于直接复用已有索引。</summary>
	[Serializable]
	public sealed class WorkshopPack
	{
		public string id;
		public string name;
		public string author;
		public string version = "1.0.0";
		public string summary;

		/// <summary>zip 直链（相对 baseUrl 时会被拼成绝对地址）。</summary>
		public string url;

		/// <summary>备用镜像直链。</summary>
		public List<string> mirrors = new List<string>();

		public long size;
		public string sha256;

		public bool affectsSimulation;
		public bool multiplayerSafe;
		public bool requiresScript;

		/// <summary>addon | save | other</summary>
		public string contentType = "addon";

		public string bpVersion;
		public string cover;
		public string coverUrl;
		public string packFilePath;

		public List<string> tags = new List<string>();

		public bool IsAddon => string.Equals(contentType, "addon", StringComparison.OrdinalIgnoreCase);

		public bool IsSave => string.Equals(contentType, "save", StringComparison.OrdinalIgnoreCase);

		public IEnumerable<string> AllUrls()
		{
			if (!string.IsNullOrEmpty(url))
			{
				yield return url;
			}
			if (mirrors != null)
			{
				foreach (string m in mirrors)
				{
					if (!string.IsNullOrEmpty(m))
					{
						yield return m;
					}
				}
			}
		}
	}

	public enum WorkshopInstallState
	{
		NotInstalled,
		Installed,
		UpdateAvailable
	}

	// ───────────────────────────── 服务 ─────────────────────────────

	/// <summary>
	/// 工坊索引服务（移植 BPSS <c>WorkshopService</c>）：缓存优先、远程兜底、支持镜像。
	/// 索引格式与 BPSS 一致（index.json + sha256）。
	/// </summary>
	public static class WorkshopService
	{
		private const string DefaultIndexUrl = "https://gitee.com/bpsim/workshop/raw/master/index.json";

		public static WorkshopIndex Index { get; private set; }

		public static bool Loading { get; private set; }

		public static bool FromCache { get; private set; }

		public static string LastError { get; private set; }

		private static string s_indexUrl;

		/// <summary>索引地址：优先 modworkshop.config.json，其次默认值。可在运行时覆盖。</summary>
		public static string IndexUrl
		{
			get
			{
				if (!string.IsNullOrEmpty(s_indexUrl))
				{
					return s_indexUrl;
				}
				s_indexUrl = ReadConfiguredUrl() ?? DefaultIndexUrl;
				return s_indexUrl;
			}
			set => s_indexUrl = value;
		}

		public static WorkshopInstallState GetState(WorkshopPack pack)
		{
			if (pack == null || ModManager.Instance == null)
			{
				return WorkshopInstallState.NotInstalled;
			}
			ModPackage installed = ModManager.Instance.Find(pack.id);
			if (installed == null)
			{
				return WorkshopInstallState.NotInstalled;
			}
			if (ModManager.CompareVersion(installed.Version, pack.version) < 0)
			{
				return WorkshopInstallState.UpdateAvailable;
			}
			return WorkshopInstallState.Installed;
		}

		/// <summary>加载索引：先读磁盘缓存（秒开），再按需拉远程。onDone 总会回调（失败也回调）。</summary>
		public static void LoadIndex(MonoBehaviour host, Action onDone, bool forceRemote = false)
		{
			if (Loading)
			{
				return;
			}
			if (!forceRemote && Index != null)
			{
				onDone?.Invoke();
				return;
			}
			Loading = true;
			LastError = null;
			host.StartCoroutine(LoadIndexRoutine(forceRemote, onDone));
		}

		private static IEnumerator LoadIndexRoutine(bool forceRemote, Action onDone)
		{
			// 1) 缓存
			if (!forceRemote)
			{
				WorkshopIndex cached = TryReadCache();
				if (cached != null)
				{
					Index = cached;
					FromCache = true;
					Loading = false;
					onDone?.Invoke();
					yield break;
				}
			}

			// 2) 远程
			string url = WithCacheBust(IndexUrl);
			using (UnityWebRequest req = UnityWebRequest.Get(url))
			{
				req.timeout = 20;
				yield return req.SendWebRequest();
				if (req.result != UnityWebRequest.Result.Success)
				{
					LastError = req.error;
					Loading = false;
					onDone?.Invoke();
					yield break;
				}
				try
				{
					WorkshopIndex parsed = JsonConvert.DeserializeObject<WorkshopIndex>(
						req.downloadHandler.text, ModPackageStore.Lenient);
					if (parsed == null || parsed.packs == null)
					{
						LastError = "索引解析为空";
					}
					else
					{
						ApplyBaseUrl(parsed);
						Index = parsed;
						FromCache = false;
						WriteCache(req.downloadHandler.text);
					}
				}
				catch (Exception ex)
				{
					LastError = "索引解析失败：" + ex.Message;
				}
			}
			Loading = false;
			onDone?.Invoke();
		}

		private static void ApplyBaseUrl(WorkshopIndex index)
		{
			if (string.IsNullOrEmpty(index.baseUrl))
			{
				return;
			}
			string baseUrl = index.baseUrl.TrimEnd('/') + "/";
			foreach (WorkshopPack p in index.packs)
			{
				p.url = Absolute(baseUrl, p.url);
				if (p.mirrors != null)
				{
					for (int i = 0; i < p.mirrors.Count; i++)
					{
						p.mirrors[i] = Absolute(baseUrl, p.mirrors[i]);
					}
				}
			}
		}

		private static string Absolute(string baseUrl, string url)
		{
			if (string.IsNullOrEmpty(url) || url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
			{
				return url;
			}
			return baseUrl + url.TrimStart('/');
		}

		private static string WithCacheBust(string url)
		{
			if (string.IsNullOrEmpty(url))
			{
				return url;
			}
			return url + (url.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks;
		}

		private static WorkshopIndex TryReadCache()
		{
			try
			{
				string path = ModPaths.WorkshopIndexCachePath;
				if (!File.Exists(path))
				{
					return null;
				}
				WorkshopIndex idx = JsonConvert.DeserializeObject<WorkshopIndex>(
					File.ReadAllText(path), ModPackageStore.Lenient);
				if (idx != null && idx.packs != null)
				{
					ApplyBaseUrl(idx);
					return idx;
				}
			}
			catch (Exception)
			{
				// ignore
			}
			return null;
		}

		private static void WriteCache(string text)
		{
			try
			{
				ModPaths.EnsureDirectory(ModPaths.WorkshopCacheDir);
				File.WriteAllText(ModPaths.WorkshopIndexCachePath, text, Encoding.UTF8);
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[Workshop] 写缓存失败：" + ex.Message);
			}
		}

		private static string ReadConfiguredUrl()
		{
			try
			{
				string cfg = Path.Combine(ModPaths.WorkshopRoot, "config.json");
				if (File.Exists(cfg))
				{
					var jo = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(cfg));
					if (jo != null && jo.TryGetValue("indexUrl", out string u) && !string.IsNullOrWhiteSpace(u))
					{
						return u;
					}
				}
			}
			catch (Exception)
			{
				// ignore
			}
			return null;
		}
	}

	// ───────────────────────────── 安装器 ─────────────────────────────

	/// <summary>工坊内容安装器（移植 BPSS <c>WorkshopInstaller</c> 的核心）：下载 → 校验 sha256 → 走 ModPackageStore 安装。</summary>
	public static class WorkshopInstaller
	{
		public static void Install(MonoBehaviour host, WorkshopPack pack, Action<bool, string> onDone)
		{
			if (pack == null)
			{
				onDone?.Invoke(false, "pack 为空");
				return;
			}
			host.StartCoroutine(InstallRoutine(pack, onDone));
		}

		private static IEnumerator InstallRoutine(WorkshopPack pack, Action<bool, string> onDone)
		{
			ModPaths.EnsureDirectory(ModPaths.WorkshopDownloadsDir);
			string tmpZip = Path.Combine(ModPaths.WorkshopDownloadsDir, pack.id + "-" + pack.version + ".zip");

			string lastError = null;
			bool downloaded = false;
			foreach (string url in pack.AllUrls())
			{
				using (UnityWebRequest req = UnityWebRequest.Get(url))
				{
					req.timeout = 60;
					req.downloadHandler = new DownloadHandlerFile(tmpZip) { removeFileOnAbort = true };
					yield return req.SendWebRequest();
					if (req.result == UnityWebRequest.Result.Success)
					{
						downloaded = true;
						break;
					}
					lastError = req.error;
				}
			}

			if (!downloaded)
			{
				onDone?.Invoke(false, "下载失败：" + (lastError ?? "无可用地址"));
				yield break;
			}

			if (!string.IsNullOrEmpty(pack.sha256))
			{
				string actual = ComputeSha256(tmpZip);
				if (!string.Equals(actual, pack.sha256, StringComparison.OrdinalIgnoreCase))
				{
					try { File.Delete(tmpZip); } catch { }
					onDone?.Invoke(false, "sha256 校验失败");
					yield break;
				}
			}

			try
			{
				ModPackage pkg = ModManager.Instance.InstallZip(tmpZip);
				onDone?.Invoke(true, pkg.DisplayName);
			}
			catch (Exception ex)
			{
				onDone?.Invoke(false, ex.Message);
			}
		}

		private static string ComputeSha256(string path)
		{
			using (FileStream fs = File.OpenRead(path))
			using (SHA256 sha = SHA256.Create())
			{
				byte[] hash = sha.ComputeHash(fs);
				StringBuilder sb = new StringBuilder(hash.Length * 2);
				foreach (byte b in hash)
				{
					sb.Append(b.ToString("x2"));
				}
				return sb.ToString();
			}
		}
	}
}
