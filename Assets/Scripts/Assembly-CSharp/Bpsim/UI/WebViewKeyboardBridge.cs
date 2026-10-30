using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Vuplex.WebView;
// Vuplex 也有一个 Keyboard 类型；本类要的是新输入系统的那个。
using Keyboard = UnityEngine.InputSystem.Keyboard;

namespace Bpsim.UI
{
	/// <summary>
	/// 新输入系统（<c>activeInputHandler: 1</c>）下的 webview 键盘桥。
	/// </summary>
	/// <remarks>
	/// Vuplex 自带的键盘支持走旧 Input：<c>Core/Scripts/Internal/NativeKeyboardListener.cs</c>
	/// 每帧读 <c>Input.inputString</c> / <c>Input.GetKey</c> / <c>Input.compositionString</c>，
	/// 本工程只有新输入系统，这些 API 一调用就抛 <c>InvalidOperationException</c>
	/// （同因见 <c>ModManager</c> 里 F10 的 try/catch）。所以两个面板宿主都把
	/// <c>CanvasWebViewPrefab.KeyboardEnabled</c> 设成 false —— 代价是网页里的
	/// <c>&lt;input&gt;</c> 一个字符都收不到（蒙皮/建造/网易云面板的数值框全部不能输入）。
	///
	/// 本类用 <see cref="Keyboard"/> 顶替那条链路：
	/// 文本 = <c>Keyboard.onTextInput</c>；中文 = <c>onIMECompositionChange</c> + <see cref="IWithIme"/>；
	/// 功能键 = 手工映射后走 <see cref="IWithKeyDownAndUp"/> / <see cref="IWebView.SendKey"/>；
	/// Ctrl+A/C/X/V = <see cref="IWebView.SelectAll"/>/<see cref="IWebView.Copy"/>/<see cref="IWebView.Cut"/>/<see cref="IWebView.Paste"/>
	/// （比注入组合键可靠，直接调用 CEF 的原生编辑命令）。
	///
	/// 焦点模型：点过一次该网页才算获得焦点（贴一张“命中区域”），点窗口外自动失焦，
	/// 同一时刻只有一个网页吃键盘。这样不会在没有焦点时和 Unity 自己的输入抢按键。
	/// </remarks>
	public sealed class WebViewKeyboardBridge : MonoBehaviour
	{
		private static readonly List<WebViewKeyboardBridge> s_instances = new List<WebViewKeyboardBridge>();

		private BaseWebViewPrefab m_prefab;
		private IWebView m_view;
		private IWithKeyDownAndUp m_keyDownUp;
		private IWithIme m_ime;
		private Keyboard m_keyboard;
		private bool m_focused;
		private bool m_pointerInside;
		private bool m_subscribed;
		private string m_composition = string.Empty;
		private int m_commitFrame = -100;

		public bool IsFocused => m_focused;

		public IWebView View => m_view != null ? m_view : (m_prefab != null ? m_prefab.WebView : null);

		/// <summary>给 <paramref name="prefab"/> 挂上键盘桥（同物体上只有一个）。</summary>
		public static WebViewKeyboardBridge Attach(BaseWebViewPrefab prefab)
		{
			if (prefab == null)
			{
				return null;
			}
			WebViewKeyboardBridge bridge = prefab.gameObject.GetComponent<WebViewKeyboardBridge>();
			if (bridge == null)
			{
				bridge = prefab.gameObject.AddComponent<WebViewKeyboardBridge>();
			}
			bridge.Bind(prefab);
			return bridge;
		}

		private void Bind(BaseWebViewPrefab prefab)
		{
			if (m_prefab == prefab)
			{
				return;
			}
			Unhook();
			m_prefab = prefab;
			m_view = null;
			if (prefab == null)
			{
				return;
			}
			prefab.Clicked += OnClicked;
			prefab.PointerEntered += OnPointerEntered;
			prefab.PointerExited += OnPointerExited;
		}

		private void OnEnable()
		{
			if (!s_instances.Contains(this))
			{
				s_instances.Add(this);
			}
		}

		private void OnDisable()
		{
			SetFocused(false);
			s_instances.Remove(this);
		}

		private void OnDestroy()
		{
			Unhook();
			SetFocused(false);
			s_instances.Remove(this);
		}

		private void Unhook()
		{
			if (m_prefab != null)
			{
				m_prefab.Clicked -= OnClicked;
				m_prefab.PointerEntered -= OnPointerEntered;
				m_prefab.PointerExited -= OnPointerExited;
			}
		}

		private void OnClicked(object sender, ClickedEventArgs eventArgs) => SetFocused(true);

		private void OnPointerEntered(object sender, System.EventArgs eventArgs) => m_pointerInside = true;

		private void OnPointerExited(object sender, System.EventArgs eventArgs) => m_pointerInside = false;

		private void Update()
		{
			if (m_prefab == null)
			{
				return;
			}
			if (m_view == null)
			{
				m_view = m_prefab.WebView;
				if (m_view == null)
				{
					return;
				}
				m_keyDownUp = m_view as IWithKeyDownAndUp;
				m_ime = m_view as IWithIme;
			}
			// 只保留“当前绑定的键盘设备”的订阅（手柄热插拔/设备切换时重订）。
			if (m_focused && m_subscribed && Keyboard.current != m_keyboard)
			{
				UnsubscribeKeyboard();
				SubscribeKeyboard();
			}
			if (m_focused && !m_prefab.gameObject.activeInHierarchy)
			{
				SetFocused(false);
				return;
			}
			if (!m_focused)
			{
				return;
			}
			// 点到网页以外的地方就失焦，免得继续吞按键。
			Mouse mouse = Mouse.current;
			if (mouse != null && mouse.leftButton.wasPressedThisFrame && !m_pointerInside)
			{
				SetFocused(false);
				return;
			}
			PollSpecialKeys();
		}

		public void SetFocused(bool focused)
		{
			if (m_focused == focused)
			{
				return;
			}
			m_focused = focused;
			if (focused)
			{
				// 同一时刻只有一个网页吃键盘。
				for (int i = 0; i < s_instances.Count; i++)
				{
					WebViewKeyboardBridge other = s_instances[i];
					if (other != null && other != this)
					{
						other.SetFocused(false);
					}
				}
				m_pointerInside = true;
				SubscribeKeyboard();
				m_view?.SetFocused(true);
				Keyboard keyboard = Keyboard.current;
				if (keyboard != null)
				{
					try
					{
						keyboard.SetIMEEnabled(true);
					}
					catch (System.Exception)
					{
						// 某些后端不支持 IME 命令，忽略即可（文本输入不受影响）。
					}
				}
			}
			else
			{
				UnsubscribeKeyboard();
				CancelComposition();
				Keyboard keyboard = Keyboard.current;
				if (keyboard != null)
				{
					try
					{
						keyboard.SetIMEEnabled(false);
					}
					catch (System.Exception) { }
				}
				m_view?.SetFocused(false);
			}
		}

		private void SubscribeKeyboard()
		{
			m_keyboard = Keyboard.current;
			if (m_keyboard == null || m_subscribed)
			{
				return;
			}
			m_keyboard.onTextInput += OnTextInput;
			m_keyboard.onIMECompositionChange += OnImeComposition;
			m_subscribed = true;
		}

		private void UnsubscribeKeyboard()
		{
			if (m_keyboard != null && m_subscribed)
			{
				m_keyboard.onTextInput -= OnTextInput;
				m_keyboard.onIMECompositionChange -= OnImeComposition;
			}
			m_subscribed = false;
			m_keyboard = null;
		}

		/// <summary>可打印字符：直接当作一次按键派发（CEF 会插到焦点元素里）。</summary>
		private void OnTextInput(char c)
		{
			if (!m_focused || m_view == null)
			{
				return;
			}
			// 输入法提交后的一两帧里 onTextInput 可能把同一段文字再送一遍，丢掉。
			if (Time.frameCount <= m_commitFrame + 1)
			{
				return;
			}
			if (c == '\b' || c == '\n' || c == '\r' || c == '\t' || c == '\u007f')
			{
				return; // 交给功能键路径，避免重复
			}
			SendKey(c.ToString());
		}

		private void OnImeComposition(IMECompositionString compositionString)
		{
			if (!m_focused || m_ime == null)
			{
				return;
			}
			string text = compositionString.ToString();
			if (!string.IsNullOrEmpty(text))
			{
				m_composition = text;
				m_ime.SetImeComposition(text);
				return;
			}
			// 组合串被清空：提交或取消，这里是提交。
			if (m_composition.Length > 0)
			{
				m_ime.FinishImeComposition(m_composition);
				m_composition = string.Empty;
				m_commitFrame = Time.frameCount;
			}
		}

		private void CancelComposition()
		{
			m_composition = string.Empty;
			if (m_ime != null)
			{
				m_ime.CancelImeComposition();
			}
		}

		private void PollSpecialKeys()
		{
			Keyboard keyboard = Keyboard.current;
			if (keyboard == null || m_view == null)
			{
				return;
			}
			bool ctrl = keyboard.ctrlKey.isPressed || keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
			bool alt = keyboard.altKey.isPressed || keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed;
			// 快捷键走原生编辑命令，别注入按键（CEF 的剪贴板命令更稳）。
			if (ctrl && !alt)
			{
				if (keyboard.aKey.wasPressedThisFrame) { m_view.SelectAll(); return; }
				if (keyboard.cKey.wasPressedThisFrame) { m_view.Copy(); return; }
				if (keyboard.xKey.wasPressedThisFrame) { m_view.Cut(); return; }
				if (keyboard.vKey.wasPressedThisFrame) { m_view.Paste(); return; }
			}
			if (keyboard.backspaceKey.wasPressedThisFrame && !ctrl) { SendKey("Backspace"); }
			if (keyboard.deleteKey.wasPressedThisFrame) { SendKey("Delete"); }
			if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) { SendKey("Enter"); }
			if (keyboard.tabKey.wasPressedThisFrame) { SendKey("Tab"); }
			if (keyboard.escapeKey.wasPressedThisFrame) { SendKey("Escape"); }
			if (keyboard.leftArrowKey.wasPressedThisFrame) { SendKey("ArrowLeft"); }
			if (keyboard.rightArrowKey.wasPressedThisFrame) { SendKey("ArrowRight"); }
			if (keyboard.upArrowKey.wasPressedThisFrame) { SendKey("ArrowUp"); }
			if (keyboard.downArrowKey.wasPressedThisFrame) { SendKey("ArrowDown"); }
			if (keyboard.homeKey.wasPressedThisFrame) { SendKey("Home"); }
			if (keyboard.endKey.wasPressedThisFrame) { SendKey("End"); }
			if (keyboard.pageUpKey.wasPressedThisFrame) { SendKey("PageUp"); }
			if (keyboard.pageDownKey.wasPressedThisFrame) { SendKey("PageDown"); }
		}

		private void SendKey(string key)
		{
			if (m_view == null || string.IsNullOrEmpty(key))
			{
				return;
			}
			try
			{
				if (m_keyDownUp != null)
				{
					m_keyDownUp.KeyDown(key, KeyModifier.None);
					m_keyDownUp.KeyUp(key, KeyModifier.None);
				}
				else
				{
					m_view.SendKey(key);
				}
			}
			catch (System.Exception ex)
			{
				Debug.LogWarning("[WebViewKeys] SendKey(" + key + ") failed: " + ex.Message);
			}
		}

		/// <summary>QA：当前所有键盘桥的状态。</summary>
		public static string Describe()
		{
			StringBuilder sb = new StringBuilder();
			sb.Append("bridges[").Append(s_instances.Count).Append("] ");
			for (int i = 0; i < s_instances.Count; i++)
			{
				WebViewKeyboardBridge b = s_instances[i];
				if (b == null)
				{
					continue;
				}
				sb.Append(b.m_prefab != null ? b.m_prefab.name : "null")
					.Append(b.m_focused ? "=focused" : "=idle")
					.Append(b.View == null ? "(view null)" : "(view ok)")
					.Append("; ");
			}
			return sb.ToString();
		}
	}
}
