using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.Automation;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Reflection.Metadata.Ecma335;

namespace Dropbox;
public unsafe static class ItemQueueUI
{
    public static List<QueueEntry> TradeQueue = [];
    public static Dictionary<ItemDescriptor, Box<int>> ItemQuantities = [];
    static bool OnlySelected = false;
    static string Filter = "";
    public static void Draw()
    {
        if(P.StopRequests.Count > 0)
        {
            ImGuiEx.TextWrapped(EColor.RedBright, $"Plugin is paused by {P.StopRequests.Print()}");
            if(ImGuiEx.IconButtonWithText(FontAwesomeIcon.Ban, "Resume"))
            {
                P.StopRequests.Clear();
            }
        }
        if (P.TaskManager.IsBusy)
        {
            if (ImGui.Button("Stop"))
            {
                P.TaskManager.Abort();
            }
            ImGuiEx.Text($"Processing task: \n{P.TaskManager.CurrentTaskName}");
            return;
        }
        ImGuiEx.Text("Select items to trade:");
        ImGuiEx.SetNextItemWidthScaled(200f);
        ImGui.InputTextWithHint("##filter", "Search", ref Filter, 100);
        ImGui.SameLine();
        ImGui.Checkbox("Show only selected", ref OnlySelected);
        //ImGui.SameLine();

        bool? selectedGil = (ItemQuantities.TryGetValue(new(1, false), out var gilc) && gilc.Value > 0) ? (gilc.Value == InventoryManager.Instance()->GetInventoryItemCount(1) ? true : null) : false;
        var selectedCrystals = Utils.CompareSelection(GetTradeableItems(false, true, false));
        var selectedItems = Utils.CompareSelection(GetTradeableItems(false, false, true));
        var selectedAll = Utils.CompareSelection(GetTradeableItems(true, true, true));
        var selectedGear = Utils.CompareSelection(GetTradeableGearItems());

        if (ImGuiEx.Checkbox("Gil", ref selectedGil))
        {
            if (selectedGil == false)
            {
                ItemQuantities[new(1, false)] = new(0);
            }
            else
            {
                ItemQuantities[new(1, false)] = new(InventoryManager.Instance()->GetInventoryItemCount(1));
            }
        }
        ImGui.SameLine();
        if (ImGuiEx.Checkbox("Items", ref selectedItems))
        {
            if (selectedItems == false)
            {
                foreach (var x in GetTradeableItems(false, false, true))
                {
                    ItemQuantities.Remove(x.Descriptor);
                }
            }
            else
            {
                foreach (var x in GetTradeableItems(false, false, true))
                {
                    ItemQuantities[x.Descriptor] = new((int)x.Count);
                }
            }
        }
        ImGui.SameLine();
        if (ImGuiEx.Checkbox("Crystals", ref selectedCrystals))
        {
            if (selectedCrystals == false)
            {
                foreach (var x in GetTradeableItems(false, true, false))
                {
                    ItemQuantities.Remove(x.Descriptor);
                }
            }
            else
            {
                foreach (var x in GetTradeableItems(false, true, false))
                {
                    ItemQuantities[x.Descriptor] = new((int)x.Count);
                }
            }
        }
        ImGui.SameLine();
        if(ImGuiEx.Checkbox("All", ref selectedAll))
        {
            if(selectedAll == false)
            {
                foreach(var x in GetTradeableItems(true, true, true))
                {
                    ItemQuantities.Remove(x.Descriptor);
                }
            }
            else
            {
                foreach(var x in GetTradeableItems(true, true, true))
                {
                    ItemQuantities[x.Descriptor] = new((int)x.Count);
                }
            }
        }
        ImGui.SameLine();
        if(ImGuiEx.Checkbox("Tradeable gear", ref selectedGear))
        {
            if(selectedGear == false)
            {
                foreach(var x in GetTradeableGearItems())
                {
                    ItemQuantities.Remove(x.Descriptor);
                }
            }
            else
            {
                foreach(var x in GetTradeableGearItems())
                {
                    ItemQuantities[x.Descriptor] = new((int)x.Count);
                }
            }
        }

        List<ImGuiEx.EzTableEntry> Entries = [];
        foreach (var x in GetTradeableItems())
        {
            if (!ItemQuantities.ContainsKey(x.Descriptor)) ItemQuantities[x.Descriptor] = new(0);
            var text = ExcelItemHelper.GetName((uint)x.Descriptor.Id);
            if (x.Descriptor.HQ) text += "";
            if (Filter != "" && !text.Contains(Filter, StringComparison.OrdinalIgnoreCase)) continue;
            if (OnlySelected && ItemQuantities[x.Descriptor].Value <= 0) continue;
            Entries.Add(new("##icon", () =>
            {
                if(ThreadLoadImageHandler.TryGetIconTextureWrap(ExcelItemHelper.Get(x.Descriptor.Id)?.Icon ?? 0, false, out var tex)) 
                {
                    ImGui.Image(tex.Handle, new Vector2(24));
                }
            }));
            Entries.Add(new("Quantity", () =>
            {
                ImGuiEx.SetNextItemWidthScaled(100f);
                if(x.Descriptor.Id == 1)
                {
                    if(ImGuiEx.InputFancyNumeric("##itemquantityGil", ref ItemQuantities[x.Descriptor].Value, 1000000))
                    {
                        update();
                    }
                }
                else
                {
                    if(ImGui.DragInt($"##quantity{x.Descriptor}", ref ItemQuantities[x.Descriptor].Value, 1f, 0, (int)x.Count))
                    {
                        update();
                    }
                }
                void update()
                {
                    var amt = ItemQuantities[x.Descriptor].Value;
                    if(amt < 0)
                    {
                        ItemQuantities[x.Descriptor].Value = (int)(x.Count + amt);
                    }
                }
                ImGui.SameLine();
                ImGuiEx.Text($"/ {x.Count:N0}");
                if (ImGuiEx.HoveredAndClicked("Left click - all\nRight click - none"))
                {
                    ItemQuantities[x.Descriptor].Value = (int)x.Count;
                }
                if (ImGuiEx.HoveredAndClicked(null, ImGuiMouseButton.Right))
                {
                    ItemQuantities[x.Descriptor].Value = 0;
                }
            }));
            Entries.Add(new("Name", () =>
            {
                ImGuiEx.Text(ItemQuantities[x.Descriptor].Value > 0? ImGuiColors.ParsedGreen:null, text);
            }));
        }
        if (ImGui.BeginChild("Table", ImGui.GetContentRegionAvail() - new Vector2(0, ImGui.GetFrameHeightWithSpacing())))
        {
            ImGuiEx.EzTable(Entries);
        }
        ImGui.EndChild();
        PurgeSelection();
        if(Svc.Targets.FocusTarget is IPlayerCharacter pc)
        {
            if(ImGuiEx.IconButtonWithText(FontAwesomeIcon.Handshake, $"Begin trading with {pc.Name}"))
            {
                BeginTrading();
            }
            ImGui.SameLine();
            if(ImGuiEx.IconButtonWithText(FontAwesomeIcon.Ban, "Clear focus target"))
            {
                Chat.Instance.ExecuteCommand("/focustarget clear");
            }
        }
        else
        {
            var t = "Focus target your trade partner to begin trading.";
            if (Svc.Targets.Target is IPlayerCharacter target)
            {
                if (ImGuiEx.IconButtonWithText(FontAwesomeIcon.Expand, t))
                {
                    Chat.Instance.ExecuteCommand("/focustarget");
                }
            }
            else
            {
                ImGuiEx.Text(EColor.RedBright, t);
            }
        }
    }

    public static void BeginTrading()
    {
        var quantitiesCopy = ItemQuantities.ToDictionary(x => x.Key, x => x.Value.Clone());
        int gil = 0;
        foreach (var item in quantitiesCopy)
        {
            if (item.Key.Id == 1)
            {
                gil += item.Value.Value;
                continue;
            }
            var im = InventoryManager.Instance();
            foreach (var type in ValidInventories.Union(CrystalInventories))
            {
                var cont = im->GetInventoryContainer(type);
                for (int i = 0; i < cont->Size; i++)
                {
                    var slot = *cont->GetInventorySlot(i);
                    if (slot.ItemId == item.Key.Id && slot.Flags.HasFlag(InventoryItem.ItemFlags.HighQuality) == item.Key.HQ && item.Value.Value > 0 && slot.SpiritbondOrCollectability == 0 && slot.GlamourId == 0)
                    {
                        var quantity = item.Value.Value < slot.Quantity ? item.Value.Value : (int)slot.Quantity;
                        PluginLog.Information($"Enqueueing slot {i} of {type} ({ExcelItemHelper.GetName(slot.ItemId, true)}) with quantity {quantity}");
                        item.Value.Value -= quantity;
                        TradeQueue.Add(new(type, i, quantity));
                    }
                }
            }
        }

        while (TradeQueue.Count > 0 || gil > 0)
        {
            List<QueueEntry> entries = [];
            for (int i = 0; i < 5; i++)
            {
                if (TradeQueue.TryDequeue(out var result))
                {
                    entries.Add(result);
                }
            }
            var tradeGil = Math.Min(TradeTask.MaxGil, gil);
            gil -= tradeGil;
            TaskAddItemsToTrade.Enqueue(entries, tradeGil);
        }
        if(C.AutoClear)
        {
            P.TaskManager.Enqueue(() => Chat.Instance.ExecuteCommand("/focustarget clear"));
        }
    }

    public static void PurgeSelection()
    {
        var items = GetTradeableItems();
        foreach (var x in ItemQuantities)
        {
            if(items.TryGetFirst(z => z.Descriptor == x.Key, out var v))
            {
                if (x.Value.Value > v.Count) x.Value.Value = (int)v.Count;
            }
            else
            {
                new TickScheduler(() => ItemQuantities.Remove(x.Key));
            }
        }
    }

    public static readonly InventoryType[] ValidInventories = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
        InventoryType.ArmoryBody,
        InventoryType.ArmoryEar,
        InventoryType.ArmoryFeets,
        InventoryType.ArmoryHands,
        InventoryType.ArmoryHead,
        InventoryType.ArmoryLegs,
        InventoryType.ArmoryMainHand,
        InventoryType.ArmoryNeck,
        InventoryType.ArmoryOffHand,
        InventoryType.ArmoryRings,
        InventoryType.ArmoryWaist,
        InventoryType.ArmoryWrist,
        ];
    public static readonly InventoryType[] CrystalInventories = [InventoryType.Crystals];

    public static readonly uint[] GearCategories = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 34, 35, 36, 37, 38, 40, 41, 42, 43, 84, 87, 88, 89, 96, 97, 98, 99, 105, 106, 107, 108, 109, 110, 111,];
    public static List<ItemRecord> GetTradeableGearItems()
    {
        return GetTradeableItems(false, false, true).Where(x => 
        ExcelItemHelper.Get(x.Descriptor.Id)?.ItemUICategory.RowId.EqualsAny(GearCategories) == true
        && ExcelItemHelper.Get(x.Descriptor.Id)?.Rarity.EqualsAny<byte>(2, 3) == true
        && ExcelItemHelper.Get(x.Descriptor.Id)?.IsUnique == false).ToList();
    }

    public static List<ItemRecord> GetTradeableItems(bool gil = true, bool crystals = true, bool items = true)
    {
        var ret = new List<ItemRecord>();
        List<InventoryType> toSearch = [];
        var im = InventoryManager.Instance();
        if (gil)
        {
            ret.Add(new(1, false, (uint)im->GetInventoryItemCount(1)));
        }
        if (items) toSearch.Add(ValidInventories);
        if (crystals) toSearch.Add(CrystalInventories);
        foreach (var inv in toSearch)
        {
            var cont = im->GetInventoryContainer(inv);
            for (var i = 0u; i < cont->Size; i++)
            {
                var item = *cont->GetInventorySlot((int)i);
                if(item.ItemId != 0 && item.SpiritbondOrCollectability == 0 && P.TradeableItems.Contains(item.ItemId) && item.GlamourId == 0)
                {
                    if(ret.TryGetFirst(x=> x.Descriptor.Id == item.ItemId && x.Descriptor.HQ == item.Flags.HasFlag(InventoryItem.ItemFlags.HighQuality), out var itemRecord))
                    {
                        itemRecord.Count += (uint)item.Quantity;
                    }
                    else
                    {
                        ret.Add(new(item.ItemId, item.Flags.HasFlag(InventoryItem.ItemFlags.HighQuality), (uint)item.Quantity));
                    }
                }
            }
        }
        return ret;
    }

    public class ItemRecord
    {
        public ItemDescriptor Descriptor;
        public uint Count;

        public ItemRecord(uint item, bool isHQ, uint count)
        {
            this.Descriptor = new(item, isHQ);
            this.Count = count;
        }
    }
}
