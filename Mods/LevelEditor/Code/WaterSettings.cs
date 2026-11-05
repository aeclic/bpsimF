using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Bpsim.LevelEditor
{
	public sealed class WaterSettings
	{
		public string Id, Name;
		public float X, SurfaceY, Width, Depth, Opacity, WaveAmplitude, WaveSpeed, WaveLength;
		public float Density, Buoyancy, LinearDrag, AngularDrag, FlowX, FlowY, FlowAcceleration;
		public float RippleStrength, RippleDamping, RippleSpeed, Refraction, Caustics, Reflection, Foam;
		public bool PhysicsEnabled, RipplesEnabled;
		public Color Color;

		// Additive schema: old visual-only pools keep physics_enabled=false and unknown fields.
		public static void Migrate(JObject body)
		{
			Default(body, "physics_enabled", false);
			Default(body, "wave_length", 2.5f);
			Default(body, "density", 1f);
			Default(body, "buoyancy_multiplier", 1f);
			Default(body, "linear_drag", .5f);
			Default(body, "angular_drag", 0f);
			Default(body, "flow_x", 0f);
			Default(body, "flow_y", 0f);
			Default(body, "flow_acceleration", 0f);
			Default(body, "ripples_enabled", true);
			Default(body, "ripple_strength", .06f);
			Default(body, "ripple_damping", 2f);
			Default(body, "ripple_speed", 3f);
			Default(body, "refraction_strength", .05f);
			Default(body, "caustics_strength", .35f);
			Default(body, "reflection_strength", .663f);
			Default(body, "foam_strength", 1f);
		}

		private static void Default(JObject body, string name, JToken value)
		{
			if (body[name] == null) body[name] = value;
		}

		public static WaterSettings Read(JObject body)
		{
			Migrate(body);
			WaterSettings value = new WaterSettings
			{
				Id = (string)body["id"], Name = (string)body["name"],
				X = Number(body, "x", -100000, 100000),
				SurfaceY = Number(body, "surface_y", -100000, 100000),
				Width = Number(body, "width", .01f, 10000),
				Depth = Number(body, "depth", .01f, 10000),
				Opacity = Number(body, "opacity", 0, 1),
				WaveAmplitude = Number(body, "wave_amplitude", 0, 10),
				WaveSpeed = Number(body, "wave_speed", 0, 100),
				WaveLength = Number(body, "wave_length", .05f, 1000),
				Density = Number(body, "density", 0, 1000),
				Buoyancy = Number(body, "buoyancy_multiplier", 0, 100),
				LinearDrag = Number(body, "linear_drag", 0, 100),
				AngularDrag = Number(body, "angular_drag", 0, 100),
				FlowX = Number(body, "flow_x", -100, 100),
				FlowY = Number(body, "flow_y", -100, 100),
				FlowAcceleration = Number(body, "flow_acceleration", 0, 100),
				PhysicsEnabled = Boolean(body, "physics_enabled"),
				RipplesEnabled = Boolean(body, "ripples_enabled"),
				RippleStrength = Number(body, "ripple_strength", 0, 2),
				RippleDamping = Number(body, "ripple_damping", .01f, 20),
				RippleSpeed = Number(body, "ripple_speed", .05f, 40),
				Refraction = Number(body, "refraction_strength", 0, 1),
				Caustics = Number(body, "caustics_strength", 0, 2),
				Reflection = Number(body, "reflection_strength", 0, 1),
				Foam = Number(body, "foam_strength", 0, 2)
			};
			if (body["color"] == null || body["color"].Type != JTokenType.String ||
				!ColorUtility.TryParseHtmlString((string)body["color"], out value.Color))
				throw new FormatException("水域颜色无效。");
			return value;
		}

		public static bool Boolean(JObject body, string key)
		{
			if (body[key] == null || body[key].Type != JTokenType.Boolean)
				throw new FormatException("水域开关参数无效：" + key);
			return (bool)body[key];
		}

		private static float Number(JObject body, string key, float minimum, float maximum)
		{
			JToken token = body[key];
			if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
				throw new FormatException("水域参数缺失：" + key);
			double number = (double)token;
			if (double.IsNaN(number) || double.IsInfinity(number) || number < minimum || number > maximum)
				throw new FormatException("水域参数越界：" + key);
			return (float)number;
		}
	}
}
