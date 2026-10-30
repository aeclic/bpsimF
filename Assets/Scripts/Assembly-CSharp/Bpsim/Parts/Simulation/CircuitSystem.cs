using System;
using System.Collections.Generic;
using Bpsim.Parts.Circuits;
using Bpsim.Parts.Gameplay;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;

namespace Bpsim.Parts.Simulation
{
    /// <summary>BPLE's persistent circuit graph, stepped at 50 Hz, bridged to ECS parts and physics.</summary>
    [UpdateInGroup(typeof(PartSimulationSystemGroup))]
    [UpdateAfter(typeof(CircuitPressureSystem))]
    internal partial class CircuitSystem : SystemBase
    {
        private sealed class Entry
        {
            public Entity Entity;
            public CircuitDevice Device;
            public PartTransform Grid;
            public byte Layer;
            public int Seen, Channel, Component;
            public float3 Position, Right, Up;
            public bool VisualInitialized, LastClosed, LastInvalid;
            public byte LastBurnedPorts;
            public float Brightness = -1;
        }
        private struct Connection : IEquatable<Connection>
        {
            public Entry A, B;
            public int X, Y;
            public bool Extra;
            public bool Equals(Connection c) => A == c.A && B == c.B && X == c.X && Y == c.Y && Extra == c.Extra;
        }
        // Reuse buckets even when every part moves to a different spatial cell.
        private sealed class SpatialIndex
        {
            private readonly Dictionary<int3, int> map = new Dictionary<int3, int>();
            private readonly List<List<Entry>> buckets = new List<List<Entry>>();
            private int used;
            public void Clear() { for (int i=0;i<used;i++) buckets[i].Clear(); used=0; map.Clear(); }
            public void Add(int3 key, Entry e)
            {
                if (!map.TryGetValue(key, out int i))
                { i=used++; if (i==buckets.Count) buckets.Add(new List<Entry>()); map.Add(key,i); }
                buckets[i].Add(e);
            }
            public List<Entry> Get(int3 key) => map.TryGetValue(key,out int i) ? buckets[i] : null;
        }
        private EntityQuery query, jointQuery;
        private PartSimulator session;
        private readonly CircuitNetwork network = new CircuitNetwork();
        private readonly Dictionary<Entity, Entry> byEntity = new Dictionary<Entity, Entry>();
        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<Connection> fixedConnections = new List<Connection>();
        private readonly List<Connection> desired = new List<Connection>();
        private readonly List<Connection> connected = new List<Connection>();
        private readonly HashSet<(Entity,Entity)> jointPairs = new HashSet<(Entity,Entity)>();
        private readonly List<Entity> pendingTriggers = new List<Entity>();
        private readonly SpatialIndex interfaces = new SpatialIndex(), senders = new SpatialIndex();
        private readonly SpatialIndex charges = new SpatialIndex(), magneticTargets = new SpatialIndex();
        private int revision, jointCount = -1, megaCount = -1;
        private int jointOrder;
        private double elapsed;
        public static int LastElementCount, LastNodeCount, LastUnknownCount;
        public static string Stats() => "circuit: elements="+LastElementCount+" nodes="+LastNodeCount+" unknowns="+LastUnknownCount;

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadWrite<CircuitPart>(),ComponentType.ReadOnly<PartTag>(),ComponentType.ReadOnly<PartTransform>());
            jointQuery = GetEntityQuery(ComponentType.ReadOnly<PhysicsConstrainedBodyPair>());
        }
        private void Reset()
        {
            network.Clear(); byEntity.Clear(); entries.Clear(); fixedConnections.Clear(); desired.Clear(); connected.Clear();
            pendingTriggers.Clear(); interfaces.Clear(); senders.Clear(); charges.Clear(); magneticTargets.Clear();
            CircuitReadouts.Clear(); jointCount=-1; megaCount=-1; elapsed=0;
            LastElementCount=LastNodeCount=LastUnknownCount=0;
        }
        protected override void OnDestroy() { Reset(); }
        protected override void OnUpdate()
        {
            var manager=PartManager.Instance;
            if (manager==null || !manager.IsSimulating || manager.PartSimulator==null)
            {
                if (session!=null) { Reset(); session=null; }
                return;
            }
            if (session!=manager.PartSimulator) { Reset(); session=manager.PartSimulator; }
            float dt=World.Time.DeltaTime;
            if (!(dt>0)) return;
            bool changed=Collect();
            if(entries.Count==0) return;
            int order=jointQuery.GetCombinedComponentOrderVersion();
            int count=session.Unmanaged.Joints.Length, mega=MegaStructure.PartCount;
            if (changed || count!=jointCount || order!=jointOrder || mega!=megaCount)
            { BuildFixedConnections(); jointCount=count; jointOrder=order; megaCount=mega; }
            BuildConnections();
            elapsed+=dt;
            // Preserve elapsed simulation time for charging and delays at any physics timestep.
            while (elapsed>=0.02-1e-8)
            {
                elapsed-=0.02;
                int failed=0;for(int i=0;i<entries.Count;i++)failed+=entries[i].Device.DisconnectedPorts;
                network.Step(0.02);
                int nowFailed=0;for(int i=0;i<entries.Count;i++)nowFailed+=entries[i].Device.DisconnectedPorts;
                if(nowFailed!=failed)BuildConnections();
                for (int i=0;i<entries.Count;i++) if (entries[i].Device.Triggered) QueueTriggers(entries[i]);
                if(pendingTriggers.Count!=0)
                {
                    WriteBack(false); DispatchTriggers();
                    // A trigger may toggle a source/switch. Preserve that change before writing results.
                    for (int i=0;i<entries.Count;i++)
                        if (EntityManager.Exists(entries[i].Entity)) entries[i].Device.Data=EntityManager.GetComponentData<CircuitPart>(entries[i].Entity);
                }
            }
            ApplyFields(dt); WriteBack(true);
            LastElementCount=network.ElementCount;
            LastNodeCount=network.NodeCount; LastUnknownCount=network.UnknownCount;
        }
        private bool Collect()
        {
            revision++;
            bool changed=false;
            using var parts=query.ToEntityArray(Allocator.Temp);
            for(int i=0;i<parts.Length;i++)
            {
                Entity entity=parts[i]; var data=EntityManager.GetComponentData<CircuitPart>(entity);
                var grid=EntityManager.GetComponentData<PartTransform>(entity);
                byte layer=EntityManager.HasComponent<PartBuildLayer>(entity)?EntityManager.GetComponentData<PartBuildLayer>(entity).Value:PartBuildLayer.MinLayer;
                if (!byEntity.TryGetValue(entity,out Entry e))
                {
                    if (!changed) network.DisconnectAll(); changed=true;
                    e=new Entry { Entity=entity }; entries.Add(e); byEntity.Add(entity,e);
                }
                if(e.Device==null || e.Device.Data.Mode!=data.Mode || e.Device.Data.Index!=data.Index || e.Device.Rotation!=grid.Rotation || e.Device.Flipped!=grid.Flipped)
                {
                    if (!changed) network.DisconnectAll(); changed=true;
                    e.Device=new CircuitDevice(data,grid.Rotation,grid.Flipped);
                }
                else e.Device.Data=data;
                if(e.Grid.X!=grid.X || e.Grid.Y!=grid.Y || e.Layer!=layer) changed=true;
                e.Grid=grid; e.Layer=layer; e.Seen=revision;
                e.Position=PhysicsPosition(entity);
                quaternion rotation=PhysicsRotation(entity);
                int mode=data.Index==1||data.Index==3||data.Kind==CircuitKind.Interface?0:grid.Rotation%4;
                quaternion basis=math.mul(rotation,quaternion.RotateZ(-mode*math.PI*0.5f));
                e.Right=math.mul(basis,new float3(1,0,0)); e.Up=math.mul(basis,new float3(0,1,0));
                e.Component=PartGameplayUtil.ConnectedComponent(entity);
                int channel=Channel(entity); if(e.Channel!=channel) changed=true; e.Channel=channel;
                if (data.Kind==CircuitKind.PressureSensor) CircuitPressureSetup.EnableEvents(entity);
            }
            for(int i=entries.Count-1;i>=0;i--) if(entries[i].Seen!=revision)
            {
                if (!changed) network.DisconnectAll(); changed=true;
                CircuitReadouts.Remove(entries[i].Entity); byEntity.Remove(entries[i].Entity); entries.RemoveAt(i);
            }
            if(changed)
            {
                network.DisconnectAll(); network.Devices.Clear();
                for(int i=0;i<entries.Count;i++) network.Devices.Add(entries[i].Device);
                network.RefreshElements(); connected.Clear();
            }
            return changed;
        }
        private int Channel(Entity entity)
        {
            if(!EntityManager.HasComponent<PartContainerValue>(entity)) return -1;
            Entity container=EntityManager.GetComponentData<PartContainerValue>(entity).Value;
            return EntityManager.Exists(container) && EntityManager.HasComponent<ColoredFrame>(container)
                ? EntityManager.GetComponentData<PartTypeValue>(container).Index : -1;
        }
        private Entity At(int x,int y,byte layer)
        {
            if(!session.Unmanaged.PartMap.TryGet(x,y,layer,out var info)) return Entity.Null;
            Entity part=info.Part;
            if(part==Entity.Null && EntityManager.Exists(info.PartContainer) && EntityManager.HasComponent<ContainedPart>(info.PartContainer))
                part=EntityManager.GetComponentData<ContainedPart>(info.PartContainer).Value;
            return EntityManager.Exists(part)?part:Entity.Null;
        }
        private static int2 Offset(int d) => d==0?new int2(1,0):d==1?new int2(0,1):d==2?new int2(-1,0):new int2(0,-1);
        private void BuildFixedConnections()
        {
            fixedConnections.Clear(); jointPairs.Clear();
            var joints=session.Unmanaged.Joints;
            for(int i=0;i<joints.Length;i++) if(EntityManager.HasComponent<PhysicsConstrainedBodyPair>(joints[i]))
            {
                var p=EntityManager.GetComponentData<PhysicsConstrainedBodyPair>(joints[i]);
                if(byEntity.ContainsKey(p.EntityA)&&byEntity.ContainsKey(p.EntityB))
                { jointPairs.Add((p.EntityA,p.EntityB)); jointPairs.Add((p.EntityB,p.EntityA)); }
            }
            for(int i=0;i<entries.Count;i++)
            {
                Entry a=entries[i];
                for(int d=0;d<4;d++)
                {
                    if(a.Device.Ports[d]==null) continue;
                    int2 o=Offset(d); Entity other=At(a.Grid.X+o.x,a.Grid.Y+o.y,a.Layer);
                    if(other.Index<=a.Entity.Index || !byEntity.TryGetValue(other,out Entry b) || b.Device.Ports[(d+2)%4]==null) continue;
                    bool welded=MegaStructure.Active && EntityManager.HasComponent<MegaPartOffset>(a.Entity)&&EntityManager.HasComponent<MegaPartOffset>(b.Entity)&&a.Component==b.Component;
                    if(jointPairs.Contains((a.Entity,b.Entity)) || welded)
                        fixedConnections.Add(new Connection { A=a,B=b,X=d,Y=(d+2)%4 });
                }
            }
        }
        private static int3 Cell(float3 p,float size,int tag=0) => new int3((int)math.floor(p.x/size),(int)math.floor(p.y/size),tag);
        private void BuildConnections()
        {
            desired.Clear(); interfaces.Clear(); senders.Clear();
            for(int i=0;i<fixedConnections.Count;i++)
            {
                var c=fixedConnections[i]; if(!FailedPort(c.A,c.X)&&!FailedPort(c.B,c.Y)) desired.Add(c);
            }
            for(int i=0;i<entries.Count;i++)
            {
                Entry e=entries[i];
                if(Failed(e))continue;
                if(e.Device.Data.Kind==CircuitKind.Interface) interfaces.Add(Cell(e.Position,1,e.Layer),e);
                if(e.Device.Data.Index==40) senders.Add(Cell(e.Position,33,e.Channel),e);
            }
            for(int i=0;i<entries.Count;i++)
            {
                Entry a=entries[i];
                if(Failed(a))continue;
                if(a.Device.Data.Kind==CircuitKind.Interface)
                {
                    int3 key=Cell(a.Position,1,a.Layer);
                    for(int x=-1;x<=1;x++) for(int y=-1;y<=1;y++)
                    {
                        var bucket=interfaces.Get(key+new int3(x,y,0)); if(bucket==null) continue;
                        for(int j=0;j<bucket.Count;j++)
                        {
                            Entry b=bucket[j]; if(b.Entity.Index<=a.Entity.Index) continue;
                            float3 delta=b.Position-a.Position;
                            if(math.lengthsq(delta.xy)>1.21f || math.lengthsq(delta.xy)<0.64f) continue;
                            int da=Facing(a,delta),db=Facing(b,-delta);
                            if(da<0||db<0||a.Device.Ports[da]==null||b.Device.Ports[db]==null||FailedPort(a,da)||FailedPort(b,db)) continue;
                            if(jointPairs.Contains((a.Entity,b.Entity))) continue;
                            desired.Add(new Connection { A=a,B=b,X=da,Y=db,Extra=true });
                        }
                    }
                }
                else if(a.Device.Data.Index==41)
                {
                    Entry nearest=null; float best=33*33; int3 key=Cell(a.Position,33,a.Channel);
                    for(int x=-1;x<=1;x++) for(int y=-1;y<=1;y++)
                    {
                        var bucket=senders.Get(key+new int3(x,y,0)); if(bucket==null) continue;
                        for(int j=0;j<bucket.Count;j++)
                        {
                            Entry b=bucket[j]; float distance=math.distancesq(a.Position.xy,b.Position.xy);
                            if(distance<best || (distance==best && (nearest==null||b.Entity.Index<nearest.Entity.Index))) {best=distance;nearest=b;}
                        }
                    }
                    if(nearest!=null)
                    {
                        double distance=System.Math.Max(System.Math.Sqrt(best)-1,0);
                        a.Device.WirelessResistance.Resistance=.1*distance*distance+.01;
                        desired.Add(new Connection { A=a,B=nearest,X=-1,Y=(nearest.Grid.Rotation+3)%4,Extra=true });
                    }
                }
            }
            bool changed=desired.Count!=connected.Count;
            if(!changed) for(int i=0;i<desired.Count;i++) if(!desired[i].Equals(connected[i])) {changed=true;break;}
            if(!changed) return;
            network.DisconnectAll(); connected.Clear();
            for(int i=0;i<desired.Count;i++)
            {
                var c=desired[i];
                network.Connect(c.X<0?c.A.Device.WirelessPort:c.Extra?c.A.Device.Tap(c.X):c.A.Device.Ports[c.X],
                    c.Extra?c.B.Device.Tap(c.Y):c.B.Device.Ports[c.Y]);
                connected.Add(c);
            }
        }
        private static bool Failed(Entry e) => e.Device.DisconnectedPorts!=0 && e.Device.DisconnectedPorts==e.Device.Data.TerminalMask;
        private static bool FailedPort(Entry e,int direction) => (e.Device.DisconnectedPorts&(1<<direction))!=0;
        private static int Facing(Entry e,float3 delta)
        {
            float right=math.dot(delta,e.Right),up=math.dot(delta,e.Up);
            if(math.abs(right)>.8f && math.abs(up)<.2f) return right>0?0:2;
            if(math.abs(up)>.8f && math.abs(right)<.2f) return up>0?1:3;
            return -1;
        }
        private void QueueTriggers(Entry e)
        {
            int2 o=Offset((e.Grid.Rotation%4+4)%4); int limit=e.Device.Data.Index==48?3:1;
            for(int distance=1;distance<=limit;distance++)
            {
                Entity target=At(e.Grid.X+o.x*distance,e.Grid.Y+o.y*distance,e.Layer);
                if(target==Entity.Null && session.Unmanaged.PartMap.TryGet(e.Grid.X+o.x*distance,e.Grid.Y+o.y*distance,e.Layer,out var p)) target=p.PartContainer;
                if(target!=Entity.Null&&EntityManager.Exists(target)&&PartGameplayUtil.ConnectedComponent(target)==e.Component) pendingTriggers.Add(target);
            }
        }
        private void DispatchTriggers()
        {
            for(int i=0;i<pendingTriggers.Count;i++) if(EntityManager.Exists(pendingTriggers[i])) PartSimulation.ProcessTouch(pendingTriggers[i]);
            pendingTriggers.Clear();
        }
        private void WriteBack(bool visuals)
        {
            for(int i=0;i<entries.Count;i++)
            {
                Entry e=entries[i]; if(!EntityManager.Exists(e.Entity)) continue;
                EntityManager.SetComponentData(e.Entity,e.Device.Data);
                if(visuals) { UpdateVisual(e); CircuitReadouts.Update(e.Entity,e.Device.Data,e.Position); }
            }
        }
        private void UpdateVisual(Entry e)
        {
            CircuitPart data=e.Device.Data;
            if(data.Kind!=CircuitKind.Wire && data.Kind!=CircuitKind.Interface && data.Kind!=CircuitKind.IntegratedCircuit && data.Kind!=CircuitKind.Switch && data.Kind!=CircuitKind.Vcc && !Failed(e)) return;
            if(!EntityManager.HasBuffer<LinkedEntityGroup>(e.Entity)) return;
            bool switches=data.Kind==CircuitKind.Switch||data.Kind==CircuitKind.Vcc;
            float brightness=data.Grounded1?(.3f+.7f*math.abs(data.Voltage1)/(math.abs(data.Voltage1)+1)):.6f;
            bool tint=data.Kind==CircuitKind.Wire||data.Kind==CircuitKind.Interface||data.Kind==CircuitKind.IntegratedCircuit||Failed(e);
            if(e.VisualInitialized && (!switches||e.LastClosed==data.Closed) && (!tint||math.abs(e.Brightness-brightness)<.01f)&&e.LastInvalid==data.Invalid&&e.LastBurnedPorts==data.BurnedPorts) return;
            int length=EntityManager.GetBuffer<LinkedEntityGroup>(e.Entity).Length;
            for(int i=0;i<length;i++)
            {
                Entity child=EntityManager.GetBuffer<LinkedEntityGroup>(e.Entity)[i].Value;
                if(tint&&EntityManager.HasComponent<MaterialColor>(child))
                {
                    bool invalid=data.Invalid;
                    if((data.Index==1||data.Index==3)&&e.Grid.Rotation%7==6&&EntityManager.HasComponent<CircuitVisualNode>(child))
                    {int mask=EntityManager.GetComponentData<CircuitVisualNode>(child).Kind==0?5:10;invalid=(data.BurnedPorts&mask)!=0;}
                    EntityManager.SetComponentData(child,new MaterialColor { Value=invalid?new float4(1,.3f,.3f,1):new float4(brightness,brightness,brightness,1) });
                }
                if(!switches||!EntityManager.HasComponent<SwitchVisual>(child)) continue;
                bool visible=EntityManager.GetComponentData<SwitchVisual>(child).Active==data.Closed;
                bool hidden=EntityManager.HasComponent<DisableRendering>(child);
                if(visible&&hidden) EntityManager.RemoveComponent<DisableRendering>(child);
                else if(!visible&&!hidden) EntityManager.AddComponent<DisableRendering>(child);
            }
            e.VisualInitialized=true;e.LastClosed=data.Closed;e.LastInvalid=data.Invalid;e.LastBurnedPorts=data.BurnedPorts;e.Brightness=brightness;
        }
        private float Charge(Entry e) => e.Device.Data.Closed?(e.Grid.Rotation%4==0?e.Device.Data.ValueA:-e.Device.Data.ValueA):0;
        private void ApplyFields(float dt)
        {
            charges.Clear(); magneticTargets.Clear();
            for(int i=0;i<entries.Count;i++)
            {
                Entry e=entries[i]; var d=e.Device.Data;
                if(d.Kind==CircuitKind.PointCharge&&d.Closed) {charges.Add(Cell(e.Position,64),e);magneticTargets.Add(Cell(e.Position,8),e);}
                else if(d.Index==2||d.Index==3||d.Index==39) magneticTargets.Add(Cell(e.Position,8),e);
            }
            for(int i=0;i<entries.Count;i++)
            {
                Entry a=entries[i]; var data=a.Device.Data;
                if(!data.Closed) continue;
                if(data.Kind==CircuitKind.PointCharge)
                {
                    int3 key=Cell(a.Position,64);
                    for(int x=-1;x<=1;x++) for(int y=-1;y<=1;y++)
                    {
                        var bucket=charges.Get(key+new int3(x,y,0));if(bucket==null)continue;
                        for(int j=0;j<bucket.Count;j++)
                        {
                            Entry b=bucket[j]; if(b.Entity.Index<=a.Entity.Index)continue;
                            float3 delta=b.Position-a.Position;delta.z=0;float r2=math.lengthsq(delta);
                            if(r2>4096)continue;float r=math.max(math.sqrt(r2),1);
                            PairForce(a,b,1.5f*Charge(a)*Charge(b)*delta/(r*r*r),dt);
                        }
                    }
                }
                else if(data.Kind==CircuitKind.ElectricField||data.Kind==CircuitKind.MagneticField)
                {
                    int3 key=Cell(a.Position,8);
                    for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)
                    {
                        var bucket=magneticTargets.Get(key+new int3(x,y,0));if(bucket==null)continue;
                        for(int j=0;j<bucket.Count;j++)
                        {
                            Entry b=bucket[j];float distance=math.distance(a.Position.xy,b.Position.xy);if(distance>8)continue;
                            float falloff=distance<=4?1:1/(distance-3); float3 force;
                            if(data.Kind==CircuitKind.ElectricField)
                            {
                                if(b.Device.Data.Kind!=CircuitKind.PointCharge)continue;
                                float3 up=math.mul(PhysicsRotation(a.Entity),new float3(0,1,0));
                                force=up*(data.ValueA*Charge(b)*falloff);
                            }
                            else
                            {
                                float field=(a.Grid.Rotation%4==0?-data.ValueA:a.Grid.Rotation%4==2?data.ValueA:0)*falloff;
                                if(b.Device.Data.Kind==CircuitKind.PointCharge)
                                {
                                    float3 v=PartGameplayUtil.LinearVelocity(b.Entity)-PartGameplayUtil.LinearVelocity(a.Entity);
                                    force=field*Charge(b)*new float3(-v.y,v.x,0);
                                }
                                else
                                {
                                    var currents=b.Device.Currents;
                                    force=field*(.5f*(float)(currents[0]-currents[2])*b.Up+.5f*(float)(currents[1]-currents[3])*b.Right);
                                }
                            }
                            if(math.all(math.isfinite(force))) PairForce(a,b,force,dt);
                        }
                    }
                }
            }
        }
        private void PairForce(Entry a,Entry b,float3 force,float dt)
        { Impulse(a.Entity,-force*dt,a.Position);Impulse(b.Entity,force*dt,b.Position); }
        private void Impulse(Entity entity,float3 impulse,float3 point)
        {
            if(!math.all(math.isfinite(impulse))||!EntityManager.HasComponent<PhysicsMass>(entity)||!EntityManager.HasComponent<PhysicsVelocity>(entity))return;
            var mass=EntityManager.GetComponentData<PhysicsMass>(entity);if(mass.InverseMass<=0)return;
            var velocity=EntityManager.GetComponentData<PhysicsVelocity>(entity);velocity.Linear+=impulse*mass.InverseMass;
            quaternion rotation=PhysicsRotation(entity);
            velocity.Angular+=math.rotate(math.inverse(math.mul(rotation,mass.Transform.rot)),math.cross(point-(PhysicsPosition(entity)+math.rotate(rotation,mass.CenterOfMass)),impulse))*mass.InverseInertia;
            EntityManager.SetComponentData(entity,velocity);
        }
        // Physics writes root LocalTransform before the presentation transform systems.
        private float3 PhysicsPosition(Entity entity) => EntityManager.HasComponent<LocalTransform>(entity)&&!EntityManager.HasComponent<Parent>(entity)
            ?EntityManager.GetComponentData<LocalTransform>(entity).Position:PartGameplayUtil.Position(entity);
        private quaternion PhysicsRotation(Entity entity) => EntityManager.HasComponent<LocalTransform>(entity)&&!EntityManager.HasComponent<Parent>(entity)
            ?EntityManager.GetComponentData<LocalTransform>(entity).Rotation:PartGameplayUtil.Rotation(entity);
    }
}
