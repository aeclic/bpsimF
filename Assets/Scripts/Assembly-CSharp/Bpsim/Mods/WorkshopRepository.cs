using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Bpsim.Mods
{
	public enum WorkshopProvider
	{
		Gitee,
		GitHub
	}

	[Serializable]
	public sealed class WorkshopRepositorySettings
	{
		public string provider = "GitHub";
		public string giteeOwner = "yctf123123";
		public string giteeRepository = "bple-workshop";
		public string giteeBranch = "master";
		public string giteeIndexPath = "index.json";
		public string githubOwner = "aeclic";
		public string githubRepository = "bpsimF";
		public string githubBranch = "main";
		public string githubIndexPath = "workshop/index.json";
		public string indexUrl;
	}

	/// <summary>工坊仓库端点配置与基于 Gitee/GitHub Contents API 的包发布。</summary>
	public static class WorkshopRepository
	{
		private const long MaxUploadBytes = 20L * 1024L * 1024L;
		private static WorkshopRepositorySettings s_settings;

		public static string SessionToken { get; set; }

		public static WorkshopRepositorySettings Settings
		{
			get
			{
				if (s_settings == null) LoadSettings();
				return s_settings;
			}
		}

		public static WorkshopProvider Provider
		{
			get => string.Equals(Settings.provider, "Gitee", StringComparison.OrdinalIgnoreCase)
				? WorkshopProvider.Gitee : WorkshopProvider.GitHub;
			set
			{
				Settings.provider = value.ToString();
				Settings.indexUrl = null;
				SaveSettings();
			}
		}

		public static string ProviderLabel => Provider == WorkshopProvider.Gitee ? "Gitee" : "GitHub";

		public static string RepositoryLabel
		{
			get
			{
				WorkshopRepositorySettings s = Settings;
				return Provider == WorkshopProvider.Gitee
					? s.giteeOwner + "/" + s.giteeRepository
					: s.githubOwner + "/" + s.githubRepository;
			}
		}

		public static string IndexUrl
		{
			get
			{
				WorkshopRepositorySettings s = Settings;
				if (!string.IsNullOrWhiteSpace(s.indexUrl)) return s.indexUrl.Trim();
				return RawUrl(IndexPath);
			}
		}

		public static string IndexPath => Provider == WorkshopProvider.Gitee
			? CleanPath(Settings.giteeIndexPath, "index.json")
			: CleanPath(Settings.githubIndexPath, "workshop/index.json");

		public static string Branch => Provider == WorkshopProvider.Gitee
			? CleanValue(Settings.giteeBranch, "master")
			: CleanValue(Settings.githubBranch, "main");

		public static string RawBaseUrl
		{
			get
			{
				WorkshopRepositorySettings s = Settings;
				string root = Provider == WorkshopProvider.Gitee
					? "https://gitee.com/" + Escape(s.giteeOwner) + "/" + Escape(s.giteeRepository) + "/raw/" + Escape(Branch) + "/"
					: "https://raw.githubusercontent.com/" + Escape(s.githubOwner) + "/" + Escape(s.githubRepository) + "/" + Escape(Branch) + "/";
				string directory = Path.GetDirectoryName(IndexPath)?.Replace('\\', '/');
				return root + (string.IsNullOrEmpty(directory) ? string.Empty : directory.Trim('/') + "/");
			}
		}

		public static string IndexCachePath
		{
			get
			{
				ModPaths.EnsureDirectory(ModPaths.WorkshopCacheDir);
				string key = ProviderLabel + "-" + RepositoryLabel.Replace('/', '-') + "-"
					+ Branch + "-" + IndexPath.Replace('/', '-');
				foreach (char c in Path.GetInvalidFileNameChars()) key = key.Replace(c, '_');
				return Path.Combine(ModPaths.WorkshopCacheDir, key + ".json");
			}
		}

		public static void LoadSettings()
		{
			WorkshopRepositorySettings loaded = null;
			try
			{
				string path = Path.Combine(ModPaths.WorkshopRoot, "config.json");
				if (File.Exists(path))
				{
					string json = File.ReadAllText(path);
					loaded = JsonConvert.DeserializeObject<WorkshopRepositorySettings>(json, ModPackageStore.Lenient);
					if (loaded == null)
					{
						JObject old = JObject.Parse(json);
						loaded = new WorkshopRepositorySettings { indexUrl = (string)old["indexUrl"] };
					}
					JObject root = JObject.Parse(json);
					if (root["provider"] == null && !string.IsNullOrWhiteSpace(loaded.indexUrl)
						&& loaded.indexUrl.IndexOf("gitee.com", StringComparison.OrdinalIgnoreCase) >= 0)
						loaded.provider = "Gitee";
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[Workshop] 仓库配置读取失败：" + ex.Message);
			}
			s_settings = loaded ?? new WorkshopRepositorySettings();
			if (string.IsNullOrWhiteSpace(s_settings.provider)) s_settings.provider = "GitHub";
		}

		public static void SaveSettings()
		{
			try
			{
				ModPaths.EnsureDirectory(ModPaths.WorkshopRoot);
				File.WriteAllText(Path.Combine(ModPaths.WorkshopRoot, "config.json"),
					JsonConvert.SerializeObject(Settings, Formatting.Indented), new UTF8Encoding(false));
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[Workshop] 仓库配置保存失败：" + ex.Message);
			}
		}

		public static void SetRepository(string owner, string repository, string branch, string indexPath)
		{
			if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
				throw new ArgumentException("仓库所有者和名称不能为空");
			if (!SafeSegment(owner) || !SafeSegment(repository)) throw new ArgumentException("仓库所有者或名称无效");
			string cleanBranch = CleanValue(branch, Provider == WorkshopProvider.Gitee ? "master" : "main");
			if (!SafeSegment(cleanBranch)) throw new ArgumentException("分支名无效");
			string cleanIndex = CleanPath(indexPath, "index.json");
			if (Provider == WorkshopProvider.Gitee)
			{
				Settings.giteeOwner = owner.Trim();
				Settings.giteeRepository = repository.Trim();
				Settings.giteeBranch = cleanBranch;
				Settings.giteeIndexPath = cleanIndex;
			}
			else
			{
				Settings.githubOwner = owner.Trim();
				Settings.githubRepository = repository.Trim();
				Settings.githubBranch = cleanBranch;
				Settings.githubIndexPath = cleanIndex;
			}
			Settings.indexUrl = null;
			SaveSettings();
		}

		public static void Upload(MonoBehaviour host, string zipPath, WorkshopPack pack, Action<bool, string> onDone)
		{
			if (host == null || pack == null)
			{
				onDone?.Invoke(false, "上传参数无效");
				return;
			}
			if (string.IsNullOrWhiteSpace(SessionToken))
			{
				onDone?.Invoke(false, "请填写仓库访问令牌");
				return;
			}
			host.StartCoroutine(UploadRoutine(zipPath, pack, onDone));
		}

		private static IEnumerator UploadRoutine(string zipPath, WorkshopPack pack, Action<bool, string> onDone)
		{
			byte[] zip;
			string hash;
			try
			{
				FileInfo file = new FileInfo(zipPath);
				if (!file.Exists || file.Length == 0) throw new InvalidDataException("ZIP 文件不存在或为空");
				if (file.Length > MaxUploadBytes) throw new InvalidDataException("单包上传上限为 20 MB");
				ValidatePackageZip(zipPath);
				zip = File.ReadAllBytes(zipPath);
				hash = ComputeSha256(zip);
			}
			catch (Exception ex)
			{
				onDone?.Invoke(false, ex.Message);
				yield break;
			}

			pack.id = CleanValue(pack.id, string.Empty);
			pack.name = CleanValue(pack.name, string.Empty);
			pack.version = CleanValue(pack.version, string.Empty);
			if (!SafeSegment(pack.id) || string.IsNullOrWhiteSpace(pack.name) || string.IsNullOrWhiteSpace(pack.version))
			{
				onDone?.Invoke(false, "名称不能为空，内容 ID 和版本只能使用字母、数字、点、下划线和连字符");
				yield break;
			}
			if (!SafeSegment(pack.version))
			{
				onDone?.Invoke(false, "版本号只能使用字母、数字、点、下划线和连字符");
				yield break;
			}

			WorkshopIndex index = null;
			string indexSha = null;
			ApiResponse indexResponse = null;
			yield return GetFile(IndexPath, result => indexResponse = result);
			if (indexResponse == null)
			{
				onDone?.Invoke(false, "读取仓库索引失败");
				yield break;
			}
			if (indexResponse.Code == 200 && indexResponse.Body.Trim() != "[]")
			{
				try
				{
					JObject file = JObject.Parse(indexResponse.Body);
					indexSha = (string)file["sha"];
					string encoded = ((string)file["content"] ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
					string text = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
					index = JsonConvert.DeserializeObject<WorkshopIndex>(text, ModPackageStore.Lenient);
				}
				catch (Exception ex)
				{
					onDone?.Invoke(false, "仓库索引解析失败：" + ex.Message);
					yield break;
				}
			}
			else if (indexResponse.Code != 404 && indexResponse.Code != 200)
			{
				onDone?.Invoke(false, Describe(indexResponse, "读取仓库索引失败"));
				yield break;
			}
			if (index == null) index = new WorkshopIndex { schema = 1, baseUrl = RawBaseUrl };
			if (index.packs == null) index.packs = new List<WorkshopPack>();

			string relativeDirectory = Path.GetDirectoryName(IndexPath)?.Replace('\\', '/');
			string relativePackPath = "packs/" + pack.id + "/" + pack.version + ".zip";
			string repoPackPath = string.IsNullOrEmpty(relativeDirectory) ? relativePackPath : relativeDirectory.Trim('/') + "/" + relativePackPath;
			pack.url = relativePackPath;
			pack.size = zip.LongLength;
			pack.sha256 = hash;
			pack.contentType = string.IsNullOrWhiteSpace(pack.contentType) ? "addon" : pack.contentType;
			pack.packFilePath = repoPackPath;
			pack.mirrors = pack.mirrors ?? new List<string>();

			ApiResponse existingZip = null;
			yield return GetFile(repoPackPath, result => existingZip = result);
			if (existingZip == null || (existingZip.Code != 200 && existingZip.Code != 404))
			{
				onDone?.Invoke(false, Describe(existingZip, "检查已有 ZIP 失败"));
				yield break;
			}
			string zipSha = null;
			if (existingZip.Code == 200 && existingZip.Body.Trim() != "[]")
			{
				try { zipSha = (string)JObject.Parse(existingZip.Body)["sha"]; }
				catch (Exception ex) { onDone?.Invoke(false, "已有 ZIP 信息解析失败：" + ex.Message); yield break; }
			}

			ApiResponse zipResponse = null;
			yield return PutFile(repoPackPath, zip, "Publish workshop pack " + pack.id + " " + pack.version, zipSha,
				result => zipResponse = result);
			if (!IsSuccess(zipResponse))
			{
				onDone?.Invoke(false, Describe(zipResponse, "上传 ZIP 失败"));
				yield break;
			}

			index.packs.RemoveAll(item => item != null && string.Equals(item.id, pack.id, StringComparison.OrdinalIgnoreCase));
			index.packs.Add(pack);
			index.baseUrl = RawBaseUrl;
			byte[] indexBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(index, Formatting.Indented));
			ApiResponse saveIndex = null;
			yield return PutFile(IndexPath, indexBytes, "Update workshop index " + pack.id + " " + pack.version,
				indexSha, result => saveIndex = result);
			if (!IsSuccess(saveIndex))
			{
				onDone?.Invoke(false, "ZIP 已上传，但索引更新失败：" + Describe(saveIndex, "索引写入失败"));
				yield break;
			}
			onDone?.Invoke(true, pack.name + " v" + pack.version);
		}

		private static IEnumerator GetFile(string path, Action<ApiResponse> done)
		{
			string url = ApiRoot + "/contents/" + EscapePath(path) + "?ref=" + Uri.EscapeDataString(Branch);
			yield return Send("GET", url, null, null, done);
		}

		private static IEnumerator PutFile(string path, byte[] data, string message, string existingSha, Action<ApiResponse> done)
		{
			Dictionary<string, object> payload = new Dictionary<string, object>
			{
				["content"] = Convert.ToBase64String(data),
				["message"] = message,
				["branch"] = Branch
			};
			if (!string.IsNullOrEmpty(existingSha)) payload["sha"] = existingSha;
			string method = "PUT";
			if (Provider == WorkshopProvider.Gitee)
			{
				payload["access_token"] = SessionToken;
				if (string.IsNullOrEmpty(existingSha)) method = "POST";
			}
			string url = ApiRoot + "/contents/" + EscapePath(path);
			yield return Send(method, url, JsonConvert.SerializeObject(payload), null, done);
		}

		private static IEnumerator Send(string method, string url, string json, byte[] raw, Action<ApiResponse> done)
		{
			using (UnityWebRequest request = new UnityWebRequest(url, method))
			{
				request.downloadHandler = new DownloadHandlerBuffer();
				request.timeout = 120;
				request.SetRequestHeader("Accept", "application/vnd.github+json");
				request.SetRequestHeader("User-Agent", "Bpsim-Workshop");
				if (Provider == WorkshopProvider.GitHub) request.SetRequestHeader("X-GitHub-Api-Version", "2022-11-28");
				request.SetRequestHeader("Authorization", Provider == WorkshopProvider.GitHub
					? "Bearer " + SessionToken : "token " + SessionToken);
				if (raw != null)
				{
					request.uploadHandler = new UploadHandlerRaw(raw);
					request.SetRequestHeader("Content-Type", "application/octet-stream");
				}
				else if (!string.IsNullOrEmpty(json))
				{
					request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
					request.SetRequestHeader("Content-Type", "application/json");
				}
				yield return request.SendWebRequest();
				done?.Invoke(new ApiResponse { Code = (int)request.responseCode, Body = request.downloadHandler.text, Error = request.error });
			}
		}

		private static string ApiRoot
		{
			get
			{
				WorkshopRepositorySettings s = Settings;
				return Provider == WorkshopProvider.Gitee
					? "https://gitee.com/api/v5/repos/" + Escape(s.giteeOwner) + "/" + Escape(s.giteeRepository)
					: "https://api.github.com/repos/" + Escape(s.githubOwner) + "/" + Escape(s.githubRepository);
			}
		}

		private static string RawUrl(string path)
		{
			WorkshopRepositorySettings s = Settings;
			return Provider == WorkshopProvider.Gitee
				? "https://gitee.com/" + Escape(s.giteeOwner) + "/" + Escape(s.giteeRepository) + "/raw/" + Escape(Branch) + "/" + EscapePath(path)
				: "https://raw.githubusercontent.com/" + Escape(s.githubOwner) + "/" + Escape(s.githubRepository) + "/" + Escape(Branch) + "/" + EscapePath(path);
		}

		private static void ValidatePackageZip(string path)
		{
			using (ZipArchive archive = ZipFile.OpenRead(path))
			{
				bool hasManifest = false;
				foreach (ZipArchiveEntry entry in archive.Entries)
				{
					string name = entry.FullName.Replace('\\', '/');
					if (Path.IsPathRooted(name) || name.Contains(":")) throw new InvalidDataException("ZIP 含有不安全路径");
					foreach (string segment in name.Split('/'))
						if (segment == "." || segment == "..") throw new InvalidDataException("ZIP 含有不安全路径");
					if (name.EndsWith("Mod.xml", StringComparison.OrdinalIgnoreCase)
						|| name.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)) hasManifest = true;
				}
				if (!hasManifest) throw new InvalidDataException("ZIP 中找不到 Mod.xml 或 manifest.json");
			}
		}

		private static bool IsSuccess(ApiResponse response) => response != null && response.Code >= 200 && response.Code < 300;

		private static string Describe(ApiResponse response, string fallback)
		{
			if (response == null) return fallback;
			string body = response.Body ?? response.Error ?? string.Empty;
			if (body.Length > 400) body = body.Substring(0, 400);
			return fallback + "（HTTP " + response.Code + "）" + (string.IsNullOrWhiteSpace(body) ? string.Empty : "：" + body);
		}

		private static string ComputeSha256(byte[] data)
		{
			using (SHA256 sha = SHA256.Create())
			{
				byte[] bytes = sha.ComputeHash(data);
				StringBuilder output = new StringBuilder(bytes.Length * 2);
				foreach (byte value in bytes) output.Append(value.ToString("x2"));
				return output.ToString();
			}
		}

		private static string CleanPath(string value, string fallback)
		{
			string path = CleanValue(value, fallback).Replace('\\', '/').Trim('/');
			foreach (string segment in path.Split('/'))
				if (!SafeSegment(segment)) throw new ArgumentException("索引路径无效");
			return path;
		}
		private static string CleanValue(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
		private static bool SafeSegment(string value)
		{
			if (string.IsNullOrWhiteSpace(value)) return false;
			foreach (char c in value) if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')) return false;
			return value != "." && value != "..";
		}
		private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);
		private static string EscapePath(string value)
		{
			string[] parts = (value ?? string.Empty).Split('/');
			for (int i = 0; i < parts.Length; i++) parts[i] = Escape(parts[i]);
			return string.Join("/", parts);
		}

		private sealed class ApiResponse
		{
			public int Code;
			public string Body;
			public string Error;
		}
	}
}
