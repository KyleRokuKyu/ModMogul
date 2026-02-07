using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModMogul
{
	public class Questify : MonoBehaviour
	{
		private static ResearchItemButton templateButton = null;
		private static List<ShopItemResearchItemDefinition> researchDefinitions;

		public void RegisterShopResearch (string questName, string iconPath, int techTreeXPosition, int techTreeYPosition, List<ShopItemDefinition> unlockedItems, int ticketCost, float moneyCost = 0, List<ResearchItemDefinition> prerequisiteResearch = null)
		{
			if (prerequisiteResearch == null) prerequisiteResearch = new();
			StartCoroutine(WaitForTemplate(questName, iconPath, techTreeXPosition, techTreeYPosition, unlockedItems, ticketCost, moneyCost, prerequisiteResearch));
		}

		IEnumerator WaitForTemplate (string questName, string iconPath, int techTreeXPosition, int techTreeYPosition, List<ShopItemDefinition> unlockedItems, int ticketCost, float moneyCost, List<ResearchItemDefinition> prerequisiteResearch)
		{
			while (templateButton == null)
			{
				templateButton = Object.FindFirstObjectByType<ResearchItemButton>();
				yield return null;
			}

			researchDefinitions ??= new();

			ShopItemResearchItemDefinition tempDef = ScriptableObject.CreateInstance<ShopItemResearchItemDefinition>();
			tempDef.ShopItemDefinitions = unlockedItems;
			tempDef.PrerequisiteResearch = prerequisiteResearch;
			ResearchItemDefinitionHarmonyAccess.SetValues(tempDef, ticketCost, moneyCost, questName);

			researchDefinitions.Add(tempDef);

			// Create ResearchItemButton
			GameObject researchButton = Object.Instantiate(templateButton.gameObject);
			researchButton.name = "Research Button - " + questName;
			researchButton.transform.SetParent(templateButton.transform.parent);
			researchButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(70 + techTreeXPosition * 100, -techTreeYPosition * 100);
			researchButton.GetComponent<RectTransform>().localRotation = Quaternion.identity;
			researchButton.GetComponent<RectTransform>().localScale = Vector3.one;
			researchButton.GetComponent<Button>().onClick.RemoveAllListeners();
			researchButton.GetComponent<Button>().onClick.AddListener(() => researchButton.GetComponent<ResearchItemButton>().OnPressed());
			researchButton.GetComponent<ResearchItemButton>().Initialize(FindFirstObjectByType<ResearchTreeUI>());
			researchButton.GetComponent<ResearchItemButton>().ResearchItemDefinition = tempDef;
			foreach (TMP_Text t in researchButton.GetComponentsInChildren<TMP_Text>())
			{
				if (t.name == "NameText") t.text = questName;
				else if (t.name == "RequiredTickets") t.text = "¤" + ticketCost.ToString();
			}
			foreach (Image i in researchButton.GetComponentsInChildren<Image>())
			{
				if (i.name == "Icon") i.sprite = Utility.ImportSprite(iconPath);
			}
		}
	}

	public static class ResearchItemDefinitionHarmonyAccess
	{
		private static readonly AccessTools.FieldRef<ShopItemResearchItemDefinition, int> TicketsRef =
			AccessTools.FieldRefAccess<ShopItemResearchItemDefinition, int>("_researchTicketsCost");

		private static readonly AccessTools.FieldRef<ShopItemResearchItemDefinition, float> MoneyRef =
			AccessTools.FieldRefAccess<ShopItemResearchItemDefinition, float>("_moneyCost");

		private static readonly AccessTools.FieldRef<ShopItemResearchItemDefinition, string> OverrideNameRef =
			AccessTools.FieldRefAccess<ShopItemResearchItemDefinition, string>("_overrideDisplayName");

		public static void SetValues(ShopItemResearchItemDefinition def, int tickets, float money, string name)
		{
			TicketsRef(def) = tickets;
			MoneyRef(def) = money;
			OverrideNameRef(def) = name;
		}
	}
}
