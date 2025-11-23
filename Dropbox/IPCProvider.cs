using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.Configuration;
using ECommons.EzIpcManager;
using ECommons.SimpleGui;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dropbox;
public unsafe class IPCProvider
{
    public IPCProvider()
    {
        EzIPC.Init(this);
    }

    [EzIPC] public bool IsBusy() => P.TaskManager.IsBusy;
    [EzIPC] public void Stop() => P.TaskManager.Abort();
    [EzIPC] public void BeginTradingQueue()
    {
        if(!IsBusy() && Svc.Targets.FocusTarget is IPlayerCharacter)
        {
            ItemQueueUI.BeginTrading();
        }
    }
    [EzIPC] public void OpenUI()
    {
        EzConfigGui.Window.IsOpen = true;
        EzConfigGui.Window.Collapsed = false;
        EzConfigGui.Window.CollapsedCondition = ImGuiCond.Always;
        P.OpenTabName = "Item Trade Queue";
        new TickScheduler(() =>
        {
            P.OpenTabName = null;
            EzConfigGui.Window.Collapsed = null;
        }, 500);
    }
    [EzIPC] public int GetItemQuantity(uint id, bool hq)
    {
        if(ItemQueueUI.ItemQuantities.TryGetValue(new(id, hq), out var value))
        {
            return value.Value;
        }
        return 0;
    }

    [EzIPC]
    public void SetItemQuantity(uint id, bool hq, int quantity)
    {
        quantity.ValidateRange(0, InventoryManager.Instance()->GetInventoryItemCount(id, hq, false));
        var d = new ItemDescriptor(id, hq);
        if (quantity == 0)
        {
            ItemQueueUI.ItemQuantities.Remove(d);
        }
        else
        {
            ItemQueueUI.ItemQuantities[d].Value = quantity;
        }
    }
}
