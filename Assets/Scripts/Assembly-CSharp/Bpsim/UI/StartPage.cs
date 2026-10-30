using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
	internal class StartPage : MonoBehaviour
	{
		[SerializeField]
		private RawImage m_background;

		[SerializeField]
		private RawImage m_image;

		private void Awake()
		{
			m_background.color = Color.black;
			m_image.color = Color.clear;
			PlayAnimation().Forget();
		}

		private async UniTask PlayAnimation()
		{
			await UniTask.Delay(500);
			await PlayLinearAnimation(0f, 1f, 1f, ignoreTimeScale: false, delegate(float t)
			{
				m_background.color = Color.Lerp(Color.black, Color.white, t);
				m_image.color = Color.Lerp(Color.clear, new Color(0f, 0.25f, 0.5f, 1f), t);
			});
			await UniTask.Delay(1000);
			await PlayLinearAnimation(0f, 1f, 1f, ignoreTimeScale: false, delegate(float t)
			{
				m_background.color = Color.Lerp(Color.white, Color.black, t);
				m_image.color = Color.Lerp(new Color(0f, 0.25f, 0.5f, 1f), Color.white, t);
			});
			await UniTask.Delay(1000);
			UnityEngine.Object.Destroy(base.gameObject);
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
