Shader "BpsimLevelEditor/UltimateLightRays"
{
Properties {
_RandomMovement ("_RandomMovement", Float) = 0
_RandomMovementSpeed ("_RandomMovementSpeed", Float) = 0
_RandomAngleTilt ("_RandomAngleTilt", Float) = 0
_RandomAngleTiltSpeed ("_RandomAngleTiltSpeed", Float) = 0
_LocalZBounds ("_LocalZBounds", Vector) = (0,0,0,0) _SplashHeightMultiplier ("_SplashHeightMultiplier", Float) = 0
_SplashEnabled ("_SplashEnabled", Float) = 0
_WakeFrequency ("_WakeFrequency", Float) = 0
_WakeTravelSpeed ("_WakeTravelSpeed", Float) = 0
_WakeDamping ("_WakeDamping", Float) = 0
_WakeFastCalculation ("_WakeFastCalculation", Float) = 0
_WaterInteractionsEnabled ("_WaterInteractionsEnabled", Float) = 0
_ImpactRippleDeformationEnabled ("_ImpactRippleDeformationEnabled", Float) = 0
_WakeRippleDeformationEnabled ("_WakeRippleDeformationEnabled", Float) = 0
_Color ("_Color", Vector) = (0,0,0,0)
_Opacity ("_Opacity", Float) = 0
_Brightness ("_Brightness", Float) = 0
_Softness ("_Softness", Float) = 0
_SurfaceFade ("_SurfaceFade", Float) = 0
_SourceFadeOffset ("_SourceFadeOffset", Float) = 0
_DepthFadePower ("_DepthFadePower", Float) = 0
_OpacityAnimationStrength ("_OpacityAnimationStrength", Float) = 0
_OpacityAnimationSpeed ("_OpacityAnimationSpeed", Float) = 0
_FlickerStrength ("_FlickerStrength", Float) = 0
_FlickerSpeed ("_FlickerSpeed", Float) = 0
_LocalXYBounds ("_LocalXYBounds", Vector) = (0,0,0,0)
_ClipToWaterBounds ("_ClipToWaterBounds", Float) = 0
_PreviewSolid ("_PreviewSolid", Float) = 0
_ClipTo2DWaterline ("_ClipTo2DWaterline", Float) = 0
_WaveAmplitude ("_WaveAmplitude", Float) = 0
_WaveSpeed ("_WaveSpeed", Float) = 0
_WaveFrequency ("_WaveFrequency", Float) = 0
_WaveMode ("_WaveMode", Float) = 0
_MultiplyBlend ("_MultiplyBlend", Float) = 0
_RippleCount ("_RippleCount", Float) = 0
_RippleImpactRadius ("_RippleImpactRadius", Float) = 0
_RippleExtraParamsTex ("_RippleExtraParamsTex", 2D) = "black" {}
_RippleCentersTex ("_RippleCentersTex", 2D) = "black" {}
_RippleParamsTex ("_RippleParamsTex", 2D) = "black" {}
uw_Rays ("Light Rays", 2D) = "black" {}
uw_Terrain ("Terrain Mask", 2D) = "black" {}
uw_Bounds ("Water Bounds", Vector) = (0,0,1,1)
uw_CaptureRect ("Capture Rect", Vector) = (0,0,1,1)
uw_LightRect ("Light Rect", Vector) = (0,0,1,1)
uw_SurfaceDepth ("Surface Depth", Float) = 1
uw_Scale ("Level Scale", Float) = 8
uw_Clock ("Animation Clock", Float) = 0 }
SubShader {
Tags { "Queue"="Transparent+80" "RenderPipeline"="UniversalPipeline" }
Cull Off ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
Pass { Tags { "LightMode"="Universal2D" }
HLSLPROGRAM
#pragma target 4.5
#pragma vertex Vert
#pragma fragment Frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
float _SplashHeightMultiplier;
float _SplashEnabled;
float _WakeFrequency;
float _WakeTravelSpeed;
float _WakeDamping;
float _WakeFastCalculation;
float _WaterInteractionsEnabled;
float _ImpactRippleDeformationEnabled;
float _WakeRippleDeformationEnabled;
float4 _Color;
float _Opacity;
float _Brightness;
float _Softness;
float _SurfaceFade;
float _SourceFadeOffset;
float _DepthFadePower;
float _OpacityAnimationStrength;
float _OpacityAnimationSpeed;
float _FlickerStrength;
float _FlickerSpeed;
float4 _LocalXYBounds;
float _ClipToWaterBounds;
float _PreviewSolid;
float _ClipTo2DWaterline;
float _WaveAmplitude;
float _WaveSpeed;
float _WaveFrequency;
float _WaveMode;
float _MultiplyBlend;
float _RippleCount;
float _RippleImpactRadius;
Texture2D<float4> _RippleExtraParamsTex; SamplerState sampler_RippleExtraParamsTex;
Texture2D<float4> _RippleCentersTex; SamplerState sampler_RippleCentersTex;
Texture2D<float4> _RippleParamsTex; SamplerState sampler_RippleParamsTex;
Texture2D<float4> uw_Terrain; SamplerState sampleruw_Terrain;
Texture2D<float4> uw_Rays; SamplerState sampleruw_Rays;
float4 uw_Bounds,uw_CaptureRect,uw_LightRect; float uw_SurfaceDepth,uw_Scale,uw_Clock;
float _RandomMovement;
float _RandomMovementSpeed;
float _RandomAngleTilt;
float _RandomAngleTiltSpeed;
float4 _LocalZBounds;
struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; float2 seed:TEXCOORD1; float2 extra:TEXCOORD2; float4 color:COLOR; };
struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; noperspective float2 world:TEXCOORD1; float2 seed:TEXCOORD2; float3 local:TEXCOORD3; float4 color:COLOR; };
float4 uwDecodeScene(float4 color) { return float4(lerp(color.rgb/12.92,pow(max((color.rgb+0.055)/1.055,0),2.4),step(0.04045,color.rgb)),color.a); }
float3 uwEncodeScene(float3 color) { color=max(color,0); return lerp(color*12.92,1.055*pow(color,1.0/2.4)-0.055,step(0.0031308,color)); }
float4 uwUnderwater(float4 color,float2 uv) { float4 rays=uw_Rays.SampleLevel(sampleruw_Rays,uv,0); return float4(uwDecodeScene(color).rgb*(1-rays.a)+rays.rgb,color.a); }
Varyings Vert(Attributes a){Varyings o;
float3 position=a.positionOS;float2 uv=a.uv,seed=a.seed,extra=a.extra;
float4 uw_Time=float4(uw_Clock/20,uw_Clock,uw_Clock*2,uw_Clock*3);
precise float4 u_xlat0;
precise float4 u_xlat1;
precise float u_xlat2;
precise float u_xlat3;
precise float u_xlat6;
precise float u_xlat9;

    u_xlat0.xy = seed.yx * float2(6.28318548, 6.28318548);
    u_xlat0.y = uw_Time.y * _RandomAngleTiltSpeed + u_xlat0.y;
    u_xlat0.x = uw_Time.y * _RandomMovementSpeed + u_xlat0.x;
    u_xlat0.xy = sin(u_xlat0.xy);
    u_xlat3 = u_xlat0.y * _RandomAngleTilt;
    u_xlat3 = u_xlat3 * 0.0174532924;
    u_xlat1.x = sin(u_xlat3);
    u_xlat2 = cos(u_xlat3);
    u_xlat3 = u_xlat1.x / u_xlat2;
    u_xlat3 = u_xlat3 * extra.y;
    u_xlat6 = (-uv.y) + 1.0;
    u_xlat6 = clamp(u_xlat6, 0.0, 1.0);
    u_xlat3 = u_xlat6 * u_xlat3;
    u_xlat0.x = u_xlat0.x * _RandomMovement + u_xlat3;
    u_xlat9 = uw_Time.y * _RandomMovementSpeed;
    u_xlat1.x = extra.x * 6.28318548;
    u_xlat9 = u_xlat9 * 0.730000019 + u_xlat1.x;
    u_xlat9 = sin(u_xlat9);
    u_xlat0.z = u_xlat9 * _RandomMovement;
    u_xlat0.y = 0.0;
    u_xlat0.xyz = u_xlat0.xyz + position.xyz;
u_xlat0.z=clamp(u_xlat0.z,_LocalZBounds.x,_LocalZBounds.y);
float d=19.4+u_xlat0.z,p=19.4/d;
float cx=(uw_CaptureRect.x+uw_CaptureRect.z*.5-uw_Bounds.x-uw_Bounds.z*.5)/uw_Scale;
float cy=(uw_CaptureRect.y+uw_CaptureRect.w*.5-uw_Bounds.y)/uw_Scale;
float2 projected=float2(uw_Bounds.x+uw_Bounds.z*.5+(cx+(u_xlat0.x-cx)*p)*uw_Scale,uw_Bounds.y+(cy+(u_xlat0.y-cy)*p)*uw_Scale);

float3 w=float3(projected,0);o.positionCS=TransformWorldToHClip(w)*d;o.world=projected;o.uv=uv;o.seed=seed;o.local=u_xlat0.xyz;o.color=a.color;return o;}
float4 Frag(Varyings i):SV_Target {
float4 uw_Time, uw_TimeParameters, uw_ScaledScreenParams, uw_ProjectionParams; float2 uw_GlobalMipBias;
float2 screenUv=(i.world-uw_CaptureRect.xy)/uw_CaptureRect.zw;
clip(0.5-uw_Terrain.Sample(sampleruw_Terrain,screenUv).r);
float3 local=float3((i.world.x-uw_Bounds.x)/uw_Scale, (i.world.y-uw_Bounds.y)/uw_Scale, i.uv.y*uw_SurfaceDepth);

float2 vs_TEXCOORD0=i.uv; float2 vs_TEXCOORD1=i.seed; float3 vs_TEXCOORD2=i.local; float4 vs_COLOR0=i.color;
float4 vs_INTERP2=float4(1,1,1,1); float3 vs_INTERP4=local;
float4 vs_INTERP3=float4(screenUv,0,1);
uw_Time=float4(uw_Clock/20,uw_Clock,uw_Clock*2,uw_Clock*3);
uw_TimeParameters=float4(uw_Clock,sin(uw_Clock),cos(uw_Clock),0);
uw_ScaledScreenParams=float4(_ScreenParams.xy,0,0); uw_ProjectionParams=float4(1,0,0,0); uw_GlobalMipBias=0;
float4 uw_FragCoord=float4(screenUv*_ScreenParams.xy,0,1); float4 SV_TARGET0=0;
precise float2 u_xlat0;
bool3 u_xlatb0;
precise float4 u_xlat1;
bool2 u_xlatb1;
precise float4 u_xlat2;
bool4 u_xlatb2;
precise float4 u_xlat3;
bool4 u_xlatb3;
precise float4 u_xlat4;
uint4 u_xlatu4;
precise float4 u_xlat5;
bool4 u_xlatb5;
precise float4 u_xlat6;
bool4 u_xlatb6;
precise float4 u_xlat7;
uint u_xlatu7;
bool u_xlatb7;
precise float3 u_xlat8;
precise float u_xlat9;
precise float3 u_xlat16_10;
precise float3 u_xlat11;
int u_xlati11;
bool u_xlatb11;
precise float u_xlat12;
precise float3 u_xlat13;
bool u_xlatb13;
precise float u_xlat14;
bool u_xlatb14;
precise float u_xlat17;
uint u_xlatu17;
bool u_xlatb17;
precise float u_xlat19;
uint u_xlatu19;
bool u_xlatb19;
precise float u_xlat23;
precise float2 u_xlat24;
bool u_xlatb24;
precise float2 u_xlat25;
int u_xlati25;
bool u_xlatb25;
precise float2 u_xlat27;
uint u_xlatu27;
bool u_xlatb27;
precise float u_xlat28;
precise float u_xlat29;
int u_xlati29;
precise float u_xlat33;
bool u_xlatb33;
precise float u_xlat34;
bool u_xlatb34;
precise float u_xlat35;
bool u_xlatb35;
precise float u_xlat36;
bool u_xlatb36;
precise float u_xlat38;
uint u_xlatu38;
bool u_xlatb38;
precise float u_xlat39;
bool u_xlatb39;
precise float u_xlat40;
int u_xlati40;
uint u_xlatu40;
int u_xlati41;

    u_xlatb0.xyz = ((float4(0.5, 0.5, 0.5, 0.0)) < (float4(_ClipToWaterBounds, _ClipTo2DWaterline, _PreviewSolid, _ClipToWaterBounds))).xyz;
    u_xlat1.xy = vs_TEXCOORD2.xy + (-_LocalXYBounds.xz);
    u_xlatb1.xy = ((u_xlat1.xyxx) < (float4(0.0, 0.0, 0.0, 0.0))).xy;
    u_xlatb1.x = u_xlatb0.x && u_xlatb1.x;
    u_xlatb1.y = u_xlatb0.x && u_xlatb1.y;
    if(u_xlatb1.x){discard;}
    u_xlat33 = (-vs_TEXCOORD2.x) + _LocalXYBounds.y;
    u_xlatb33 = u_xlat33<0.0;
    u_xlatb0.x = u_xlatb0.x && u_xlatb33;
    if(u_xlatb0.x){discard;}
    if(u_xlatb1.y){discard;}
    if(u_xlatb0.y){
        u_xlat0.x = max(abs(_WaveFrequency), 9.99999975e-05);
        u_xlat11.x = vs_TEXCOORD2.x * _WaveFrequency;
        u_xlat33 = uw_Time.y * _WaveSpeed;
        u_xlat1.xyz = ((float3)(u_xlat33)) * float3(0.172999993, 0.0710000023, 0.0370000005) + float3(1.71000004, 4.13000011, 0.469999999);
        u_xlat1.xyz = sin(u_xlat1.xyz);
        u_xlat33 = u_xlat1.x * 0.779999971;
        u_xlat33 = uw_Time.y * _WaveSpeed + u_xlat33;
        u_xlat33 = u_xlat1.y * 0.479999989 + u_xlat33;
        u_xlat33 = u_xlat1.z * 0.300000012 + u_xlat33;
        u_xlat1.x = vs_TEXCOORD2.x * _WaveFrequency + u_xlat33;
        u_xlat12 = u_xlat11.x * 0.430000007;
        u_xlat2 = ((float4)(u_xlat33)) * float4(0.629999995, 0.310000002, 0.319999993, 0.230000004);
        u_xlat23 = u_xlat11.x * 1.87 + (-u_xlat2.x);
        u_xlat1.z = u_xlat23 + 1.37;
        u_xlat1.xz = sin(u_xlat1.xz);
        u_xlat11.x = u_xlat11.x * 0.529999971 + u_xlat2.y;
        u_xlat11.x = u_xlat11.x + 4.11999989;
        u_xlat11.x = sin(u_xlat11.x);
        u_xlat1.xw = u_xlat1.xx * float2(0.680000007, 0.239999995);
        u_xlat1.x = u_xlat1.z * 0.219999999 + u_xlat1.x;
        u_xlat1.x = u_xlat11.x * 0.100000001 + u_xlat1.x;
        u_xlat1.x = u_xlat1.x * _WaveAmplitude;
        u_xlat23 = u_xlat0.x * vs_TEXCOORD2.x;
        u_xlat0.x = vs_TEXCOORD2.x * u_xlat0.x + u_xlat2.z;
        u_xlat2.x = sin(u_xlat2.w);
        u_xlat2.x = u_xlat2.x * 0.709999979;
        u_xlat3.x = floor(u_xlat0.x);
        u_xlat3.y = floor(u_xlat2.x);
        u_xlat4.x = frac(u_xlat0.x);
        u_xlat4.y = frac(u_xlat2.x);
        u_xlat2 = u_xlat3.xyxy * float4(289.0, 289.0, 289.0, 289.0);
        u_xlatb2 = ((u_xlat2) >= ((-u_xlat2.zwzw)));
        u_xlat2.x = (u_xlatb2.x) ? float(289.0) : float(-289.0);
        u_xlat2.y = (u_xlatb2.y) ? float(289.0) : float(-289.0);
        u_xlat2.z = (u_xlatb2.z) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat2.w = (u_xlatb2.w) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat24.xy = u_xlat2.zw * u_xlat3.xy;
        u_xlat24.xy = frac(u_xlat24.xy);
        u_xlat2.xy = u_xlat24.xy * u_xlat2.xy;
        u_xlat0.x = u_xlat2.x * 34.0 + 1.0;
        u_xlat0.x = u_xlat2.x * u_xlat0.x;
        u_xlat2.x = u_xlat0.x * 289.0;
        u_xlatb2.x = u_xlat2.x>=(-u_xlat2.x);
        u_xlat2.xz = (u_xlatb2.x) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat0.x = u_xlat0.x * u_xlat2.z;
        u_xlat0.x = frac(u_xlat0.x);
        u_xlat0.x = u_xlat2.x * u_xlat0.x + u_xlat2.y;
        u_xlat2.x = u_xlat0.x * 34.0 + 1.0;
        u_xlat0.x = u_xlat0.x * u_xlat2.x;
        u_xlat2.x = u_xlat0.x * 289.0;
        u_xlatb2.x = u_xlat2.x>=(-u_xlat2.x);
        u_xlat2.xy = (u_xlatb2.x) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat0.x = u_xlat0.x * u_xlat2.y;
        u_xlat0.x = frac(u_xlat0.x);
        u_xlat0.x = u_xlat0.x * u_xlat2.x;
        u_xlat0.x = u_xlat0.x * 0.024390243;
        u_xlat0.x = frac(u_xlat0.x);
        u_xlat2.xy = u_xlat0.xx * float2(2.0, 2.0) + float2(-1.0, -0.5);
        u_xlat0.x = floor(u_xlat2.y);
        u_xlat0.x = (-u_xlat0.x) + u_xlat2.x;
        u_xlat2 = u_xlat3.xyxy + float4(0.0, 1.0, 1.0, 0.0);
        u_xlat5 = u_xlat2 * float4(289.0, 289.0, 289.0, 289.0);
        u_xlatb5 = ((u_xlat5) >= ((-u_xlat5)));
        u_xlat6.x = (u_xlatb5.x) ? float(289.0) : float(-289.0);
        u_xlat6.y = (u_xlatb5.y) ? float(289.0) : float(-289.0);
        u_xlat6.z = (u_xlatb5.x) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat6.w = (u_xlatb5.y) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat2.xy = u_xlat2.xy * u_xlat6.zw;
        u_xlat2.xy = frac(u_xlat2.xy);
        u_xlat2.xy = u_xlat2.xy * u_xlat6.xy;
        u_xlat25.x = u_xlat2.x * 34.0 + 1.0;
        u_xlat2.x = u_xlat2.x * u_xlat25.x;
        u_xlat25.x = u_xlat2.x * 289.0;
        u_xlatb25 = u_xlat25.x>=(-u_xlat25.x);
        u_xlat25.xy = (bool(u_xlatb25)) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat2.x = u_xlat2.x * u_xlat25.y;
        u_xlat2.x = frac(u_xlat2.x);
        u_xlat2.x = u_xlat25.x * u_xlat2.x + u_xlat2.y;
        u_xlat13.x = u_xlat2.x * 34.0 + 1.0;
        u_xlat2.x = u_xlat2.x * u_xlat13.x;
        u_xlat13.x = u_xlat2.x * 289.0;
        u_xlatb13 = u_xlat13.x>=(-u_xlat13.x);
        u_xlat25.xy = (bool(u_xlatb13)) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat2.x = u_xlat2.x * u_xlat25.y;
        u_xlat2.x = frac(u_xlat2.x);
        u_xlat2.x = u_xlat2.x * u_xlat25.x;
        u_xlat2.x = u_xlat2.x * 0.024390243;
        u_xlat2.x = frac(u_xlat2.x);
        u_xlat2.xy = u_xlat2.xx * float2(2.0, 2.0) + float2(-1.0, -0.5);
        u_xlat13.x = floor(u_xlat2.y);
        u_xlat2.x = (-u_xlat13.x) + u_xlat2.x;
        u_xlat5.x = (u_xlatb5.z) ? float(289.0) : float(-289.0);
        u_xlat5.y = (u_xlatb5.w) ? float(289.0) : float(-289.0);
        u_xlat5.z = (u_xlatb5.z) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat5.w = (u_xlatb5.w) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat13.xy = u_xlat2.zw * u_xlat5.zw;
        u_xlat13.xy = frac(u_xlat13.xy);
        u_xlat13.xy = u_xlat13.xy * u_xlat5.xy;
        u_xlat35 = u_xlat13.x * 34.0 + 1.0;
        u_xlat13.x = u_xlat13.x * u_xlat35;
        u_xlat35 = u_xlat13.x * 289.0;
        u_xlatb35 = u_xlat35>=(-u_xlat35);
        u_xlat25.xy = (bool(u_xlatb35)) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat13.x = u_xlat13.x * u_xlat25.y;
        u_xlat13.x = frac(u_xlat13.x);
        u_xlat13.x = u_xlat25.x * u_xlat13.x + u_xlat13.y;
        u_xlat24.x = u_xlat13.x * 34.0 + 1.0;
        u_xlat13.x = u_xlat13.x * u_xlat24.x;
        u_xlat24.x = u_xlat13.x * 289.0;
        u_xlatb24 = u_xlat24.x>=(-u_xlat24.x);
        u_xlat24.xy = (bool(u_xlatb24)) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat13.x = u_xlat24.y * u_xlat13.x;
        u_xlat13.x = frac(u_xlat13.x);
        u_xlat13.x = u_xlat13.x * u_xlat24.x;
        u_xlat13.x = u_xlat13.x * 0.024390243;
        u_xlat13.x = frac(u_xlat13.x);
        u_xlat13.xy = u_xlat13.xx * float2(2.0, 2.0) + float2(-1.0, -0.5);
        u_xlat24.x = floor(u_xlat13.y);
        u_xlat13.x = (-u_xlat24.x) + u_xlat13.x;
        u_xlat24.xy = u_xlat3.xy + float2(1.0, 1.0);
        u_xlat3 = u_xlat24.xyxy * float4(289.0, 289.0, 289.0, 289.0);
        u_xlatb3 = ((u_xlat3) >= ((-u_xlat3.zwzw)));
        u_xlat3.x = (u_xlatb3.x) ? float(289.0) : float(-289.0);
        u_xlat3.y = (u_xlatb3.y) ? float(289.0) : float(-289.0);
        u_xlat3.z = (u_xlatb3.z) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat3.w = (u_xlatb3.w) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat24.xy = u_xlat24.xy * u_xlat3.zw;
        u_xlat24.xy = frac(u_xlat24.xy);
        u_xlat24.xy = u_xlat24.xy * u_xlat3.xy;
        u_xlat3.x = u_xlat24.x * 34.0 + 1.0;
        u_xlat24.x = u_xlat24.x * u_xlat3.x;
        u_xlat3.x = u_xlat24.x * 289.0;
        u_xlatb3.x = u_xlat3.x>=(-u_xlat3.x);
        u_xlat3.xy = (u_xlatb3.x) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat24.x = u_xlat24.x * u_xlat3.y;
        u_xlat24.x = frac(u_xlat24.x);
        u_xlat24.x = u_xlat3.x * u_xlat24.x + u_xlat24.y;
        u_xlat35 = u_xlat24.x * 34.0 + 1.0;
        u_xlat24.x = u_xlat24.x * u_xlat35;
        u_xlat35 = u_xlat24.x * 289.0;
        u_xlatb35 = u_xlat35>=(-u_xlat35);
        u_xlat3.xy = (bool(u_xlatb35)) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat24.x = u_xlat24.x * u_xlat3.y;
        u_xlat24.x = frac(u_xlat24.x);
        u_xlat24.x = u_xlat24.x * u_xlat3.x;
        u_xlat24.x = u_xlat24.x * 0.024390243;
        u_xlat24.x = frac(u_xlat24.x);
        u_xlat24.xy = u_xlat24.xx * float2(2.0, 2.0) + float2(-1.0, -0.5);
        u_xlat35 = floor(u_xlat24.y);
        u_xlat24.x = (-u_xlat35) + u_xlat24.x;
        u_xlat3.xy = u_xlat4.xy * u_xlat4.xy;
        u_xlat3.xy = u_xlat4.xy * u_xlat3.xy;
        u_xlat25.xy = u_xlat4.xy * float2(6.0, 6.0) + float2(-15.0, -15.0);
        u_xlat25.xy = u_xlat4.xy * u_xlat25.xy + float2(10.0, 10.0);
        u_xlat3.xy = u_xlat25.xy * u_xlat3.xy;
        u_xlat2.x = (-u_xlat0.x) + u_xlat2.x;
        u_xlat0.x = u_xlat3.y * u_xlat2.x + u_xlat0.x;
        u_xlat2.x = (-u_xlat13.x) + u_xlat24.x;
        u_xlat2.x = u_xlat3.y * u_xlat2.x + u_xlat13.x;
        u_xlat2.x = (-u_xlat0.x) + u_xlat2.x;
        u_xlat0.x = u_xlat3.x * u_xlat2.x + u_xlat0.x;
        u_xlat0.x = u_xlat0.x + 0.5;
        u_xlat2.xy = ((float2)(u_xlat33)) * float2(0.270000011, 0.189999998);
        u_xlat2.x = u_xlat23 * 1.90999997 + (-u_xlat2.x);
        u_xlat2.zw = ((float2)(u_xlat33)) * float2(0.409999996, -0.529999971) + float2(7.30999994, 13.1700001);
        u_xlat3.xyw = floor(u_xlat2.xzw);
        u_xlat4.xyw = frac(u_xlat2.xzw);
        u_xlat5 = u_xlat3.xyxy * float4(289.0, 289.0, 289.0, 289.0);
        u_xlatb5 = ((u_xlat5) >= ((-u_xlat5.zwzw)));
        u_xlat5.x = (u_xlatb5.x) ? float(289.0) : float(-289.0);
        u_xlat5.y = (u_xlatb5.y) ? float(289.0) : float(-289.0);
        u_xlat5.z = (u_xlatb5.z) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat5.w = (u_xlatb5.w) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat2.xz = u_xlat3.xy * u_xlat5.zw;
        u_xlat2.xz = frac(u_xlat2.xz);
        u_xlat2.xz = u_xlat2.xz * u_xlat5.xy;
        u_xlat35 = u_xlat2.x * 34.0 + 1.0;
        u_xlat2.x = u_xlat2.x * u_xlat35;
        u_xlat35 = u_xlat2.x * 289.0;
        u_xlatb35 = u_xlat35>=(-u_xlat35);
        u_xlat5.xy = (bool(u_xlatb35)) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat2.x = u_xlat2.x * u_xlat5.y;
        u_xlat2.x = frac(u_xlat2.x);
        u_xlat2.x = u_xlat5.x * u_xlat2.x + u_xlat2.z;
        u_xlat24.x = u_xlat2.x * 34.0 + 1.0;
        u_xlat2.x = u_xlat2.x * u_xlat24.x;
        u_xlat24.x = u_xlat2.x * 289.0;
        u_xlatb24 = u_xlat24.x>=(-u_xlat24.x);
        u_xlat24.xy = (bool(u_xlatb24)) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat2.x = u_xlat24.y * u_xlat2.x;
        u_xlat2.x = frac(u_xlat2.x);
        u_xlat2.x = u_xlat2.x * u_xlat24.x;
        u_xlat2.x = u_xlat2.x * 0.024390243;
        u_xlat2.x = frac(u_xlat2.x);
        u_xlat2.xz = u_xlat2.xx * float2(2.0, 2.0) + float2(-1.0, -0.5);
        u_xlat24.x = floor(u_xlat2.z);
        u_xlat2.x = (-u_xlat24.x) + u_xlat2.x;
        u_xlat5 = u_xlat3.xyxy + float4(0.0, 1.0, 1.0, 0.0);
        u_xlat6 = u_xlat5 * float4(289.0, 289.0, 289.0, 289.0);
        u_xlatb6 = ((u_xlat6) >= ((-u_xlat6)));
        u_xlat7.x = (u_xlatb6.x) ? float(289.0) : float(-289.0);
        u_xlat7.y = (u_xlatb6.y) ? float(289.0) : float(-289.0);
        u_xlat7.z = (u_xlatb6.x) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat7.w = (u_xlatb6.y) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat24.xy = u_xlat5.xy * u_xlat7.zw;
        u_xlat24.xy = frac(u_xlat24.xy);
        u_xlat24.xy = u_xlat24.xy * u_xlat7.xy;
        u_xlat5.x = u_xlat24.x * 34.0 + 1.0;
        u_xlat24.x = u_xlat24.x * u_xlat5.x;
        u_xlat5.x = u_xlat24.x * 289.0;
        u_xlatb5.x = u_xlat5.x>=(-u_xlat5.x);
        u_xlat5.xy = (u_xlatb5.x) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat24.x = u_xlat24.x * u_xlat5.y;
        u_xlat24.x = frac(u_xlat24.x);
        u_xlat24.x = u_xlat5.x * u_xlat24.x + u_xlat24.y;
        u_xlat35 = u_xlat24.x * 34.0 + 1.0;
        u_xlat24.x = u_xlat24.x * u_xlat35;
        u_xlat35 = u_xlat24.x * 289.0;
        u_xlatb35 = u_xlat35>=(-u_xlat35);
        u_xlat5.xy = (bool(u_xlatb35)) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat24.x = u_xlat24.x * u_xlat5.y;
        u_xlat24.x = frac(u_xlat24.x);
        u_xlat24.x = u_xlat24.x * u_xlat5.x;
        u_xlat24.x = u_xlat24.x * 0.024390243;
        u_xlat24.x = frac(u_xlat24.x);
        u_xlat24.xy = u_xlat24.xx * float2(2.0, 2.0) + float2(-1.0, -0.5);
        u_xlat35 = floor(u_xlat24.y);
        u_xlat24.x = (-u_xlat35) + u_xlat24.x;
        u_xlat6.x = (u_xlatb6.z) ? float(289.0) : float(-289.0);
        u_xlat6.y = (u_xlatb6.w) ? float(289.0) : float(-289.0);
        u_xlat6.z = (u_xlatb6.z) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat6.w = (u_xlatb6.w) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat5.xy = u_xlat5.zw * u_xlat6.zw;
        u_xlat5.xy = frac(u_xlat5.xy);
        u_xlat5.xy = u_xlat5.xy * u_xlat6.xy;
        u_xlat35 = u_xlat5.x * 34.0 + 1.0;
        u_xlat35 = u_xlat5.x * u_xlat35;
        u_xlat5.x = u_xlat35 * 289.0;
        u_xlatb5.x = u_xlat5.x>=(-u_xlat5.x);
        u_xlat5.xz = (u_xlatb5.x) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat35 = u_xlat35 * u_xlat5.z;
        u_xlat35 = frac(u_xlat35);
        u_xlat35 = u_xlat5.x * u_xlat35 + u_xlat5.y;
        u_xlat5.x = u_xlat35 * 34.0 + 1.0;
        u_xlat35 = u_xlat35 * u_xlat5.x;
        u_xlat5.x = u_xlat35 * 289.0;
        u_xlatb5.x = u_xlat5.x>=(-u_xlat5.x);
        u_xlat5.xy = (u_xlatb5.x) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat35 = u_xlat35 * u_xlat5.y;
        u_xlat35 = frac(u_xlat35);
        u_xlat35 = u_xlat35 * u_xlat5.x;
        u_xlat35 = u_xlat35 * 0.024390243;
        u_xlat35 = frac(u_xlat35);
        u_xlat5.xy = ((float2)(u_xlat35)) * float2(2.0, 2.0) + float2(-1.0, -0.5);
        u_xlat35 = floor(u_xlat5.y);
        u_xlat35 = (-u_xlat35) + u_xlat5.x;
        u_xlat3.xy = u_xlat3.xy + float2(1.0, 1.0);
        u_xlat5 = u_xlat3.xyxy * float4(289.0, 289.0, 289.0, 289.0);
        u_xlatb5 = ((u_xlat5) >= ((-u_xlat5.zwzw)));
        u_xlat5.x = (u_xlatb5.x) ? float(289.0) : float(-289.0);
        u_xlat5.y = (u_xlatb5.y) ? float(289.0) : float(-289.0);
        u_xlat5.z = (u_xlatb5.z) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat5.w = (u_xlatb5.w) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat3.xy = u_xlat3.xy * u_xlat5.zw;
        u_xlat3.xy = frac(u_xlat3.xy);
        u_xlat3.xy = u_xlat3.xy * u_xlat5.xy;
        u_xlat5.x = u_xlat3.x * 34.0 + 1.0;
        u_xlat3.x = u_xlat3.x * u_xlat5.x;
        u_xlat5.x = u_xlat3.x * 289.0;
        u_xlatb5.x = u_xlat5.x>=(-u_xlat5.x);
        u_xlat5.xy = (u_xlatb5.x) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat3.x = u_xlat3.x * u_xlat5.y;
        u_xlat3.x = frac(u_xlat3.x);
        u_xlat3.x = u_xlat5.x * u_xlat3.x + u_xlat3.y;
        u_xlat14 = u_xlat3.x * 34.0 + 1.0;
        u_xlat3.x = u_xlat3.x * u_xlat14;
        u_xlat14 = u_xlat3.x * 289.0;
        u_xlatb14 = u_xlat14>=(-u_xlat14);
        u_xlat5.xy = (bool(u_xlatb14)) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat3.x = u_xlat3.x * u_xlat5.y;
        u_xlat3.x = frac(u_xlat3.x);
        u_xlat3.x = u_xlat3.x * u_xlat5.x;
        u_xlat3.x = u_xlat3.x * 0.024390243;
        u_xlat3.x = frac(u_xlat3.x);
        u_xlat3.xy = u_xlat3.xx * float2(2.0, 2.0) + float2(-1.0, -0.5);
        u_xlat14 = floor(u_xlat3.y);
        u_xlat3.x = (-u_xlat14) + u_xlat3.x;
        u_xlat5.xy = u_xlat4.xy * u_xlat4.xy;
        u_xlat5.xy = u_xlat4.xy * u_xlat5.xy;
        u_xlat27.xy = u_xlat4.xy * float2(6.0, 6.0) + float2(-15.0, -15.0);
        u_xlat4.xy = u_xlat4.xy * u_xlat27.xy + float2(10.0, 10.0);
        u_xlat4.xy = u_xlat4.xy * u_xlat5.xy;
        u_xlat24.x = (-u_xlat2.x) + u_xlat24.x;
        u_xlat2.x = u_xlat4.y * u_xlat24.x + u_xlat2.x;
        u_xlat24.x = (-u_xlat35) + u_xlat3.x;
        u_xlat24.x = u_xlat4.y * u_xlat24.x + u_xlat35;
        u_xlat24.x = (-u_xlat2.x) + u_xlat24.x;
        u_xlat2.x = u_xlat4.x * u_xlat24.x + u_xlat2.x;
        u_xlat2.x = u_xlat2.x + 0.5;
        u_xlat23 = u_xlat23 * 3.73000002 + u_xlat2.y;
        u_xlat3.z = floor(u_xlat23);
        u_xlat4.z = frac(u_xlat23);
        u_xlat5 = u_xlat3.zwzw * float4(289.0, 289.0, 289.0, 289.0);
        u_xlatb5 = ((u_xlat5) >= ((-u_xlat5.zwzw)));
        u_xlat5.x = (u_xlatb5.x) ? float(289.0) : float(-289.0);
        u_xlat5.y = (u_xlatb5.y) ? float(289.0) : float(-289.0);
        u_xlat5.z = (u_xlatb5.z) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat5.w = (u_xlatb5.w) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat13.xy = u_xlat3.zw * u_xlat5.zw;
        u_xlat13.xy = frac(u_xlat13.xy);
        u_xlat13.xy = u_xlat13.xy * u_xlat5.xy;
        u_xlat23 = u_xlat13.x * 34.0 + 1.0;
        u_xlat23 = u_xlat13.x * u_xlat23;
        u_xlat13.x = u_xlat23 * 289.0;
        u_xlatb13 = u_xlat13.x>=(-u_xlat13.x);
        u_xlat13.xz = (bool(u_xlatb13)) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat23 = u_xlat23 * u_xlat13.z;
        u_xlat23 = frac(u_xlat23);
        u_xlat23 = u_xlat13.x * u_xlat23 + u_xlat13.y;
        u_xlat13.x = u_xlat23 * 34.0 + 1.0;
        u_xlat23 = u_xlat23 * u_xlat13.x;
        u_xlat13.x = u_xlat23 * 289.0;
        u_xlatb13 = u_xlat13.x>=(-u_xlat13.x);
        u_xlat13.xy = (bool(u_xlatb13)) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat23 = u_xlat23 * u_xlat13.y;
        u_xlat23 = frac(u_xlat23);
        u_xlat23 = u_xlat23 * u_xlat13.x;
        u_xlat23 = u_xlat23 * 0.024390243;
        u_xlat23 = frac(u_xlat23);
        u_xlat13.xy = ((float2)(u_xlat23)) * float2(2.0, 2.0) + float2(-1.0, -0.5);
        u_xlat23 = floor(u_xlat13.y);
        u_xlat23 = (-u_xlat23) + u_xlat13.x;
        u_xlat5 = u_xlat3.zwzw + float4(0.0, 1.0, 1.0, 0.0);
        u_xlat6 = u_xlat5 * float4(289.0, 289.0, 289.0, 289.0);
        u_xlatb6 = ((u_xlat6) >= ((-u_xlat6)));
        u_xlat7.x = (u_xlatb6.x) ? float(289.0) : float(-289.0);
        u_xlat7.y = (u_xlatb6.y) ? float(289.0) : float(-289.0);
        u_xlat7.z = (u_xlatb6.x) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat7.w = (u_xlatb6.y) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat13.xy = u_xlat5.xy * u_xlat7.zw;
        u_xlat13.xy = frac(u_xlat13.xy);
        u_xlat13.xy = u_xlat13.xy * u_xlat7.xy;
        u_xlat35 = u_xlat13.x * 34.0 + 1.0;
        u_xlat13.x = u_xlat13.x * u_xlat35;
        u_xlat35 = u_xlat13.x * 289.0;
        u_xlatb35 = u_xlat35>=(-u_xlat35);
        u_xlat3.xy = (bool(u_xlatb35)) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat13.x = u_xlat13.x * u_xlat3.y;
        u_xlat13.x = frac(u_xlat13.x);
        u_xlat13.x = u_xlat3.x * u_xlat13.x + u_xlat13.y;
        u_xlat24.x = u_xlat13.x * 34.0 + 1.0;
        u_xlat13.x = u_xlat13.x * u_xlat24.x;
        u_xlat24.x = u_xlat13.x * 289.0;
        u_xlatb24 = u_xlat24.x>=(-u_xlat24.x);
        u_xlat24.xy = (bool(u_xlatb24)) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat13.x = u_xlat24.y * u_xlat13.x;
        u_xlat13.x = frac(u_xlat13.x);
        u_xlat13.x = u_xlat13.x * u_xlat24.x;
        u_xlat13.x = u_xlat13.x * 0.024390243;
        u_xlat13.x = frac(u_xlat13.x);
        u_xlat13.xy = u_xlat13.xx * float2(2.0, 2.0) + float2(-1.0, -0.5);
        u_xlat24.x = floor(u_xlat13.y);
        u_xlat13.x = (-u_xlat24.x) + u_xlat13.x;
        u_xlat6.x = (u_xlatb6.z) ? float(289.0) : float(-289.0);
        u_xlat6.y = (u_xlatb6.w) ? float(289.0) : float(-289.0);
        u_xlat6.z = (u_xlatb6.z) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat6.w = (u_xlatb6.w) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat24.xy = u_xlat5.zw * u_xlat6.zw;
        u_xlat24.xy = frac(u_xlat24.xy);
        u_xlat24.xy = u_xlat24.xy * u_xlat6.xy;
        u_xlat3.x = u_xlat24.x * 34.0 + 1.0;
        u_xlat24.x = u_xlat24.x * u_xlat3.x;
        u_xlat3.x = u_xlat24.x * 289.0;
        u_xlatb3.x = u_xlat3.x>=(-u_xlat3.x);
        u_xlat3.xy = (u_xlatb3.x) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat24.x = u_xlat24.x * u_xlat3.y;
        u_xlat24.x = frac(u_xlat24.x);
        u_xlat24.x = u_xlat3.x * u_xlat24.x + u_xlat24.y;
        u_xlat35 = u_xlat24.x * 34.0 + 1.0;
        u_xlat24.x = u_xlat24.x * u_xlat35;
        u_xlat35 = u_xlat24.x * 289.0;
        u_xlatb35 = u_xlat35>=(-u_xlat35);
        u_xlat3.xy = (bool(u_xlatb35)) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat24.x = u_xlat24.x * u_xlat3.y;
        u_xlat24.x = frac(u_xlat24.x);
        u_xlat24.x = u_xlat24.x * u_xlat3.x;
        u_xlat24.x = u_xlat24.x * 0.024390243;
        u_xlat24.x = frac(u_xlat24.x);
        u_xlat24.xy = u_xlat24.xx * float2(2.0, 2.0) + float2(-1.0, -0.5);
        u_xlat35 = floor(u_xlat24.y);
        u_xlat24.x = (-u_xlat35) + u_xlat24.x;
        u_xlat3.xy = u_xlat3.zw + float2(1.0, 1.0);
        u_xlat5 = u_xlat3.xyxy * float4(289.0, 289.0, 289.0, 289.0);
        u_xlatb5 = ((u_xlat5) >= ((-u_xlat5.zwzw)));
        u_xlat5.x = (u_xlatb5.x) ? float(289.0) : float(-289.0);
        u_xlat5.y = (u_xlatb5.y) ? float(289.0) : float(-289.0);
        u_xlat5.z = (u_xlatb5.z) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat5.w = (u_xlatb5.w) ? float(0.00346020772) : float(-0.00346020772);
        u_xlat3.xy = u_xlat3.xy * u_xlat5.zw;
        u_xlat3.xy = frac(u_xlat3.xy);
        u_xlat3.xy = u_xlat3.xy * u_xlat5.xy;
        u_xlat35 = u_xlat3.x * 34.0 + 1.0;
        u_xlat35 = u_xlat3.x * u_xlat35;
        u_xlat3.x = u_xlat35 * 289.0;
        u_xlatb3.x = u_xlat3.x>=(-u_xlat3.x);
        u_xlat3.xz = (u_xlatb3.x) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat35 = u_xlat35 * u_xlat3.z;
        u_xlat35 = frac(u_xlat35);
        u_xlat35 = u_xlat3.x * u_xlat35 + u_xlat3.y;
        u_xlat3.x = u_xlat35 * 34.0 + 1.0;
        u_xlat35 = u_xlat35 * u_xlat3.x;
        u_xlat3.x = u_xlat35 * 289.0;
        u_xlatb3.x = u_xlat3.x>=(-u_xlat3.x);
        u_xlat3.xy = (u_xlatb3.x) ? float2(289.0, 0.00346020772) : float2(-289.0, -0.00346020772);
        u_xlat35 = u_xlat35 * u_xlat3.y;
        u_xlat35 = frac(u_xlat35);
        u_xlat35 = u_xlat35 * u_xlat3.x;
        u_xlat35 = u_xlat35 * 0.024390243;
        u_xlat35 = frac(u_xlat35);
        u_xlat3.xy = ((float2)(u_xlat35)) * float2(2.0, 2.0) + float2(-1.0, -0.5);
        u_xlat35 = floor(u_xlat3.y);
        u_xlat35 = (-u_xlat35) + u_xlat3.x;
        u_xlat3.xy = u_xlat4.zw * u_xlat4.zw;
        u_xlat3.xy = u_xlat4.zw * u_xlat3.xy;
        u_xlat25.xy = u_xlat4.zw * float2(6.0, 6.0) + float2(-15.0, -15.0);
        u_xlat25.xy = u_xlat4.zw * u_xlat25.xy + float2(10.0, 10.0);
        u_xlat3.xy = u_xlat25.xy * u_xlat3.xy;
        u_xlat13.x = (-u_xlat23) + u_xlat13.x;
        u_xlat23 = u_xlat3.y * u_xlat13.x + u_xlat23;
        u_xlat13.x = (-u_xlat24.x) + u_xlat35;
        u_xlat13.x = u_xlat3.y * u_xlat13.x + u_xlat24.x;
        u_xlat13.x = (-u_xlat23) + u_xlat13.x;
        u_xlat23 = u_xlat3.x * u_xlat13.x + u_xlat23;
        u_xlat23 = u_xlat23 + 0.5;
        u_xlat0.x = u_xlat0.x * 2.0 + -1.0;
        u_xlat2.x = u_xlat2.x * 2.0 + -1.0;
        u_xlat23 = u_xlat23 * 2.0 + -1.0;
        u_xlat33 = u_xlat33 * 0.469999999 + u_xlat12;
        u_xlat12 = u_xlat0.x * 0.569999993;
        u_xlat0.x = u_xlat0.x * 1.64999998 + u_xlat33;
        u_xlat0.x = sin(u_xlat0.x);
        u_xlat33 = u_xlat2.x * 0.25 + u_xlat12;
        u_xlat33 = u_xlat23 * 0.100000001 + u_xlat33;
        u_xlat0.x = u_xlat0.x * 0.159999996 + u_xlat33;
        u_xlat11.x = u_xlat11.x * 0.0799999982 + u_xlat1.w;
        u_xlat0.x = u_xlat0.x * 0.680000007 + u_xlat11.x;
        u_xlat11.x = _WaveMode;
        u_xlat11.x = clamp(u_xlat11.x, 0.0, 1.0);
        u_xlat0.x = u_xlat0.x * _WaveAmplitude + (-u_xlat1.x);
        u_xlat0.x = u_xlat11.x * u_xlat0.x + u_xlat1.x;
        u_xlati11 = int(_RippleCount);
        u_xlatb33 = 0>=u_xlati11;
        u_xlatb1.x = 0.0>=_WaterInteractionsEnabled;
        u_xlatb33 = u_xlatb33 || u_xlatb1.x;
        if(!u_xlatb33){
            u_xlat1.xyz = float3(_WakeRippleDeformationEnabled, _ImpactRippleDeformationEnabled, _WaterInteractionsEnabled);
            u_xlat1.xyz = clamp(u_xlat1.xyz, 0.0, 1.0);
            u_xlat33 = _RippleImpactRadius + _RippleImpactRadius;
            u_xlat33 = max(u_xlat33, 0.0500000007);
            u_xlat2.xyz = max(float3(_WakeFrequency, _WakeTravelSpeed, _WakeDamping), float3(0.00100000005, 0.00100000005, 0.0));
            u_xlatb34 = 0.5<_WakeFastCalculation;
            u_xlatb35 = 0.5<_SplashEnabled;
            u_xlatb3.x = 9.99999975e-06<_SplashHeightMultiplier;
            u_xlatu4.y = uint(uint(0u));
            u_xlatu4.z = uint(uint(0u));
            u_xlatu4.w = uint(uint(0u));
            u_xlat5.x = vs_TEXCOORD2.x;
            u_xlat5.y = 0.0;
            u_xlat14 = 0.0;
            u_xlatu4.x = uint(0u);
            while(true){
                u_xlatb25 = int(u_xlatu4.x)<u_xlati11;
                u_xlatb36 = int(u_xlatu4.x)<64;
                u_xlatb25 = u_xlatb36 && u_xlatb25;
                if(!u_xlatb25){break;}
                u_xlat6 = _RippleCentersTex.Load(int3(((int2)(u_xlatu4.xw)), int(u_xlatu4.w)));
                u_xlat7 = _RippleParamsTex.Load(int3(((int2)(u_xlatu4.xw)), int(u_xlatu4.w)));
                u_xlat8.xyz = _RippleExtraParamsTex.Load(int3(((int2)(u_xlatu4.xy)), int(u_xlatu4.w))).wyx;
                u_xlati25 = int((u_xlat6.w<0.0) ? 0xFFFFFFFFu : uint(0));
                u_xlat36 = (u_xlati25 != 0) ? u_xlat1.x : u_xlat1.y;
                u_xlat8.xy = u_xlat8.xy;
                u_xlat8.xy = clamp(u_xlat8.xy, 0.0, 1.0);
                u_xlat27.x = (-u_xlat36) + u_xlat8.y;
                u_xlat36 = u_xlat8.x * u_xlat27.x + u_xlat36;
                u_xlat36 = u_xlat1.z * u_xlat36;
                u_xlat36 = u_xlat36 * u_xlat7.x;
                u_xlatb27 = u_xlat36==0.0;
                if(u_xlatb27){
                    u_xlatu27 = u_xlatu4.x + 1u;
                    u_xlatu4.x = u_xlatu27;
                    continue;
                }
                u_xlat27.x = (-u_xlat6.z) + uw_Time.y;
                u_xlatb38 = 0.0>=u_xlat27.x;
                if(u_xlatb38){
                    u_xlatu38 = u_xlatu4.x + 1u;
                    u_xlatu4.x = u_xlatu38;
                    continue;
                }
                u_xlat38 = max(abs(u_xlat6.w), 0.00100000005);
                u_xlat28 = (u_xlati25 != 0) ? u_xlat2.y : u_xlat7.z;
                u_xlat39 = u_xlat27.x * u_xlat28;
                u_xlatb7 = u_xlat39>=u_xlat38;
                if(u_xlatb7){
                    u_xlatu7 = u_xlatu4.x + 1u;
                    u_xlatu4.x = u_xlatu7;
                    continue;
                }
                u_xlat7.x = max(u_xlat7.w, 0.0500000007);
                u_xlat29 = (u_xlati25 != 0) ? u_xlat7.x : u_xlat33;
                u_xlat8.x = u_xlat29 * 0.5;
                u_xlat8.x = max(u_xlat8.x, 0.0500000007);
                u_xlat29 = max(u_xlat29, 1.0);
                u_xlat7.x = (u_xlati25 != 0) ? u_xlat7.x : u_xlat29;
                u_xlati29 = u_xlatb34 ? u_xlati25 : int(0);
                u_xlat6.xy = u_xlat5.xy + (-u_xlat6.xy);
                u_xlat6.x = dot(u_xlat6.xy, u_xlat6.xy);
                u_xlat17 = u_xlat7.x * 1.5;
                u_xlat19 = (u_xlati29 != 0) ? u_xlat17 : u_xlat7.x;
                u_xlat19 = u_xlat27.x * u_xlat28 + u_xlat19;
                u_xlati41 = (~(u_xlati25));
                u_xlati41 = u_xlatb35 ? u_xlati41 : int(0);
                u_xlat9 = u_xlat8.x * 6.0;
                u_xlat9 = max(u_xlat19, u_xlat9);
                u_xlat19 = (u_xlati41 != 0) ? u_xlat9 : u_xlat19;
                u_xlat19 = u_xlat19 * u_xlat19;
                u_xlatb19 = u_xlat19<u_xlat6.x;
                if(u_xlatb19){
                    u_xlatu19 = u_xlatu4.x + 1u;
                    u_xlatu4.x = u_xlatu19;
                    continue;
                }
                u_xlat17 = u_xlat27.x * u_xlat28 + (-u_xlat17);
                u_xlat17 = max(u_xlat17, 0.0);
                u_xlat19 = (-u_xlat7.x) * 10.0 + u_xlat39;
                u_xlat19 = max(u_xlat19, 0.0);
                u_xlat19 = (u_xlati41 != 0) ? 0.0 : u_xlat19;
                u_xlat17 = (u_xlati29 != 0) ? u_xlat17 : u_xlat19;
                u_xlatb19 = 0.0<u_xlat17;
                u_xlat17 = u_xlat17 * u_xlat17;
                u_xlatb17 = u_xlat6.x<u_xlat17;
                u_xlatb17 = u_xlatb17 && u_xlatb19;
                if(u_xlatb17){
                    u_xlatu17 = u_xlatu4.x + 1u;
                    u_xlatu4.x = u_xlatu17;
                    continue;
                }
                u_xlat17 = (u_xlati25 != 0) ? u_xlat2.z : u_xlat7.w;
                u_xlat38 = u_xlat39 / u_xlat38;
                u_xlat38 = clamp(u_xlat38, 0.0, 1.0);
                u_xlat38 = u_xlat38 + -0.349999994;
                u_xlat38 = u_xlat38 * 1.53846157;
                u_xlat38 = max(u_xlat38, 0.0);
                u_xlat39 = u_xlat38 * -2.0 + 3.0;
                u_xlat38 = u_xlat38 * u_xlat38;
                u_xlat38 = (-u_xlat39) * u_xlat38 + 1.0;
                u_xlat39 = sqrt(u_xlat6.x);
                if(u_xlati29 != 0) {
                    u_xlat29 = u_xlat7.x * 0.850000024;
                    u_xlat40 = (-u_xlat27.x) * u_xlat28 + u_xlat39;
                    u_xlat40 = (-u_xlat7.x) * 0.649999976 + abs(u_xlat40);
                    u_xlat29 = float(1.0) / u_xlat29;
                    u_xlat29 = u_xlat29 * u_xlat40;
                    u_xlat29 = clamp(u_xlat29, 0.0, 1.0);
                    u_xlat40 = u_xlat29 * -2.0 + 3.0;
                    u_xlat29 = u_xlat29 * u_xlat29;
                    u_xlat29 = (-u_xlat40) * u_xlat29 + 1.0;
                    u_xlat40 = u_xlat17 * u_xlat27.x + 1.0;
                    u_xlat40 = float(1.0) / float(u_xlat40);
                    u_xlat19 = u_xlat27.x * 5.0;
                    u_xlat19 = clamp(u_xlat19, 0.0, 1.0);
                    u_xlat9 = u_xlat19 * -2.0 + 3.0;
                    u_xlat19 = u_xlat19 * u_xlat19;
                    u_xlat19 = u_xlat19 * u_xlat9;
                    u_xlat29 = u_xlat36 * u_xlat29;
                    u_xlat29 = u_xlat40 * u_xlat29;
                    u_xlat29 = u_xlat19 * u_xlat29;
                    u_xlat29 = u_xlat29 * u_xlat38 + u_xlat14;
                    u_xlatu40 = u_xlatu4.x + 1u;
                    u_xlat14 = u_xlat29;
                    u_xlatu4.x = u_xlatu40;
                    continue;
                }
                u_xlat29 = u_xlat8.x + u_xlat8.x;
                u_xlati40 = u_xlatb3.x ? u_xlati41 : int(0);
                u_xlat29 = u_xlat29 * u_xlat29;
                u_xlat6.x = (-u_xlat6.x) / u_xlat29;
                u_xlat6.x = u_xlat6.x * 1.44269502;
                u_xlat6.x = exp2(u_xlat6.x);
                u_xlat8.xy = u_xlat27.xx * float2(20.0, -8.65616989);
                u_xlat29 = sin(u_xlat8.x);
                u_xlat8.x = exp2(u_xlat8.y);
                u_xlat29 = u_xlat29 * u_xlat8.x;
                u_xlat8.x = u_xlat36 * _SplashHeightMultiplier;
                u_xlat19 = max(u_xlat8.z, 0.0);
                u_xlat8.x = u_xlat19 * u_xlat8.x;
                u_xlat6.x = u_xlat6.x * u_xlat8.x;
                u_xlat6.x = u_xlat29 * u_xlat6.x;
                u_xlat6.x = u_xlat38 * u_xlat6.x;
                u_xlat6.x = asfloat(uint(uint(asuint(u_xlat6.x)) & uint(u_xlati40)));
                u_xlat28 = (-u_xlat27.x) * u_xlat28 + u_xlat39;
                u_xlat28 = u_xlat28 / u_xlat7.x;
                u_xlatb39 = 1.0<u_xlat28;
                u_xlatb7 = u_xlat28<-10.0;
                u_xlatb39 = u_xlatb39 || u_xlatb7;
                if(u_xlatb39){
                    u_xlat39 = u_xlat14 + u_xlat6.x;
                    u_xlatu7 = u_xlatu4.x + 1u;
                    u_xlat14 = u_xlat39;
                    u_xlatu4.x = u_xlatu7;
                    continue;
                }
                u_xlat25.x = (u_xlati25 != 0) ? u_xlat2.x : u_xlat7.y;
                u_xlatb39 = 0.0<u_xlat28;
                u_xlat7.xy = ((float2)(u_xlat28)) * float2(-5.77078009, 0.721347511);
                u_xlat7.xy = exp2(u_xlat7.xy);
                u_xlat39 = (u_xlatb39) ? u_xlat7.x : u_xlat7.y;
                u_xlat25.x = u_xlat28 * u_xlat25.x;
                u_xlat25.x = cos(u_xlat25.x);
                u_xlat17 = u_xlat27.x * (-u_xlat17);
                u_xlat17 = u_xlat17 * 1.44269502;
                u_xlat17 = exp2(u_xlat17);
                u_xlat27.x = u_xlat27.x * 5.0;
                u_xlat27.x = clamp(u_xlat27.x, 0.0, 1.0);
                u_xlat28 = u_xlat27.x * -2.0 + 3.0;
                u_xlat27.x = u_xlat27.x * u_xlat27.x;
                u_xlat27.x = u_xlat27.x * u_xlat28;
                u_xlat25.x = u_xlat25.x * u_xlat36;
                u_xlat25.x = u_xlat39 * u_xlat25.x;
                u_xlat25.x = u_xlat17 * u_xlat25.x;
                u_xlat25.x = u_xlat27.x * u_xlat25.x;
                u_xlat25.x = u_xlat25.x * u_xlat38 + u_xlat6.x;
                u_xlat14 = u_xlat25.x + u_xlat14;
                u_xlatu4.x = u_xlatu4.x + 1u;
            }
        } else {
            u_xlat14 = 0.0;
        }
        u_xlat0.x = u_xlat0.x + u_xlat14;
        u_xlat0.x = u_xlat0.x + (-vs_TEXCOORD2.y);
        u_xlatb0.x = u_xlat0.x<0.0;
        if(u_xlatb0.x){discard;}
    }
    u_xlat0.xy = (-vs_TEXCOORD0.yx) + float2(1.0, 1.0);
    u_xlat11.x = min(u_xlat0.y, vs_TEXCOORD0.x);
    u_xlat33 = _Softness * 0.5;
    u_xlat33 = max(u_xlat33, 9.99999975e-05);
    u_xlat33 = float(1.0) / u_xlat33;
    u_xlat11.x = u_xlat33 * u_xlat11.x;
    u_xlat11.x = clamp(u_xlat11.x, 0.0, 1.0);
    u_xlat33 = u_xlat11.x * -2.0 + 3.0;
    u_xlat11.x = u_xlat11.x * u_xlat11.x;
    u_xlat11.x = u_xlat11.x * u_xlat33;
    u_xlat0.x = u_xlat0.x;
    u_xlat0.x = clamp(u_xlat0.x, 0.0, 1.0);
    u_xlatb33 = 9.99999975e-05>=_SurfaceFade;
    u_xlat1.x = _SurfaceFade * _SourceFadeOffset + _SurfaceFade;
    u_xlat12 = _SurfaceFade * _SourceFadeOffset + u_xlat0.x;
    u_xlat1.x = float(1.0) / u_xlat1.x;
    u_xlat1.x = u_xlat1.x * u_xlat12;
    u_xlat1.x = clamp(u_xlat1.x, 0.0, 1.0);
    u_xlat12 = u_xlat1.x * -2.0 + 3.0;
    u_xlat1.x = u_xlat1.x * u_xlat1.x;
    u_xlat1.x = u_xlat1.x * u_xlat12;
    u_xlat1.x = (u_xlatb33) ? 1.0 : u_xlat1.x;
    u_xlat12 = vs_TEXCOORD0.y;
    u_xlat12 = clamp(u_xlat12, 0.0, 1.0);
    u_xlat23 = log2(u_xlat12);
    u_xlat23 = u_xlat23 * _DepthFadePower;
    u_xlat23 = exp2(u_xlat23);
    u_xlat16_10.x = vs_COLOR0.x * 5.30999994;
    u_xlat34 = uw_Time.y * _FlickerSpeed + u_xlat16_10.x;
    u_xlat34 = sin(u_xlat34);
    u_xlat34 = u_xlat34 * 0.119999997 + -0.120000005;
    u_xlat34 = _FlickerStrength * u_xlat34 + 1.0;
    u_xlat2.x = vs_TEXCOORD1.x * 6.28318548;
    u_xlat2.x = uw_Time.y * _OpacityAnimationSpeed + u_xlat2.x;
    u_xlat2.x = sin(u_xlat2.x);
    u_xlat2.x = u_xlat2.x * 0.5 + -0.5;
    u_xlat2.x = _OpacityAnimationStrength * u_xlat2.x + 1.0;
    u_xlat11.x = u_xlat11.x * u_xlat1.x;
    u_xlat11.x = u_xlat23 * u_xlat11.x;
    u_xlat11.x = u_xlat11.x * vs_COLOR0.w;
    u_xlat11.x = u_xlat34 * u_xlat11.x;
    u_xlat11.x = u_xlat2.x * u_xlat11.x;
    u_xlat1.x = vs_TEXCOORD0.y * 0.479999989 + 0.219999999;
    u_xlat1.x = u_xlat1.x * vs_COLOR0.w;
    u_xlat11.x = (u_xlatb0.z) ? u_xlat1.x : u_xlat11.x;
    u_xlat11.x = u_xlat11.x * _Opacity;
    u_xlat2.w = u_xlat11.x * _Color.w;
    u_xlat2.w = clamp(u_xlat2.w, 0.0, 1.0);
    u_xlatb11 = 0.5<_MultiplyBlend;
    if(u_xlatb11){
        u_xlat11.x = max(_Brightness, 0.0);
        u_xlat11.x = u_xlat11.x * u_xlat2.w;
        u_xlat11.x = min(u_xlat11.x, 1.0);
        u_xlat16_10.xyz = vs_COLOR0.xyz * _Color.xyz;
        u_xlat16_10.xyz = clamp(u_xlat16_10.xyz, 0.0, 1.0);
        u_xlat1.xzw = u_xlat16_10.xyz + float3(-1.0, -1.0, -1.0);
        u_xlat2.xyz = u_xlat11.xxx * u_xlat1.xzw + float3(1.0, 1.0, 1.0);
        SV_TARGET0 = u_xlat2;
        return max(SV_TARGET0,0);
    }
    u_xlat11.x = u_xlat12 * 0.180000007 + 0.819999993;
    u_xlat1.xyz = _Color.xyz * float3(float3(_Brightness, _Brightness, _Brightness));
    u_xlat1.xyz = u_xlat1.xyz * vs_COLOR0.xyz;
    u_xlat1.xyz = u_xlat11.xxx * u_xlat1.xyz;
    u_xlat1.xyz = u_xlat2.www * u_xlat1.xyz;
    u_xlat11.x = _SurfaceFade * 0.349999994;
    u_xlat11.x = max(u_xlat11.x, 9.99999975e-05);
    u_xlat11.x = float(1.0) / u_xlat11.x;
    u_xlat0.x = u_xlat11.x * u_xlat0.x;
    u_xlat0.x = min(u_xlat0.x, 1.0);
    u_xlat11.x = u_xlat0.x * -2.0 + 3.0;
    u_xlat0.x = u_xlat0.x * u_xlat0.x;
    u_xlat0.x = u_xlat0.x * u_xlat11.x;
    u_xlat0.x = u_xlat0.x * 30.6499996 + 1.35000002;
    u_xlat0.x = (u_xlatb33) ? 32.0 : u_xlat0.x;
    u_xlat11.xyz = (-u_xlat1.xyz) / u_xlat0.xxx;
    u_xlat11.xyz = u_xlat11.xyz * float3(1.44269502, 1.44269502, 1.44269502);
    u_xlat11.xyz = exp2(u_xlat11.xyz);
    u_xlat11.xyz = (-u_xlat11.xyz) + float3(1.0, 1.0, 1.0);
    u_xlat2.xyz = u_xlat11.xyz * u_xlat0.xxx;
    SV_TARGET0 = u_xlat2;
    return max(SV_TARGET0,0);

}
ENDHLSL
}
}
}
