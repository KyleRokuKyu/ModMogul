using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ModMogul
{
	public static class Itemizer
	{

		[Serializable]
		public struct ItemSpec
		{
			public int BlockID;
			public string InternalName;
			public string DisplayName;
			public string Description;
			public int Price;
			public string ShopCategory;
			public int MaxStackSize;
			public bool IsLockedByDefault;
			public string QFunction;
			public string IconPath;

			public ItemSpec(
				int blockID,
				string internalName,
				string displayName,
				string description,
				int price,
				string shopCategory,
				int maxStackSize = 999,
				bool isLockedByDefault = false,
				string qFunction = "Mirror")
			{
				BlockID = blockID;
				InternalName = internalName;
				DisplayName = displayName;
				Description = description;
				Price = price;
				ShopCategory = shopCategory;
				MaxStackSize = maxStackSize;
				IsLockedByDefault = isLockedByDefault;
				QFunction = qFunction;
				IconPath = null;
			}
		}

		public sealed class ItemRuntime
		{
			public ItemSpec Spec;

			// Registration-time
			public BuildingObject Prefab;

			// Start-time
			public BuildingInventoryDefinition Def;
			public ShopItemDefinition ShopItemDef;
		}

		public static readonly Dictionary<int, ItemRuntime> _itemsByBlockId = new();

		private static readonly object _lock = new();
		private static readonly HashSet<int> _injectedEconomyIds = new();
		private static EconomyManager _lastEconomy;
		private static GameObject prefabHolder;

		[HarmonyPatch(typeof(EconomyManager), "Start")]
		private static class Patch_EconomyManager_Start_Postfix
		{
			private static void Postfix(EconomyManager __instance)
			{
				Debug.Log("[ModMogul.Itemizer] Injecting Custom Items into EconomyManager");
				TryInjectAllIntoEconomy(__instance);
			}
		}

		public static BuildingObject RegisterItem(ItemSpec spec)
		{
			ValidateSpec(spec);

			ItemRuntime rt;
			lock (_lock)
			{
				if (!_itemsByBlockId.TryGetValue(spec.BlockID, out rt))
				{
					rt = new ItemRuntime();
					_itemsByBlockId.Add(spec.BlockID, rt);
				}

				rt.Spec = spec;
				rt.Prefab = CreateOrGetPrefabTemplate(spec.BlockID, spec.InternalName, rt.Prefab);
			}

			// Late load support: if economy already exists, inject right now
			var econ = _lastEconomy ?? Singleton<EconomyManager>.Instance;
			if (econ != null)
				TryInjectAllIntoEconomy(econ);
			RegisterWithSaveLoadManager(rt.Prefab.gameObject);

			return rt.Prefab;
		}

		public static BuildingObject RegisterItem(ItemSpec spec, string iconPath)
		{
			ValidateSpec(spec);

			// IMPORTANT: ItemSpec is a struct (value type). We set the field on this local copy,
			// then store the whole struct into rt.Spec so the iconPath persists per-item.
			spec.IconPath = iconPath;

			ItemRuntime rt;
			lock (_lock)
			{
				if (!_itemsByBlockId.TryGetValue(spec.BlockID, out rt))
				{
					rt = new ItemRuntime();
					_itemsByBlockId.Add(spec.BlockID, rt);
				}

				rt.Spec = spec;
				rt.Prefab = CreateOrGetPrefabTemplate(spec.BlockID, spec.InternalName, rt.Prefab);
			}

			// Late load support: if economy already exists, inject right now
			var econ = _lastEconomy ?? Singleton<EconomyManager>.Instance;
			if (econ != null)
				TryInjectAllIntoEconomy(econ);
			RegisterWithSaveLoadManager(rt.Prefab.gameObject);

			return rt.Prefab;
		}

		private static void RegisterWithSaveLoadManager(GameObject savableObjectPrefab)
		{
			GameObject.FindFirstObjectByType<SavingLoadingManager>().AllSavableObjectPrefabs.Add(savableObjectPrefab);
		}

		private static void ValidateSpec(ItemSpec spec)
		{
			if (spec.BlockID <= 0) throw new ArgumentException("BlockID must be > 0");
			if (string.IsNullOrWhiteSpace(spec.InternalName)) throw new ArgumentException("InternalName required");
			if (string.IsNullOrWhiteSpace(spec.DisplayName)) throw new ArgumentException("DisplayName required");
			if (string.IsNullOrWhiteSpace(spec.ShopCategory)) throw new ArgumentException("ShopCategory required");
		}

		// Creates a hidden prefab template if it doesn't exist yet.
		// Mods can customize it immediately.
		internal static BuildingObject CreateOrGetPrefabTemplate(int blockID, string internalName, BuildingObject existing)
		{
			if (existing != null) return existing;

			var go = new GameObject(internalName + "_BuildingPrefab");

			// Required children the game expects
			var placement = new GameObject("BuildingPlacementColliderObject");
			placement.transform.SetParent(go.transform, false);

			var spawn = new GameObject("BuildingCrateSpawnPoint");
			spawn.transform.SetParent(go.transform, false);

			// Minimal visible geometry by default (modders can replace)
			var prim = GameObject.CreatePrimitive(PrimitiveType.Cube);
			var mf = go.GetComponent<MeshFilter>() ?? go.AddComponent<MeshFilter>();
			mf.sharedMesh = prim.GetComponent<MeshFilter>().sharedMesh;

			var mr = go.GetComponent<MeshRenderer>() ?? go.AddComponent<MeshRenderer>();
			mr.sharedMaterial = prim.GetComponent<MeshRenderer>().sharedMaterial;

			UnityEngine.Object.Destroy(prim);

			// Colliders
			//var mainCol = go.GetComponent<BoxCollider>() ?? go.AddComponent<BoxCollider>();
			//mainCol.size *= 0.99f;

			//var placementCol = placement.GetComponent<BoxCollider>() ?? placement.AddComponent<BoxCollider>();
			//placementCol.size *= 0.99f;

			// Keep inert and out of view
			//go.transform.position = Vector3.up * -1000f;
			//UnityEngine.Object.DontDestroyOnLoad(go);
			//go.hideFlags = HideFlags.HideAndDontSave;

			if (prefabHolder == null)
			{
				prefabHolder = new GameObject("Itemizer_PrefabHolder");
				prefabHolder.transform.position = -Vector3.up * 1000;
				UnityEngine.Object.DontDestroyOnLoad(prefabHolder);
				prefabHolder.hideFlags = HideFlags.HideAndDontSave;
				prefabHolder.SetActive(false);
			}

			go.transform.parent = prefabHolder.transform;

			var prefab = go.AddComponent<BuildingObject>();
			prefab.SavableObjectID = (SavableObjectID)blockID;
			prefab.BuildingPlacementColliderObject = placement;
			prefab.BuildingCrateSpawnPoint = spawn.transform;

			// Safe to attempt; may no-op if SavingLoadingManager not ready yet
			RegisterSavableLookupIfPossible(blockID, prefab);
			return prefab;
		}

		/// <summary>
		/// Injects all registered items into the given EconomyManager instance once.
		/// IMPORTANT: No ItemSpec parameter here — each item uses its own rt.Spec.
		/// </summary>
		internal static void TryInjectAllIntoEconomy(EconomyManager econ)
		{
			if (econ == null) return;

			_lastEconomy = econ;

			int econId = econ.GetInstanceID();
			lock (_lock)
			{
				if (!_injectedEconomyIds.Add(econId))
					return; // already injected into this instance
			}

			var allCats = (List<ShopCategory>)AccessTools.Field(typeof(EconomyManager), "_allShopCategories")?.GetValue(econ);
			if (allCats == null) return;

			List<ItemRuntime> snapshot;
			lock (_lock) snapshot = _itemsByBlockId.Values.ToList();

			foreach (var rt in snapshot)
			{
				try
				{
					EnsureInjectedIntoEconomy(econ, allCats, rt);
				}
				catch (Exception e)
				{
					Debug.LogError($"[ModMogul.Itemizer] Inject into EconomyManager failed for {rt?.Spec.InternalName} ({rt?.Spec.BlockID}): {e}");
				}
			}
		}

		private static void EnsureInjectedIntoEconomy(EconomyManager econ, List<ShopCategory> allCats, ItemRuntime rt)
		{
			var s = rt.Spec;

			// If mod registered but never asked for prefab (unlikely), make sure we have one.
			rt.Prefab = CreateOrGetPrefabTemplate(s.BlockID, s.InternalName, rt.Prefab);

			if (rt.Def == null)
			{
				rt.Def = ScriptableObject.CreateInstance<BuildingInventoryDefinition>();
				rt.Def.name = s.InternalName + "_Definition";
				UnityEngine.Object.DontDestroyOnLoad(rt.Def);
				rt.Def.hideFlags = HideFlags.HideAndDontSave;

				rt.Def.Name = s.DisplayName;
				rt.Def.Description = s.Description;
				rt.Def.MaxInventoryStackSize = s.MaxStackSize;
				rt.Def.QButtonFunction = s.QFunction;

				rt.Def.BuildingPrefabs = new List<BuildingObject> { rt.Prefab };

				// Use the per-item iconPath stored in rt.Spec
				rt.Def.InventoryIcon = Utility.ImportSprite(s.IconPath);
				rt.Def.ProgrammerInventoryIcon = Utility.ImportSprite(s.IconPath);

				rt.Def.PackedPrefab = GetABuildingCrate(allCats);
				rt.Def.UseReverseRotationDirection = false;

				rt.Prefab.Definition = rt.Def;

				// quick sanity
				if (rt.Def.GetMainPrefab() == null)
					Debug.LogError($"[ModMogul.Itemizer] {s.InternalName} def has no main prefab after init.");
			}

			if (rt.ShopItemDef == null)
			{
				rt.ShopItemDef = ScriptableObject.CreateInstance<ShopItemDefinition>();
				rt.ShopItemDef.name = s.InternalName + "_ShopItem";
				UnityEngine.Object.DontDestroyOnLoad(rt.ShopItemDef);
				rt.ShopItemDef.hideFlags = HideFlags.HideAndDontSave;

				rt.ShopItemDef.UseNameAndDescriptionOfBuildingDefinition = true;
				rt.ShopItemDef.BuildingInventoryDefinition = rt.Def;
				rt.ShopItemDef.Price = s.Price;
				rt.ShopItemDef.IsLockedByDefault = s.IsLockedByDefault;
				rt.ShopItemDef.IsDummyItem = false;
				rt.ShopItemDef.PrefabToSpawn = null;
			}
			else
			{
				// In case of hot reload or late def creation
				rt.ShopItemDef.BuildingInventoryDefinition = rt.Def;
				rt.ShopItemDef.Price = s.Price;
				rt.ShopItemDef.IsLockedByDefault = s.IsLockedByDefault;
			}

			RegisterSavableLookupIfPossible(s.BlockID, rt.Prefab);

			SetupCategory(econ, allCats, rt.ShopItemDef, s.ShopCategory);
		}

		static void SetupCategory(EconomyManager __instance, List<ShopCategory> allCats, ShopItemDefinition def, string shopCategory)
		{
			if (allCats == null) return;

			var cat = allCats.FirstOrDefault(c => c != null && c.CategoryName == shopCategory && !c.IsAnyHolidayCategory());
			if (cat == null)
			{
				cat = new ShopCategory
				{
					CategoryName = shopCategory,
					ShopItemDefinitions = new List<ShopItemDefinition>(),
					ShopItems = new List<ShopItem>(),
					DontShowIfAllItemsAreLocked = false,
					HolidayType = HolidayType.None
				};
				allCats.Add(cat);
			}

			cat.ShopItemDefinitions ??= new List<ShopItemDefinition>();
			cat.ShopItems ??= new List<ShopItem>();

			// Def list
			if (!cat.ShopItemDefinitions.Contains(def))
				cat.ShopItemDefinitions.Add(def);

			// ShopItem list
			var existing = cat.ShopItems.FirstOrDefault(si => si != null && si.Definition == def);
			if (existing == null)
			{
				existing = new ShopItem(def);
				cat.ShopItems.Add(existing);
			}

			existing.IsLocked = false;
			__instance.UnlockShopItem(def);

			// Economy AllShopItems list
			if (!__instance.AllShopItems.Any(si => si != null && si.Definition == def))
				__instance.AllShopItems.Add(existing);

			// Keep definition set updated
			var defSetField = AccessTools.Field(typeof(EconomyManager), "_allShopItemDefinitions");
			if (defSetField?.GetValue(__instance) is HashSet<ShopItemDefinition> defSet)
				defSet.Add(def);
		}

		public static void RegisterSavableLookupIfPossible(int blockID, BuildingObject prefab)
		{
			if (prefab == null) return;

			var slm = Singleton<SavingLoadingManager>.Instance;
			if (slm == null) return;

			var lookup = (Dictionary<SavableObjectID, GameObject>)
				AccessTools.Field(typeof(SavingLoadingManager), "_lookup")?.GetValue(slm);

			if (lookup == null) return;

			lookup[(SavableObjectID)blockID] = prefab.gameObject;
		}

		private static BuildingCrate GetABuildingCrate(List<ShopCategory> allCats)
		{
			if (allCats == null) return null;

			foreach (ShopCategory allCat in allCats)
			{
				var list = allCat?.ShopItemDefinitions;
				if (list == null) continue;

				foreach (ShopItemDefinition item in list)
				{
					var bid = item?.BuildingInventoryDefinition;
					if (bid == null) continue;

					var main = bid.GetMainPrefab();
					if (main == null) continue;

					var component = main.GetComponentInChildren<BuildingObject>(includeInactive: true);
					if (component == null) continue;

					if (component.GetSavableObjectID() == SavableObjectID.ConveyorStraight)
						return bid.PackedPrefab;
				}
			}
			return null;
		}
	}
}
