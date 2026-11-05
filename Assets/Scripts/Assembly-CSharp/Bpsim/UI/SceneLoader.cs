using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Bpsim.Levels;
using Bpsim.Parts;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
	internal class SceneLoader : InterfaceBase
	{
		[SerializeField]
		private Text m_header;

		[SerializeField]
		private InputField m_path;

		[SerializeField]
		private List<Button> m_browseButtons;

		[SerializeField]
		private Dropdown m_loadFormat;

		[SerializeField]
		private Button m_applyButton;

		[SerializeField]
		private Button m_cancelButton;

		[SerializeField]
		private Text m_state;

		private bool m_dirty;

		private SceneLoaderMode m_mode;

		private string m_bpsimDataPath;

		private string m_innovationDataPath;

		public SceneLoaderMode Mode
		{
			get
			{
				return m_mode;
			}
			set
			{
				if (m_mode != value)
				{
					m_mode = value;
					SetDirty();
				}
			}
		}

		public void SetDirty()
		{
			m_dirty = true;
		}

		private void Awake()
		{
			m_browseButtons[0].onClick.AddListener(Browse);
			m_browseButtons[1].onClick.AddListener(BrowseBpsimData);
			m_browseButtons[2].onClick.AddListener(BrowseInnovationData);
			m_applyButton.onClick.AddListener(Apply);
			m_cancelButton.onClick.AddListener(Cancel);
			EnsureBpirFormatOption();
			string directoryName = GetDefaultDataDirectory();
			m_bpsimDataPath = Path.Combine(directoryName, "bput");
			m_innovationDataPath = Path.Combine(directoryName, "Rovio");
		}

		/// <summary>
		/// 存档格式下拉框（prefab 里只有 4 项）。BPIR 格式是后加的，为了不改动 prefab 的
		/// yaml（手改容易破坏序列化），在运行期把第 5 项补上；重复打开界面也不会重复追加。
		/// </summary>
		private const string BpirFormatLabel = "BPIR 存档 (原版 Innovation Rebuild)";

		private void EnsureBpirFormatOption()
		{
			if (m_loadFormat == null)
			{
				return;
			}
			foreach (Dropdown.OptionData option in m_loadFormat.options)
			{
				if (option != null && option.text == BpirFormatLabel)
				{
					return;
				}
			}
			m_loadFormat.options.Add(new Dropdown.OptionData(BpirFormatLabel));
			m_loadFormat.RefreshShownValue();
		}

		// The data folders are looked for two levels above persistentDataPath
		// (%USERPROFILE%\AppData\LocalLow\<company> on Windows).  That layout does not exist on
		// Android, where persistentDataPath is /storage/emulated/0/Android/data/<pkg>/files and
		// the old string manipulation produced an empty parent ("\bput"), which FileAPI then
		// rejected with "ArgumentException: Invalid path".  Fall back to persistentDataPath so
		// the picker always gets a directory that exists.
		private static string GetDefaultDataDirectory()
		{
			string path = Application.persistentDataPath;
			for (int i = 0; i < 2; i++)
			{
				DirectoryInfo parent = Directory.GetParent(path);
				if (parent == null)
				{
					return Application.persistentDataPath;
				}
				path = parent.FullName;
			}
			return string.IsNullOrEmpty(path) ? Application.persistentDataPath : path;
		}

		private void Update()
		{
			if (m_dirty)
			{
				m_dirty = false;
				switch (m_mode)
				{
				case SceneLoaderMode.Read:
					m_header.text = "加载场景";
					m_applyButton.transform.Find("Text").GetComponent<Text>().text = "加载";
					break;
				case SceneLoaderMode.Write:
					m_header.text = "保存场景";
					m_applyButton.transform.Find("Text").GetComponent<Text>().text = "保存";
					break;
				}
			}
		}

		private void Browse()
		{
			BrowseAsync(null).Forget();
		}

		private void BrowseBpsimData()
		{
			BrowseAsync(m_bpsimDataPath).Forget();
		}

		private void BrowseInnovationData()
		{
			BrowseAsync(m_innovationDataPath).Forget();
		}

		private async UniTask BrowseAsync(string initialDirectory)
		{
			SceneLoaderMode mode = m_mode;
			string text = await (mode switch
			{
				SceneLoaderMode.Read => FileAPI.OpenFile(initialDirectory), 
				SceneLoaderMode.Write => FileAPI.CreateFile(initialDirectory), 
				_ => throw new SwitchExpressionException(mode), 
			});
			if (!string.IsNullOrEmpty(text))
			{
				m_path.text = text;
			}
		}

		private void Apply()
		{
			string text = m_path.text;
			SchematicsFormat format = m_loadFormat.value switch
			{
				0 => SchematicsFormat.Csv, 
				1 => SchematicsFormat.Json, 
				2 => SchematicsFormat.EncryptedXml, 
				3 => SchematicsFormat.Xml, 
				4 => SchematicsFormat.BpirCsv, 
				_ => SchematicsFormat.None, 
			};
			Exception ex = null;
			try
			{
				switch (m_mode)
				{
				case SceneLoaderMode.Read:
					Load(text, format);
					break;
				case SceneLoaderMode.Write:
					Save(text, format);
					break;
				}
			}
			catch (Exception ex2)
			{
				Debug.LogException(ex2);
				ex = ex2;
			}
			if (ex == null)
			{
				m_state.text = string.Empty;
				Close();
			}
			else
			{
				m_state.text = $"<color=#FF8080>ERROR: {ex.GetType()}</color>";
			}
		}

		private void Load(string path, SchematicsFormat format)
		{
			LoadFromPath(path, format);
		}

		/// <summary>
		/// Loads <paramref name="path"/> into a new scene and records it as the session's
		/// scene, so the next launch can put it back. A blank path asks for an empty scene
		/// and forgets the recorded one, exactly like the dialog's blank input.
		/// </summary>
		internal static void LoadFromPath(string path, SchematicsFormat format)
		{
			if (string.IsNullOrEmpty(path))
			{
				PartManager.Instance.LoadEmptyScene("Empty");
				LevelSession.ClearScene();
				return;
			}
			using Stream stream = FileAPI.ReadFileAsStream(path);
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
			PartManager.Instance.LoadScene(stream, fileNameWithoutExtension, format);
			LevelSession.RecordScene(path, format);
		}

		private void Save(string path, SchematicsFormat format)
		{
			if (PartManager.Instance.HasActiveScene())
			{
				using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
				{
					int sceneID = PartManager.Instance.ActiveScene.SceneID;
					PartManager.Instance.SaveScene(stream, sceneID, format);
				}
			}
		}

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
