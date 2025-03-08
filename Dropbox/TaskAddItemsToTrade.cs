using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.Automation;
using ECommons.ChatMethods;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dropbox;
public unsafe static class TaskAddItemsToTrade
{
    public static void Enqueue(IEnumerable<QueueEntry> Entries, int gil)
    {
        P.TaskManager.Enqueue(delegate { TradeTask.ConfirmAllowed = false; }, "ConfirmAllowed = false");
        P.TaskManager.Enqueue(() => TradeTask.UseTradeOn(new Sender((IPlayerCharacter)Svc.Targets.FocusTarget).ToString()), $"UseTradeOn({Svc.Targets.FocusTarget})");
        P.TaskManager.Enqueue(TradeTask.WaitUntilTradeOpen);
        if (gil > 0)
        {
            P.TaskManager.Enqueue(TradeTask.OpenGilInput);
            P.TaskManager.Enqueue(() => TradeTask.SetNumericInput(gil), $"SetNumericInput({gil})");
        }
        var entries = Entries.ToArray();
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var numSlot = i;
            P.TaskManager.Enqueue(() =>
            {
                if (TryGetAddonByName<AtkUnitBase>("Trade", out var addon) && IsAddonReady(addon))
                {
                    if (Utils.GetSlot(InventoryType.HandIn, numSlot).ItemId != 0) return true;
                    if (TradeTask.GenericThrottle() && EzThrottler.Throttle("OfferTrade", 250) && EzThrottler.Throttle($"OfferSlot{entry}", 2000))
                    {
                        P.Memory.SafeOfferItemTrade(entry.Type, (ushort)entry.SlotID);
                        return false;
                    }
                }
                return false;
            }, $"OfferItemTask for {entry}");
            if (Utils.GetSlot(entry.Type, entry.SlotID).Quantity > 1)
            {
                var amount = Math.Min(Utils.GetSlot(entry.Type, entry.SlotID).Quantity, entry.Quantity);
                if (amount < 1) throw new ArgumentOutOfRangeException();
                P.TaskManager.Enqueue(() => TradeTask.SetNumericInput((int)amount), $"SetInputNumeric {amount}");
            }
        }
        P.TaskManager.Enqueue(delegate { TradeTask.ConfirmAllowed = true; }, "ConfirmAllowed = true");
        P.TaskManager.Enqueue(TradeTask.WaitUntilTradeNotOpen);
        P.TaskManager.DelayNext(Math.Max(15, C.TradeDelay), true);
    }
}
