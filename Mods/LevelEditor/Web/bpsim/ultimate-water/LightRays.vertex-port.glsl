#version 300 es
precision highp float;precision highp int;
layout(location=0) in vec3 position;layout(location=1) in vec2 uv;layout(location=2) in vec2 seed;layout(location=3) in vec2 extra;layout(location=4) in vec4 color;
uniform vec4 uw_CaptureRect,uw_Bounds;uniform float uw_Scale;
out vec2 uw_world,uw_uv,uw_seed;out vec3 uw_local;out vec4 uw_color;
uniform vec4 _Time;
uniform float _RandomMovement;
uniform float _RandomMovementSpeed;
uniform float _RandomAngleTilt;
uniform float _RandomAngleTiltSpeed;
uniform vec4 _LocalZBounds;
void main(){
vec4 u_xlat0;
vec4 u_xlat1;
float u_xlat2;
float u_xlat3;
float u_xlat6;
float u_xlat9;

    u_xlat0.xy = seed.yx * vec2(6.28318548, 6.28318548);
    u_xlat0.y = _Time.y * _RandomAngleTiltSpeed + u_xlat0.y;
    u_xlat0.x = _Time.y * _RandomMovementSpeed + u_xlat0.x;
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
    u_xlat9 = _Time.y * _RandomMovementSpeed;
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
vec2 projected=vec2(uw_Bounds.x+uw_Bounds.z*.5+(cx+(u_xlat0.x-cx)*p)*uw_Scale,uw_Bounds.y+(cy+(u_xlat0.y-cy)*p)*uw_Scale);

gl_Position=vec4(((projected-uw_CaptureRect.xy)/uw_CaptureRect.zw*2.-1.)*d,0,d);
uw_world=projected;uw_uv=uv;uw_seed=seed;uw_local=u_xlat0.xyz;uw_color=color;}
