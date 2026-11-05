using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Bpsim.Mods
{
	/// <summary>Windows file-copy clipboard data and text paths are different formats.</summary>
	public static class ModZipClipboard
	{
		public static bool TryRead(Func<string> readText, out string path, out string error)
		{
			path = null;
			error = null;
			try
			{
				string[] files;
				if (TryReadFiles(out files)) return TryResolve(null, files, out path, out error);
				return TryResolve(readText != null ? readText() : null, null, out path, out error);
			}
			catch (Exception ex)
			{
				error = "无法读取剪贴板：" + ex.Message + "。也可以点击“选择 zip 安装”。";
				return false;
			}
		}

		public static bool TryResolve(string text, IEnumerable<string> files, out string path, out string error)
		{
			path = null;
			error = null;
			if (files != null)
			{
				List<string> candidates = new List<string>();
				foreach (string file in files)
				{
					if (!string.IsNullOrEmpty(file) && file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) candidates.Add(file);
				}
				if (candidates.Count != 1)
				{
					error = candidates.Count > 1 ? "剪贴板中有多个 zip，请只复制一个文件。" : "剪贴板里的文件不是 zip。";
					return false;
				}
				return TryNormalizePath(candidates[0], out path, out error);
			}
			return TryNormalizePath(text, out path, out error);
		}

		public static bool TryNormalizePath(string text, out string path, out string error)
		{
			path = null;
			error = null;
			if (string.IsNullOrWhiteSpace(text))
			{
				error = "剪贴板中没有 zip 文件或路径。请复制文件，或点击“选择 zip 安装”。";
				return false;
			}
			string candidate = text.Trim().Trim('\uFEFF', '\u200B').Trim();
			if (candidate.IndexOfAny(new[] { '\r', '\n' }) >= 0)
			{
				error = "剪贴板中有多行内容，请复制单个 zip 文件或它的完整路径。";
				return false;
			}
			int markdown = candidate.LastIndexOf("](", StringComparison.Ordinal);
			if (candidate.StartsWith("[", StringComparison.Ordinal) && markdown > 0 && candidate.EndsWith(")", StringComparison.Ordinal))
				candidate = candidate.Substring(markdown + 2, candidate.Length - markdown - 3).Trim();
			for (int i = 0; i < 3 && candidate.Length > 1; i++)
			{
				char first = candidate[0], last = candidate[candidate.Length - 1];
				if ((first == '"' && last == '"') || (first == '\'' && last == '\'') || (first == '<' && last == '>'))
					candidate = candidate.Substring(1, candidate.Length - 2).Trim();
				else break;
			}
			try
			{
				if (candidate.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
				{
					Uri uri;
					if (!Uri.TryCreate(candidate, UriKind.Absolute, out uri) || !uri.IsFile) throw new ArgumentException("本地文件地址无效");
					candidate = uri.LocalPath;
				}
				else if (candidate.IndexOf("://", StringComparison.Ordinal) >= 0)
				{
					error = "这里安装的是本地 zip；请先下载文件，再复制文件或路径。";
					return false;
				}
				if (!Path.IsPathRooted(candidate))
				{
					error = "请复制 zip 的完整路径，而不是仅复制文件名。";
					return false;
				}
				if (!File.Exists(candidate) && candidate.IndexOf('%') >= 0) candidate = Uri.UnescapeDataString(candidate);
				candidate = Path.GetFullPath(candidate);
				if (!string.Equals(Path.GetExtension(candidate), ".zip", StringComparison.OrdinalIgnoreCase))
				{
					error = "所选文件不是 .zip 安装包。";
					return false;
				}
				if (!File.Exists(candidate))
				{
					error = "找不到 zip 文件：" + Path.GetFileName(candidate);
					return false;
				}
				path = candidate;
				return true;
			}
			catch (Exception ex)
			{
				error = "zip 路径无效：" + ex.Message;
				return false;
			}
		}

		private static bool TryReadFiles(out string[] files)
		{
			files = null;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
			if (!IsClipboardFormatAvailable(15)) return false; // CF_HDROP; Explorer Ctrl+C.
			if (!OpenClipboard(IntPtr.Zero)) throw new IOException("剪贴板正被其他程序占用，请稍后重试");
			try
			{
				IntPtr drop = GetClipboardData(15);
				if (drop == IntPtr.Zero) throw new IOException("无法取得剪贴板文件列表");
				uint count = DragQueryFileW(drop, uint.MaxValue, null, 0);
				if (count > 64) throw new IOException("剪贴板文件过多，请只复制一个 zip");
				files = new string[count];
				for (uint i = 0; i < count; i++)
				{
					uint length = DragQueryFileW(drop, i, null, 0);
					if (length == 0 || length > 32767) throw new IOException("剪贴板文件路径无效");
					StringBuilder buffer = new StringBuilder((int)length + 1);
					if (DragQueryFileW(drop, i, buffer, length + 1) == 0) throw new IOException("无法读取剪贴板文件路径");
					files[i] = buffer.ToString();
				}
				return true;
			}
			finally { CloseClipboard(); }
#else
			return false;
#endif
		}

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
		[DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
		[DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr window);
		[DllImport("user32.dll")] private static extern bool CloseClipboard();
		[DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
		[DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
		private static extern uint DragQueryFileW(IntPtr drop, uint index, StringBuilder path, uint characters);
#endif
	}
}
