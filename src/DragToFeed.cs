using HarmonyLib;

// Drag to Feed
//
// Feeding animals normally means opening the cage, opening the feed dispenser inside it and
// putting the food in. With this mod, food dropped on an animal cage or a recirculating
// aquaculture system goes straight into the feed dispenser slotted in that machine.
//
// Whether the food is then blended is the dispenser's own Auto Blend setting (right click on
// a dispenser), exactly as if the food had been dropped on the dispenser itself. With Auto
// Blend on, the game blends what is in the dispenser each time something is added, so a
// selection larger than the dispenser's storage still goes through, one item at a time.
//
// How: every drop asks its target item for a free slot. While the player is dragging, a cage
// or aquaculture system with a dispenser answers that question for food with a slot of the
// dispenser's storage instead of its own. The move itself stays the game's.
namespace DragToFeed
{
	public class Mod : IMod
	{
		public void Init(ModManifest manifest)
		{
		}

		public void OnEnable()
		{
		}

		public void OnDisable()
		{
			Drag.Reset();
		}
	}

	internal static class Feed
	{
		// What a dispenser turns into feed directly. Its storage also takes animals and fish, but
		// those dropped on a cage or a tank are meant to live there.
		public static bool IsFood(GameItem item)
		{
			return item != null && item.IsTag("CALORIE_VALUE_TAG") && !item.IsTag("ANIMAL_ITEM_TAG") && !AquacultureHelper.IsFish(item);
		}

		// The storage of the feed dispenser slotted in an animal cage or an aquaculture system.
		public static GameGridInventory DispenserStorageIn(GameItem machine)
		{
			if (machine == null)
			{
				return null;
			}
			GameSlotInventory slot = null;
			if (machine.IsTag("CAGE_TAG"))
			{
				slot = AnimalCage.GetDispenserSlot(machine);
			}
			else if (machine.IsTag("RAS_TAG"))
			{
				slot = MachineRAS.GetDispenserSlot(machine);
			}
			GameItem dispenser = slot?.childItem;
			if (dispenser == null || !dispenser.IsTag("FEED_DISPENSER_TAG"))
			{
				return null;
			}
			return MachineFeedDispenser.GetStorageGrid(dispenser);
		}
	}

	// Whether one of the game's drag routines is running right now, and whether it is the drop.
	internal static class Drag
	{
		private static int running;

		private static int dropping;

		public static bool Running => running > 0;

		public static bool Dropping => dropping > 0;

		public static void Enter(bool drop)
		{
			running++;
			if (drop)
			{
				dropping++;
			}
		}

		public static void Leave(bool drop)
		{
			if (running > 0)
			{
				running--;
			}
			if (drop && dropping > 0)
			{
				dropping--;
			}
		}

		public static void Reset()
		{
			running = 0;
			dropping = 0;
		}
	}

	// ------------------------------------------------------------------ the drag in progress

	// Picking an item up highlights everything that could take it.
	[HarmonyPatch(typeof(ItemMouseDragHandler), "StartDrag")]
	internal static class SingleStartPatch
	{
		private static void Prefix()
		{
			Drag.Enter(false);
		}

		private static void Finalizer()
		{
			Drag.Leave(false);
		}
	}

	[HarmonyPatch(typeof(ItemMouseDragHandler), "Drag")]
	internal static class SingleHoverPatch
	{
		private static void Prefix()
		{
			Drag.Enter(false);
		}

		private static void Finalizer()
		{
			Drag.Leave(false);
		}
	}

	[HarmonyPatch(typeof(ItemMouseDragHandler), "EndDrag")]
	internal static class SingleDropPatch
	{
		private static void Prefix()
		{
			Drag.Enter(true);
		}

		private static void Finalizer()
		{
			Drag.Leave(true);
		}
	}

	[HarmonyPatch(typeof(ItemMultiSelectHandler), "UpdateDropTarget")]
	internal static class GroupHoverPatch
	{
		private static void Prefix()
		{
			Drag.Enter(false);
		}

		private static void Finalizer()
		{
			Drag.Leave(false);
		}
	}

	[HarmonyPatch(typeof(ItemMultiSelectHandler), "EndGroupDrag")]
	internal static class GroupDropPatch
	{
		private static void Prefix()
		{
			Drag.Enter(true);
		}

		private static void Finalizer()
		{
			Drag.Leave(true);
		}
	}

	// ------------------------------------------------------------------ where dropped food goes

	// A full cage still takes food when its dispenser can.
	[HarmonyPatch(typeof(GameItem), "MayHaveValidInventorySlot")]
	internal static class MayHaveSlotPatch
	{
		private static void Postfix(GameItem __instance, GameItem item, ref bool __result)
		{
			if (__result || !Drag.Running || !Feed.IsFood(item))
			{
				return;
			}
			GameGridInventory storage = Feed.DispenserStorageIn(__instance);
			if (storage != null)
			{
				__result = storage.MayHaveValidInventorySlot(item);
			}
		}
	}

	// Food dropped on a cage or tank with a dispenser gets a slot in the dispenser's storage, or
	// none when the dispenser has no room: it then stays where it was instead of landing in
	// the cage.
	[HarmonyPatch(typeof(GameItem), "TryFindOneValidInventorySlot")]
	internal static class FindSlotPatch
	{
		private static bool Prefix(GameItem __instance, GameItem item, ref SlotMarker __result)
		{
			if (!Drag.Running || !Feed.IsFood(item))
			{
				return true;
			}
			GameGridInventory storage = Feed.DispenserStorageIn(__instance);
			if (storage == null || item.parentInventory == storage)
			{
				return true;
			}
			SlotMarker slot = storage.TryFindOneValidInventorySlot(item);
			if ((slot == null || slot.numTransfer < 1) && Drag.Dropping && MachineFeedDispenser.FlushPendingAutoBlend(storage))
			{
				// With Auto Blend on, the food added a moment ago is still waiting for the
				// game's end-of-frame blend. The game's hoppers make room the same way.
				slot = storage.TryFindOneValidInventorySlot(item);
			}
			__result = slot;
			return false;
		}
	}
}
