using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bpsim.UI
{
	public static class CanvasConverter
	{
		public static GameObject Convert(GameObject gameObject, Action<GameObject> visitor)
		{
			GameObject gameObject2 = UnityEngine.Object.Instantiate(gameObject);
			gameObject2.name = gameObject.name;
			Visit(0, gameObject2, visitor);
			SetSortingOrder(0, gameObject2);
			return gameObject2;
		}

		private static void Visit(int depth, GameObject gameObject, Action<GameObject> visitor)
		{
			MeshRenderer component = gameObject.GetComponent<MeshRenderer>();
			MeshFilter component2 = gameObject.GetComponent<MeshFilter>();
			gameObject.AddOrGetComponent<RectTransform>();
			if (component != null && component2 != null)
			{
				CanvasRenderer canvasRenderer = gameObject.AddOrGetComponent<CanvasRenderer>();
				canvasRenderer.materialCount = 1;
				canvasRenderer.SetMaterial(component.material, 0);
			}
			if (component != null)
			{
				UnityEngine.Object.DestroyImmediate(component);
			}
			if (component2 != null)
			{
				UnityEngine.Object.DestroyImmediate(component2);
			}
			visitor?.Invoke(gameObject);
			int childCount = gameObject.transform.childCount;
			for (int i = 0; i < childCount; i++)
			{
				Transform child = gameObject.transform.GetChild(i);
				Visit(depth + 1, child.gameObject, visitor);
			}
		}

		private static float SetSortingOrder(int depth, GameObject gameObject)
		{
			float num = ((gameObject.GetComponent<CanvasRenderer>() == null) ? float.PositiveInfinity : gameObject.transform.position.z);
			List<(float, Transform)> list = new List<(float, Transform)>();
			int childCount = gameObject.transform.childCount;
			for (int i = 0; i < childCount; i++)
			{
				Transform child = gameObject.transform.GetChild(i);
				float num2 = SetSortingOrder(depth + 1, child.gameObject);
				list.Add((num2, child));
				if (num2 < num)
				{
					num = num2;
				}
			}
			list.Sort(((float, Transform) x, (float, Transform) y) => -x.Item1.CompareTo(y.Item1));
			for (int num3 = 0; num3 < childCount; num3++)
			{
				list[num3].Item2.SetSiblingIndex(num3);
			}
			return num;
		}
	}
}
