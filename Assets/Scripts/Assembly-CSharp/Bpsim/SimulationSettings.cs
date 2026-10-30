using Unity.Physics;

namespace Bpsim
{
	public partial class SimulationSettings : SettingsBase
	{
		private SimulationType m_simulationType;

		private float m_fixedTimeStep;

		private int m_solverIterationCount;

		private float m_gravityX;

		private float m_gravityY;

		private bool m_createJoints;

		private bool m_infiniteConnectionStrength;

		private float m_connectionStrengthFactor;

		private bool m_partSelfCollision;

		private bool m_megaStructure;

		private float m_megaStructureBreakSpeed;

		public SimulationType SimulationType
		{
			get
			{
				return m_simulationType;
			}
			set
			{
				SetProperty(ref m_simulationType, value, "SimulationType");
			}
		}

		public float FixedTimeStep
		{
			get
			{
				return m_fixedTimeStep;
			}
			set
			{
				if (float.IsFinite(value) && value >= 0f)
				{
					SetProperty(ref m_fixedTimeStep, value, "FixedTimeStep");
				}
			}
		}

		public int SolverIterationCount
		{
			get
			{
				return m_solverIterationCount;
			}
			set
			{
				if (value >= 1)
				{
					SetProperty(ref m_solverIterationCount, value, "SolverIterationCount");
				}
			}
		}

		public float GravityX
		{
			get
			{
				return m_gravityX;
			}
			set
			{
				if (float.IsFinite(value))
				{
					SetProperty(ref m_gravityX, value, "GravityX");
				}
			}
		}

		public float GravityY
		{
			get
			{
				return m_gravityY;
			}
			set
			{
				if (float.IsFinite(value))
				{
					SetProperty(ref m_gravityY, value, "GravityY");
				}
			}
		}

		public bool CreateJoints
		{
			get
			{
				return m_createJoints;
			}
			set
			{
				SetProperty(ref m_createJoints, value, "CreateJoints");
			}
		}

		public bool InfiniteConnectionStrength
		{
			get
			{
				return m_infiniteConnectionStrength;
			}
			set
			{
				SetProperty(ref m_infiniteConnectionStrength, value, "InfiniteConnectionStrength");
			}
		}

		public float ConnectionStrengthFactor
		{
			get
			{
				return m_connectionStrengthFactor;
			}
			set
			{
				if (float.IsFinite(value) && value >= 0f)
				{
					SetProperty(ref m_connectionStrengthFactor, value, "ConnectionStrengthFactor");
				}
			}
		}

		/// <summary>
		/// 同一焊接连通块内部的部件是否互相碰撞。默认 false：连通块内部已被 fixed joint 焊死，
		/// 这些接触没有物理意义，而 Unity.Physics 会按速度膨胀 AABB，大载具高速旋转时
		/// 1600+ 个部件的膨胀体会互相吞掉，宽相位/推测接触数量爆炸（整块自旋 ω=32 时单帧 243ms）。
		/// 改成 true 可以恢复改动前的原生行为（每个部件独立碰撞体过滤），代价是高速旋转卡顿回归。
		/// </summary>
		public bool PartSelfCollision
		{
			get
			{
				return m_partSelfCollision;
			}
			set
			{
				SetProperty(ref m_partSelfCollision, value, "PartSelfCollision");
			}
		}

		/// <summary>
		/// 巨构优化（mega structure）：整台载具焊成一个刚体来模拟。
		/// <para>
		/// 开启后不再为每个部件创建独立刚体与 fixed joint（2 万部件 = 18 万关节的巨型求解岛），
		/// 而是把所有部件碰撞体合并成一个 <see cref="Unity.Physics.CompoundCollider"/>，
		/// 只留一个动态刚体。动力部件（引擎/轮子/推进器）的施力仍发生在部件原来的位置：
		/// 部件保留 <c>PhysicsVelocity</c> 作为「期望速度」，每步由系统把「实际速度 - 期望速度」
		/// 还原成冲量作用到整车刚体的对应点上，所以施力点、力矩臂、重心都保持原样。
		/// </para>
		/// <para>
		/// 代价：载具内部不再是一个可变形的结构，而是纯刚体（这正是「巨构」想要的效果，
		/// 同时也是性能收益的来源）。部件被撞到超过 <see cref="MegaStructureBreakSpeed" />
		/// 时会从复合体里摘出来，变回一个独立刚体。
		/// </para>
		/// <para>要恢复「每个部件一个刚体」的老行为，把它设成 false 即可（默认 false）。</para>
		/// </summary>
		public bool MegaStructure
		{
			get
			{
				return m_megaStructure;
			}
			set
			{
				SetProperty(ref m_megaStructure, value, "MegaStructure");
			}
		}

		/// <summary>
		/// 巨构模式下部件掉落的冲击阈值，单位 m/s（接触点法向接近速度，用法向速度而不是冲量，
		/// 因为冲量还要除以质量才直观：8 大约是「从 3 米高摔到硬地」）。
		/// 0 表示永不掉落。默认 8。
		/// </summary>
		public float MegaStructureBreakSpeed
		{
			get
			{
				return m_megaStructureBreakSpeed;
			}
			set
			{
				if (float.IsFinite(value) && value >= 0f)
				{
					SetProperty(ref m_megaStructureBreakSpeed, value, "MegaStructureBreakSpeed");
				}
			}
		}

		public SimulationSettings()
		{
			// 与发布版一致，默认走 Havok（见 BPSIM_HAVOK_PORT.md）。
			// 注意：Havok 后端必须配合「真 Burst」的构建才有效 —— 发布版是 Burst AOT 打包，
			// 本工程原来带的 Assets/Plugins/Unity.Burst.dll 是被补丁强制关闭 Burst 的版本，
			// 会让 Havok 的系统走托管回退（表现为部件不下落）。构建配置见 BPSIM_HAVOK_PORT.md §12。
			SimulationType = SimulationType.HavokPhysics;
			FixedTimeStep = 0.02f;
			SolverIterationCount = 8;
			GravityX = 0f;
			GravityY = -9.81f;
			CreateJoints = true;
			InfiniteConnectionStrength = false;
			ConnectionStrengthFactor = 4f;
			PartSelfCollision = false;
			MegaStructure = false;
			MegaStructureBreakSpeed = 8f;
		}

		public void Update(SimulationSettings settings)
		{
			UpdateTnt(settings);
			SimulationType = settings.SimulationType;
			FixedTimeStep = settings.FixedTimeStep;
			SolverIterationCount = settings.SolverIterationCount;
			GravityX = settings.GravityX;
			GravityY = settings.GravityY;
			CreateJoints = settings.CreateJoints;
			InfiniteConnectionStrength = settings.InfiniteConnectionStrength;
			ConnectionStrengthFactor = settings.ConnectionStrengthFactor;
			PartSelfCollision = settings.PartSelfCollision;
			MegaStructure = settings.MegaStructure;
			MegaStructureBreakSpeed = settings.MegaStructureBreakSpeed;
		}
	}
}
