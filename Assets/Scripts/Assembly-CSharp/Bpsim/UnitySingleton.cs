using UnityEngine;

namespace Bpsim
{
	public class UnitySingleton<T> : MonoBehaviour where T : UnitySingleton<T>
	{
		protected static T s_instance;

		public static T Instance => s_instance;

		protected virtual void Awake()
		{
			Set();
		}

		protected virtual void OnDestroy()
		{
			if (s_instance == this)
			{
				Unset();
			}
		}

		public void Set()
		{
			s_instance = (T)this;
		}

		public void Unset()
		{
			s_instance = null;
		}
	}
}
