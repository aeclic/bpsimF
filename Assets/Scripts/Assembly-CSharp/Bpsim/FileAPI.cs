using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using ShellFileDialogs;
#endif

namespace Bpsim
{
	internal sealed class FileSelection : IDisposable
	{
		private Action m_cleanup;

		public string Path { get; }

		public FileSelection(string path, Action cleanup = null)
		{
			Path = path;
			m_cleanup = cleanup;
		}

		public void Keep() => m_cleanup = null;

		public void Dispose()
		{
			Action cleanup = m_cleanup;
			m_cleanup = null;
			cleanup?.Invoke();
		}
	}

	internal static class FileAPI
	{
		public static UniTask<string> CreateFile(string initialDirectory)
		{
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
			return UniTask.FromResult(CreateFileWindows(initialDirectory));
#else
			throw new PlatformNotSupportedException();
#endif
		}

		public static UniTask<FileSelection> OpenFile(string initialDirectory, bool keepForSession = false)
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			return AndroidDocuments.Open(directory: false, keepForSession: keepForSession);
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
			return UniTask.FromResult(OpenFileWindows(initialDirectory));
#else
			throw new PlatformNotSupportedException();
#endif
		}

		public static UniTask<FileSelection> OpenDirectory(string initialDirectory)
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			return AndroidDocuments.Open(directory: true, keepForSession: false);
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
			return UniTask.FromResult(OpenFileWindows(initialDirectory));
#else
			throw new PlatformNotSupportedException();
#endif
		}

#if UNITY_ANDROID && !UNITY_EDITOR
		public static UniTask<bool> ExportFile(string fileName, string mimeType, Action<string> write) =>
			AndroidDocuments.Export(fileName, mimeType, write);
#endif

		public static byte[] ReadFileAsBytes(string path)
		{
			return File.ReadAllBytes(path);
		}

		public static Stream ReadFileAsStream(string path)
		{
			return File.OpenRead(path);
		}

		public static void WriteFile(string path, byte[] data)
		{
			File.WriteAllBytes(path, data);
		}

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
		private static string CreateFileWindows(string initialDirectory)
		{
			Filter[] filters = new Filter[1]
			{
				new Filter("All files", "*")
			};
			return FileSaveDialog.ShowDialog(IntPtr.Zero, string.Empty, initialDirectory, string.Empty, (IReadOnlyCollection<Filter>)(object)filters, 0);
		}

		private static FileSelection OpenFileWindows(string initialDirectory)
		{
			Filter[] filters = new Filter[1]
			{
				new Filter("All files", "*")
			};
			string path = FileOpenDialog.ShowSingleSelectDialog(IntPtr.Zero, string.Empty, initialDirectory, string.Empty, (IReadOnlyCollection<Filter>)(object)filters, 0);
			return string.IsNullOrEmpty(path) ? null : new FileSelection(path);
		}
#endif
	}
}
