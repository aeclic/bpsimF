using System.Collections.Generic;
using Bpsim.Serialization;
using Newtonsoft.Json;
using UnityEngine;

namespace Bpsim.Templates
{
	[JsonConverter(typeof(JsonAliasConverterLegacy<GameObjectTemplate>))]
	public class GameObjectTemplate : ITemplate<GameObject>, ITemplate
	{
		[JsonAlias("名称")]
		public string Name { get; set; }

		[JsonAlias("层级")]
		public int Layer { get; set; }

		[JsonAlias("是否启用")]
		public bool Active { get; set; }

		[JsonAlias("组件")]
		public List<ComponentTemplate> Components { get; set; }

		[JsonAlias("子物体")]
		public List<GameObjectTemplate> Children { get; set; }

		public GameObjectTemplate()
		{
			Active = true;
			Components = new List<ComponentTemplate>();
			Children = new List<GameObjectTemplate>();
		}

		public static GameObjectTemplate Create(GameObject gameObject)
		{
			GameObjectTemplate gameObjectTemplate = new GameObjectTemplate();
			gameObjectTemplate.Name = gameObject.name;
			Component[] components = gameObject.GetComponents<Component>();
			gameObjectTemplate.Components = new List<ComponentTemplate>(components.Length);
			Component[] array = components;
			foreach (Component component in array)
			{
				if (ComponentTemplate.IsSupported(component))
				{
					gameObjectTemplate.Components.Add(ComponentTemplate.Create(component));
				}
			}
			int childCount = gameObject.transform.childCount;
			gameObjectTemplate.Children = new List<GameObjectTemplate>(childCount);
			for (int j = 0; j < childCount; j++)
			{
				GameObjectTemplate item = Create(gameObject.transform.GetChild(j).gameObject);
				gameObjectTemplate.Children.Add(item);
			}
			return gameObjectTemplate;
		}

		public GameObject Apply(IResourceResolver resolver)
		{
			return Apply(null, resolver);
		}

		public GameObject Apply(GameObject gameObject, IResourceResolver resolver)
		{
			if (gameObject == null)
			{
				gameObject = new GameObject(Name);
			}
			gameObject.name = Name;
			gameObject.layer = Layer;
			gameObject.SetActive(Active);
			if (Components != null)
			{
				foreach (ComponentTemplate component in Components)
				{
					component.Apply(gameObject, resolver);
				}
			}
			if (Children != null)
			{
				foreach (GameObjectTemplate child in Children)
				{
					child.Apply(resolver).transform.SetParent(gameObject.transform, worldPositionStays: false);
				}
			}
			return gameObject;
		}

		public ComponentTemplate AddComponent(ComponentType type)
		{
			ComponentTemplate componentTemplate = ComponentTemplate.Create(type);
			Components.Add(componentTemplate);
			return componentTemplate;
		}

		public ComponentTemplate GetComponent(ComponentType type)
		{
			return Components.Find((ComponentTemplate template) => type == ComponentType.All || template.Type == type);
		}

		public T GetComponent<T>() where T : ComponentTemplate
		{
			return (T)Components.Find((ComponentTemplate template) => template is T);
		}

		public IEnumerable<ComponentTemplate> GetComponents(ComponentType type)
		{
			foreach (ComponentTemplate component in Components)
			{
				if (type == ComponentType.All || component.Type == type)
				{
					yield return component;
				}
			}
		}

		public ComponentTemplate GetComponentInChildren(ComponentType type)
		{
			foreach (ComponentTemplate component in Components)
			{
				if (type == ComponentType.All || component.Type == type)
				{
					return component;
				}
			}
			foreach (GameObjectTemplate child in Children)
			{
				ComponentTemplate componentInChildren = child.GetComponentInChildren(type);
				if (componentInChildren != null)
				{
					return componentInChildren;
				}
			}
			return null;
		}

		public IEnumerable<ComponentTemplate> GetComponentsInChildren(ComponentType type)
		{
			foreach (ComponentTemplate component in Components)
			{
				if (type == ComponentType.All || component.Type == type)
				{
					yield return component;
				}
			}
			foreach (GameObjectTemplate child in Children)
			{
				foreach (ComponentTemplate componentsInChild in child.GetComponentsInChildren(type))
				{
					yield return componentsInChild;
				}
			}
		}
	}
}
