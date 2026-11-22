using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Bpsim.Updates
{
	public sealed class BpsimUpdateInfo
	{
		public string Version;
		public string ReleaseUrl;
		public string Notes;
		public string AssetName;
		public string AssetUrl;
		public long Size;
		public string Sha256;
		public string ApplicationId;
		public string WindowsExecutable;
		public bool HasUpdate;
	}

	/// <summary>bpsimF GitHub Release 检查、完整性验证与平台安装入口。</summary>
	public static class BpsimUpdater
	{
		private const string ReleasesUrl = "https://api.github.com/repos/aeclic/bpsimF/releases?per_page=100";
		private const string Product = "bpsimF";
		private const string AndroidApplicationId = "com.bprc.bpsimF";
		private const string WindowsPlatform = "windows-x64";
		private const string AndroidPlatform = "android-arm";
		private const string TagPrefix = "BPSIM-";

		public static void Check(MonoBehaviour host, Action<BpsimUpdateInfo, string> onDone)
		{
			if (host == null)
			{
				onDone?.Invoke(null, "更新检查组件不可用");
				return;
			}
			host.StartCoroutine(CheckRoutine(onDone));
		}

		public static void Install(MonoBehaviour host, BpsimUpdateInfo update, Action<bool, string> onDone)
		{
			if (host == null || update == null || !update.HasUpdate)
			{
				onDone?.Invoke(false, "没有可安装的更新");
				return;
			}
			host.StartCoroutine(InstallRoutine(update, onDone));
		}

		private static IEnumerator CheckRoutine(Action<BpsimUpdateInfo, string> onDone)
		{
			if (!IsSupportedPlatform())
			{
				onDone?.Invoke(null, "当前平台不支持游戏自动更新");
				yield break;
			}

			string releasesJson;
			using (UnityWebRequest request = UnityWebRequest.Get(ReleasesUrl))
			{
				request.timeout = 20;
				request.SetRequestHeader("User-Agent", "bpsimF-updater");
				yield return request.SendWebRequest();
				if (request.result != UnityWebRequest.Result.Success)
				{
					onDone?.Invoke(null, "读取游戏 Release 失败：" + request.error);
					yield break;
				}
				releasesJson = request.downloadHandler.text;
			}

			BpsimUpdateInfo latest;
			string parseError = null;
			try
			{
				latest = FindLatestRelease(releasesJson);
			}
			catch (Exception exception)
			{
				latest = null;
				parseError = exception.Message;
			}
			if (parseError != null)
			{
				onDone?.Invoke(null, "Release 列表解析失败：" + parseError);
				yield break;
			}

			if (latest == null)
			{
				onDone?.Invoke(null, "没有包含有效更新清单的游戏 Release");
				yield break;
			}

			JArray assets = null;
			try { assets = JArray.Parse(releasesJson); }
			catch (Exception) { }
			JObject selectedRelease = FindRelease(assets, latest.Version);
			JObject manifestAsset = FindAsset(selectedRelease, "update-manifest.json");
			if (manifestAsset == null)
			{
				onDone?.Invoke(null, "Release 缺少 update-manifest.json");
				yield break;
			}

			string manifestUrl = null;
			parseError = null;
			try { manifestUrl = AssetUrl(manifestAsset); }
			catch (Exception exception)
			{
				parseError = exception.Message;
			}
			if (parseError != null)
			{
				onDone?.Invoke(null, "更新清单下载地址无效：" + parseError);
				yield break;
			}
			string manifestJson;
			using (UnityWebRequest request = UnityWebRequest.Get(manifestUrl))
			{
				request.timeout = 20;
				request.SetRequestHeader("User-Agent", "bpsimF-updater");
				yield return request.SendWebRequest();
				if (request.result != UnityWebRequest.Result.Success)
				{
					onDone?.Invoke(null, "下载更新清单失败：" + request.error);
					yield break;
				}
				manifestJson = request.downloadHandler.text;
			}

			BpsimUpdateInfo update;
			parseError = null;
			try
			{
				update = ParseManifest(manifestJson, selectedRelease, latest.ReleaseUrl);
			}
			catch (Exception exception)
			{
				update = null;
				parseError = exception.Message;
			}
			if (parseError != null)
			{
				onDone?.Invoke(null, "更新清单无效：" + parseError);
				yield break;
			}

			if (update == null)
			{
				onDone?.Invoke(null, "更新清单没有当前平台的安装包");
				yield break;
			}
			update.HasUpdate = CompareVersion(update.Version, Application.version) > 0;
			onDone?.Invoke(update, update.HasUpdate ? null : "当前版本已是最新版本");
		}

		private static IEnumerator InstallRoutine(BpsimUpdateInfo update, Action<bool, string> onDone)
		{
			string directory = Path.Combine(Application.persistentDataPath, "Updates");
			string packagePath = null;
			string installError = null;
			try { packagePath = Path.Combine(directory, SafeFileName(update.AssetName)); }
			catch (Exception exception)
			{
				installError = "安装包文件名无效：" + exception.Message;
			}
			if (installError != null)
			{
				onDone?.Invoke(false, installError);
				yield break;
			}
			installError = null;
			try { Directory.CreateDirectory(directory); }
			catch (Exception exception)
			{
				installError = "创建更新目录失败：" + exception.Message;
			}
			if (installError != null)
			{
				onDone?.Invoke(false, installError);
				yield break;
			}

			installError = null;
			try { if (File.Exists(packagePath)) File.Delete(packagePath); }
			catch (Exception exception)
			{
				installError = "清理旧更新包失败：" + exception.Message;
			}
			if (installError != null)
			{
				onDone?.Invoke(false, installError);
				yield break;
			}

			using (UnityWebRequest request = UnityWebRequest.Get(update.AssetUrl))
			{
				request.timeout = 60 * 30;
				request.SetRequestHeader("User-Agent", "bpsim-bpir-updater");
				request.downloadHandler = new DownloadHandlerFile(packagePath) { removeFileOnAbort = true };
				yield return request.SendWebRequest();
				if (request.result != UnityWebRequest.Result.Success)
				{
					DeleteQuietly(packagePath);
					onDone?.Invoke(false, "下载更新包失败：" + request.error);
					yield break;
				}
			}

			long actualSize = 0;
			string actualHash = null;
			Exception hashError = null;
			Task hashTask = Task.Run(() =>
			{
				try
				{
					actualSize = new FileInfo(packagePath).Length;
					actualHash = ComputeSha256(packagePath);
				}
				catch (Exception exception) { hashError = exception; }
			});
			while (!hashTask.IsCompleted) yield return null;
			if (hashError != null)
			{
				DeleteQuietly(packagePath);
				onDone?.Invoke(false, "读取更新包失败：" + hashError.Message);
				yield break;
			}

			if (actualSize != update.Size || !string.Equals(actualHash, update.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				DeleteQuietly(packagePath);
				onDone?.Invoke(false, "更新包大小或 SHA-256 校验失败");
				yield break;
			}

#if UNITY_ANDROID && !UNITY_EDITOR
			string androidInstallError;
			bool launched = AndroidApkInstaller.TryInstall(packagePath, out androidInstallError);
			onDone?.Invoke(launched, launched ? "已打开 Android 系统安装器" : androidInstallError);
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
			string currentExecutable = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);
			if (!string.Equals(currentExecutable, update.WindowsExecutable, StringComparison.OrdinalIgnoreCase))
			{
				onDone?.Invoke(false, "更新包目标程序与当前游戏不匹配");
				yield break;
			}
			string launchError;
			bool launched = WindowsUpdateLauncher.TryLaunch(packagePath, out launchError);
			onDone?.Invoke(launched, launched ? "更新器已启动，游戏将在安装后重启" : launchError);
#else
			onDone?.Invoke(false, "只能在 Windows 或 Android 玩家版本中安装更新");
#endif
		}

		private static BpsimUpdateInfo FindLatestRelease(string json)
		{
			JArray releases = JArray.Parse(json);
			BpsimUpdateInfo best = null;
			foreach (JToken token in releases)
			{
				JObject release = token as JObject;
				if (release == null || Bool(release["draft"]) || Bool(release["prerelease"])) continue;
				string tag = String(release["tag_name"]);
				string version = StripTag(tag);
				if (string.IsNullOrEmpty(version) || FindAsset(release, "update-manifest.json") == null) continue;
				if (best == null || CompareVersion(version, best.Version) > 0)
				{
					best = new BpsimUpdateInfo
					{
						Version = version,
						ReleaseUrl = String(release["html_url"]),
						Notes = String(release["body"])
					};
				}
			}
			return best;
		}

		private static JObject FindRelease(JArray releases, string version)
		{
			if (releases == null) return null;
			foreach (JToken token in releases)
			{
				JObject release = token as JObject;
				if (release != null && string.Equals(StripTag(String(release["tag_name"])), version, StringComparison.Ordinal))
					return release;
			}
			return null;
		}

		private static JObject FindAsset(JObject release, string name)
		{
			JArray assets = release?["assets"] as JArray;
			if (assets == null) return null;
			foreach (JToken token in assets)
			{
				JObject asset = token as JObject;
				if (asset != null && string.Equals(String(asset["name"]), name, StringComparison.OrdinalIgnoreCase)) return asset;
			}
			return null;
		}

		private static BpsimUpdateInfo ParseManifest(string json, JObject release, string releaseUrl)
		{
			JObject manifest = JObject.Parse(json);
			if (Int(manifest["schema"]) != 1 || !string.Equals(String(manifest["product"]), Product, StringComparison.Ordinal))
				throw new InvalidDataException("产品或 schema 不匹配");

			string version = String(manifest["version"]);
			if (string.IsNullOrEmpty(version) || !IsVersion(version)) throw new InvalidDataException("版本号无效");
			if (CompareVersion(version, StripTag(String(release["tag_name"]))) != 0)
				throw new InvalidDataException("清单版本与 Release 标签不一致");
			string applicationId = String(manifest["applicationId"]);
			if (string.IsNullOrEmpty(applicationId)) throw new InvalidDataException("缺少 applicationId");
			if (!string.Equals(applicationId, AndroidApplicationId, StringComparison.Ordinal))
				throw new InvalidDataException("安装包标识与当前游戏不匹配");
			string windowsExecutable = String(manifest["windowsExecutable"]);
			if (CurrentPlatform() == WindowsPlatform && string.IsNullOrEmpty(windowsExecutable))
				throw new InvalidDataException("缺少 windowsExecutable");

			JObject platforms = manifest["platforms"] as JObject;
			JObject platform = platforms?[CurrentPlatform()] as JObject;
			if (platform == null) return null;

			string name = String(platform["assetName"]);
			string hash = String(platform["sha256"]);
			long size = Long(platform["size"]);
			if (string.IsNullOrEmpty(name) || !string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal)
				|| size <= 0 || hash.Length != 64 || !IsHex(hash))
				throw new InvalidDataException("平台附件的文件名、大小或 SHA-256 无效");

			JObject asset = FindAsset(release, name);
			if (asset == null) throw new InvalidDataException("Release 缺少平台附件 " + name);
			return new BpsimUpdateInfo
			{
				Version = version,
				ReleaseUrl = string.IsNullOrEmpty(releaseUrl) ? String(manifest["releaseUrl"]) : releaseUrl,
				Notes = String(manifest["notes"]),
				AssetName = name,
				AssetUrl = AssetUrl(asset),
				Size = size,
				Sha256 = hash,
				ApplicationId = applicationId,
				WindowsExecutable = windowsExecutable
			};
		}

		private static string AssetUrl(JObject asset)
		{
			string url = String(asset["browser_download_url"]);
			if (string.IsNullOrEmpty(url)) url = String(asset["download_url"]);
			if (!Uri.TryCreate(url, UriKind.Absolute, out Uri parsed) || parsed.Scheme != Uri.UriSchemeHttps)
				throw new InvalidDataException("附件下载地址不是有效的 HTTPS URL");
			return url;
		}

		private static string CurrentPlatform()
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			return AndroidPlatform;
#else
			return WindowsPlatform;
#endif
		}

		private static bool IsSupportedPlatform()
		{
#if UNITY_ANDROID || UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
			return true;
#else
			return false;
#endif
		}

		private static string StripTag(string tag)
		{
			const string prefix = TagPrefix;
			if (tag != null && tag.StartsWith(prefix, StringComparison.Ordinal)) tag = tag.Substring(prefix.Length);
			if (tag != null && tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) && tag.Length > 1 && char.IsDigit(tag[1]))
				tag = tag.Substring(1);
			return IsVersion(tag) ? tag : null;
		}

		private static bool IsVersion(string value)
		{
			if (string.IsNullOrWhiteSpace(value)) return false;
			string[] parts = value.Split('.');
			foreach (string part in parts)
			{
				if (part.Length == 0) return false;
				for (int i = 0; i < part.Length; i++) if (!char.IsDigit(part[i])) return false;
			}
			return true;
		}

		private static int CompareVersion(string left, string right)
		{
			string[] a = (left ?? string.Empty).Split('.');
			string[] b = (right ?? string.Empty).Split('.');
			int count = Math.Max(a.Length, b.Length);
			for (int i = 0; i < count; i++)
			{
				long av = i < a.Length && long.TryParse(a[i], out long parsedA) ? parsedA : 0;
				long bv = i < b.Length && long.TryParse(b[i], out long parsedB) ? parsedB : 0;
				if (av != bv) return av < bv ? -1 : 1;
			}
			return 0;
		}

		private static string ComputeSha256(string path)
		{
			using (FileStream stream = File.OpenRead(path))
			using (SHA256 sha = SHA256.Create())
			{
				byte[] hash = sha.ComputeHash(stream);
				StringBuilder result = new StringBuilder(hash.Length * 2);
				foreach (byte value in hash) result.Append(value.ToString("x2"));
				return result.ToString();
			}
		}

		private static bool IsHex(string value)
		{
			foreach (char character in value)
				if (!Uri.IsHexDigit(character)) return false;
			return true;
		}

		private static string SafeFileName(string value)
		{
			string name = Path.GetFileName(value ?? string.Empty);
			if (string.IsNullOrEmpty(name) || name == "." || name == "..") throw new InvalidDataException("安装包文件名无效");
			return name;
		}

		private static void DeleteQuietly(string path)
		{
			try { if (File.Exists(path)) File.Delete(path); }
			catch (Exception) { }
		}

		private static string String(JToken token) => token == null ? string.Empty : token.ToString();
		private static int Int(JToken token) => token != null && int.TryParse(token.ToString(), out int value) ? value : 0;
		private static long Long(JToken token) => token != null && long.TryParse(token.ToString(), out long value) ? value : 0L;
		private static bool Bool(JToken token) => token != null && bool.TryParse(token.ToString(), out bool value) && value;
	}

	internal static class WindowsUpdateLauncher
	{
		public static bool TryLaunch(string packagePath, out string error)
		{
			error = null;
			try
			{
				string executable = Process.GetCurrentProcess().MainModule.FileName;
				string installDirectory = Path.GetDirectoryName(executable);
				string scriptPath = Path.Combine(Application.persistentDataPath, "Updates", "install-update.ps1");
				Directory.CreateDirectory(Path.GetDirectoryName(scriptPath));
				File.WriteAllText(scriptPath, Script, new UTF8Encoding(true));
				string args = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " + Quote(scriptPath)
					+ " -ProcessId " + Process.GetCurrentProcess().Id
					+ " -InstallDir " + Quote(installDirectory)
					+ " -PackageZip " + Quote(packagePath)
					+ " -ExeName " + Quote(Path.GetFileName(executable));
				ProcessStartInfo start = new ProcessStartInfo("powershell.exe", args)
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					WorkingDirectory = Path.GetDirectoryName(scriptPath)
				};
				Process.Start(start);
				return true;
			}
			catch (Exception exception)
			{
				error = "启动 Windows 更新器失败：" + exception.Message;
				return false;
			}
		}

		private static string Quote(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";

		private const string Script = @"
param([int]$ProcessId, [string]$InstallDir, [string]$PackageZip, [string]$ExeName)
$ErrorActionPreference = 'Stop'
$parent = Split-Path -Parent $MyInvocation.MyCommand.Path
$stage = Join-Path $parent 'staging'
$backup = Join-Path $parent 'backup'
try {
    Wait-Process -Id $ProcessId -Timeout 120
    if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    if (Test-Path $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
    New-Item -ItemType Directory -Path $stage | Out-Null
    Expand-Archive -LiteralPath $PackageZip -DestinationPath $stage -Force
    $updatedExe = Get-ChildItem -LiteralPath $stage -Filter $ExeName -File -Recurse | Select-Object -First 1
    if ($null -eq $updatedExe) { throw 'Updated executable was not found in the package.' }
    $source = $updatedExe.Directory.FullName
    Move-Item -LiteralPath $InstallDir -Destination $backup
    try {
        New-Item -ItemType Directory -Path $InstallDir | Out-Null
        Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $InstallDir -Recurse -Force
        if (-not (Test-Path -LiteralPath (Join-Path $InstallDir $ExeName))) { throw 'Updated executable was not installed.' }
    } catch {
        if (Test-Path $InstallDir) { Remove-Item -LiteralPath $InstallDir -Recurse -Force }
        Move-Item -LiteralPath $backup -Destination $InstallDir
        throw
    }
    Remove-Item -LiteralPath $backup -Recurse -Force
    Remove-Item -LiteralPath $stage -Recurse -Force
    Start-Process -FilePath (Join-Path $InstallDir $ExeName) -WorkingDirectory $InstallDir
} catch {
    $log = Join-Path $parent 'update-error.txt'
    [System.IO.File]::WriteAllText($log, $_.Exception.ToString())
}
";
	}

	internal static class AndroidApkInstaller
	{
		public static bool TryInstall(string apkPath, out string error)
		{
			error = null;
#if UNITY_ANDROID && !UNITY_EDITOR
			try
			{
				using (AndroidJavaClass installer = new AndroidJavaClass("com.bpsim.updater.ApkInstaller"))
				using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
				using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
				{
					error = installer.CallStatic<string>("install", activity, apkPath);
					return string.IsNullOrEmpty(error);
				}
			}
			catch (Exception exception)
			{
				error = "无法打开 Android 安装器：" + exception.Message;
				return false;
			}
#else
			error = "当前平台不支持 APK 安装";
			return false;
#endif
		}
	}
}
