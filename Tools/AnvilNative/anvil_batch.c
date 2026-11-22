// Anvil bridge (fast batch path): same job as anvil_ReadBatch / anvil_WriteBatch, but it
// resolves the world once per call and walks the active solver set directly instead of
// going through the public per-body entry points (each of which re-resolves the world and
// re-validates the generation).
//
// Why a separate translation unit: this file needs box3d's internal headers.

#include "box3d/box3d.h"
#include "box3d/collision.h"

#include "physics_world.h"
#include "body.h"

#ifdef _WIN32
#define ANVIL_EXPORT __declspec(dllexport)
#else
#define ANVIL_EXPORT __attribute__((visibility("default")))
#endif

// Read transforms + velocities for a batch of bodies (fast path).
ANVIL_EXPORT void anvil_ReadBatchFast( b3WorldId worldId, const b3BodyId* bodies, int count, b3WorldTransform* transforms, b3Vec3* linearVelocities, b3Vec3* angularVelocities )
{
	b3World* world = b3GetWorldFromId( worldId );
	if ( world == NULL )
	{
		return;
	}

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

		transforms[i] = sim->transform;

		b3BodyState* state = b3GetBodyState( world, body );
		if ( state != NULL )
		{
			linearVelocities[i] = state->linearVelocity;
			angularVelocities[i] = state->angularVelocity;
		}
		else
		{
			linearVelocities[i] = b3Vec3_zero;
			angularVelocities[i] = b3Vec3_zero;
		}
	}
}

// Push transforms + velocities for a batch of bodies (fast path).
ANVIL_EXPORT void anvil_WriteBatchFast( b3WorldId worldId, const b3BodyId* bodies, int count, const b3WorldTransform* transforms, const b3Vec3* linearVelocities, const b3Vec3* angularVelocities )
{
	b3World* world = b3GetWorldFromId( worldId );
	if ( world == NULL )
	{
		return;
	}

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

		b3WorldTransform transform = transforms[i];
		b3Vec3 translation = b3SubPos( transform.p, sim->transform.p );
		b3Quat current = sim->transform.q;
		float sign = b3DotQuat( transform.q, current ) < 0.0f ? -1.0f : 1.0f;
		b3Vec3 rotation = b3MulSub( transform.q.v, sign, current.v );
		float scalar = transform.q.s - sign * current.s;
		if ( b3LengthSquared( translation ) > 1e-8f || b3LengthSquared( rotation ) + scalar * scalar > 1e-12f )
		{
			b3Body_SetTransform( bodyId, transform.p, transform.q );
		}

		b3BodyState* state = b3GetBodyState( world, body );
		if ( state != NULL )
		{
			state->linearVelocity = linearVelocities[i];
			state->angularVelocity = angularVelocities[i];
		}
	}
}
