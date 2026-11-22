// Anvil bridge: 全命中射线投射
//
// Unity.Physics 的三个消费点（PartBehaviorsGuidance / AutoGunProjectileSystem /
// WheelSystem）都用 AllHitsCollector 取回整条射线上的全部命中，再自己剔除
// 「属于本部件」的那些 —— 因为射线起点常常落在自己的复合碰撞体内部。
// b3World_CastRayClosest 只给最近的一个，不够用，所以这里用带回调的
// b3World_CastRay 把每个命中都记下来。
//
// 回调返回 1 表示「继续遍历」，因此拿到的是全部命中（未排序）。

#include "box3d/box3d.h"

#include <stdint.h>

#define ANVIL_EXPORT __declspec(dllexport)

typedef struct b3AnvilRayHit
{
	int bodyIndex;   // = b3BodyId.index1（1-based），0 表示无效
	b3Vec3 point;
	b3Vec3 normal;
	float fraction;
} b3AnvilRayHit;

typedef struct b3AnvilRayContext
{
	b3AnvilRayHit* hits;
	int capacity;
	int count;
} b3AnvilRayContext;

static float anvil_RayCallback( b3ShapeId shapeId, b3Pos point, b3Vec3 normal, float fraction,
								uint64_t userMaterialId, int triangleIndex, int childIndex, void* context )
{
	(void)userMaterialId;
	(void)triangleIndex;
	(void)childIndex;

	b3AnvilRayContext* ctx = (b3AnvilRayContext*)context;
	if ( ctx->count >= ctx->capacity )
	{
		return 0.0f;
	}

	b3BodyId bodyId = b3Shape_GetBody( shapeId );
	b3AnvilRayHit* dst = ctx->hits + ctx->count;
	dst->bodyIndex = bodyId.index1;
	dst->point = point;
	dst->normal = normal;
	dst->fraction = fraction;
	ctx->count++;

	return 1.0f;
}

ANVIL_EXPORT int anvil_CastRayAll( b3WorldId worldId, b3Pos origin, b3Vec3 translation,
								   uint64_t categoryBits, uint64_t maskBits,
								   b3AnvilRayHit* outHits, int capacity )
{
	if ( outHits == 0 || capacity <= 0 )
	{
		return 0;
	}

	b3QueryFilter filter;
	filter.categoryBits = categoryBits;
	filter.maskBits = maskBits;
	filter.id = 0;
	filter.name = 0;

	b3AnvilRayContext ctx;
	ctx.hits = outHits;
	ctx.capacity = capacity;
	ctx.count = 0;

	b3World_CastRay( worldId, origin, translation, filter, anvil_RayCallback, &ctx );

	return ctx.count;
}
