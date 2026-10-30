using Bpsim.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace Bpsim.Parts.Circuits
{
    internal struct CircuitVisualNode : IComponentData
    {
        // 0 fixed X, 1 fixed Y, 2 interface sprite.
        public byte Kind;
        public int AppliedMode;
    }

    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(PartTransformSystem))]
    internal partial class CircuitRoutingVisualSystem : SystemBase
    {
        private EntityQuery query;
        protected override void OnCreate() { query=GetEntityQuery(ComponentType.ReadWrite<CircuitVisualNode>(),ComponentType.ReadOnly<Parent>()); }
        protected override void OnUpdate()
        {
            if(SpriteManager.Instance==null)return;
            using var nodes=query.ToEntityArray(Allocator.Temp);
            for(int i=0;i<nodes.Length;i++)
            {
                Entity node=nodes[i],root=EntityManager.GetComponentData<Parent>(node).Value;
                if(!EntityManager.HasComponent<CircuitPart>(root)||!EntityManager.HasComponent<PartTransform>(root))continue;
                var data=EntityManager.GetComponentData<CircuitPart>(root);var marker=EntityManager.GetComponentData<CircuitVisualNode>(node);
                int mode=EntityManager.GetComponentData<PartTransform>(root).Rotation;
                if(marker.AppliedMode==mode)continue;
                bool visible=true;float angle=0;string sprite;
                if(marker.Kind<2)
                {
                    mode=CircuitPart.NormalizeRotation(data.Index,mode);
                    visible=marker.Kind==0||mode==6;
                    angle=marker.Kind==1?90:mode<=1?mode*90:mode<=5?(mode-2)*90:0;
                    sprite="Wire2_Sprite_"+(marker.Kind==0&&mode>=2&&mode<=5?2:1);
                }
                else
                {
                    mode=CircuitPart.NormalizeRotation(data.Index,mode);
                    angle=mode==2||mode==4?90:0;
                    int variant=(mode==0?0:mode<=2?2:4)+(data.Index==39?1:0);
                    sprite="Interface"+(variant+1)+"_Sprite";
                }
                if(EntityManager.HasComponent<LocalTransform>(node))
                {
                    var t=EntityManager.GetComponentData<LocalTransform>(node);t.Rotation=quaternion.RotateZ(math.radians(angle));EntityManager.SetComponentData(node,t);
                }
                if(EntityManager.HasComponent<SpriteID>(node)&&SpriteManager.Instance.TryGetID(sprite,out int id))
                    EntityManager.SetComponentData(node,new SpriteID { Value=id });
                bool hidden=EntityManager.HasComponent<DisableRendering>(node);
                if(visible&&hidden)EntityManager.RemoveComponent<DisableRendering>(node);
                else if(!visible&&!hidden)EntityManager.AddComponent<DisableRendering>(node);
                marker.AppliedMode=mode;EntityManager.SetComponentData(node,marker);
            }
        }
    }
}
