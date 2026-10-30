using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Bpsim.UI
{
	internal static class UIExtensions
	{
		private static List<RaycastResult> s_raycastResults = new List<RaycastResult>();

		/// <summary>bpsimtaffy's blue-orange runtime palette: subtle track and blue-gray thumb.</summary>
		public static void StyleScrollbars(GameObject root)
		{
			foreach (var bar in root.GetComponentsInChildren<UnityEngine.UI.Scrollbar>(true))
			{
				bool vertical = bar.direction == UnityEngine.UI.Scrollbar.Direction.BottomToTop || bar.direction == UnityEngine.UI.Scrollbar.Direction.TopToBottom;
				((RectTransform)bar.transform).SetSizeWithCurrentAnchors(vertical ? RectTransform.Axis.Horizontal : RectTransform.Axis.Vertical, 20);
				var track = bar.GetComponent<UnityEngine.UI.Graphic>();
				if (track != null) track.color = new Color(1, 1, 1, .05f);
				if (track is UnityEngine.UI.Image trackImage) trackImage.sprite = null;
				if (bar.targetGraphic != null) bar.targetGraphic.color = Color.Lerp(new Color(.16f, .16f, .16f, 1), new Color(47f / 255, 128f / 255, 237f / 255, 1), .35f);
				if (bar.targetGraphic is UnityEngine.UI.Image handleImage) handleImage.sprite = null;
			}
		}

		public static bool IsPointerOverUIObject(this EventSystem eventSystem, Vector2 position)
		{
			RaycastResult raycastResult;
			return eventSystem.IsPointerOverUIObject(position, out raycastResult);
		}

		public static bool IsPointerOverUIObject(this EventSystem eventSystem, Vector2 position, out RaycastResult raycastResult)
		{
			PointerEventData pointerEventData = new PointerEventData(eventSystem);
			pointerEventData.position = position;
			eventSystem.RaycastAll(pointerEventData, s_raycastResults);
			bool flag = s_raycastResults.Count > 0;
			raycastResult = (flag ? s_raycastResults[0] : default(RaycastResult));
			s_raycastResults.Clear();
			return flag;
		}

		public static UniTask PlayFadeInAnimation(this CanvasRenderer canvasRenderer, float duration, bool ignoreTimeScale)
		{
			return canvasRenderer.PlayAlphaAnimation(0f, 1f, duration, ignoreTimeScale);
		}

		public static UniTask PlayFadeInAnimation(this CanvasGroup canvasGroup, float duration, bool ignoreTimeScale)
		{
			return canvasGroup.PlayAlphaAnimation(0f, 1f, duration, ignoreTimeScale);
		}

		public static UniTask PlayFadeOutAnimation(this CanvasRenderer canvasRenderer, float duration, bool ignoreTimeScale)
		{
			return canvasRenderer.PlayAlphaAnimation(1f, 0f, duration, ignoreTimeScale);
		}

		public static UniTask PlayFadeOutAnimation(this CanvasGroup canvasGroup, float duration, bool ignoreTimeScale)
		{
			return canvasGroup.PlayAlphaAnimation(1f, 0f, duration, ignoreTimeScale);
		}

		public static UniTask PlayAlphaAnimation(this CanvasRenderer canvasRenderer, float alpha0, float alpha1, float duration, bool ignoreTimeScale)
		{
			Action<float> setter = delegate(float alpha2)
			{
				canvasRenderer.SetAlpha(alpha2);
			};
			return PlayLinearAnimation(alpha0, alpha1, duration, ignoreTimeScale, setter);
		}

		public static UniTask PlayAlphaAnimation(this CanvasGroup canvasGroup, float alpha0, float alpha1, float duration, bool ignoreTimeScale)
		{
			Action<float> setter = delegate(float alpha2)
			{
				canvasGroup.alpha = alpha2;
			};
			return PlayLinearAnimation(alpha0, alpha1, duration, ignoreTimeScale, setter);
		}

		public static async UniTask PlayLinearAnimation(float from, float to, float duration, bool ignoreTimeScale, Action<float> setter)
		{
			float deltaTime = (ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime);
			for (float time = 0f; time < duration; time += deltaTime)
			{
				float num = time / duration;
				float obj = from * (1f - num) + to * num;
				setter(obj);
				await UniTask.NextFrame();
			}
			setter(to);
		}
	}
}
