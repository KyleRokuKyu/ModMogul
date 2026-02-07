using BepInEx;
using HarmonyLib;
using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ModMogul
{
	[BepInPlugin("modmogul.core", "Mod Mogul", "0.4.0")]
	public class ModMogulPlugin : BaseUnityPlugin
	{
		static SettingsSetter _settingsSetter;
		public static Shader fadeShader;
		public static Questify Questify;

		private void Start ()
		{
			new Harmony("modmogul.core").PatchAll();

			string assetPath = Path.Combine(
									Paths.PluginPath,   // BepInEx/plugins
									"ModMogul",
									"modmogul.assets");
			AssetBundle bundle = AssetBundle.LoadFromFile(assetPath);
			fadeShader = bundle.LoadAsset<Shader>("Fade");

			StartCoroutine(WaitForMenu());
			_settingsSetter = gameObject.AddComponent<SettingsSetter>();
			Questify = gameObject.AddComponent<Questify>();

			SceneManager.activeSceneChanged += OnSceneChanged;
		}

		private void OnSceneChanged (Scene current, Scene next)
		{
			if (next.name == "MainMenu")
			{
				StartCoroutine(WaitForMenu());
			}
		}

		private IEnumerator WaitForMenu ()
		{
			Transform menuUI = null;
			while (menuUI == null)
			{
				foreach (Image c in FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
				{
					if (c.gameObject.name == "MainMenu")
					{
						menuUI = c.transform;
					}
				}
				yield return null;
			}

			Transform content;
			RectTransform modListRectTransform = ModListUI.SpawnScrollRect(menuUI, out content).GetComponent<RectTransform>();
			modListRectTransform.anchorMax = new Vector2(0.95f, 0.8f);
			modListRectTransform.anchorMin = new Vector2(0.75f, 0.3f);
			modListRectTransform.offsetMin = Vector2.zero;
			modListRectTransform.offsetMax = Vector2.zero;

			GameObject modListGo = new GameObject("ModList", typeof(RectTransform), typeof(TextMeshProUGUI));
			TMP_Text modsText = modListGo.GetComponent<TMP_Text>();
			modsText.horizontalAlignment = HorizontalAlignmentOptions.Left;
			modsText.fontSize = 14;
			modsText.transform.SetParent(content, false);
			modsText.color = new Color(0.7765f, 0.6196f, 0.2588f);
			modsText.GetComponent<RectTransform>().anchorMin = Vector2.zero;
			modsText.GetComponent<RectTransform>().anchorMax = Vector2.one;
			modsText.GetComponent<RectTransform>().offsetMin = Vector2.zero;
			modsText.GetComponent<RectTransform>().offsetMax = Vector2.zero;

			ModListUI.Populate(modsText);

			string path = Path.Combine(
							Paths.PluginPath,   // BepInEx/plugins
							"ModMogul",
							"ModMogul_Logo.png");
			Sprite modMogulLogo = Utility.ImportSprite(path);

			foreach (Image i in FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				if (i.gameObject.name == "Logo")
				{
					i.sprite = modMogulLogo;
					break;
				}
			}

			yield return null;
			GameObject.FindFirstObjectByType<SavingLoadingManager>().SendMessage("Awake");

			//StartCoroutine(WaitForReturnToMenu());
		}

		/*
		IEnumerator WaitForReturnToMenu ()
		{
			while (SceneManager.GetActiveScene() == null)
			{
				yield return null;
			}
			while (SceneManager.GetActiveScene().name.ToLower() != "gameplay")
			{
				yield return null;
			}
			while (SceneManager.GetActiveScene().name.ToLower() == "gameplay")
			{
				yield return null;
			}
			StartCoroutine(WaitForMenu());
		}
		*/

		public static SettingsSetter SettingSetter
		{
			get { return _settingsSetter; }
		}
	}
}