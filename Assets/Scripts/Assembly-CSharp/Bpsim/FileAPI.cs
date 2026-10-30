using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using ShellFileDialogs;

namespace Bpsim
{
	internal static class FileAPI
	{
		public static UniTask<string> CreateFile(string initialDirectory)
		{
			return UniTask.FromResult(CreateFileWindows(initialDirectory));
		}

		public static UniTask<string> OpenFile(string initialDirectory)
		{
			return UniTask.FromResult(OpenFileWindows(initialDirectory));
		}

		public static UniTask<string> OpenDirectory(string initialDirectory)
		{
			return UniTask.FromResult(OpenFileWindows(initialDirectory));
		}

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

		private static string CreateFileWindows(string initialDirectory)
		{
			Filter[] filters = new Filter[1]
			{
				new Filter("All files", "*")
			};
			return FileSaveDialog.ShowDialog(IntPtr.Zero, string.Empty, initialDirectory, string.Empty, (IReadOnlyCollection<Filter>)(object)filters, 0);
		}

		private static string OpenFileWindows(string initialDirectory)
		{
			Filter[] filters = new Filter[1]
			{
				new Filter("All files", "*")
			};
			return FileOpenDialog.ShowSingleSelectDialog(IntPtr.Zero, string.Empty, initialDirectory, string.Empty, (IReadOnlyCollection<Filter>)(object)filters, 0);
		}
	}
}
