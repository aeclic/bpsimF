using System;

namespace Bpsim.Parts
{
	public struct PartExtensionData
	{
		public float Mass;

		public float EnginePower;

		public float PowerConsumption;

		public bool IsConnectionSource;

		public bool HasCustomConnection;

		public BitDirection ConnectionDirection;

		public float ConnectionStrength;

		public PartExtensionData(float mass, float enginePower, float powerConsumption, bool isConnectionSource, bool hasCustomConnection, BitDirection connectionDirection, float connectionStrength)
		{
			Mass = mass;
			EnginePower = enginePower;
			PowerConsumption = powerConsumption;
			IsConnectionSource = isConnectionSource;
			HasCustomConnection = hasCustomConnection;
			ConnectionDirection = connectionDirection;
			ConnectionStrength = connectionStrength;
		}

		public override bool Equals(object obj)
		{
			if (obj is PartExtensionData other)
			{
				return Equals(other);
			}
			return false;
		}

		public bool Equals(PartExtensionData other)
		{
			if (Mass == other.Mass && EnginePower == other.EnginePower && PowerConsumption == other.PowerConsumption && IsConnectionSource == other.IsConnectionSource && HasCustomConnection == other.HasCustomConnection && ConnectionDirection == other.ConnectionDirection)
			{
				return ConnectionStrength == other.ConnectionStrength;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Mass, EnginePower, PowerConsumption, IsConnectionSource, HasCustomConnection, ConnectionDirection, ConnectionStrength);
		}
	}
}
