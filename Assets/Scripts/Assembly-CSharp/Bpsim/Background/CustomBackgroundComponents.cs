using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Video;

namespace Bpsim.Background
{
	/// <summary>单例实体标记:整个自定义背景只有一个渲染对象。</summary>
	public struct CustomBackgroundTag : IComponentData
	{
	}

	/// <summary>
	/// 系统每帧算出来的最终姿态（世界空间），既给渲染用，也是「快速填充」按钮回读的来源。
	/// </summary>
	public struct CustomBackgroundSurface : IComponentData
	{
		public bool Visible;

		public float Opacity;

		/// <summary>TRS 矩阵，直接提交给 Graphics.RenderMesh。</summary>
		public float4x4 Matrix;

		public float3 Position;

		/// <summary>世界单位尺寸（quad 的 X/Y 缩放）。</summary>
		public float2 Size;

		public float Rotation;

		public float Distance;
	}

	/// <summary>
	/// 托管媒体状态。Texture2D / VideoPlayer / RenderTexture 都是托管对象，
	/// 只能放在 class 形式的 IComponentData 里（托管组件）。
	/// </summary>
	public class CustomBackgroundMedia : IComponentData
	{
		/// <summary>当前已加载资源的指纹（路径 + 文件写入时间），用来判断要不要重新加载。</summary>
		public string SourceStamp = string.Empty;

		public string LoadedPath = string.Empty;

		public bool IsVideo;

		/// <summary>资源是否已经可画（图片解码完成 / 视频 prepare 完成）。</summary>
		public bool Ready;

		/// <summary>媒体宽高比；0 表示未知（系统回退到屏幕宽高比）。</summary>
		public float Aspect;

		public Texture2D Texture;

		public VideoPlayer Video;

		public RenderTexture Target;

		public GameObject VideoRoot;

		public float AppliedVolume = float.NaN;

		public float AppliedSpeed = float.NaN;

		public float AppliedSeek = float.NaN;

		public bool AppliedLoop;

		public bool AppliedPlaying;

		/// <summary>已经按视频原始分辨率重建过 RenderTexture 的宽高（0 = 还没做过）。</summary>
		public int TargetWidth;

		public int TargetHeight;
	}
}
