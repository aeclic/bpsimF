using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Bpsim
{
	public class ResourceBinder : MonoBehaviour
	{
		[Serializable]
		public class Binding
		{
			[SerializeField]
			private ResourceKind m_kind;

			[SerializeField]
			private string m_source;

			[SerializeField]
			private UnityEngine.Object m_target;

			[SerializeField]
			private string m_path;

			public ResourceKind Kind => m_kind;

			public string Source => m_source;

			public UnityEngine.Object Target => m_target;

			public string Path => m_path;
		}

		[SerializeField]
		private List<Binding> m_bindings;

		private void Awake()
		{
			Bind();
		}

		public void Bind()
		{
			foreach (Binding binding in m_bindings)
			{
				MethodInfo setMethod = binding.Target.GetType().GetProperty(binding.Path).GetSetMethod();
				object obj = CoreManager.Instance.Resources.LoadAsset(binding.Kind, binding.Source);
				setMethod.Invoke(binding.Target, new object[1] { obj });
			}
		}
	}
}
