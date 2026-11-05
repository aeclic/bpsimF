"""Generate both renderer backends from the demo's original fragment programs.

The generated arithmetic is retained verbatim; only GLSL/HLSL syntax and the
host-provided vertex/capture interface differ. Re-run after asset extraction.
"""
from pathlib import Path
import hashlib
import json
import re

ROOT = Path(__file__).resolve().parents[2]
WEB = ROOT / 'Mods/LevelEditor/Web/bpsim/ultimate-water'
SHADERS = ROOT / 'Mods/LevelEditor/Shaders'


def stage(source):
    return source.split('#ifdef FRAGMENT', 1)[1] if '#ifdef FRAGMENT' in source else source


def function_calls(text, name, replace):
    pattern = re.compile(r'\b' + re.escape(name) + r'\(')
    while (match := pattern.search(text)):
        start, cursor, depth = match.start(), match.end(), 1
        parts, last = [], cursor
        while depth:
            char = text[cursor]
            if char == '(':
                depth += 1
            elif char == ')':
                depth -= 1
            elif char == ',' and depth == 1:
                parts.append(text[last:cursor].strip())
                last = cursor + 1
            cursor += 1
        parts.append(text[last:cursor - 1].strip())
        text = text[:start] + replace(parts) + text[cursor:]
    return text


def generate(name):
    active = WEB / (name + '.active.glsl')
    source = (active if active.exists() else WEB / (name + '.glsl')).read_text(encoding='utf-8')
    fragment = stage(source)
    fragment = re.sub(r'uniform\s+\w+\s*\{([^}]+)\};', lambda match: re.sub(r'(?m)^\s*(vec[234]|float)\s+', r'\nuniform \1 ', match[1]), fragment)
    uniforms = {}
    for typ, key, array in re.findall(r'(?:UNITY_UNIFORM|uniform)\s+(?:mediump\s+)?(vec[234]|float|sampler2D)\s+(\w+)\s*(\[\d+\])?;', fragment):
        if 'Unused' not in key:
            uniforms[key] = {'type': typ, 'array': array or ''}
    samplers = [key for key, value in uniforms.items() if value['type'] == 'sampler2D']
    inputs = re.findall(r'in\s+(?:(?:highp|mediump)\s+)?(vec[234])\s+(\w+);', fragment)
    body = fragment[fragment.index('void main()'):].rsplit('#endif', 1)[0]
    body = body[body.index('{') + 1:body.rfind('}')]
    locals_ = fragment[re.search(r'\n(?:[biu]?vec[234]|float) u_\w+;', fragment).start():fragment.index('void main()')]
    # Keep temporaries local so multiple water passes cannot share mutable state.
    locals_ = re.sub(r'\b(?:lowp|mediump|highp)\s+', '', locals_)
    locals_ = '\n'.join(line for line in locals_.splitlines() if re.fullmatch(r'(?:[biu]?vec[234]|float|int|uint|bool)\s+u_\w+;', line.strip()))
    gl = '#version 300 es\nprecision highp float;\nprecision highp int;\n'
    gl += '\n'.join('uniform ' + v['type'] + ' ' + k + v['array'] + ';' for k, v in uniforms.items())
    gl += '\nuniform sampler2D uw_Terrain;\nuniform vec4 uw_CaptureRect;\nuniform vec4 uw_LightRect;\nuniform vec4 uw_Bounds;\nuniform float uw_Scale;\nin vec2 uw_world;\nin vec2 uw_uv;\n'
    gl += '\nout vec4 SV_TARGET0;\n'
    # LightRays uses a different output identifier.
    body = body.replace('SV_Target0', 'SV_TARGET0')
    gl += '''int op_not(int value) { return ~value; }
uniform float uw_CapturesLinear;
vec4 uwDecodeScene(vec4 color) { return uw_CapturesLinear>0.5?color:vec4(mix(color.rgb/12.92,pow((color.rgb+0.055)/1.055,vec3(2.4)),step(vec3(0.04045),color.rgb)),color.a); }
vec3 uwEncodeScene(vec3 color) { color=max(color,vec3(0)); return mix(color*12.92,1.055*pow(color,vec3(1.0/2.4))-0.055,step(vec3(0.0031308),color)); }
'''
    gl += '''uniform sampler2D uw_Rays;
vec4 uwUnderwater(vec4 color,vec2 uv) { vec4 rays=textureLod(uw_Rays,uv,0.0); return vec4(uwDecodeScene(color).rgb*(1.0-rays.a)+rays.rgb,color.a); }
'''
    gl += 'void main() {\nvec2 maskUv=(uw_world-uw_CaptureRect.xy)/uw_CaptureRect.zw;\nif(texture(uw_Terrain,maskUv).r>0.5) discard;\n'
    if name == 'WaterSurface':
        gl = gl.replace('in vec2 uw_uv;', 'in vec2 uw_uv;\nin vec2 uw_local;')
        gl += '''float projected=clamp(uw_uv.y,0.0,1.0)*294.121521/(294.121521+19.4);
float z=19.4*projected/max(1.0-projected,0.0001);
float cameraX=(uw_CaptureRect.x+uw_CaptureRect.z*0.5-uw_Bounds.x-uw_Bounds.z*0.5)/uw_Scale;
float x=cameraX+((uw_world.x-uw_Bounds.x-uw_Bounds.z*0.5)/uw_Scale-cameraX)*(1.0+z/19.4);
vec4 vs_INTERP0=vec4(x/(uw_Bounds.z/uw_Scale*(1.0+0.8*z/294.121521))+0.5,z/294.121521,0,0);
vec4 vs_INTERP1=vec4(x,z,0,0); vec3 vs_INTERP4=vec3(x,0,z);
'''
        start = gl.index('float projected=')
        gl = gl[:start] + '''vec4 vs_INTERP0=vec4(uw_uv,0,0);
vec4 vs_INTERP1=vec4(uw_local,0,0); vec3 vs_INTERP4=vec3(uw_local.x,0,uw_local.y);
'''
    elif name == 'Underwater':
        gl += '''float y=(uw_world.y-uw_Bounds.y)/uw_Scale;
vec4 vs_INTERP0=vec4(uw_uv.x,1.0-uw_uv.y,0,0);
vec4 vs_INTERP1=vec4(uw_uv.x,-y,0,0); vec3 vs_INTERP4=vec3((uw_world.x-uw_Bounds.x)/uw_Scale,y,0);
'''
    if name == 'LightRays':
        gl = gl.replace('in vec2 uw_uv;', 'in vec2 uw_uv;\nin vec2 uw_seed;\nin vec3 uw_local;\nin vec4 uw_color;')
        gl += 'vec2 vs_TEXCOORD0=uw_uv; vec2 vs_TEXCOORD1=uw_seed; vec3 vs_TEXCOORD2=uw_local; vec4 vs_COLOR0=uw_color;\n'
    else:
        gl += 'vec4 vs_INTERP2=vec4(1);\n'
        gl += 'vec4 vs_INTERP3=vec4((uw_world-uw_CaptureRect.xy)/uw_CaptureRect.zw,0,1);\n'
    scene_textures = ('_CameraSortingLayerTexture', '_LocalReflectionMap', '_LocalUnderwaterSurfaceMap')
    # Keep the source's implicit derivatives and mip selection. Texture color
    # space is handled by each backend's texture storage, as in the demo.
    gl_body = function_calls(body, 'texture', lambda p: ('uwUnderwater(' if p[0] == '_CameraSortingLayerTexture' else 'uwDecodeScene(' if p[0] in scene_textures else '') + 'uwSample(' + ', '.join(p) + ')' + (', '+p[1]+')' if p[0] == '_CameraSortingLayerTexture' else ')' if p[0] in scene_textures else '')).replace('uwSample(', 'texture(')
    gl_body=re.sub(r'texture\(_ShapeLightTexture0, ([^,]+),',r'texture(_ShapeLightTexture0, (uw_world-uw_LightRect.xy)/uw_LightRect.zw,',gl_body)
    gl_body = gl_body.replace('gl_FrontFacing', 'true')
    if name != 'LightRays':
        gl_body = gl_body.replace('    return;', '    SV_TARGET0.rgb=uwEncodeScene(SV_TARGET0.rgb);\n    return;')
    gl += locals_ + '\n' + gl_body + '\n}\n'
    if name == 'WaterSurface':
        gl = gl.replace('void main() {', 'void main() {\nvec2 uw_world=uw_CaptureRect.xy+gl_FragCoord.xy/_ScaledScreenParams.xy*uw_CaptureRect.zw;')
        gl = gl.replace('in vec2 uw_world;', '')
    (WEB / (name + '.fragment.glsl')).write_text(gl, encoding='utf-8')

    hs = locals_ + '\n' + body
    hs = re.sub(r'\b(lowp|mediump|highp)\s+', '', hs)
    hs = function_calls(hs, 'texture', lambda p: ('uwUnderwater(' if p[0] == '_CameraSortingLayerTexture' else 'uwDecodeScene(' if p[0] in scene_textures else '') + f'{p[0]}.SampleBias(sampler{p[0]}, {p[1]}, {p[2] if len(p)>2 else "0.0"})' + (', '+p[1]+')' if p[0] == '_CameraSortingLayerTexture' else ')' if p[0] in scene_textures else ''))
    hs=re.sub(r'_ShapeLightTexture0.SampleBias\(sampler_ShapeLightTexture0, ([^,]+),',r'_ShapeLightTexture0.SampleBias(sampler_ShapeLightTexture0, (i.world-uw_LightRect.xy)/uw_LightRect.zw,',hs)
    hs = function_calls(hs, 'textureLod', lambda p: f'{p[0]}.SampleLevel(sampler{p[0]}, {p[1]}, {p[2]})')
    hs = function_calls(hs, 'texelFetch', lambda p: f'{p[0]}.Load(int3({p[1]}, {p[2]}))')
    for fn, op in [('lessThanEqual', '<='), ('greaterThanEqual', '>='), ('lessThan', '<'), ('greaterThan', '>'), ('equal', '=='), ('notEqual', '!=')]:
        hs = function_calls(hs, fn, lambda p, op=op: f'(({p[0]}) {op} ({p[1]}))')
    hs = function_calls(hs, 'op_not', lambda p: f'(~({p[0]}))')
    for a, b in {'fract':'frac','inversesqrt':'rsqrt','dFdx':'ddx','dFdy':'ddy','mix':'lerp','uintBitsToFloat':'asfloat','floatBitsToUint':'asuint'}.items():
        hs = re.sub(r'\b' + a + r'\b', b, hs)
    for prefix, out in [('ivec', 'int'), ('uvec', 'uint'), ('bvec', 'bool'), ('vec', 'float')]:
        for n in (2, 3, 4):
            hs = re.sub(r'\b' + prefix + str(n) + r'\b', out + str(n), hs)
    for typ in ('float','int','uint','bool'):
        for n in (2,3,4):
            # GLSL vecN(scalar) broadcasts; HLSL requires a cast for this form.
            hs = re.sub(r'\b' + typ + str(n) + r'\(([^(),]+)\)', lambda m,t=typ,n=n: f'(({t}{n})({m[1]}))', hs)
    hs = re.sub(r'(?m)^(float[234]?\s+u_\w+;)$', r'precise \1', hs)
    hs = hs.replace('gl_FragCoord', 'uw_FragCoord').replace('gl_FrontFacing', 'true')
    hs = re.sub(r'\breturn;', 'return max(SV_TARGET0,0);' if name == 'LightRays' else 'return float4(uwEncodeScene(SV_TARGET0.rgb), max(SV_TARGET0.a, 0.0));', hs)
    # Core.hlsl owns these globals; custom aliases allow fixed-time comparison.
    aliases = {'_Time':'uw_Time','_TimeParameters':'uw_TimeParameters','_ScaledScreenParams':'uw_ScaledScreenParams','_ProjectionParams':'uw_ProjectionParams','_GlobalMipBias':'uw_GlobalMipBias'}
    for a, b in aliases.items():
        hs = re.sub(r'\b' + a + r'\b', b, hs)
    declarations = []
    for key, value in uniforms.items():
        if key in aliases:
            continue
        if value['type'] == 'sampler2D':
            declarations.append(f'Texture2D<float4> {key}; SamplerState sampler{key};')
        else:
            typ = value['type'].replace('vec','float')
            declarations.append(f'{typ} {aliases.get(key,key)}{value["array"]};')
    properties = []
    for key, value in uniforms.items():
        if key.startswith('hlslcc') or key in aliases or value['array']:
            continue
        typ = value['type']
        properties.append(f'{key} ("{key}", ' + ('2D) = "black" {}' if typ == 'sampler2D' else 'Float) = 0' if typ == 'float' else 'Vector) = (0,0,0,0)'))
    properties += ['uw_Rays ("Light Rays", 2D) = "black" {}','uw_Terrain ("Terrain Mask", 2D) = "black" {}','uw_Bounds ("Water Bounds", Vector) = (0,0,1,1)','uw_CaptureRect ("Capture Rect", Vector) = (0,0,1,1)','uw_LightRect ("Light Rect", Vector) = (0,0,1,1)','uw_SurfaceDepth ("Surface Depth", Float) = 1','uw_Scale ("Level Scale", Float) = 8','uw_Clock ("Animation Clock", Float) = 0']
    is_surface = name == 'WaterSurface'
    # This host supplies object-local coordinates, so all matrix columns are
    # identity. Replace the full vector before swizzles, including the normal
    # transform's xyz accesses used by the surface program.
    for column, vector in enumerate(('float4(1,0,0,0)', 'float4(0,1,0,0)', 'float4(0,0,1,0)', 'float4(0,0,0,1)')):
        hs = hs.replace(f'hlslcc_mtx4x4unity_WorldToObject[{column}]', vector)
    declarations = [line for line in declarations if 'hlslcc_mtx4x4unity_WorldToObject' not in line]
    inputs_setup = ('float projected=clamp(i.uv.y,0,1)*294.121521/(294.121521+19.4);\nlocal.z=19.4*projected/max(1-projected,0.0001);\nfloat cameraX=(uw_CaptureRect.x+uw_CaptureRect.z*0.5-uw_Bounds.x-uw_Bounds.z*0.5)/uw_Scale;\nlocal.x=cameraX+((i.world.x-uw_Bounds.x-uw_Bounds.z*0.5)/uw_Scale-cameraX)*(1+local.z/19.4);\nfloat4 vs_INTERP0=float4(local.x/(uw_Bounds.z/uw_Scale*(1+0.8*local.z/294.121521))+0.5,local.z/294.121521,0,0);\nfloat4 vs_INTERP1=float4(local.x,local.z,0,0);' if is_surface else 'float4 vs_INTERP0=float4(i.uv.x,1-i.uv.y,0,0);\nfloat4 vs_INTERP1=float4(i.uv.x,-local.y,0,0);')
    hs = re.sub(r'\buw_(?:Time|TimeParameters|ScaledScreenParams|ProjectionParams|GlobalMipBias)\b', lambda m:m[0], hs)
    shader = f'''Shader "BpsimLevelEditor/Ultimate{name}"
{{
Properties {{ {chr(10).join(properties)} }}
SubShader {{
Tags {{ "Queue"="Transparent+80" "RenderPipeline"="UniversalPipeline" }}
Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha
Pass {{ Tags {{ "LightMode"="Universal2D" }}
HLSLPROGRAM
#pragma target 4.5
#pragma vertex Vert
#pragma fragment Frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
{chr(10).join(declarations)}
Texture2D<float4> uw_Terrain; SamplerState sampleruw_Terrain;
Texture2D<float4> uw_Rays; SamplerState sampleruw_Rays;
float4 uw_Bounds,uw_CaptureRect,uw_LightRect; float uw_SurfaceDepth,uw_Scale,uw_Clock;
struct Attributes {{ float3 positionOS:POSITION; float2 uv:TEXCOORD0; }};
struct Varyings {{ float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float2 world:TEXCOORD1; }};
float4 uwDecodeScene(float4 color) {{ return float4(lerp(color.rgb/12.92,pow(max((color.rgb+0.055)/1.055,0),2.4),step(0.04045,color.rgb)),color.a); }}
float3 uwEncodeScene(float3 color) {{ color=max(color,0); return lerp(color*12.92,1.055*pow(color,1.0/2.4)-0.055,step(0.0031308,color)); }}
float4 uwUnderwater(float4 color,float2 uv) {{ float4 rays=uw_Rays.SampleLevel(sampleruw_Rays,uv,0); return float4(uwDecodeScene(color).rgb*(1-rays.a)+rays.rgb,color.a); }}
Varyings Vert(Attributes a) {{ Varyings o; float3 w=TransformObjectToWorld(a.positionOS); o.positionCS=TransformWorldToHClip(w); o.world=w.xy; o.uv=a.uv; return o; }}
float4 Frag(Varyings i):SV_Target {{
float4 uw_Time, uw_TimeParameters, uw_ScaledScreenParams, uw_ProjectionParams; float2 uw_GlobalMipBias;
float2 screenUv=(i.world-uw_CaptureRect.xy)/uw_CaptureRect.zw;
clip(0.5-uw_Terrain.Sample(sampleruw_Terrain,screenUv).r);
float3 local=float3((i.world.x-uw_Bounds.x)/uw_Scale, (i.world.y-uw_Bounds.y)/uw_Scale, i.uv.y*uw_SurfaceDepth);
{'local.y=0;' if is_surface else ''}
{inputs_setup}
float4 vs_INTERP2=float4(1,1,1,1); float3 vs_INTERP4=local;
float4 vs_INTERP3=float4(screenUv,0,1);
uw_Time=float4(uw_Clock/20,uw_Clock,uw_Clock*2,uw_Clock*3);
uw_TimeParameters=float4(uw_Clock,sin(uw_Clock),cos(uw_Clock),0);
uw_ScaledScreenParams=float4(_ScreenParams.xy,0,0); uw_ProjectionParams=float4(1,0,0,0); uw_GlobalMipBias=0;
float4 uw_FragCoord=float4(screenUv*_ScreenParams.xy,0,1); float4 SV_TARGET0=0;
{hs}
}}
ENDHLSL
}}
}}
}}
'''
    if name == 'LightRays':
        shader = shader.replace('Blend SrcAlpha OneMinusSrcAlpha','Blend One OneMinusSrcAlpha')
        shader = shader.replace('float2 uv:TEXCOORD0; };','float2 uv:TEXCOORD0; float2 seed:TEXCOORD1; float3 local:TEXCOORD2; float4 color:COLOR; };',1)
        shader = shader.replace('float2 world:TEXCOORD1; };','float2 world:TEXCOORD1; float2 seed:TEXCOORD2; float3 local:TEXCOORD3; float4 color:COLOR; };',1)
        shader = shader.replace('o.uv=a.uv; return o;','o.uv=a.uv; o.seed=a.seed; o.local=a.local; o.color=a.color; return o;')
        shader = shader.replace(inputs_setup, 'float2 vs_TEXCOORD0=i.uv; float2 vs_TEXCOORD1=i.seed; float3 vs_TEXCOORD2=i.local; float4 vs_COLOR0=i.color;')
    if name == 'WaterSurface':
        shader = shader.replace('float2 uv:TEXCOORD0; };','float2 uv:TEXCOORD0; float2 local:TEXCOORD1; };',1)
        shader = shader.replace('float2 world:TEXCOORD1; };','noperspective float2 world:TEXCOORD1; float2 local:TEXCOORD2; };',1)
        shader = shader.replace('o.positionCS=TransformWorldToHClip(w);', '''float cameraX=(uw_CaptureRect.x+uw_CaptureRect.z*.5-uw_Bounds.x-uw_Bounds.z*.5)/uw_Scale;
float p=19.4/(19.4+a.local.y);
w.xy=float2(uw_Bounds.x+uw_Bounds.z*.5+(cameraX+(a.local.x-cameraX)*p)*uw_Scale,uw_Bounds.y+3.8*(1-p)*uw_Scale);
o.local=a.local; o.positionCS=TransformWorldToHClip(w); o.positionCS*=19.4+a.local.y;''')
        shader = shader.replace(inputs_setup, 'local=float3(i.local.x,0,i.local.y); float4 vs_INTERP0=float4(i.uv,0,0); float4 vs_INTERP1=float4(i.local,0,0);')
    (SHADERS / ('Ultimate' + name + '.shader')).write_text(shader, encoding='utf-8')
    return {'source_sha256':hashlib.sha256(source.encode()).hexdigest(),'uniforms':uniforms,'inputs':inputs}


if __name__ == '__main__':
    metadata = {name:generate(name) for name in ('WaterSurface','Underwater','LightRays')}
    from port_water_vertex import port_vertex
    port_vertex(metadata)
    (WEB / 'programs.json').write_text(json.dumps(metadata,indent=2),encoding='utf-8')
    print(json.dumps({key:len(value['uniforms']) for key,value in metadata.items()}))
