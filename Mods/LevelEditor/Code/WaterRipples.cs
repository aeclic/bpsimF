using System;
using UnityEngine;

namespace Bpsim.LevelEditor
{
    // Original shader layout: position, birth time, signed range; then strength,
    // frequency, travel speed and damping. Negative range identifies a wake.
    public sealed class WaterRipples : IDisposable
    {
        private readonly Color[] m_centers=new Color[64],m_parameters=new Color[64],m_extras=new Color[64];
        private readonly Texture2D m_centerTexture,m_parameterTexture,m_extraTexture;
        private int m_count;
        private bool m_dirty;
        public int Count { get { return m_count; } }
        public WaterRipples() { m_centerTexture=Create();m_parameterTexture=Create();m_extraTexture=Create(); }
        private static Texture2D Create() { return new Texture2D(64,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp}; }
        public void Add(float x,float z,float time,float strength,bool wake=false)
        {
            if(m_count==64){Array.Copy(m_centers,1,m_centers,0,63);Array.Copy(m_parameters,1,m_parameters,0,63);Array.Copy(m_extras,1,m_extras,0,63);m_count--;}
            m_centers[m_count]=new Color(x,z,time,wake?-40:40);m_parameters[m_count]=new Color(strength,3,6,2.5f);m_extras[m_count]=new Color(1,1,1,0);m_count++;m_dirty=true;
        }
        public void Update(float time,Material surface,Material underwater)
        {
            for(int i=m_count-1;i>=0;i--)if(time-m_centers[i].b>=Mathf.Abs(m_centers[i].a)/6f)
            {
                int remaining=m_count-i-1;Array.Copy(m_centers,i+1,m_centers,i,remaining);Array.Copy(m_parameters,i+1,m_parameters,i,remaining);Array.Copy(m_extras,i+1,m_extras,i,remaining);m_count--;m_dirty=true;
            }
            if(m_dirty)
            {
                m_centerTexture.SetPixels(m_centers);m_parameterTexture.SetPixels(m_parameters);m_extraTexture.SetPixels(m_extras);
                m_centerTexture.Apply(false);m_parameterTexture.Apply(false);m_extraTexture.Apply(false);m_dirty=false;
            }
            Bind(surface);Bind(underwater);
        }
        private void Bind(Material material)
        {
            if(material==null)return;
            material.SetFloat("_RippleCount",m_count);material.SetTexture("_RippleCentersTex",m_centerTexture);material.SetTexture("_RippleParamsTex",m_parameterTexture);material.SetTexture("_RippleExtraParamsTex",m_extraTexture);
        }
        public void Dispose(){UnityEngine.Object.Destroy(m_centerTexture);UnityEngine.Object.Destroy(m_parameterTexture);UnityEngine.Object.Destroy(m_extraTexture);}
    }
}
