// Anvil bridge: batch helpers so the managed side pays one interop hop per frame
// instead of 2*N. Compiled into box3d.dll alongside the regular exports.

#include "box3d/box3d.h"
#include "box3d/collision.h"

#ifdef _WIN32
#define ANVIL_EXPORT __declspec(dllexport)
#else
#define ANVIL_EXPORT __attribute__((visibility("default")))
#endif

// Flattened contact record handed to managed code. bodyA/bodyB carry the raw
// b3BodyId.index1 slots; the managed side maps them back to ECS entities through its
// own body table, so no O(N) search happens in the hot loop.
typedef struct b3AnvilContact
{
	int bodyA;
	int bodyB;
	b3Vec3 point;
	b3Vec3 normal;
	float normalImpulse;
} b3AnvilContact;

// Read transforms + velocities for a batch of bodies.
ANVIL_EXPORT void anvil_ReadBatch( const b3BodyId* bodies, int count, b3WorldTransform* transforms, b3Vec3* linearVelocities, b3Vec3* angularVelocities )
{
	for ( int i = 0; i < count; ++i )
	{
		transforms[i] = b3Body_GetTransform( bodies[i] );
		linearVelocities[i] = b3Body_GetLinearVelocity( bodies[i] );
		angularVelocities[i] = b3Body_GetAngularVelocity( bodies[i] );
	}
}

// Push transforms + velocities for a batch of bodies.
ANVIL_EXPORT void anvil_WriteBatch( const b3BodyId* bodies, int count, const b3WorldTransform* transforms, const b3Vec3* linearVelocities, const b3Vec3* angularVelocities )
{
	for ( int i = 0; i < count; ++i )
	{
		b3BodyId bodyId = bodies[i];
		if ( b3Body_IsValid( bodyId ) == false )
		{
			continue;
		}

		b3WorldTransform xf = transforms[i];
		b3Body_SetTransform( bodyId, xf.p, xf.q );
		b3Body_SetLinearVelocity( bodyId, linearVelocities[i] );
		b3Body_SetAngularVelocity( bodyId, angularVelocities[i] );
	}
}

// Step the world and copy the profile in one call.
ANVIL_EXPORT void anvil_StepProfile( b3WorldId worldId, float timeStep, int subStepCount, b3Profile* outProfile )
{
	b3World_Step( worldId, timeStep, subStepCount );
	if ( outProfile != NULL )
	{
		*outProfile = b3World_GetProfile( worldId );
	}
}

// Collect contacts reachable from a batch of bodies.
//
// NOTE: kept for API compatibility, but this is the slow path (it walks each body's
// contact list, so every contact is visited twice and produces one record per manifold
// point). Use anvil_CollectAllContacts instead when the whole world is wanted.
ANVIL_EXPORT int anvil_CollectContacts( const b3BodyId* bodies, int count, b3AnvilContact* outContacts, int capacity )
{
	int written = 0;
	for ( int i = 0; i < count && written < capacity; ++i )
	{
		b3BodyId bodyId = bodies[i];
		int contactCapacity = b3Body_GetContactCapacity( bodyId );
		if ( contactCapacity <= 0 )
		{
			continue;
		}

		b3ContactData contacts[64];
		int remaining = contactCapacity;
		while ( remaining > 0 && written < capacity )
		{
			int chunk = remaining < 64 ? remaining : 64;
			int got = b3Body_GetContactData( bodyId, contacts, chunk );
			if ( got <= 0 )
			{
				break;
			}

			for ( int c = 0; c < got && written < capacity; ++c )
			{
				b3ContactData* contact = contacts + c;
				if ( contact->manifoldCount <= 0 )
				{
					continue;
				}

				const b3Manifold* manifold = contact->manifolds;
				if ( manifold->pointCount <= 0 )
				{
					continue;
				}

				b3BodyId shapeBodyA = b3Shape_GetBody( contact->shapeIdA );
				b3BodyId shapeBodyB = b3Shape_GetBody( contact->shapeIdB );
				bool selfIsA = ( shapeBodyA.index1 == bodyId.index1 );
				b3Vec3 center = b3Body_GetWorldCenter( bodyId );

				for ( int p = 0; p < manifold->pointCount && written < capacity; ++p )
				{
					const b3ManifoldPoint* point = manifold->points + p;
					b3AnvilContact* dst = outContacts + written;
					dst->bodyA = selfIsA ? shapeBodyA.index1 : shapeBodyB.index1;
					dst->bodyB = selfIsA ? shapeBodyB.index1 : shapeBodyA.index1;
					dst->point = b3Add( center, selfIsA ? point->anchorA : point->anchorB );
					dst->normal = manifold->normal;
					dst->normalImpulse = point->normalImpulse;
					written += 1;
				}
			}

			remaining -= got;
			if ( got < chunk )
			{
				break;
			}
		}
	}

	return written;
}

// Cheap capability report used by the managed self test: bit0 = double precision,
// bit1 = AVX2.
ANVIL_EXPORT int anvil_Capabilities( void )
{
	int flags = 0;
	if ( b3IsDoublePrecision() )
	{
		flags |= 1;
	}
	if ( b3IsAVX2Available() )
	{
		flags |= 2;
	}
	return flags;
}
