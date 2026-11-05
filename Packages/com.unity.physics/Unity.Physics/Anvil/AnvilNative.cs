// Anvil Physics (box3d) — native ABI bindings.
//
// 逆向自 https://valgore.com/anvil/demo/ (WebGL/IL2CPP) 的 AnvilPhysics.Interop 层：
//   - 该 demo 的 Interop 程序集名族（AvWorldNative / AvBodyNative / AvShapeNative / AvJointHandle /
//     AvQuery / AvSnapshotNative …）只是 box3d 公开 C API 的一层薄封装，
//     其版本自检串 "Anvil: box3d {0}.{1}.{2}, ABI 0x{3:X16}, {4} layout entries, {5} exports,
//     double precision {6}" 暴露了它包装的正是 box3d 本身（demo 内 Solver 标注 "Box3D e77352c"）。
//   - wasm 里 b3* 符号被静态链接、不导出，所以本实现不复用 demo 二进制，而是按同一 commit
//     (box3d e77352c) 重建原生库，并由 anvil_bridge.c 补一层批量 interop（见 Assets/Plugins/x86_64/box3d.dll）。
//
// 结构体布局全部经 C 侧 offsetof/sizeof 探针核对（单精度 x64）：
//   b3WorldDef=152, b3BodyDef=112, b3ShapeDef=120, b3SurfaceMaterial=40, b3Filter=24,
//   b3Capacity=20, b3JointDef=112, b3WeldJointDef=128, b3Profile=92, b3MassData=52,
//   b3Sphere=16, b3Capsule=28, b3RayResult=64, b3QueryFilter=32, b3BoxHull=696.
using System;
using System.Runtime.InteropServices;

namespace Bpsim.Physics.Anvil
{
	#region 基础数学

	[StructLayout(LayoutKind.Sequential)]
	public struct b3Vec3
	{
		public float x, y, z;

		public b3Vec3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
		public static readonly b3Vec3 zero = new b3Vec3(0f, 0f, 0f);
		public static readonly b3Vec3 one = new b3Vec3(1f, 1f, 1f);
		public override string ToString() => $"({x:R}, {y:R}, {z:R})";
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct b3Quat
	{
		public float x, y, z, w;

		public b3Quat(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
		public static readonly b3Quat identity = new b3Quat(0f, 0f, 0f, 1f);
	}

	/// <summary>b3Transform = {b3Vec3 p; b3Quat q}，28 字节。</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct b3Transform
	{
		public b3Vec3 p;
		public b3Quat q;

		public static readonly b3Transform identity = new b3Transform { p = b3Vec3.zero, q = b3Quat.identity };
	}

	/// <summary>b3WorldTransform = {b3Pos p; b3Quat q}；单精度下 b3Pos 就是 b3Vec3。</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct b3WorldTransform
	{
		public b3Vec3 p;
		public b3Quat q;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct b3Matrix3
	{
		public b3Vec3 cx, cy, cz;
	}

	#endregion

	#region 句柄与枚举

	[StructLayout(LayoutKind.Sequential)]
	public struct b3WorldId
	{
		public ushort index1;
		public ushort generation;

		public bool IsNull => index1 == 0;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct b3BodyId
	{
		public int index1;
		public ushort world0;
		public ushort generation;

		public bool IsNull => index1 == 0;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct b3ShapeId
	{
		public int index1;
		public ushort world0;
		public ushort generation;

		public bool IsNull => index1 == 0;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct b3JointId
	{
		public int index1;
		public ushort world0;
		public ushort generation;

		public bool IsNull => index1 == 0;
	}

	public enum b3BodyType
	{
		b3_staticBody = 0,
		b3_kinematicBody = 1,
		b3_dynamicBody = 2,
	}

	#endregion

	#region 描述符

	[StructLayout(LayoutKind.Sequential)]
	public struct b3SurfaceMaterial
	{
		public float friction;
		public float restitution;
		public float rollingResistance;
		public b3Vec3 tangentVelocity;
		public ulong userMaterialId;
		public uint customColor;
		public uint padding;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct b3Filter
	{
		public ulong categoryBits;
		public ulong maskBits;
		public int groupIndex;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct b3Capacity
	{
		public int staticShapeCount;
		public int dynamicShapeCount;
		public int staticBodyCount;
		public int dynamicBodyCount;
		public int contactCount;
	}

	/// <summary>
	/// b3WorldDef = 152 字节。frictionCallback/restitutionCallback/enqueueTask/finishTask/
	/// createDebugShape/destroyDebugShape 一律用 IntPtr，本后端不注册任何回调（workerCount 用
	/// box3d 内建调度器）。
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct b3WorldDef
	{
		public b3Vec3 gravity;                        // 0
		public float restitutionThreshold;            // 12
		public int restitutionIterations;             // 16
		public byte enableRestitutionPropagation;     // 20
		public float hitEventThreshold;               // 24
		public float contactHertz;                    // 28
		public float contactDampingRatio;             // 32
		public float contactSpeed;                    // 36
		public float maximumLinearSpeed;              // 40
		public IntPtr frictionCallback;               // 48
		public IntPtr restitutionCallback;            // 56
		public byte enableSleep;                      // 64
		public byte enableContinuous;                 // 65
		public uint workerCount;                      // 68
		public IntPtr enqueueTask;                    // 72
		public IntPtr finishTask;                     // 80
		public IntPtr userTaskContext;                // 88
		public IntPtr userData;                       // 96
		public IntPtr createDebugShape;               // 104
		public IntPtr destroyDebugShape;              // 112
		public IntPtr userDebugShapeContext;          // 120
		public b3Capacity capacity;                   // 128
		public int internalValue;                     // 148（必须来自 b3DefaultWorldDef）
	}

	/// <summary>b3BodyDef = 112 字节。motionLocks 是 6 个 bool（linearX/Y/Z + angularX/Y/Z）。</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct b3BodyDef
	{
		public int type;                              // 0（b3BodyType）
		public b3Vec3 position;                       // 4
		public b3Quat rotation;                       // 16
		public b3Vec3 linearVelocity;                 // 32
		public b3Vec3 angularVelocity;                // 44
		public float linearDamping;                   // 56
		public float angularDamping;                  // 60
		public float gravityScale;                    // 64
		public float sleepThreshold;                  // 68
		public float safetyFactor;                    // 72
		public IntPtr name;                           // 80
		public IntPtr userData;                       // 88
		public byte motionLockLinearX;                // 96
		public byte motionLockLinearY;                // 97
		public byte motionLockLinearZ;                // 98
		public byte motionLockAngularX;               // 99
		public byte motionLockAngularY;               // 100
		public byte motionLockAngularZ;               // 101
		public byte enableSleep;                      // 102
		public byte isAwake;                          // 103
		public byte isBullet;                         // 104
		public byte isEnabled;                        // 105
		public byte allowFastRotation;                // 106
		public byte enableContactRecycling;           // 107
		public int internalValue;                     // 108
	}

	/// <summary>b3ShapeDef = 120 字节。density 默认 1000（b3DefaultShapeDef）。</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct b3ShapeDef
	{
		public IntPtr name;                           // 0
		public IntPtr userData;                       // 8
		public IntPtr materials;                      // 16
		public int materialCount;                     // 24
		public b3SurfaceMaterial baseMaterial;        // 32
		public float density;                         // 72
		public float explosionScale;                  // 76
		public b3Filter filter;                       // 80
		public byte enableCustomFiltering;            // 104
		public byte isSensor;                         // 105
		public byte enableSensorEvents;               // 106
		public byte enableContactEvents;              // 107
		public byte enableHitEvents;                  // 108
		public byte enablePreSolveEvents;             // 109
		public byte invokeContactCreation;            // 110
		public byte updateBodyMass;                   // 111
		public byte enableSpeculativeContact;         // 112
		public int internalValue;                     // 116
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct b3Sphere
	{
		public b3Vec3 center;
		public float radius;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct b3Capsule
	{
		public b3Vec3 center1;
		public b3Vec3 center2;
		public float radius;
	}

	/// <summary>不透明：只用于把 b3Make*BoxHull 的返回值整体交给 b3CreateHullShape。</summary>
	[StructLayout(LayoutKind.Sequential, Size = 696)]
	public struct b3BoxHull
	{
		private byte _opaque;
	}

	/// <summary>不透明：b3CreateHull 产出的 b3HullData*，只以指针流转。</summary>
	[StructLayout(LayoutKind.Sequential, Size = 152)]
	public struct b3HullData
	{
		private byte _opaque;
	}

	/// <summary>b3MeshDef = 48 字节（4 指针 + float + 2 int + 4 bool）。顶点数据只在创建期间被读。</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct b3MeshDef
	{
		public IntPtr vertices;
		public IntPtr stride;          // size_t，0 表示连续顶点
		public IntPtr indices;
		public IntPtr materialIndices;
		public float weldTolerance;
		public int vertexCount;
		public int triangleCount;
		public byte weldVertices;
		public byte useMedianSplit;
		public byte identifyEdges;
		public byte clockWiseWinding;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct b3MassData
	{
		public float mass;
		public b3Vec3 center;
		public b3Matrix3 inertia;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct b3JointDef
	{
		public IntPtr userData;                       // 0
		public b3BodyId bodyIdA;                      // 8
		public b3BodyId bodyIdB;                      // 16
		public b3Transform localFrameA;               // 24
		public b3Transform localFrameB;               // 52
		public float forceThreshold;                  // 80
		public float torqueThreshold;                 // 84
		public float constraintHertz;                 // 88
		public float constraintDampingRatio;          // 92
		public float drawScale;                       // 96
		public byte collideConnected;                 // 100
		public int internalValue;                     // 104
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct b3WeldJointDef
	{
		public b3JointDef baseDef;                    // 0（112）
		public float linearHertz;                     // 112
		public float angularHertz;                    // 116
		public float linearDampingRatio;              // 120
		public float angularDampingRatio;             // 124
	}

	/// <summary>b3JointEvent { b3JointId jointId; void* userData; }</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct b3JointEvent
	{
		public b3JointId jointId;
		public IntPtr userData;
	}

	/// <summary>b3JointEvents { b3JointEvent* jointEvents; int count; }（transient）</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct b3JointEvents
	{
		public IntPtr jointEvents;
		public int count;
	}

	#endregion

	#region 查询与统计

	[StructLayout(LayoutKind.Sequential)]
	public struct b3QueryFilter
	{
		public ulong categoryBits;
		public ulong maskBits;
		public ulong id;
		public IntPtr name;
	}

	[StructLayout(LayoutKind.Explicit, Size = 64)]
	public struct b3RayResult
	{
		[FieldOffset(0)] public b3ShapeId shapeId;
		[FieldOffset(8)] public b3Vec3 point;
		[FieldOffset(20)] public b3Vec3 normal;
		[FieldOffset(32)] public ulong userMaterialId;
		[FieldOffset(40)] public float fraction;
		[FieldOffset(44)] public int triangleIndex;
		[FieldOffset(48)] public int childIndex;
		[FieldOffset(52)] public int nodeVisits;
		[FieldOffset(56)] public int leafVisits;
		[FieldOffset(60)] public byte hit;
	}

	/// <summary>anvil_CastRayAll 的单个命中（32 字节）。bodyIndex = b3BodyId.index1（1-based，0 = 无效）。</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct b3AnvilRayHit
	{
		public int bodyIndex;
		public b3Vec3 point;
		public b3Vec3 normal;
		public float fraction;
	}

	/// <summary>b3Profile = 23 个连续 float（92 字节）；全部单位毫秒。</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct b3Profile
	{
		public float step;
		public float pairs;
		public float collide;
		public float solve;
		public float solverSetup;
		public float constraints;
		public float prepareConstraints;
		public float integrateVelocities;
		public float warmStart;
		public float solveImpulses;
		public float integratePositions;
		public float relaxImpulses;
		public float restitution;
		public float storeImpulses;
		public float splitIslands;
		public float transforms;
		public float sensorHits;
		public float jointEvents;
		public float hitEvents;
		public float refit;
		public float bullets;
		public float sleepIslands;
		public float sensors;
	}

	/// <summary>b3Counters = 208 字节；只映射本后端用到的前缀字段。</summary>
	[StructLayout(LayoutKind.Explicit, Size = 208)]
	public struct b3Counters
	{
		[FieldOffset(0)] public long byteCount;
		[FieldOffset(8)] public int bodyCount;
		[FieldOffset(12)] public int shapeCount;
		[FieldOffset(16)] public int contactCount;
		[FieldOffset(20)] public int jointCount;
		[FieldOffset(24)] public int islandCount;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct b3AnvilContact
	{
		public int bodyA;
		public int bodyB;
		public b3Vec3 point;
		public b3Vec3 normal;
		public float normalImpulse;
	}

	#endregion

	/// <summary>
	/// box3d.dll（box3d e77352c + anvil_bridge.c）的 P/Invoke 面。
	/// 只声明本后端实际使用的导出；默认描述符必须走 b3Default*，因为 box3d 用 internalValue
	/// 做版本/布局校验（world 默认 1152023，手填 0 会被拒绝）。
	/// </summary>
	public static class AnvilNative
	{
		public const string Library = "box3d";

		#region 世界

		[DllImport(Library)] public static extern b3WorldDef b3DefaultWorldDef();
		[DllImport(Library)] public static extern b3BodyDef b3DefaultBodyDef();
		[DllImport(Library)] public static extern b3ShapeDef b3DefaultShapeDef();
		[DllImport(Library)] public static extern b3WeldJointDef b3DefaultWeldJointDef();

		[DllImport(Library)] public static extern b3WorldId b3CreateWorld(ref b3WorldDef def);
		[DllImport(Library)] public static extern void b3DestroyWorld(b3WorldId worldId);
		[DllImport(Library)] public static extern int b3GetWorldCount();

		[DllImport(Library)] public static extern void b3World_Step(b3WorldId worldId, float timeStep, int subStepCount);
		[DllImport(Library)] public static extern b3Profile b3World_GetProfile(b3WorldId worldId);
		[DllImport(Library)] public static extern b3Counters b3World_GetCounters(b3WorldId worldId);
		[DllImport(Library)] public static extern b3Capacity b3World_GetMaxCapacity(b3WorldId worldId);
		[DllImport(Library)] public static extern void b3World_SetGravity(b3WorldId worldId, b3Vec3 gravity);
		[DllImport(Library)] public static extern b3Vec3 b3World_GetGravity(b3WorldId worldId);
		[DllImport(Library)] public static extern void b3World_SetWorkerCount(b3WorldId worldId, int workerCount);
		[DllImport(Library)] public static extern int b3World_GetWorkerCount(b3WorldId worldId);
		[DllImport(Library)] public static extern int b3World_GetAwakeBodyCount(b3WorldId worldId);
		[DllImport(Library)] public static extern void b3World_SetMaximumLinearSpeed(b3WorldId worldId, float speed);
		[DllImport(Library)] public static extern void b3World_EnableSleeping(b3WorldId worldId, byte flag);
		[DllImport(Library)] public static extern void b3World_EnableContinuous(b3WorldId worldId, byte flag);

		[DllImport(Library)] public static extern b3RayResult b3World_CastRayClosest(b3WorldId worldId, b3Vec3 origin, b3Vec3 translation, b3QueryFilter filter);

		#endregion

		#region 刚体

		[DllImport(Library)] public static extern b3BodyId b3CreateBody(b3WorldId worldId, ref b3BodyDef def);
		[DllImport(Library)] public static extern void b3DestroyBody(b3BodyId bodyId);
		[DllImport(Library)] public static extern byte b3Body_IsValid(b3BodyId bodyId);

		[DllImport(Library)] public static extern b3WorldTransform b3Body_GetTransform(b3BodyId bodyId);
		[DllImport(Library)] public static extern void b3Body_SetTransform(b3BodyId bodyId, b3Vec3 position, b3Quat rotation);
		[DllImport(Library)] public static extern b3Vec3 b3Body_GetPosition(b3BodyId bodyId);
		[DllImport(Library)] public static extern b3Quat b3Body_GetRotation(b3BodyId bodyId);
		[DllImport(Library)] public static extern b3Vec3 b3Body_GetWorldCenter(b3BodyId bodyId);
		[DllImport(Library)] public static extern b3Vec3 b3Body_GetLinearVelocity(b3BodyId bodyId);
		[DllImport(Library)] public static extern void b3Body_SetLinearVelocity(b3BodyId bodyId, b3Vec3 velocity);
		[DllImport(Library)] public static extern b3Vec3 b3Body_GetAngularVelocity(b3BodyId bodyId);
		[DllImport(Library)] public static extern void b3Body_SetAngularVelocity(b3BodyId bodyId, b3Vec3 velocity);

		[DllImport(Library)] public static extern void b3Body_SetMassData(b3BodyId bodyId, b3MassData massData);
		[DllImport(Library)] public static extern b3MassData b3Body_GetMassData(b3BodyId bodyId);
		[DllImport(Library)] public static extern void b3Body_ApplyMassFromShapes(b3BodyId bodyId);
		[DllImport(Library)] public static extern float b3Body_GetMass(b3BodyId bodyId);

		[DllImport(Library)] public static extern void b3Body_SetType(b3BodyId bodyId, int type);
		[DllImport(Library)] public static extern int b3Body_GetType(b3BodyId bodyId);
		[DllImport(Library)] public static extern void b3Body_SetAwake(b3BodyId bodyId, byte flag);
		[DllImport(Library)] public static extern byte b3Body_IsAwake(b3BodyId bodyId);
		[DllImport(Library)] public static extern byte b3Body_IsEnabled(b3BodyId bodyId);
		[DllImport(Library)] public static extern void b3Body_Disable(b3BodyId bodyId);
		[DllImport(Library)] public static extern void b3Body_Enable(b3BodyId bodyId);
		[DllImport(Library)] public static extern void b3Body_SetBullet(b3BodyId bodyId, byte flag);
		[DllImport(Library)] public static extern void b3Body_SetLinearDamping(b3BodyId bodyId, float damping);
		[DllImport(Library)] public static extern void b3Body_SetAngularDamping(b3BodyId bodyId, float damping);
		[DllImport(Library)] public static extern void b3Body_SetGravityScale(b3BodyId bodyId, float scale);

		[DllImport(Library)] public static extern void b3Body_ApplyLinearImpulseToCenter(b3BodyId bodyId, b3Vec3 impulse, byte wake);
		[DllImport(Library)] public static extern void b3Body_ApplyAngularImpulse(b3BodyId bodyId, b3Vec3 impulse, byte wake);

		#endregion

		#region 形状

		[DllImport(Library)] public static extern b3ShapeId b3CreateSphereShape(b3BodyId bodyId, ref b3ShapeDef def, ref b3Sphere sphere);
		[DllImport(Library)] public static extern b3ShapeId b3CreateCapsuleShape(b3BodyId bodyId, ref b3ShapeDef def, ref b3Capsule capsule);
		[DllImport(Library)] public static extern b3ShapeId b3CreateHullShape(b3BodyId bodyId, ref b3ShapeDef def, IntPtr hull);
		[DllImport(Library)] public static extern b3ShapeId b3CreateMeshShape(b3BodyId bodyId, ref b3ShapeDef def, IntPtr mesh, b3Vec3 scale);
		[DllImport(Library)] public static extern void b3DestroyShape(b3ShapeId shapeId);
		[DllImport(Library)] public static extern byte b3Shape_IsValid(b3ShapeId shapeId);
		[DllImport(Library)] public static extern b3BodyId b3Shape_GetBody(b3ShapeId shapeId);

		[DllImport(Library)] public static extern b3BoxHull b3MakeBoxHull(float hx, float hy, float hz);
		[DllImport(Library)] public static extern b3BoxHull b3MakeTransformedBoxHull(float hx, float hy, float hz, b3Transform transform);
		[DllImport(Library)] public static extern IntPtr b3CreateHull(IntPtr points, int pointCount, int maxVertexCount);
		[DllImport(Library)] public static extern void b3DestroyHull(IntPtr hull);
		[DllImport(Library)] public static extern IntPtr b3CreateMesh(ref b3MeshDef def, IntPtr degenerateTriangleIndices, int degenerateCapacity);
		[DllImport(Library)] public static extern void b3DestroyMesh(IntPtr mesh);

		#endregion

		#region 关节

		[DllImport(Library)] public static extern b3JointId b3CreateWeldJoint(b3WorldId worldId, ref b3WeldJointDef def);
		[DllImport(Library)] public static extern void b3DestroyJoint(b3JointId jointId);
		[DllImport(Library)] public static extern byte b3Joint_IsValid(b3JointId jointId);
		[DllImport(Library)] public static extern void b3Joint_SetForceThreshold(b3JointId jointId, float threshold);
		[DllImport(Library)] public static extern void b3Joint_SetTorqueThreshold(b3JointId jointId, float threshold);
		[DllImport(Library)] public static extern void b3Joint_SetCollideConnected(b3JointId jointId, byte flag);
		[DllImport(Library)] public static extern void b3Joint_SetConstraintTuning(b3JointId jointId, float hertz, float dampingRatio);
		[DllImport(Library)] public static extern b3Vec3 b3Joint_GetConstraintForce(b3JointId jointId);
		[DllImport(Library)] public static extern b3Vec3 b3Joint_GetConstraintTorque(b3JointId jointId);
		[DllImport(Library)] public static extern b3BodyId b3Joint_GetBodyA(b3JointId jointId);
		[DllImport(Library)] public static extern b3BodyId b3Joint_GetBodyB(b3JointId jointId);
		[DllImport(Library)] public static extern void b3Joint_SetLocalFrameA(b3JointId jointId, b3Transform localFrame);
		[DllImport(Library)] public static extern void b3Joint_SetLocalFrameB(b3JointId jointId, b3Transform localFrame);
		[DllImport(Library)] public static extern void b3Joint_WakeBodies(b3JointId jointId);
		[DllImport(Library)] public static extern b3JointEvents b3World_GetJointEvents(b3WorldId worldId);

		#endregion

		#region 批量桥接（anvil_bridge.c）

		[DllImport(Library)] public static extern unsafe void anvil_ReadBatch(b3BodyId* bodies, int count, b3WorldTransform* transforms, b3Vec3* linearVelocities, b3Vec3* angularVelocities);
		[DllImport(Library)] public static extern unsafe void anvil_WriteBatch(b3BodyId* bodies, int count, b3WorldTransform* transforms, b3Vec3* linearVelocities, b3Vec3* angularVelocities);
		[DllImport(Library)] public static extern unsafe void anvil_StepProfile(b3WorldId worldId, float timeStep, int subStepCount, b3Profile* outProfile);
		[DllImport(Library)] public static extern unsafe int anvil_CollectContacts(b3BodyId* bodies, int count, b3AnvilContact* outContacts, int capacity);
		[DllImport(Library)] public static extern unsafe int anvil_CollectAllContacts(b3WorldId worldId, b3AnvilContact* outContacts, int capacity);
		[DllImport(Library)] public static extern unsafe void anvil_ReadBatchFast(b3WorldId worldId, b3BodyId* bodies, int count, b3WorldTransform* transforms, b3Vec3* linearVelocities, b3Vec3* angularVelocities);
		[DllImport(Library)] public static extern unsafe void anvil_WriteBatchFast(b3WorldId worldId, b3BodyId* bodies, int count, b3WorldTransform* transforms, b3Vec3* linearVelocities, b3Vec3* angularVelocities);
		[DllImport(Library)] public static extern unsafe int anvil_ReadAwakeFast(b3WorldId worldId, b3BodyId* bodies, int count, b3WorldTransform* lastReported, int* outIndices, b3WorldTransform* transforms, b3Vec3* linearVelocities, b3Vec3* angularVelocities, int capacity);
		[DllImport(Library)] public static extern int anvil_Capabilities();
		[DllImport(Library)] public static extern unsafe int anvil_CastRayAll(b3WorldId worldId, b3Vec3 origin, b3Vec3 translation, ulong categoryBits, ulong maskBits, b3AnvilRayHit* outHits, int capacity);

		#endregion

		/// <summary>bit0 = 双精度，bit1 = AVX2 可用。</summary>
		public const int CapabilityDoublePrecision = 1;
		public const int CapabilityAvx2 = 2;
	}
}
