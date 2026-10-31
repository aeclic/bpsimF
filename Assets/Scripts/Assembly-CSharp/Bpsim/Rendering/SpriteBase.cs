using UnityEngine;

namespace Bpsim.Rendering
{
	public abstract class SpriteBase : MonoBehaviour
	{
		[SerializeField]
		protected string m_name;

		protected bool m_dirty;

		protected int m_id;

		protected SpriteRect m_spriteRect;

		public bool IsDirty => m_dirty;

		public string SpriteName
		{
			get
			{
				return m_name;
			}
			set
			{
				if (m_name != value)
				{
					m_name = value;
					SetDirty();
				}
			}
		}

		public int SpriteID => m_id;

		public SpriteRect SpriteData => m_spriteRect;

		protected virtual void Awake()
		{
			m_dirty = true;
			Apply();
		}

		protected virtual void Update()
		{
			Apply();
		}

		public void SetDirty()
		{
			m_dirty = true;
		}

		public void Apply()
		{
			if (m_dirty && !string.IsNullOrEmpty(m_name))
			{
				// Prefab preview scenes and other edit-mode loads run Awake/Update without a
				// live SpriteManager; stay dirty and retry later instead of throwing an NRE.
				SpriteManager manager = SpriteManager.Instance;
				if (manager == null)
				{
					return;
				}

				m_dirty = false;
				m_id = manager.GetID(m_name);
				m_spriteRect = manager.GetSprite(m_id);
				ApplySprite();
			}
		}

		protected abstract void ApplySprite();
	}
}
