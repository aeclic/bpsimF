// Anvil bridge: emit one record per native contact, with the normal impulse
// projected onto the reported normal and summed over its manifold points.
//
// Why a separate translation unit: this file needs box3d's internal headers
// (physics_world.h / contact.h / body.h / shape.h), which must not be mixed with the
// public header in anvil_bridge.c.

#include "box3d/box3d.h"
#include "box3d/collision.h"

#include "physics_world.h"
#include "contact.h"
#include "body.h"
#include "shape.h"

#ifdef _WIN32
#define ANVIL_EXPORT __declspec(dllexport)
#else
#define ANVIL_EXPORT __attribute__((visibility("default")))
#endif

// Same layout as b3AnvilContact in anvil_bridge.c (36 bytes, blittable).
typedef struct b3AnvilContactFast
{
	int bodyA;
	int bodyB;
	b3Vec3 point;
	b3Vec3 normal;
	float normalImpulse;
} b3AnvilContactFast;

static inline b3Vec3 anvil_ContactWorldPoint( b3World* world, int bodyIndex, b3Vec3 anchor )
{
	b3Body* body = b3Array_Get( world->bodies, bodyIndex );
	b3BodySim* sim = b3GetBodySim( world, body );
	return b3Add( sim->center, anchor );
}

// bodyA/bodyB are b3BodyId.index1 slots (1-based).
ANVIL_EXPORT int anvil_CollectAllContacts( b3WorldId worldId, b3AnvilContactFast* outContacts, int capacity )
{
	if ( capacity <= 0 || outContacts == NULL )
	{
		return 0;
	}

	b3World* world = b3GetWorldFromId( worldId );
	if ( world == NULL )
	{
		return 0;
	}

	b3Contact* contacts = world->contacts.data;
	int contactCount = world->contacts.count;
	int written = 0;
#ifdef ANVIL_INTEGRATED_SUBSTEPS
	bool combined = world->inv_dt != world->inv_h;
#else
	bool combined = false;
#endif

	for ( int i = 0; i < contactCount && written < capacity; ++i )
	{
		b3Contact* contact = contacts + i;
		if ( contact->manifoldCount <= 0 || contact->manifolds == NULL )
		{
			continue;
		}

		int bodyIndexA = contact->edges[0].bodyId;
		int bodyIndexB = contact->edges[1].bodyId;
		if ( bodyIndexA < 0 || bodyIndexB < 0 )
		{
			continue;
		}

		const b3Manifold* manifold = contact->manifolds;
		const b3ManifoldPoint* deepest = NULL;
		for ( int manifoldIndex = 0; manifoldIndex < contact->manifoldCount && deepest == NULL; ++manifoldIndex )
		{
			const b3Manifold* candidate = contact->manifolds + manifoldIndex;
			if ( candidate->pointCount > 0 )
			{
				manifold = candidate;
				deepest = manifold->points;
			}
		}
		if ( deepest == NULL )
		{
			continue;
		}
		float impulse = 0.0f;
		for ( const b3Manifold* sample = manifold; sample < contact->manifolds + contact->manifoldCount; ++sample )
		{
			float projection = sample == manifold ? 1.0f : b3MaxFloat( 0.0f, b3Dot( sample->normal, manifold->normal ) );
			for ( int pointIndex = 0; pointIndex < sample->pointCount; ++pointIndex )
			{
				const b3ManifoldPoint* point = sample->points + pointIndex;
				impulse += projection * ( combined ? point->totalNormalImpulse : point->normalImpulse );
				if ( sample == manifold && point->separation < deepest->separation )
				{
					deepest = point;
				}
			}
		}

		b3AnvilContactFast* dst = outContacts + written;
		// +1 to match the opaque b3BodyId.index1 encoding, so the managed side can reuse
		// the same id table it already keeps for SetTransform/GetTransform.
		dst->bodyA = bodyIndexA + 1;
		dst->bodyB = bodyIndexB + 1;
		dst->point = anvil_ContactWorldPoint( world, bodyIndexA, deepest->anchorA );
		dst->normal = manifold->normal;
		dst->normalImpulse = impulse;
		written += 1;
	}

	return written;
}
