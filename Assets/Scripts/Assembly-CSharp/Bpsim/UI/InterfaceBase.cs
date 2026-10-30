using UnityEngine;

namespace Bpsim.UI
{
	internal abstract class InterfaceBase : MonoBehaviour
	{
		public void Close()
		{
			Object.Destroy(base.gameObject);
		}
	}
}
