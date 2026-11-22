#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.IO;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;

namespace Bpsim
{
	internal static class AndroidDocuments
	{
		private const string BridgeName = "com.bpsim.documents.DocumentPicker";
		private static bool s_pending;

		internal static async UniTask<FileSelection> Open(bool directory, bool keepForSession)
		{
			string root = keepForSession ? Application.persistentDataPath : Application.temporaryCachePath;
			string workspace = Path.Combine(root, "BpsimDocuments", Guid.NewGuid().ToString("N"));
			try
			{
				Directory.CreateDirectory(workspace);
				string action = directory ? "OPEN_DOCUMENT_TREE" : "OPEN_DOCUMENT";
				string path = await Request(action, workspace, "*/*", string.Empty);
				if (path != null) return new FileSelection(path, () => Clean(workspace));
				Clean(workspace);
				return null;
			}
			catch
			{
				Clean(workspace);
				throw;
			}
		}

		internal static async UniTask<bool> Export(string fileName, string mimeType, Action<string> write)
		{
			string workspace = Path.Combine(Application.temporaryCachePath, "BpsimDocuments", Guid.NewGuid().ToString("N"));
			try
			{
				Directory.CreateDirectory(workspace);
				string path = Path.Combine(workspace, "export");
				write(path);
				return await Request("CREATE_DOCUMENT", path, mimeType, fileName) != null;
			}
			finally { Clean(workspace); }
		}

		private static async UniTask<string> Request(string action, string localPath, string mimeType, string title)
		{
			if (s_pending) throw new InvalidOperationException("已有文件选择窗口，请先关闭它。");
			s_pending = true;
			try
			{
				var callback = new PickerResult();
				using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
				using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
				using (var picker = new AndroidJavaClass(BridgeName))
				{
					if (activity == null) throw new InvalidOperationException("无法取得 Android 游戏窗口。");
					picker.CallStatic("launch", activity, "android.intent.action." + action, localPath, mimeType, title, callback);
				}
				var value = await callback.Completion.Task;
				await UniTask.SwitchToMainThread();
				if (!string.IsNullOrEmpty(value.error)) throw new IOException(value.error);
				return string.IsNullOrEmpty(value.path) ? null : value.path;
			}
			finally { s_pending = false; }
		}

		private static void Clean(string workspace)
		{
			try { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); }
			catch (IOException failure) { Debug.LogWarning(failure.Message); }
			catch (UnauthorizedAccessException failure) { Debug.LogWarning(failure.Message); }
		}

		private sealed class PickerResult : AndroidJavaProxy
		{
			public readonly TaskCompletionSource<(string path, string error)> Completion =
				new TaskCompletionSource<(string path, string error)>(TaskCreationOptions.RunContinuationsAsynchronously);

			public PickerResult() : base(BridgeName + "$Result") { }

			[Preserve]
			public void complete(string path, string error) => Completion.TrySetResult((path, error));
		}
	}
}
#endif
