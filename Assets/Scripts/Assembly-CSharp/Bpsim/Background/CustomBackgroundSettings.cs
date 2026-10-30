using Bpsim.ComponentModel;

namespace Bpsim.Background
{
	/// <summary>
	/// 「自定义背景」分组的设置数据（随 UserSettings.json 一起持久化）。
	///
	/// 这里只放参数，不放任何渲染对象:设置面板写它，<see cref="CustomBackgroundSystem"/>
	/// 每帧读它并把参数折算成一个矩阵直接提交给 Graphics.RenderMesh，
	/// 因此自定义背景不需要场景里的 GameObject，也没有 MonoBehaviour.LateUpdate 的开销。
	///
	/// 字段语义与 BPSS 的 CustomBackgroundConfig / INCustomBackgroundInterface 一致:
	/// <list type="bullet">
	/// <item>Enabled = 是否显示背景（BPSS 的 BackgroundEnabled）。</item>
	/// <item>CustomScale = 是否用 X/Y 拉伸 + 摄像机自适应来算尺寸（BPSS 的 Enabled）。</item>
	/// <item>UseAbsolute = 固定在绝对世界坐标，不跟随相机。</item>
	/// </list>
	/// </summary>
	public class CustomBackgroundSettings : SettingsBase
	{
		private bool m_enabled;

		private string m_path = string.Empty;

		private float m_volume = 1f;

		private float m_opacity = 1f;

		private bool m_customScale = true;

		private bool m_cameraAdaptive = true;

		private float m_scaleX = 1f;

		private float m_scaleY = 1f;

		private float m_offsetX;

		private float m_offsetY;

		private bool m_useAbsolute;

		private float m_absoluteX;

		private float m_absoluteY;

		private float m_rotation;

		private float m_distance = 4f;

		private bool m_loop = true;

		private float m_playbackSpeed = 1f;

		private float m_seekTime;

		private bool m_playing = true;

		/// <summary>背景是否显示。关闭时系统直接跳过绘制（BPSS 里这颗是「开启背景」开关）。</summary>
		public bool Enabled
		{
			get
			{
				return m_enabled;
			}
			set
			{
				SetProperty(ref m_enabled, value, nameof(Enabled));
			}
		}

		/// <summary>
		/// 背景资源路径。图片会被复制到 <see cref="CustomBackgroundMedia.StorageDirectory"/>，
		/// 视频则直接记录原文件路径（与 BPSS 一致）。
		/// </summary>
		public string Path
		{
			get
			{
				return m_path;
			}
			set
			{
				SetProperty(ref m_path, value ?? string.Empty, nameof(Path));
			}
		}

		/// <summary>视频音量（0~1）。图片背景忽略它。</summary>
		public float Volume
		{
			get
			{
				return m_volume;
			}
			set
			{
				if (float.IsFinite(value))
				{
					SetProperty(ref m_volume, Clamp01(value), nameof(Volume));
				}
			}
		}

		/// <summary>不透明度（0~1）。</summary>
		public float Opacity
		{
			get
			{
				return m_opacity;
			}
			set
			{
				if (float.IsFinite(value))
				{
					SetProperty(ref m_opacity, Clamp01(value), nameof(Opacity));
				}
			}
		}

		/// <summary>自定义缩放：开启时按视野高度 × X/Y 拉伸算尺寸；关闭时自动撑满视野。</summary>
		public bool CustomScale
		{
			get
			{
				return m_customScale;
			}
			set
			{
				SetProperty(ref m_customScale, value, nameof(CustomScale));
			}
		}

		/// <summary>摄像机自适应：开启时尺寸随相机缩放变化，关闭时用固定世界尺寸。</summary>
		public bool CameraAdaptive
		{
			get
			{
				return m_cameraAdaptive;
			}
			set
			{
				SetProperty(ref m_cameraAdaptive, value, nameof(CameraAdaptive));
			}
		}

		/// <summary>X 轴拉伸（摄像机自适应开启时是视野高度的倍数）。</summary>
		public float ScaleX
		{
			get
			{
				return m_scaleX;
			}
			set
			{
				if (float.IsFinite(value))
				{
					SetProperty(ref m_scaleX, value, nameof(ScaleX));
				}
			}
		}

		/// <summary>Y 轴拉伸（摄像机自适应开启时是视野高度的倍数）。</summary>
		public float ScaleY
		{
			get
			{
				return m_scaleY;
			}
			set
			{
				if (float.IsFinite(value))
				{
					SetProperty(ref m_scaleY, value, nameof(ScaleY));
				}
			}
		}

		/// <summary>X 轴偏移，单位是视野宽度。</summary>
		public float OffsetX
		{
			get
			{
				return m_offsetX;
			}
			set
			{
				if (float.IsFinite(value))
				{
					SetProperty(ref m_offsetX, value, nameof(OffsetX));
				}
			}
		}

		/// <summary>Y 轴偏移，单位是视野高度。</summary>
		public float OffsetY
		{
			get
			{
				return m_offsetY;
			}
			set
			{
				if (float.IsFinite(value))
				{
					SetProperty(ref m_offsetY, value, nameof(OffsetY));
				}
			}
		}

		/// <summary>启用绝对坐标：固定在全局世界坐标，不跟随相机移动/旋转。</summary>
		public bool UseAbsolute
		{
			get
			{
				return m_useAbsolute;
			}
			set
			{
				SetProperty(ref m_useAbsolute, value, nameof(UseAbsolute));
			}
		}

		/// <summary>绝对坐标 X（世界坐标）。</summary>
		public float AbsoluteX
		{
			get
			{
				return m_absoluteX;
			}
			set
			{
				if (float.IsFinite(value))
				{
					SetProperty(ref m_absoluteX, value, nameof(AbsoluteX));
				}
			}
		}

		/// <summary>绝对坐标 Y（世界坐标）。</summary>
		public float AbsoluteY
		{
			get
			{
				return m_absoluteY;
			}
			set
			{
				if (float.IsFinite(value))
				{
					SetProperty(ref m_absoluteY, value, nameof(AbsoluteY));
				}
			}
		}

		/// <summary>绕 Z 轴的旋转角度（度）。</summary>
		public float Rotation
		{
			get
			{
				return m_rotation;
			}
			set
			{
				if (float.IsFinite(value))
				{
					SetProperty(ref m_rotation, value, nameof(Rotation));
				}
			}
		}

		/// <summary>Z 轴距离（世界坐标 z；相机在 z=-10 朝 +z，数值越大越远）。</summary>
		public float Distance
		{
			get
			{
				return m_distance;
			}
			set
			{
				if (float.IsFinite(value) && value > 0f)
				{
					SetProperty(ref m_distance, value, nameof(Distance));
				}
			}
		}

		/// <summary>视频是否循环播放。</summary>
		public bool Loop
		{
			get
			{
				return m_loop;
			}
			set
			{
				SetProperty(ref m_loop, value, nameof(Loop));
			}
		}

		/// <summary>视频播放速度（1 = 原速）。</summary>
		public float PlaybackSpeed
		{
			get
			{
				return m_playbackSpeed;
			}
			set
			{
				if (float.IsFinite(value) && value > 0f)
				{
					SetProperty(ref m_playbackSpeed, value, nameof(PlaybackSpeed));
				}
			}
		}

		/// <summary>跳转时间（秒）。改动即触发一次 seek，系统不会回写它。</summary>
		public float SeekTime
		{
			get
			{
				return m_seekTime;
			}
			set
			{
				if (float.IsFinite(value) && value >= 0f)
				{
					SetProperty(ref m_seekTime, value, nameof(SeekTime));
				}
			}
		}

		/// <summary>播放 / 暂停。</summary>
		public bool Playing
		{
			get
			{
				return m_playing;
			}
			set
			{
				SetProperty(ref m_playing, value, nameof(Playing));
			}
		}

		public CustomBackgroundSettings()
		{
			Enabled = false;
			Path = string.Empty;
			Volume = 1f;
			Opacity = 1f;
			CustomScale = true;
			CameraAdaptive = true;
			ScaleX = 1f;
			ScaleY = 1f;
			OffsetX = 0f;
			OffsetY = 0f;
			UseAbsolute = false;
			AbsoluteX = 0f;
			AbsoluteY = 0f;
			Rotation = 0f;
			Distance = 4f;
			Loop = true;
			PlaybackSpeed = 1f;
			SeekTime = 0f;
			Playing = true;
		}

		/// <summary>从另一份设置拷贝（UserSettings.Load 用）。</summary>
		public void Update(CustomBackgroundSettings settings)
		{
			if (settings == null)
			{
				return;
			}
			Enabled = settings.Enabled;
			Path = settings.Path;
			Volume = settings.Volume;
			Opacity = settings.Opacity;
			CustomScale = settings.CustomScale;
			CameraAdaptive = settings.CameraAdaptive;
			ScaleX = settings.ScaleX;
			ScaleY = settings.ScaleY;
			OffsetX = settings.OffsetX;
			OffsetY = settings.OffsetY;
			UseAbsolute = settings.UseAbsolute;
			AbsoluteX = settings.AbsoluteX;
			AbsoluteY = settings.AbsoluteY;
			Rotation = settings.Rotation;
			Distance = settings.Distance;
			Loop = settings.Loop;
			PlaybackSpeed = settings.PlaybackSpeed;
			SeekTime = settings.SeekTime;
			Playing = settings.Playing;
		}

		private static float Clamp01(float value)
		{
			if (value < 0f)
			{
				return 0f;
			}
			return (value > 1f) ? 1f : value;
		}
	}
}
