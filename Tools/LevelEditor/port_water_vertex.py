"""Retain the original surface vertex deformation and perspective varyings."""
from pathlib import Path
import re
from port_ultimate_water import function_calls, WEB, SHADERS


def hlsl(text):
    text = re.sub(r'\b(lowp|mediump|highp)\s+', '', text)
    text = function_calls(text, 'textureLod', lambda p: f'{p[0]}.SampleLevel(sampler{p[0]}, {p[1]}, {p[2]})')
    text = function_calls(text, 'texelFetch', lambda p: f'{p[0]}.Load(int3({p[1]}, {p[2]}))')
    for fn, op in [('lessThanEqual','<='),('greaterThanEqual','>='),('lessThan','<'),('greaterThan','>'),('equal','=='),('notEqual','!=')]:
        text = function_calls(text, fn, lambda p,op=op:f'(({p[0]}) {op} ({p[1]}))')
    text = function_calls(text, 'op_not', lambda p:f'(~({p[0]}))')
    for a,b in {'fract':'frac','inversesqrt':'rsqrt','mix':'lerp','uintBitsToFloat':'asfloat','floatBitsToUint':'asuint'}.items():
        text = re.sub(r'\b'+a+r'\b',b,text)
    for prefix,out in [('ivec','int'),('uvec','uint'),('bvec','bool'),('vec','float')]:
        for n in (2,3,4): text = re.sub(r'\b'+prefix+str(n)+r'\b',out+str(n),text)
    for typ in ('float','int','uint','bool'):
        for n in (2,3,4): text = re.sub(r'\b'+typ+str(n)+r'\(([^(),]+)\)',lambda m,t=typ,n=n:f'(({t}{n})({m[1]}))',text)
    for a,b in {'_Time':'uw_Time','_TimeParameters':'uw_TimeParameters','_ProjectionParams':'uw_ProjectionParams'}.items():
        text = re.sub(r'\b'+a+r'\b',b,text)
    return re.sub(r'(?m)^(float[234]?\s+u_\w+;)$',r'precise \1',text)


def port_vertex(metadata):
    for name in ('WaterSurface','Underwater'):
        port_program(metadata,name)
    port_rays(metadata)


def port_program(metadata,name):
    surface=name=='WaterSurface'
    path=WEB/(name+'.vertex.glsl')
    if not path.exists(): raise FileNotFoundError('Capture the demo vertex program before porting')
    source=path.read_text(encoding='utf-8')
    source=re.sub(r'uniform\s+\w+\s*\{([^}]+)\};',lambda m:re.sub(r'(?m)^\s*(vec[234]|float)\s+',r'\nuniform \1 ',m[1]),source)
    uniforms={k:{'type':t,'array':a or ''} for t,k,a in re.findall(r'uniform\s+(?:(?:highp|mediump|lowp)\s+)?(vec[234]|float|sampler2D)\s+(\w+)\s*(\[\d+\])?;',source) if 'Unused' not in k and not k.startswith('hlslcc_')}
    body=source[source.index('void main()'):];body=body[body.index('{')+1:body.rfind('}')]
    locals_=source[:source.index('void main()')]
    locals_='\n'.join(line for line in re.sub(r'\b(?:lowp|mediump|highp)\s+','',locals_).splitlines() if re.fullmatch(r'(?:[biu]?vec[234]|float|int|uint|bool)\s+u_\w+;',line.strip()))
    # The host moves the original local geometry into its orthographic scene.
    # Keep the virtual camera's homogeneous W for perspective interpolation.
    begin=body.index('    u_xlat1 = u_xlat0.yyyy * hlslcc_mtx4x4unity_MatrixVP[1];')
    end=body.index('    u_xlat2.xz =',begin)
    projection='''float d=19.4+u_xlat0.z,p=19.4/d;
float cx=(uw_CaptureRect.x+uw_CaptureRect.z*.5-uw_Bounds.x-uw_Bounds.z*.5)/uw_Scale;
float cy=(uw_CaptureRect.y+uw_CaptureRect.w*.5-uw_Bounds.y)/uw_Scale;
vec2 projected=vec2(uw_Bounds.x+uw_Bounds.z*.5+(cx+(u_xlat0.x-cx)*p)*uw_Scale,uw_Bounds.y+(cy+(u_xlat0.y-cy)*p)*uw_Scale);
u_xlat1=vec4(((projected-uw_CaptureRect.xy)/uw_CaptureRect.zw*2.0-1.0)*d,0,d);
'''
    body=body[:begin]+projection+body[end:]
    for matrix in ('ObjectToWorld','WorldToObject'):
        for i,vector in enumerate(('vec4(1,0,0,0)','vec4(0,1,0,0)','vec4(0,0,1,0)','vec4(0,0,0,1)')):
            body=body.replace(f'hlslcc_mtx4x4unity_{matrix}[{i}]',vector)
    for old,new in {'in_POSITION0':'uw_position','in_NORMAL0':'vec3(0,0,1)','in_TEXCOORD0':'vec4(uv,0,0)','in_TEXCOORD1':'vec4(uw_position.xz,0,0)' if surface else 'vec4(uv.x,-uw_position.y,0,0)','in_COLOR0':'vec4(1)'}.items():
        body=re.sub(r'\b'+old+r'\b',new,body)
    body=body.replace('return;','')
    outputs='vec4 vs_INTERP0,vs_INTERP1,vs_INTERP2,vs_INTERP3;vec3 vs_INTERP4,vs_INTERP5;'
    gl='''#version 300 es
precision highp float;precision highp int;
layout(location=0) in vec2 local;layout(location=2) in vec2 uv;
uniform vec4 uw_CaptureRect,uw_Bounds;uniform float uw_Scale;
out vec2 uw_world,uw_uv,uw_local;out vec3 uw_original;out vec4 uw_color;
int op_not(int v){return ~v;}
'''
    gl+='\n'.join('uniform '+v['type']+' '+k+v['array']+';' for k,v in uniforms.items())
    gl+='\nvoid main(){vec3 uw_position=vec3(local.x*uw_Bounds.z/uw_Scale/303.75,0,local.y);\n'+outputs+'\n'+locals_+'\n'+body
    gl+='\nuw_world=projected;uw_uv=vs_INTERP0.xy;uw_local=vs_INTERP1.xy;uw_original=vs_INTERP4;uw_color=vs_INTERP2;}'
    if not surface:
        gl=gl.replace('layout(location=0) in vec2 local;layout(location=2) in vec2 uv;','layout(location=0) in vec2 position;layout(location=1) in vec2 world;layout(location=2) in vec2 hostUv;')
        gl=gl.replace('vec3 uw_position=vec3(local.x*uw_Bounds.z/uw_Scale/303.75,0,local.y);','vec2 uv=vec2(hostUv.x,1.0-hostUv.y);vec3 uw_position=vec3((world.x-uw_Bounds.x-uw_Bounds.z*.5)/uw_Scale,(world.y-uw_Bounds.y)/uw_Scale,0);')
    (WEB/(name+'.vertex-port.glsl')).write_text(gl,encoding='utf-8')
    frag=(WEB/(name+'.fragment.glsl')).read_text(encoding='utf-8')
    frag=frag.replace('in vec2 uw_local;','in vec2 uw_local;in vec3 uw_original;in vec4 uw_color;')
    frag=frag.replace('vec3 vs_INTERP4=vec3(uw_local.x,0,uw_local.y);','vec3 vs_INTERP4=uw_original;').replace('vec4 vs_INTERP2=vec4(1);','vec4 vs_INTERP2=uw_color;')
    if not surface:
        frag=frag.replace('in vec2 uw_uv;','in vec2 uw_uv;in vec2 uw_local;in vec3 uw_original;in vec4 uw_color;')
        frag=frag.replace('vec4 vs_INTERP0=vec4(uw_uv.x,1.0-uw_uv.y,0,0);','vec4 vs_INTERP0=vec4(uw_uv,0,0);').replace('vec4 vs_INTERP1=vec4(uw_uv.x,-y,0,0);','vec4 vs_INTERP1=vec4(uw_local,0,0);')
        frag=frag.replace('vec3 vs_INTERP4=vec3((uw_world.x-uw_Bounds.x)/uw_Scale,y,0);','vec3 vs_INTERP4=uw_original;')
    (WEB/(name+'.fragment.glsl')).write_text(frag,encoding='utf-8')
    shader=(SHADERS/('Ultimate'+name+'.shader')).read_text(encoding='utf-8')
    added={k:v for k,v in uniforms.items() if k not in metadata[name]['uniforms']}
    metadata[name]['uniforms'].update(uniforms)
    declarations=[];properties=[]
    for k,v in added.items():
        if k.startswith('_Time') or k=='_ProjectionParams':continue
        typ=v['type'].replace('vec','float');declarations.append(('Texture2D<float4> '+k+'; SamplerState sampler'+k+';' if typ=='sampler2D' else typ+' '+k+v['array']+';'))
        properties.append(k+' ("'+k+'", '+('2D) = "black" {}' if typ=='sampler2D' else 'Float) = 0' if typ=='float' else 'Vector) = (0,0,0,0)'))
    shader=shader.replace('Properties {','Properties {\n'+'\n'.join(properties),1)
    shader=shader.replace('struct Attributes {','\n'.join(declarations)+'\nstruct Attributes {',1)
    shader=shader.replace('float2 local:TEXCOORD2; };','float2 local:TEXCOORD2; float3 original:TEXCOORD3; float4 color:COLOR; };',1)
    if not surface:shader=shader.replace('float2 world:TEXCOORD1; };','noperspective float2 world:TEXCOORD1; float2 local:TEXCOORD2; float3 original:TEXCOORD3; float4 color:COLOR; };',1)
    shader=shader.replace('float3 vs_INTERP4=local;','float3 vs_INTERP4=i.original;').replace('float4 vs_INTERP2=float4(1,1,1,1);','float4 vs_INTERP2=i.color;')
    if not surface:
        shader=shader.replace('float4 vs_INTERP0=float4(i.uv.x,1-i.uv.y,0,0);','float4 vs_INTERP0=float4(i.uv,0,0);').replace('float4 vs_INTERP1=float4(i.uv.x,-local.y,0,0);','float4 vs_INTERP1=float4(i.local,0,0);')
    hsbody=body.replace(projection,'''float d=19.4+u_xlat0.z,p=19.4/d;
float cx=(uw_CaptureRect.x+uw_CaptureRect.z*.5-uw_Bounds.x-uw_Bounds.z*.5)/uw_Scale;
float cy=(uw_CaptureRect.y+uw_CaptureRect.w*.5-uw_Bounds.y)/uw_Scale;
float3 projected=TransformObjectToWorld(a.positionOS);
projected.xy=float2(uw_Bounds.x+uw_Bounds.z*.5+(cx+(u_xlat0.x-cx)*p)*uw_Scale,uw_Bounds.y+(cy+(u_xlat0.y-cy)*p)*uw_Scale);
u_xlat1=TransformWorldToHClip(projected)*d;
''')
    hsbody=hsbody.replace('gl_Position = u_xlat1;','o.positionCS=u_xlat1;')
    vertex='''Varyings Vert(Attributes a){Varyings o;
float2 uv=a.uv;float3 uw_position=float3(a.local.x,0,a.local.y);
float4 uw_Time=float4(uw_Clock/20,uw_Clock,uw_Clock*2,uw_Clock*3),uw_TimeParameters=float4(uw_Clock,sin(uw_Clock),cos(uw_Clock),0),uw_ProjectionParams=float4(1,0,0,0);
'''+hlsl(outputs+'\n'+locals_+'\n'+hsbody)+'''
o.world=projected.xy;o.uv=vs_INTERP0.xy;o.local=vs_INTERP1.xy;o.original=vs_INTERP4;o.color=vs_INTERP2;return o;}
'''
    if not surface:vertex=vertex.replace('float2 uv=a.uv;float3 uw_position=float3(a.local.x,0,a.local.y);','float2 uv=float2(a.uv.x,1-a.uv.y);float3 w=TransformObjectToWorld(a.positionOS);float3 uw_position=float3((w.x-uw_Bounds.x-uw_Bounds.z*.5)/uw_Scale,(w.y-uw_Bounds.y)/uw_Scale,0);')
    start=shader.index('Varyings Vert(');end=shader.index('float4 Frag(',start)
    shader=shader[:start]+vertex+shader[end:]
    (SHADERS/('Ultimate'+name+'.shader')).write_text(shader,encoding='utf-8')


def port_rays(metadata):
    source=(WEB/'LightRays.vertex.glsl').read_text(encoding='utf-8')
    body=source[source.index('void main()'):];body=body[body.index('{')+1:body.index('    u_xlat1 = u_xlat0.yyyy * hlslcc_mtx4x4unity_ObjectToWorld[1];')]
    locals_='\n'.join(re.findall(r'(?m)^(?:vec[234]|float) u_\w+;',source))
    for old,new in {'in_POSITION0':'position','in_TEXCOORD0':'uv','in_TEXCOORD1':'seed','in_TEXCOORD2':'extra'}.items():body=re.sub(r'\b'+old+r'\b',new,body)
    uniforms={k:{'type':t,'array':''} for t,k in re.findall(r'uniform\s+(vec[234]|float)\s+(\w+);',source) if not k.startswith('hlslcc')}
    projection='''u_xlat0.z=clamp(u_xlat0.z,_LocalZBounds.x,_LocalZBounds.y);
float d=19.4+u_xlat0.z,p=19.4/d;
float cx=(uw_CaptureRect.x+uw_CaptureRect.z*.5-uw_Bounds.x-uw_Bounds.z*.5)/uw_Scale;
float cy=(uw_CaptureRect.y+uw_CaptureRect.w*.5-uw_Bounds.y)/uw_Scale;
vec2 projected=vec2(uw_Bounds.x+uw_Bounds.z*.5+(cx+(u_xlat0.x-cx)*p)*uw_Scale,uw_Bounds.y+(cy+(u_xlat0.y-cy)*p)*uw_Scale);
'''
    gl='''#version 300 es
precision highp float;precision highp int;
layout(location=0) in vec3 position;layout(location=1) in vec2 uv;layout(location=2) in vec2 seed;layout(location=3) in vec2 extra;layout(location=4) in vec4 color;
uniform vec4 uw_CaptureRect,uw_Bounds;uniform float uw_Scale;
out vec2 uw_world,uw_uv,uw_seed;out vec3 uw_local;out vec4 uw_color;
'''+ '\n'.join('uniform '+v['type']+' '+k+';' for k,v in uniforms.items())+'\nvoid main(){\n'+locals_+'\n'+body+projection+'''
gl_Position=vec4(((projected-uw_CaptureRect.xy)/uw_CaptureRect.zw*2.-1.)*d,0,d);
uw_world=projected;uw_uv=uv;uw_seed=seed;uw_local=u_xlat0.xyz;uw_color=color;}
'''
    (WEB/'LightRays.vertex-port.glsl').write_text(gl,encoding='utf-8')
    shader=(SHADERS/'UltimateLightRays.shader').read_text(encoding='utf-8')
    added={k:v for k,v in uniforms.items() if k not in metadata['LightRays']['uniforms']}
    metadata['LightRays']['uniforms'].update(uniforms)
    declarations=[];properties=[]
    for k,v in added.items():
        if k=='_Time':continue
        declarations.append(v['type'].replace('vec','float')+' '+k+';')
        properties.append(k+' ("'+k+'", '+('Float) = 0' if v['type']=='float' else 'Vector) = (0,0,0,0)'))
    shader=shader.replace('Properties {','Properties {\n'+'\n'.join(properties),1).replace('struct Attributes {','\n'.join(declarations)+'\nstruct Attributes {',1)
    shader=shader.replace('float3 local:TEXCOORD2; float4 color:COLOR; };','float2 extra:TEXCOORD2; float4 color:COLOR; };',1)
    vertex='''Varyings Vert(Attributes a){Varyings o;
float3 position=a.positionOS;float2 uv=a.uv,seed=a.seed,extra=a.extra;
float4 uw_Time=float4(uw_Clock/20,uw_Clock,uw_Clock*2,uw_Clock*3);
'''+hlsl(locals_+'\n'+body+projection)+'''
float3 w=float3(projected,0);o.positionCS=TransformWorldToHClip(w)*d;o.world=projected;o.uv=uv;o.seed=seed;o.local=u_xlat0.xyz;o.color=a.color;return o;}
'''
    start=shader.index('Varyings Vert(');end=shader.index('float4 Frag(',start)
    shader=shader[:start]+vertex+shader[end:]
    shader=shader.replace('float2 world:TEXCOORD1;','noperspective float2 world:TEXCOORD1;')
    (SHADERS/'UltimateLightRays.shader').write_text(shader,encoding='utf-8')
