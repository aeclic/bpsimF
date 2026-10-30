using System;
using System.Collections.Generic;
using System.IO;
using ShellFileDialogs;
using UnityEngine;
using UnityEngine.Video;

namespace Bpsim.Background
{
	/// <summary>
	/// 自定义背景的资源加载 / 销毁。全部是编辑器无关的静态工具，
	/// 由 <see cref="CustomBackgroundSystem"/> 调用（它负责"什么时候加载"，这里负责"怎么加载"）。
	/// </summary>
	internal static class CustomBackgroundMediaLoader
	{
		/// <summary>图片与视频共用的文件对话框过滤器（与 BPSS 的一致）。</summary>
		private const string MediaFilterPattern = "*.png;*.jpg;*.jpeg;*.bmp;*.tga;*.psd;*.gif;*.mp4;*.mov;*.webm;*.m4v;*.avi;*.mkv;*.wmv";

		private static readonly string[] s_videoExtensions =
		{
			".mp4", ".mov", ".webm", ".m4v", ".avi", ".mkv", ".wmv", ".asf", ".mpg", ".mpeg", ".ogv"
		};

		/// <summary>资源存放目录（上传的图片会被复制到这里，避免用户删掉源文件后背景丢失）。</summary>
		public static string StorageDirectory => Path.Combine(Application.persistentDataPath, "CustomBackground");

		/// <summary>上传图片落盘后的固定文件名（内容可以是任意格式，Texture2D.LoadImage 按文件头识别）。</summary>
		public static string UploadedImagePath => Path.Combine(StorageDirectory, "uploaded.png");

		/// <summary>按扩展名判断是不是视频。</summary>
		public static bool IsVideoFile(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return false;
			}
			string extension = Path.GetExtension(path).ToLowerInvariant();
			foreach (string candidate in s_videoExtensions)
			{
				if (extension == candidate)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>打开文件对话框选一张图片或一个视频；取消返回空串。</summary>
		public static string PickMediaFile(string initialDirectory)
		{
			try
			{
				if (string.IsNullOrEmpty(initialDirectory) || !Directory.Exists(initialDirectory))
				{
					initialDirectory = Application.dataPath;
				}
				Filter[] filters = new Filter[1]
				{
					new Filter("Image/Video", MediaFilterPattern)
				};
				string path = FileOpenDialog.ShowSingleSelectDialog(IntPtr.Zero, string.Empty, initialDirectory, string.Empty, (IReadOnlyCollection<Filter>)(object)filters, 0);
				return path ?? string.Empty;
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[BpsimBackground] 打开文件对话框失败: " + ex.Message);
				return string.Empty;
			}
		}

		/// <summary>
		/// 导入用户选中的文件：图片复制进 <see cref="StorageDirectory"/> 并解码，
		/// 视频直接记录原路径（与 BPSS 行为一致）。返回真正要写进设置的路径。
		/// </summary>
		public static string Import(string sourcePath, CustomBackgroundMedia media)
		{
			if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
			{
				return string.Empty;
			}
			if (IsVideoFile(sourcePath))
			{
				LoadVideo(sourcePath, media);
				return sourcePath;
			}
			Directory.CreateDirectory(StorageDirectory);
			string target = UploadedImagePath;
			if (string.Compare(Path.GetFullPath(sourcePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase) != 0)
			{
				File.Copy(sourcePath, target, overwrite: true);
			}
			LoadTexture(target, media);
			return target;
		}

		/// <summary>解码一张图片。</summary>
		public static void LoadTexture(string path, CustomBackgroundMedia media)
		{
			byte[] bytes = File.ReadAllBytes(path);
			Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
			{
				hideFlags = HideFlags.DontSave,
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
				name = "[BpsimBackground] " + Path.GetFileName(path)
			};
			if (!texture.LoadImage(bytes, markNonReadable: true))
			{
				UnityEngine.Object.Destroy(texture);
				throw new InvalidDataException("无法解码图片: " + path);
			}
			media.Texture = texture;
			media.Video = null;
			media.IsVideo = false;
			media.Aspect = (texture.height > 0) ? ((float)texture.width / texture.height) : 0f;
			media.Ready = true;
			media.LoadedPath = path;
		}

		/// <summary>
		/// 起一个隐藏的 VideoPlayer 播视频。画面进 RenderTexture（尺寸按视频原始分辨率），
		/// 由系统把它当贴图交给材质，所以视频和图片走同一条绘制路径。
		/// </summary>
		public static void LoadVideo(string path, CustomBackgroundMedia media)
		{
			GameObject root = new GameObject("[BpsimCustomBackgroundVideo]");
			root.hideFlags = HideFlags.DontSave;
			UnityEngine.Object.DontDestroyOnLoad(root);
			VideoPlayer player = root.AddComponent<VideoPlayer>();
			player.playOnAwake = false;
			player.source = VideoSource.Url;
			player.url = path;
			player.renderMode = VideoRenderMode.RenderTexture;
			player.audioOutputMode = VideoAudioOutputMode.Direct;
			player.skipOnDrop = true;
			player.isLooping = true;
			player.waitForFirstFrame = false;
			player.errorReceived += delegate(VideoPlayer source, string message)
			{
				Debug.LogWarning("[BpsimBackground] 视频播放错误: " + message + " (" + path + ")");
			};
			player.prepareCompleted += delegate(VideoPlayer source)
			{
				OnVideoPrepared(source, media);
			};
			player.Prepare();

			media.VideoRoot = root;
			media.Video = player;
			media.Target = null;
			media.TargetWidth = 0;
			media.TargetHeight = 0;
			media.Texture = null;
			media.IsVideo = true;
			media.Ready = false;
			media.Aspect = 0f;
			media.LoadedPath = path;
			media.AppliedVolume = float.NaN;
			media.AppliedSpeed = float.NaN;
			media.AppliedSeek = float.NaN;
			media.AppliedLoop = false;
			media.AppliedPlaying = false;
		}

		private static void OnVideoPrepared(VideoPlayer player, CustomBackgroundMedia media)
		{
			if (player == null || media == null)
			{
				return;
			}
			int width = (int)player.width;
			int height = (int)player.height;
			if (width <= 0 || height <= 0)
			{
				Debug.LogWarning("[BpsimBackground] 视频尺寸无效，跳过背景渲染: " + media.LoadedPath);
				return;
			}
			// 视频分辨率只在 prepare 之后才知道，所以 RenderTexture 在这里才建。
			// 尺寸一致时不再重建（重建会让 VideoPlayer 重新 prepare，形成循环）。
			if (media.Target == null || media.TargetWidth != width || media.TargetHeight != height)
			{
				ReleaseTarget(media);
				RenderTexture target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
				{
					hideFlags = HideFlags.DontSave,
					useMipMap = false,
					wrapMode = TextureWrapMode.Clamp,
					filterMode = FilterMode.Bilinear,
					name = "[BpsimBackground] Video"
				};
				target.Create();
				media.Target = target;
				media.TargetWidth = width;
				media.TargetHeight = height;
				player.targetTexture = target;
			}
			media.Aspect = (float)width / height;
			media.Ready = true;
			// 参数会被系统下一帧同步；这里先按当前设置把第一帧状态摆正。
			CustomBackgroundSettings settings = UserSettings.Instance?.CustomBackground;
			if (settings != null)
			{
				player.isLooping = settings.Loop;
				player.playbackSpeed = settings.PlaybackSpeed;
				player.SetDirectAudioVolume(0, Mathf.Clamp01(settings.Volume));
				media.AppliedVolume = settings.Volume;
				media.AppliedSpeed = settings.PlaybackSpeed;
				media.AppliedLoop = settings.Loop;
				if (settings.SeekTime > 0f)
				{
					player.time = Mathf.Clamp(settings.SeekTime, 0f, Mathf.Max(0f, (float)player.length));
					media.AppliedSeek = settings.SeekTime;
				}
				if (settings.Playing)
				{
					player.Play();
				}
				media.AppliedPlaying = settings.Playing;
			}
			else
			{
				player.Play();
				media.AppliedPlaying = true;
			}
		}

		/// <summary>视频的当前播放位置 / 总时长（给设置面板的进度显示用）。</summary>
		public static bool TryGetPlayback(CustomBackgroundMedia media, out float time, out float duration)
		{
			time = 0f;
			duration = 0f;
			VideoPlayer player = media?.Video;
			if (player == null || !player.isPrepared)
			{
				return false;
			}
			time = (float)player.time;
			duration = (float)player.length;
			return duration > 0f;
		}

		/// <summary>跳转到指定秒数。</summary>
		public static void Seek(CustomBackgroundMedia media, float seconds)
		{
			VideoPlayer player = media?.Video;
			if (player == null || !player.isPrepared)
			{
				return;
			}
			player.time = Mathf.Clamp(seconds, 0f, Mathf.Max(0f, (float)player.length));
		}

		/// <summary>释放全部媒体资源（换资源、清空背景、系统销毁时调用）。</summary>
		public static void Release(CustomBackgroundMedia media)
		{
			if (media == null)
			{
				return;
			}
			if (media.Video != null)
			{
				try
				{
					media.Video.Stop();
					media.Video.targetTexture = null;
				}
				catch (Exception)
				{
				}
				media.Video = null;
			}
			if (media.VideoRoot != null)
			{
				UnityEngine.Object.Destroy(media.VideoRoot);
				media.VideoRoot = null;
			}
			ReleaseTarget(media);
			if (media.Texture != null)
			{
				UnityEngine.Object.Destroy(media.Texture);
				media.Texture = null;
			}
			media.Ready = false;
			media.IsVideo = false;
			media.Aspect = 0f;
			media.LoadedPath = string.Empty;
			media.SourceStamp = string.Empty;
			media.AppliedVolume = float.NaN;
			media.AppliedSpeed = float.NaN;
			media.AppliedSeek = float.NaN;
			media.AppliedLoop = false;
			media.AppliedPlaying = false;
		}

		private static void ReleaseTarget(CustomBackgroundMedia media)
		{
			if (media.Target == null)
			{
				return;
			}
			media.Target.Release();
			UnityEngine.Object.Destroy(media.Target);
			media.Target = null;
			media.TargetWidth = 0;
			media.TargetHeight = 0;
		}

		/// <summary>当前可用的贴图（图片原始贴图或视频 RenderTexture）。</summary>
		public static Texture CurrentTexture(CustomBackgroundMedia media)
		{
			if (media == null || !media.Ready)
			{
				return null;
			}
			if (media.IsVideo)
			{
				return media.Target;
			}
			return media.Texture;
		}
	}
}
