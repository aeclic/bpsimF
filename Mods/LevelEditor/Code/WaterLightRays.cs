using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Bpsim.LevelEditor
{
    // The source demo's ray quads, seeds, colors, movement and perspective mapping.
    public sealed class WaterLightRays
    {
        public readonly Mesh Mesh;
        public readonly Material Material;
        private readonly Vector3[] m_original;
        private readonly Vector2[] m_uv, m_seed, m_extra;
        private readonly float m_scale;
        public WaterLightRays(WaterAssets assets, float width, float depth, float scale)
        {
            Material = assets.CreateOriginalMaterial("LightRays"); m_scale = scale / 8;
            Material.SetVector("_LocalXYBounds",new Vector4(-width/m_scale/2,width/m_scale/2,-depth/m_scale,0));
            Material.SetFloat("uw_Scale",m_scale);
            JObject data = assets.LightRaysGeometry;
            int count = ((JArray)data["in_POSITION0"]).Count / 3;
            m_original = new Vector3[count]; m_uv = new Vector2[count];
            m_seed = new Vector2[count]; m_extra = new Vector2[count];
            Color[] colors = new Color[count]; int[] triangles = new int[count / 4 * 6];
            for (int i=0;i<count;i++)
            {
                m_original[i]=new Vector3((float)data["in_POSITION0"][i*3]*width/m_scale/303.75f,(float)data["in_POSITION0"][i*3+1],(float)data["in_POSITION0"][i*3+2]);
                m_uv[i]=new Vector2((float)data["in_TEXCOORD0"][i*2],(float)data["in_TEXCOORD0"][i*2+1]);
                m_seed[i]=new Vector2((float)data["in_TEXCOORD1"][i*2],(float)data["in_TEXCOORD1"][i*2+1]);
                m_extra[i]=new Vector2((float)data["in_TEXCOORD2"][i*2],(float)data["in_TEXCOORD2"][i*2+1]);
                colors[i]=new Color((float)data["in_COLOR0"][i*4],(float)data["in_COLOR0"][i*4+1],(float)data["in_COLOR0"][i*4+2],(float)data["in_COLOR0"][i*4+3]);
                if(i%4==0) { int t=i/4*6; triangles[t]=i; triangles[t+1]=i+1; triangles[t+2]=i+2; triangles[t+3]=i+2; triangles[t+4]=i+1; triangles[t+5]=i+3; }
            }
            Mesh=new Mesh { name="Ultimate Water Source Light Rays" }; Mesh.MarkDynamic();
            Mesh.vertices=m_original; Mesh.uv=m_uv; Mesh.uv2=m_seed; Mesh.colors=colors; Mesh.triangles=triangles;Mesh.SetUVs(2,new List<Vector2>(m_extra));
            Update(0,width);
        }
        public void Update(float time,float width)
        {
            Material.SetFloat("uw_Clock",time);
        }
        public void Dispose() { Object.Destroy(Mesh); Object.Destroy(Material); }
    }
}
