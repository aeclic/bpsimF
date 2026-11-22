#include "box3d/box3d.h"

#include "joint.h"
#include "parallel_for.h"
#include "physics_world.h"

#ifdef ANVIL_INTEGRATED_SUBSTEPS
#include "box3d/collision.h"
#include "box3d/constants.h"
#include "body.h"
#endif

#ifdef _WIN32
#define ANVIL_EXPORT __declspec(dllexport)
#else
#define ANVIL_EXPORT __attribute__((visibility("default")))
#endif

ANVIL_EXPORT int anvil_BridgeVersion( void )
{
#ifdef ANVIL_INTEGRATED_SUBSTEPS
	return 3;
#else
	return 4;
#endif
}

static inline b3Joint* anvil_ResolveJoint( b3World* world, b3JointId id )
{
	if ( id.index1 <= 0 || id.index1 > world->joints.count || id.world0 != world->worldId )
	{
		return NULL;
	}
	b3Joint* joint = world->joints.data + id.index1 - 1;
	return joint->jointId != B3_NULL_INDEX && joint->generation == id.generation ? joint : NULL;
}

ANVIL_EXPORT void anvil_ValidateJoints( b3WorldId worldId, const b3JointId* ids, int count, uint8_t* valid )
{
	b3World* world = b3GetWorldFromId( worldId );
	for ( int index = 0; index < count; ++index )
	{
		valid[index] = anvil_ResolveJoint( world, ids[index] ) != NULL;
	}
}

typedef struct AnvilJointSampling
{
	const b3JointId* ids;
	const uint8_t* masks;
	int count;
	b3Vec3* linear;
	b3Vec3* angular;
	b3World* world;
	float h;
} AnvilJointSampling;

static void anvil_SampleReactionRange( int start, int end, int workerIndex, void* context )
{
	( void )workerIndex;
	const AnvilJointSampling* sample = context;
	b3World* world = sample->world;
	float h = sample->h;
	const b3JointId* ids = sample->ids;
	const uint8_t* masks = sample->masks;
	b3Vec3* linear = sample->linear;
	b3Vec3* angular = sample->angular;
	for ( int index = start; index < end; ++index )
	{
		uint8_t mask = masks[index];
		if ( mask == 0 )
		{
			continue;
		}

		b3JointId id = ids[index];
		b3Joint* joint = anvil_ResolveJoint( world, id );
		if ( joint == NULL )
		{
			continue;
		}

		if ( joint->type == b3_weldJoint )
		{
			b3JointSim* sim = b3GetJointSim( world, joint );
			if ( mask & 1 )
			{
				linear[index] = b3Add( linear[index], b3MulSV( h, b3GetWeldJointForce( world, sim ) ) );
			}
			if ( mask & 2 )
			{
				angular[index] = b3Add( angular[index], b3MulSV( h, b3GetWeldJointTorque( world, sim ) ) );
			}
		}
		else
		{
			if ( mask & 1 )
			{
				linear[index] = b3Add( linear[index], b3MulSV( h, b3Joint_GetConstraintForce( id ) ) );
			}
			if ( mask & 2 )
			{
				angular[index] = b3Add( angular[index], b3MulSV( h, b3Joint_GetConstraintTorque( id ) ) );
			}
		}
	}
}

static void anvil_SampleReactions( b3World* world, float h, void* context )
{
	AnvilJointSampling* sample = context;
	sample->h = h;
	if ( !world->locked && world->workerCount > 1 && sample->count >= 16384 )
	{
		b3ParallelFor( world, anvil_SampleReactionRange, sample->count, 4096, sample, "Anvil joint reactions" );
	}
	else
	{
		anvil_SampleReactionRange( 0, sample->count, 0, sample );
	}
}

ANVIL_EXPORT int anvil_StepAndAccumulate( b3WorldId worldId, float h, const b3JointId* ids, const uint8_t* masks,
	int count, b3Vec3* linear, b3Vec3* angular, b3Profile* profile )
{
	b3World_Step( worldId, h, 1 );
	b3World* world = b3GetWorldFromId( worldId );
	AnvilJointSampling sample = { ids, masks, count, linear, angular, world, h };
	*profile = world->profile;
	anvil_SampleReactions( world, h, &sample );
	return b3GetIdCount( &world->contactIdPool );
}

#ifdef ANVIL_INTEGRATED_SUBSTEPS
static bool anvil_StaticSweepHit( int proxyId, uint64_t userData, void* context )
{
	( void )proxyId;
	( void )userData;
	*( bool* )context = true;
	return false;
}

static bool anvil_CanCombineSubsteps( const b3World* world, float timeStep )
{
	const b3SolverSet* awake = world->solverSets.data + b3_awakeSet;
	const b3DynamicTree* statics = world->broadPhase.trees + b3_staticBody;
	float skin = 0.5f * B3_SPECULATIVE_DISTANCE;
	float noShape = B3_HUGE;
	b3Vec3 reference = b3Vec3_zero;
	// Common motion cannot close a gap between awake bodies, but sleepers and static terrain do not share it.
	if ( awake->bodyStates.count > 0 && b3GetIdCount( &world->solverSetIdPool ) == b3_firstSleepingSet &&
		 b3GetIdCount( &world->contactIdPool ) == 0 )
	{
		reference = awake->bodyStates.data[0].linearVelocity;
	}
	for ( int index = 0; index < awake->bodyStates.count; ++index )
	{
		const b3BodySim* sim = awake->bodySims.data + index;
		if ( sim->minExtent >= noShape )
		{
			continue;
		}
		const b3BodyState* state = awake->bodyStates.data + index;
		float radiusSquared = b3LengthSquared( sim->maxExtent );
		float limit = b3MinFloat( skin, 0.5f * sim->minExtent );
		float halfVelocityLimit = 0.5f * limit / timeStep;
		float limitSquared = halfVelocityLimit * halfVelocityLimit;
		if ( b3LengthSquared( b3Sub( state->linearVelocity, reference ) ) > limitSquared ||
			 b3LengthSquared( state->angularVelocity ) * radiusSquared > limitSquared ||
			 b3LengthSquared( sim->force ) > 0.0f || b3LengthSquared( sim->torque ) > 0.0f )
		{
			return false;
		}
		if ( statics->proxyCount > 0 && b3LengthSquared( state->linearVelocity ) > limitSquared )
		{
			float radius = sqrtf( radiusSquared ) + skin;
			b3Vec3 pad = { radius, radius, radius };
			b3Vec3 finish = b3MulAdd( sim->center, timeStep, state->linearVelocity );
			b3AABB sweep = { b3Sub( b3Min( sim->center, finish ), pad ), b3Add( b3Max( sim->center, finish ), pad ) };
			bool hit = false;
			b3DynamicTree_Query( statics, sweep, B3_DEFAULT_MASK_BITS, false, anvil_StaticSweepHit, &hit );
			if ( hit )
			{
				return false;
			}
		}
	}
	return true;
}

ANVIL_EXPORT int anvil_TryStepIntegrated( b3WorldId worldId, float timeStep, int subStepCount,
	const b3JointId* ids, const uint8_t* masks, int count, b3Vec3* linear, b3Vec3* angular, b3Profile* profile )
{
	b3World* world = b3GetWorldFromId( worldId );
	if ( !anvil_CanCombineSubsteps( world, timeStep ) )
	{
		return -1;
	}
	// Some non-weld reaction getters need body transforms that are only published after a full Box3D step.
	for ( int index = 0; index < count; ++index )
	{
		b3JointId id = ids[index];
		if ( masks[index] != 0 && id.index1 > 0 && id.index1 <= world->joints.count &&
			 world->joints.data[id.index1 - 1].type != b3_weldJoint )
		{
			return -1;
		}
	}
	AnvilJointSampling sample = { ids, masks, count, linear, angular, world, timeStep / subStepCount };
	world->anvilSampleJoints = count > 0 ? anvil_SampleReactions : NULL;
	world->anvilJointContext = &sample;
	b3World_Step( worldId, timeStep, subStepCount );
	world->anvilSampleJoints = NULL;
	world->anvilJointContext = NULL;
	*profile = world->profile;
	return b3GetIdCount( &world->contactIdPool );
}
#endif
