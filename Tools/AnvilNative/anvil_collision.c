#include <stdint.h>

#include "box3d/box3d.h"
#include "body.h"
#include "contact.h"
#include "physics_world.h"
#include "shape.h"

#ifdef _WIN32
#define ANVIL_EXPORT __declspec(dllexport)
#else
#define ANVIL_EXPORT __attribute__((visibility("default")))
#endif

static const uint64_t anvil_halfBit = UINT64_C(1) << 40;

static uint64_t anvil_GetCollisionTag( const b3World* world, int bodyIndex )
{
	return (uintptr_t)world->bodies.data[bodyIndex].userData;
}

static bool anvil_CollisionTagsAllow( uint64_t first, uint64_t second )
{
	if ( ( ( first | second ) & anvil_halfBit ) == 0 )
	{
		return true;
	}
	uint32_t group = (uint32_t)first;
	if ( ( ( first ^ second ) & anvil_halfBit ) != 0 && group != 0 && group == (uint32_t)second )
	{
		return false;
	}
	uint8_t firstLayer = (uint8_t)( first >> 32 );
	uint8_t secondLayer = (uint8_t)( second >> 32 );
	return firstLayer == 0 || secondLayer == 0 || firstLayer == secondLayer;
}

static bool anvil_CanCollide( b3ShapeId shapeA, b3ShapeId shapeB, void* context )
{
	const b3World* world = context;
	return anvil_CollisionTagsAllow( anvil_GetCollisionTag( world, world->shapes.data[shapeA.index1 - 1].bodyId ),
		anvil_GetCollisionTag( world, world->shapes.data[shapeB.index1 - 1].bodyId ) );
}

ANVIL_EXPORT void anvil_EnableCollisionTags( b3WorldId id )
{
	b3World_SetCustomFilterCallback( id, anvil_CanCollide, b3GetWorldFromId( id ) );
}

ANVIL_EXPORT void anvil_SetBodyCollisionTag( b3WorldId id, b3BodyId bodyId, uint64_t tag, uint8_t refilter )
{
	b3World* world = b3GetWorldFromId( id );
	b3Body* body = world->bodies.data + bodyId.index1 - 1;
	body->userData = (void*)(uintptr_t)tag;
	for ( int shapeIndex = body->headShapeId; shapeIndex != B3_NULL_INDEX; )
	{
		b3Shape* shape = world->shapes.data + shapeIndex;
		if ( ( tag & anvil_halfBit ) != 0 )
		{
			shape->flags |= b3_enableCustomFiltering;
		}
		else
		{
			shape->flags &= ~b3_enableCustomFiltering;
		}
		if ( refilter && shape->proxyKey != B3_NULL_INDEX )
		{
			b3BroadPhase_MoveProxy( &world->broadPhase, shape->proxyKey, world->fatAABBs.data[shapeIndex] );
		}
		shapeIndex = shape->nextShapeId;
	}
	if ( !refilter )
	{
		return;
	}
	for ( int contactKey = body->headContactKey; contactKey != B3_NULL_INDEX; )
	{
		b3Contact* contact = world->contacts.data + ( contactKey >> 1 );
		int edge = contactKey & 1;
		contactKey = contact->edges[edge].nextKey;
		if ( !anvil_CollisionTagsAllow( tag, anvil_GetCollisionTag( world, contact->edges[edge ^ 1].bodyId ) ) )
		{
			b3DestroyContact( world, contact, true );
		}
	}
	b3Body_SetAwake( bodyId, true );
}
