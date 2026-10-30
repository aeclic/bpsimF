using System;
using System.Collections.Generic;

namespace Bpsim.Parts.Circuits.Model
{
    /// <summary>
    /// Large passive/DC networks: collapse ideal conductors before solving the sparse
    /// positive semidefinite nodal equations. Scratch storage grows linearly with ports.
    /// Small circuits retain BPLE's exact branch solver, including its current convention.
    /// </summary>
    internal sealed class SparseCircuitSolver
    {
        private struct Branch { public int A,B; public double R,V; }
        private struct Edge { public int A,B; public bool Physical; }
        private readonly Dictionary<Electrode,int> ids=new Dictionary<Electrode,int>();
        private readonly List<Electrode> ports=new List<Electrode>();
        private readonly List<Branch> branches=new List<Branch>();
        private readonly List<Edge> tree=new List<Edge>();
        private int[] uf=Array.Empty<int>(),size=Array.Empty<int>(),node=Array.Empty<int>(),groups=Array.Empty<int>();
        private int[] first=Array.Empty<int>(),next=Array.Empty<int>(),to=Array.Empty<int>(),edgeIds=Array.Empty<int>();
        private int[] parent=Array.Empty<int>(),order=Array.Empty<int>(),parentEdge=Array.Empty<int>();
        private double[] pin=Array.Empty<double>(),diagonal=Array.Empty<double>(),rhs=Array.Empty<double>(),voltage=Array.Empty<double>();
        private double[] residual=Array.Empty<double>(),direction=Array.Empty<double>(),product=Array.Empty<double>(),preconditioned=Array.Empty<double>();
        private double[] demand=Array.Empty<double>(),current=Array.Empty<double>();
        private bool[] grounded=Array.Empty<bool>(),fixedNode=Array.Empty<bool>();
        public int NodeCount { get; private set; }
        public bool Converged { get; private set; }
        private static void Grow<T>(ref T[] a,int n) { if(a.Length<n)Array.Resize(ref a,Math.Max(n,Math.Max(16,a.Length*2))); }
        private int Find(int x) { while(uf[x]!=x){uf[x]=uf[uf[x]];x=uf[x];}return x; }
        private bool Union(int a,int b)
        {
            a=Find(a);b=Find(b);if(a==b)return false;
            if(size[a]<size[b]){int t=a;a=b;b=t;}uf[b]=a;size[a]+=size[b];return true;
        }
        private void Join(int a,int b,bool physical)
        { if(Union(a,b))tree.Add(new Edge {A=a,B=b,Physical=physical}); }
        private void BranchElement(int a,int b,double r,double v)
        {
            if(r<=1e-12&&v==0){Join(a,b,false);return;}
            branches.Add(new Branch {A=a,B=b,R=Math.Max(r,1e-9),V=v});
        }
        public void Simulate(List<CircuitElement> elements,CircuitSimulator simulator)
        {
            Converged=false;
            ids.Clear();ports.Clear();branches.Clear();tree.Clear();
            for(int i=0;i<elements.Count;i++)
            {
                var electrodes=elements[i].Electrodes;
                for(int j=0;j<electrodes.Count;j++){ids.Add(electrodes[j],ports.Count);ports.Add(electrodes[j]);}
            }
            int count=ports.Count;
            Grow(ref uf,count);Grow(ref size,count);Grow(ref node,count);Grow(ref pin,count);Grow(ref current,count);Grow(ref demand,count);
            for(int i=0;i<count;i++){uf[i]=i;size[i]=1;node[i]=-1;pin[i]=double.NaN;current[i]=0;demand[i]=0;}
            for(int i=0;i<count;i++) if(ports[i].IsConnected&&ids.TryGetValue(ports[i].ConnectedElectrode,out int other)&&other>i)Join(i,other,true);
            for(int i=0;i<elements.Count;i++)
            {
                var e=elements[i];var p=e.Electrodes;int a=ids[p[0]];
                if(e is Wire||e is Switch||e is SPDTSwitch)
                {
                    int start=-1;
                    for(int j=0;j<p.Count;j++)if(p[j].IsClosed){int id=ids[p[j]];if(start<0)start=id;else Join(start,id,false);}
                }
                else if(e is Resistor r)BranchElement(a,ids[p[1]],r.Resistance,0);
                else if(e is VoltageSource s)BranchElement(a,ids[p[1]],s.Resistance,s.Voltage);
                else if(e is Capacitor c)BranchElement(a,ids[p[1]],c.Resistance,c.Charge/c.Capacitance);
                else if(e is Vcc v)
                {
                    if(v.Resistance<=1e-12)pin[a]=v.Potential;
                    else BranchElement(a,-1,v.Resistance,v.Potential);
                }
                else if(e is Ground g)
                {
                    if(g.Resistance<=1e-12)pin[a]=0;else BranchElement(a,-1,g.Resistance,0);
                }
            }
            int nodes=0;
            for(int i=0;i<count;i++){int root=Find(i);if(node[root]<0)node[root]=nodes++;}
            for(int i=0;i<count;i++)node[i]=node[Find(i)];
            NodeCount=nodes;
            Grow(ref diagonal,nodes);Grow(ref rhs,nodes);Grow(ref voltage,nodes);Grow(ref residual,nodes);Grow(ref direction,nodes);Grow(ref product,nodes);Grow(ref preconditioned,nodes);
            Grow(ref fixedNode,nodes);Grow(ref grounded,nodes);Grow(ref groups,nodes);
            for(int i=0;i<nodes;i++){diagonal[i]=rhs[i]=voltage[i]=0;fixedNode[i]=grounded[i]=false;groups[i]=i;}
            for(int i=0;i<count;i++)if(!double.IsNaN(pin[i])){fixedNode[node[i]]=true;grounded[node[i]]=true;voltage[node[i]]=pin[i];}
            for(int i=0;i<branches.Count;i++)
            {
                Branch b=branches[i];b.A=node[b.A];if(b.B>=0)b.B=node[b.B];branches[i]=b;
                if(b.B>=0){int a=Group(b.A),c=Group(b.B);groups[c]=a;}
                else grounded[b.A]=true;
            }
            // Choose a numerical reference independently in each floating island, while
            // preserving the distinction between a numerical reference and real ground.
            for(int i=0;i<nodes;i++)if(grounded[i])grounded[Group(i)]=true;
            // Pin actual voltage sources and one numerical reference per floating island.
            for(int i=0;i<nodes;i++)fixedNode[i]=!grounded[Group(i)]&&Group(i)==i;
            for(int i=0;i<count;i++)if(!double.IsNaN(pin[i]))fixedNode[node[i]]=true;
            for(int i=0;i<nodes;i++)grounded[i]=grounded[Group(i)];
            for(int i=0;i<branches.Count;i++)
            {
                Branch b=branches[i];double g=1/b.R;
                if(b.A==b.B)continue;
                if(!fixedNode[b.A])
                {diagonal[b.A]+=g;rhs[b.A]+=g*b.V;if(b.B>=0&&fixedNode[b.B])rhs[b.A]+=g*voltage[b.B];}
                if(b.B>=0&&!fixedNode[b.B])
                {diagonal[b.B]+=g;rhs[b.B]-=g*b.V;if(fixedNode[b.A])rhs[b.B]+=g*voltage[b.A];}
            }
            double norm=0,rz=0;
            for(int i=0;i<nodes;i++)
            {
                if(fixedNode[i]||diagonal[i]==0){residual[i]=direction[i]=0;continue;}
                residual[i]=rhs[i];direction[i]=residual[i]/diagonal[i];rz+=residual[i]*direction[i];norm+=rhs[i]*rhs[i];
            }
            double tolerance=Math.Max(1e-22,norm*1e-18);
            // A series chain or resistor tree admits linear-time elimination. Besides
            // being faster, this avoids the poor conditioning of very long CG chains.
            Converged=SolveForest(nodes);
            if(!Converged)for(int iteration=0;iteration<Math.Min(nodes*2+8,4096)&&rz>1e-24;iteration++)
            {
                Multiply(direction,product,nodes);double denom=0;for(int i=0;i<nodes;i++)denom+=direction[i]*product[i];
                if(!(denom>0))break;double alpha=rz/denom,error=0;
                for(int i=0;i<nodes;i++)if(!fixedNode[i]){voltage[i]+=alpha*direction[i];residual[i]-=alpha*product[i];error+=residual[i]*residual[i];}
                if(error<=tolerance)break;
                double nextRz=0;for(int i=0;i<nodes;i++){preconditioned[i]=fixedNode[i]||diagonal[i]==0?0:residual[i]/diagonal[i];nextRz+=residual[i]*preconditioned[i];}
                double beta=nextRz/rz;for(int i=0;i<nodes;i++)direction[i]=preconditioned[i]+beta*direction[i];rz=nextRz;
            }
            // Verify the actual residual, including after CG's iteration limit. Never
            // publish an unconverged approximation as a valid voltage/current reading.
            Multiply(voltage,product,nodes);double actualError=0;
            for(int i=0;i<nodes;i++)if(!fixedNode[i]){double error=rhs[i]-product[i];actualError+=error*error;}
            Converged=!double.IsNaN(actualError)&&!double.IsInfinity(actualError)&&actualError<=Math.Max(tolerance,norm*1e-16);
            if(!Converged)
            {
                for(int i=0;i<count;i++)if(ports[i].IsConnected)
                    ports[i].Element.UpdateElectrode(simulator,new SimulationResult(ports[i].Element,ports[i],double.NaN,double.NaN,false));
                return;
            }
            // Recover branch demands at the original electrodes, then recover ideal-wire
            // currents using the union spanning forest (cycles have nonunique ideal currents).
            for(int i=0;i<elements.Count;i++)
            {
                var e=elements[i];var p=e.Electrodes;int a=ids[p[0]];
                int b=p.Count>1?ids[p[1]]:-1;double r=0,v=0;
                if(e is Resistor res)r=res.Resistance;
                else if(e is VoltageSource src){r=src.Resistance;v=src.Voltage;}
                else if(e is Capacitor cap){r=cap.Resistance;v=cap.Charge/cap.Capacitance;}
                else if(e is Vcc supply){r=supply.Resistance;v=supply.Potential;b=-1;}
                else if(e is Ground ground){r=ground.Resistance;b=-1;}
                else continue;
                if(r<=1e-12&&v==0)continue;
                if(b<0&&r<=1e-12)continue;
                double flow=(voltage[node[a]]-(b<0?0:voltage[node[b]])-v)/Math.Max(r,1e-9);
                demand[a]+=flow;if(b>=0)demand[b]-=flow;
            }
            RecoverCurrents(count);
            for(int i=0;i<count;i++)
            {
                var p=ports[i];if(!p.IsConnected)continue;
                p.Element.UpdateElectrode(simulator,new SimulationResult(p.Element,p,voltage[node[i]],current[i],grounded[node[i]]));
            }
        }
        private int Group(int i) {while(groups[i]!=i){groups[i]=groups[groups[i]];i=groups[i];}return i;}
        private bool SolveForest(int count)
        {
            Grow(ref first,count);Grow(ref parent,count);Grow(ref parentEdge,count);Grow(ref order,count);
            Grow(ref next,branches.Count*2);Grow(ref to,branches.Count*2);Grow(ref edgeIds,branches.Count*2);
            for(int i=0;i<count;i++){first[i]=-1;parent[i]=-2;groups[i]=i;}
            int edges=0;
            for(int i=0;i<branches.Count;i++)
            {
                var b=branches[i];if(b.B<0||b.A==b.B||fixedNode[b.A]||fixedNode[b.B])continue;
                int a=Group(b.A),c=Group(b.B);if(a==c)return false;groups[c]=a;
                to[edges]=b.B;edgeIds[edges]=i;next[edges]=first[b.A];first[b.A]=edges++;
                to[edges]=b.A;edgeIds[edges]=i;next[edges]=first[b.B];first[b.B]=edges++;
            }
            int used=0;
            for(int root=0;root<count;root++)
            {
                if(fixedNode[root]||parent[root]!=-2)continue;
                int start=used;parent[root]=-1;order[used++]=root;
                for(int j=start;j<used;j++)
                {
                    int a=order[j];for(int edge=first[a];edge>=0;edge=next[edge])
                    {
                        int b=to[edge];if(parent[b]!=-2)continue;
                        parent[b]=a;parentEdge[b]=edgeIds[edge];order[used++]=b;
                    }
                }
            }
            for(int i=0;i<count;i++){product[i]=diagonal[i];preconditioned[i]=rhs[i];}
            for(int j=used-1;j>=0;j--)
            {
                int b=order[j],a=parent[b];
                if(!(product[b]>0))return false;
                if(a<0)continue;
                double g=1/branches[parentEdge[b]].R;
                product[a]-=g*g/product[b];preconditioned[a]+=g*preconditioned[b]/product[b];
            }
            for(int j=0;j<used;j++)
            {
                int b=order[j],a=parent[b];double value=preconditioned[b];
                if(a>=0)value+=voltage[a]/branches[parentEdge[b]].R;
                voltage[b]=value/product[b];
            }
            return true;
        }
        private void Multiply(double[] vector,double[] result,int count)
        {
            for(int i=0;i<count;i++)result[i]=fixedNode[i]?0:diagonal[i]*vector[i];
            for(int i=0;i<branches.Count;i++)
            {
                Branch b=branches[i];if(b.B<0||b.A==b.B||fixedNode[b.A]||fixedNode[b.B])continue;
                double g=1/b.R;result[b.A]-=g*vector[b.B];result[b.B]-=g*vector[b.A];
            }
        }
        private void RecoverCurrents(int count)
        {
            Grow(ref first,count);Grow(ref parent,count);Grow(ref parentEdge,count);Grow(ref order,count);
            Grow(ref next,tree.Count*2);Grow(ref to,tree.Count*2);Grow(ref edgeIds,tree.Count*2);
            for(int i=0;i<count;i++){first[i]=-1;parent[i]=-2;}
            for(int i=0;i<tree.Count;i++)
            {
                var e=tree[i];int x=2*i;to[x]=e.B;next[x]=first[e.A];first[e.A]=x;edgeIds[x]=i;
                x++;to[x]=e.A;next[x]=first[e.B];first[e.B]=x;edgeIds[x]=i;
            }
            int used=0;
            // Pin terminals must be forest roots so they supply the net demand to ground.
            for(int pass=0;pass<2;pass++)for(int root=0;root<count;root++)
            {
                if(parent[root]!=-2||(pass==0&&double.IsNaN(pin[root])))continue;
                int start=used;parent[root]=-1;order[used++]=root;
                for(int j=start;j<used;j++)
                {
                    int a=order[j];for(int edge=first[a];edge>=0;edge=next[edge])
                    {
                        int b=to[edge];if(parent[b]!=-2)continue;parent[b]=a;parentEdge[b]=edgeIds[edge];order[used++]=b;
                    }
                }
            }
            for(int j=used-1;j>=0;j--)
            {
                int b=order[j],a=parent[b];if(a<0)continue;
                var edge=tree[parentEdge[b]];
                if(edge.Physical){current[b]=demand[b];current[a]=-demand[b];}
                demand[a]+=demand[b];
            }
        }
    }
}
