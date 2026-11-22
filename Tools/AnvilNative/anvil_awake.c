// Anvil bridge (awake-only readback).
//
// box3d 在刚体进入 sleep 后会移除它的 body state —— 这正是「这一步没有任何变化」的信号。
// 因此在「大量刚体已经堆稳」的场景里（bpsim 的部件绝大多数时间如此），回读可以只处理：
//   1) 仍然活着的刚体（有 state）；或
//   2) 刚刚在本步入睡、位姿与上次上报值不同的刚体。
// 其余刚体的 MotionData/MotionVelocity 在 ECS 侧保持上一帧的值即可，无需再写一遍。

#include "box3d/box3d.h"

#include "physics_world.h"
#include "body.h"

#ifdef _WIN32
#define ANVIL_EXPORT __declspec(dllexport)
#else
#define ANVIL_EXPORT __attribute__((visibility("default")))
#endif

// 返回实际写入的条数；outIndices[k] 是被写入项在 bodies[] 里的下标。
ANVIL_EXPORT int anvil_ReadAwakeFast( b3WorldId worldId, const b3BodyId* bodies, int count, const b3WorldTransform* lastReported,
									  int* outIndices, b3WorldTransform* transforms, b3Vec3* linearVelocities, b3Vec3* angularVelocities,
									  int capacity )
{
	b3World* world = b3GetWorldFromId( worldId );
	if ( world == NULL || capacity <= 0 )
	{
		return 0;
	}

	int written = 0;

	for ( int i = 0; i < count; ++i )
	{
		b3BodyId bodyId = bodies[i];
		if ( bodyId.index1 <= 0 || bodyId.index1 > world->bodies.count )
		{
			continue;
		}

		b3Body* body = b3Array_Get( world->bodies, bodyId.index1 - 1 );
		if ( body->generation != bodyId.generation )
		{
			continue;
		}

		b3SolverSet* set = b3Array_Get( world->solverSets, body->setIndex );
		b3BodySim* sim = b3Array_Get( set->bodySims, body->localIndex );
		b3BodyState* state = b3GetBodyState( world, body );

		bool changed = true;
		if ( state == NULL )
		{
			// 睡眠中：只有「本步刚入睡且位姿变了」才需要上报。
			const b3WorldTransform* last = lastReported + i;
			changed = sim->transform.p.x != last->p.x || sim->transform.p.y != last->p.y || sim->transform.p.z != last->p.z ||
					  sim->transform.q.v.x != last->q.v.x || sim->transform.q.v.y != last->q.v.y || sim->transform.q.v.z != last->q.v.z ||
					  sim->transform.q.s != last->q.s;
			if ( changed == false )
			{
				continue;
			}
		}

		outIndices[written] = i;
		transforms[written] = sim->transform;

		if ( state != NULL )
		{
			linearVelocities[written] = state->linearVelocity;
			angularVelocities[written] = state->angularVelocity;
		}
		else
		{
			linearVelocities[written] = b3Vec3_zero;
			angularVelocities[written] = b3Vec3_zero;
		}

		++written;
		if ( written >= capacity )
		{
			break;
		}
	}

	return written;
}
