using System;
using Bpsim.Parts;
using UnityEngine;

namespace Bpsim
{
	internal static class Screenshot
	{
		public static Texture2D Capture(Camera camera)
		{
			return Capture(camera, Screen.width, Screen.height);
		}

		public static Texture2D Capture(Camera camera, int width, int height)
		{
			if (!PartManager.Instance.HasActiveScene())
			{
				throw new NullReferenceException();
			}
			if (width <= 0)
			{
				throw new ArgumentOutOfRangeException("width");
			}
			if (height <= 0)
			{
				throw new ArgumentOutOfRangeException("height");
			}
			PartScene activeScene = PartManager.Instance.ActiveScene;
			bool gridEnabled = activeScene.GridEnabled;
			activeScene.GridEnabled = false;
			try
			{
				RenderTexture active = (camera.targetTexture = new RenderTexture(width, height, 0));
				camera.Render();
				RenderTexture.active = active;
				Texture2D texture2D = new Texture2D(width, height);
				texture2D.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
				texture2D.Apply();
				RenderTexture.active = null;
				return texture2D;
			}
			finally
			{
				camera.targetTexture = null;
				activeScene.GridEnabled = gridEnabled;
			}
		}
	}
}
