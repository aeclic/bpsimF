using System;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
	internal class ScreenshotPanel : InterfaceBase
	{
		[SerializeField]
		private InputField m_path;

		[SerializeField]
		private Button m_browseButton;

		[SerializeField]
		private InputField m_width;

		[SerializeField]
		private InputField m_height;

		[SerializeField]
		private RawImage m_image;

		[SerializeField]
		private Button m_applyButton;

		[SerializeField]
		private Button m_saveButton;

		[SerializeField]
		private Button m_cancelButton;

		private Vector2 m_imageSize;

		private void Awake()
		{
			m_browseButton.onClick.AddListener(Browse);
			m_applyButton.onClick.AddListener(Apply);
			m_saveButton.onClick.AddListener(Save);
			m_cancelButton.onClick.AddListener(Cancel);
			m_width.text = Screen.width.ToString();
			m_height.text = Screen.height.ToString();
			m_imageSize = m_image.rectTransform.sizeDelta;
		}

		private void Browse()
		{
			BrowseAsync().Forget();
		}

		private async UniTask BrowseAsync()
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			if (!(m_image.texture is Texture2D)) Apply();
			await ExportScreenshot();
#else
			using FileSelection selected = await FileAPI.OpenFile(Application.dataPath);
			if (selected != null)
			{
				m_path.text = selected.Path;
			}
#endif
		}

		private void Apply()
		{
			int width = int.Parse(m_width.text);
			int height = int.Parse(m_height.text);
			Texture2D texture2D = Screenshot.Capture(SceneCamera.Instance.Camera, width, height);
			if (texture2D != null)
			{
				m_image.gameObject.SetActive(value: true);
				m_image.texture = texture2D;
				float num = m_imageSize.x / m_imageSize.y;
				float num2 = (float)texture2D.width / (float)texture2D.height;
				m_image.rectTransform.sizeDelta = ((num2 > num) ? new Vector2(m_imageSize.x, m_imageSize.x / num2) : new Vector2(m_imageSize.y * num2, m_imageSize.y));
			}
		}

		private void Save()
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			if (string.IsNullOrWhiteSpace(m_path.text))
			{
				ExportScreenshot().Forget();
				return;
			}
#endif
			File.WriteAllBytes(m_path.text, ((Texture2D)m_image.texture).EncodeToPNG());
		}

#if UNITY_ANDROID && !UNITY_EDITOR
		private async UniTask ExportScreenshot()
		{
			if (!(m_image.texture is Texture2D texture)) return;
			try { await FileAPI.ExportFile("Bpsim-Screenshot.png", "image/png", path => File.WriteAllBytes(path, texture.EncodeToPNG())); }
			catch (Exception ex) { Debug.LogException(ex); }
		}
#endif

		private void Cancel()
		{
			Close();
		}

		private new void Close()
		{
			UnityEngine.Object.Destroy(base.gameObject);
		}
	}
}
