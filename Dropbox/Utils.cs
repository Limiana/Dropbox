using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dropbox;
public static unsafe class Utils
{
    public static ulong GetRealAccountId(this IPlayerCharacter character)
    {
        return (uint)((Player.Character->AccountId ^ character.Character()->AccountId) >> 31) ^ P.Memory.MyAccountId;
    }

    public static void UpdateCharaWhitelistNames()
    {
        foreach(var x in Svc.Objects.OfType<IPlayerCharacter>())
        {
            var id = x.Struct()->ContentId;
            if(C.WhitelistedCharacters.ContainsKey(id))
            {
                C.WhitelistedCharacters[id] = x.GetNameWithWorld();
            }
        }
    }

    public static bool CanAutoTrade()
    {
        if(!C.Active) return false;
        if(P.StopRequests.Count > 0) return false;
        if(C.WhitelistMode)
        {
            if(P.TradePartnerName != "" && Svc.Objects.OfType<IPlayerCharacter>().TryGetFirst(x => x.GetNameWithWorld() == P.TradePartnerName, out var pc) && C.WhitelistedCharacters.ContainsKey(pc.Struct()->ContentId))
            {
                return true;
            }
            return false;
        }
        else
        {
            return true;
        }
    }

    public static InventoryItem GetSlot(InventoryType type, int slot)
    {
        var im = InventoryManager.Instance();
        var cont = im->GetInventoryContainer(type);
        return *cont->GetInventorySlot(slot);
    }

    public static bool? CompareSelection(List<ItemQueueUI.ItemRecord> records)
    {
        var all = true;
        var some = false;
        foreach(var x in records)
        {
            if(ItemQueueUI.ItemQuantities.TryGetValue(x.Descriptor, out var value))
            {
                if(value.Value < x.Count)
                {
                    all = false;
                }
                if (value.Value > 0)
                {
                    some = true;
                }
            }
        }
        if (all) return true;
        if (some) return null;
        return false;
    }
}
