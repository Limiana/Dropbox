
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Memory;
using ECommons.Automation;
using ECommons.Automation.LegacyTaskManager;
using ECommons.Automation.UIInput;
using ECommons.ChatMethods;
using ECommons.Configuration;
using ECommons.Events;
using ECommons.EzSharedDataManager;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.Reflection;
using ECommons.SimpleGui;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Dalamud.Game;

namespace Dropbox;

public unsafe class Dropbox : IDalamudPlugin
{
    public string Name => "Dropbox";
    public string TradePartnerName = "";
    internal static Config C;
    internal static Dropbox P;
    private const string ThrottleName = "TradeArtificialThrottle";
    public uint[] TradeableItems;
    public Memory Memory;
    public bool[] IsActive;
    public HashSet<string> StopRequests;
    public FileDialogManager FileDM;

    internal TaskManager TaskManager;

    private string TradeText => Svc.Data.GetExcelSheet<Addon>().GetRow(102223).Text.GetText();
    private string TradeCompleteLog => Svc.Data.GetExcelSheet<LogMessage>().GetRow(38).Text.GetText();
    private string TradeCanceledLog => Svc.Data.GetExcelSheet<LogMessage>().GetRow(36).Text.GetText();
    private string TradeCanceledGilFullSelfLog => Svc.Data.GetExcelSheet<LogMessage>().GetRow(46).Text.GetText();
    private string TradeCanceledGilFullOtherLog => Svc.Data.GetExcelSheet<LogMessage>().GetRow(47).Text.GetText();

    private Regex TradeRequestLogRegex
    {
        get
        {
            return field ??= GetTradeRequestLogRegex();
        }
    }

    public Dropbox(IDalamudPluginInterface i)
    {
        P = this;
        ECommonsMain.Init(i, this, Module.DalamudReflector);
        StopRequests = EzSharedData.GetOrCreate<HashSet<string>>("Dropbox.StopRequests", []);
        IsActive = EzSharedData.GetOrCreate<bool[]>("Dropbox.IsProcessingTasks", [false]);
        TaskManager = new()
        {
            AbortOnTimeout = true,
            TimeLimitMS = 60000,
        };
        Svc.Framework.Update += Framework_Update;
        C = EzConfig.Init<Config>();
        EzConfigGui.Init(Draw);
        EzCmd.Add("/dropbox", EzConfigGui.Open);
        Svc.Chat.ChatMessage += Chat_ChatMessage;
        if(!C.PermanentActive)
        {
            C.Active = false;
        }
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "ContextMenu", ContextMenuHandler);
        TradeableItems = Svc.Data.GetExcelSheet<Item>().Where(x => !x.IsUntradable).Select(x => x.RowId).ToArray();
        Memory = new();
        new IPCProvider();
        DalamudReflector.RegisterOnInstalledPluginsChangedEvents(() => PluginLog.Information("Changed!"));
        ProperOnLogin.RegisterAvailable(Utils.UpdateCharaWhitelistNames, true);
        FileDM = new();
    }

    private static Regex GetTradeRequestLogRegex()
    {
        string pattern;

        switch (Svc.Data.Language)
        {
            case ClientLanguage.Japanese:
                pattern = @"にトレードを申し込みました。$|からトレードを申し込まれました。$";
                break;
            case ClientLanguage.German:
                pattern = @"^Du hast .*? einen Handel angeboten\.$|möchte mit dir handeln\.$";
                break;
            case ClientLanguage.French:
                pattern = @"^Vous proposez un échange|vous propose un échange\.$";
                break;
            case ClientLanguage.English:
            default:
                pattern = @"^Trade request sent to|wishes to trade with you\.$";
                break;
        }

        return new Regex(pattern, RegexOptions.Compiled);
    }

    private void ContextMenuHandler(AddonEvent type, AddonArgs args)
    {
        /*if (C.Active)
        {
            var addon = (AtkUnitBase*)args.Addon;
            if (IsAddonReady(addon))
            {
                var r = new ReaderContextMenu(addon);
                PluginLog.Verbose($"Entries: {r.Count}");
                for (int i = 0; i < r.Count; i++)
                {
                    var x = r.Entries[i];
                    PluginLog.Verbose($"- {x.Name}");
                    if(x.Name == "Trade" && FrameThrottler.Throttle("TradeAutoClick", 2))
                    {
                        Callback.Fire(addon, true, 0, i, 0u, Callback.ZeroAtkValue, Callback.ZeroAtkValue);
                        addon->Hide(false, false, 0);
                    }
                }
            }
        }*/
    }

    private void Chat_ChatMessage(XivChatType type, int senderId, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if(((int)type).EqualsAny(313, 569))
        {
            var mStr = message.ToString();
            if(TradeRequestLogRegex.Match(mStr).Success)
            {
                Utils.UpdateCharaWhitelistNames();
                PluginLog.Debug("Detected trade request");
                foreach(var payload in message.Payloads)
                {
                    if(payload.Type == PayloadType.Player)
                    {
                        var playerPayload = (PlayerPayload)payload;
                        var senderNameWithWorld = $"{playerPayload.PlayerName}@{playerPayload.World.ValueNullable?.Name}";
                        PluginLog.Debug($"Name trade out: {senderNameWithWorld}");
                        TradePartnerName = senderNameWithWorld;
                        if(!C.Silent) Notify.Info($"You begin trade with {TradePartnerName}.");
                        break;
                    }
                }
            }
        }
        if(type == XivChatType.SystemMessage)
        {
            var msg = message.ToString();
            if(msg.Equals(TradeCompleteLog))
            {
                if(!C.Silent) Notify.Info($"You finished trade with {TradePartnerName}");
                TradePartnerName = "";
            }
            else if(msg.Equals(TradeCanceledLog) || msg.Equals(TradeCanceledGilFullSelfLog) ||  msg.Equals(TradeCanceledGilFullOtherLog))
            {
                if(!C.Silent) Notify.Info("Trade canceled");
                TradePartnerName = "";
            }
        }
    }

    private bool YesAlreadyStopRequired = false;
    private void Framework_Update(object framework)
    {
        if(TaskManager.IsBusy)
        {
            if(EzSharedData.TryGet<HashSet<string>>("YesAlready.StopRequests", out var data))
            {
                YesAlreadyStopRequired = true;
                data.Add(Svc.PluginInterface.InternalName);
            }
        }
        else if(YesAlreadyStopRequired)
        {
            if(EzSharedData.TryGet<HashSet<string>>("YesAlready.StopRequests", out var data))
            {
                data.Remove(Svc.PluginInterface.InternalName);
            }
            YesAlreadyStopRequired = false;
        }
        IsActive[0] = TaskManager.IsBusy;
        if((Utils.CanAutoTrade() || TaskManager.IsBusy) && Svc.Condition[ConditionFlag.TradeOpen])
        {
            {
                if(TryGetAddonByName<AtkUnitBase>("Trade", out var addon) && IsAddonReady(addon))
                {
                    //InternalLog.Information($"My: {GetMyTradeItemCount()}, other: {GetOtherTradeItemCount(addon)}");
                    var check = addon->UldManager.NodeList[31]->GetAsAtkComponentNode()->Component->UldManager.NodeList[0]->GetAsAtkImageNode();
                    var ready = check->AtkResNode.Color.A == 0xFF;

                    if(C.AutoConfirmGil > 0)
                    {
                        var gilOffered = MemoryHelper.ReadSeString(&addon->UldManager.NodeList[6]->GetAsAtkTextNode()->NodeText).GetText().ReplaceByChar(" ,.", "", true);
                        if(uint.TryParse(gilOffered, out var gil) && gil >= C.AutoConfirmGil)
                        {
                            //InternalLog.Information($"Gil is 1m");
                            ready = true;
                        }
                    }

                    if(C.AutoConfirm5)
                    {
                        if(GetMyTradeItemCount() == 5) ready = true;
                        if(GetOtherTradeItemCount(addon) == 5) ready = true;
                    }

                    var tradeButton = (AtkComponentButton*)(addon->UldManager.NodeList[3]->GetComponent());

                    if(TradeTask.IsActive) ready = TradeTask.ConfirmAllowed;

                    if(ready)
                    {
                        if(EzThrottler.Check(ThrottleName) && FrameThrottler.Check(ThrottleName) && tradeButton->IsEnabled && EzThrottler.Throttle("Delay", 200) && EzThrottler.Throttle("ReadyTrade", 2000))
                        {
                            PluginLog.Information($"Locking trade");
                            if(!C.NoOp) tradeButton->ClickAddonButton(addon);
                        }
                    }
                    else
                    {
                        EzThrottler.Throttle(ThrottleName, C.Delay, true);
                        FrameThrottler.Throttle(ThrottleName, 8, true);
                    }
                }
                else
                {
                    EzThrottler.Throttle(ThrottleName, C.Delay, true);
                    FrameThrottler.Throttle(ThrottleName, 8, true);
                }
            }
            {
                var addon = GetSpecificYesno(TradeText);
                if(addon != null && EzThrottler.Throttle("Delay", 200) && EzThrottler.Throttle("SelectYes", 2000))
                {
                    PluginLog.Information($"Confirming trade");
                    if(!C.NoOp) new AddonMaster.SelectYesno(addon).Yes();
                }
            }
        }
    }

    private int GetOtherTradeItemCount(AtkUnitBase* addon)
    {
        var ret = 0;
        for(var i = 0; i < 5; i++)
        {
            var slot = addon->UldManager.NodeList[15 + i];
            if(slot->GetAsAtkComponentNode()->Component->UldManager.NodeList[0]->IsVisible())
            {
                ret++;
            }
        }
        return ret;
    }

    private int GetMyTradeItemCount()
    {
        var ret = 0;
        var inv = InventoryManager.Instance()->GetInventoryContainer(InventoryType.HandIn);
        for(var i = 0; i < 5; i++)
        {
            if(inv->GetInventorySlot(i)->ItemId != 0) ret++;
        }
        if(TryGetAddonByName<byte>("InputNumeric", out _)) ret--;
        return ret;
    }

    private void Draw()
    {
        FileDM.Draw();
        void MainTab()
        {
            ImGui.Checkbox($"Enable auto-accept trades", ref C.Active);
            ImGui.Checkbox($"Save enabled state through game restarts", ref C.PermanentActive);
            ImGui.SetNextItemWidth(200f);
            ImGuiEx.SliderIntAsFloat("Delay before accepting, s", ref C.Delay, 0, 10000);
            ImGui.Checkbox("Auto-confirm once 5 item slots are filled", ref C.AutoConfirm5);
            ImGui.SetNextItemWidth(150f);
            ImGui.SliderInt("Auto-confirm on incoming gil offering, >=", ref C.AutoConfirmGil, 0, 1000000);
            ImGui.Checkbox($"Silent operation", ref C.Silent);
            ImGuiEx.SetNextItemWidthScaled(100);
            ImGui.SliderInt("Delay between trades, frames", ref C.TradeDelay, 4, 60, flags: ImGuiSliderFlags.Logarithmic);
            ImGuiEx.SetNextItemWidthScaled(100);
            ImGui.SliderInt("Trade open command throttle, ms", ref C.TradeThrottle, 1000, 5000);
            //ImGui.Checkbox("Enable busy while trading", ref C.Busy);
            ImGui.Checkbox("Auto-clear focus target when finished trade", ref C.AutoClear);
            ImGui.Separator();
            ImGui.Checkbox($"Not operational", ref C.NoOp);
        }
        ImGuiEx.EzTabBar("Tabs",
            ("Main", MainTab, null, true),
            ("Item Trade Queue", ItemQueueUI.Draw, null, true),
            ("Whitelist", () =>
            {
                ImGui.Checkbox("Accept trades only from whitelisted characters", ref C.WhitelistMode);
                if(ImGuiEx.IconButtonWithText(FontAwesomeIcon.Plus, "Add target", Svc.Targets.Target is IPlayerCharacter))
                {
                    var pc = (IPlayerCharacter)Svc.Targets.Target;
                    C.WhitelistedCharacters[pc.Struct()->ContentId] = pc.GetNameWithWorld();
                }
                ImGui.SameLine();
                if(ImGuiEx.IconButtonWithText(FontAwesomeIcon.ArrowRightToBracket, "Import from Game Files"))
                {
                    FileDM.OpenFolderDialog("Select folder", (state, path) =>
                    {
                        if(state)
                        {
                            try
                            {
                                var files = Directory.GetDirectories(path);
                                int n = 0;
                                int a = 0;
                                foreach(var f in files)
                                {
                                    var name = Path.GetFileName(f);
                                    if(name.StartsWith("FFXIV_CHR") && ulong.TryParse(name.Replace("FFXIV_CHR", ""), NumberStyles.HexNumber, null, out var result))
                                    {
                                        a++;
                                        if(!C.WhitelistedCharacters.ContainsKey(result))
                                        {
                                            n++;
                                            C.WhitelistedCharacters[result] = $"Character {result:X}";
                                        }
                                    }
                                }
                                ChatPrinter.Green($"Processed {a} entries, added {n} entries");
                            }
                            catch(Exception e)
                            {
                                e.Log();
                                DuoLog.Error(e.Message);
                            }
                        }
                    });
                }
                ImGuiEx.Tooltip("""
                    Point it to your game configuration folder, usually it's "Documents\My Games\FINAL FANTASY XIV - A Realm Reborn" unless you changed it or using Linux. Character IDs on which you ever logged in will be whitelisted. Their names, however, will not be resolved. You'll have to trade with them or log into them to populate name.
                    """);
                if(ImGuiEx.BeginDefaultTable(["~Character", "##control"], false))
                {
                    foreach(var x in C.WhitelistedCharacters)
                    {
                        ImGui.PushID(x.Key.ToString());
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGuiEx.TextV($"Character {x.Key} ({x.Value})");
                        ImGui.TableNextColumn();
                        if(ImGuiEx.IconButton(FontAwesomeIcon.Trash))
                        {
                            new TickScheduler(() => C.WhitelistedCharacters.Remove(x.Key));
                        }
                        ImGui.PopID();
                    }

                    ImGui.EndTable();
                }
            }, C.WhitelistMode ? null : ImGuiColors.DalamudGrey, true),
            InternalLog.ImGuiTab(),
            ("Debug", () =>
            {
                if(ImGui.CollapsingHeader("Tasks"))
                {
                    P.TaskManager.TaskStack.Print("\n");
                    if(ImGui.Button("Step on")) P.TaskManager.SetStepMode(true);
                    if(ImGui.Button("Step off")) P.TaskManager.SetStepMode(false);
                    if(ImGui.Button("Step")) P.TaskManager.Step();
                }
                ImGui.InputInt("Maxgil", ref TradeTask.MaxGil.ValidateRange(1, 1000000));
                EzThrottler.ImGuiPrintDebugInfo();
                FrameThrottler.ImGuiPrintDebugInfo();
                if(ImGui.Button("Open"))
                {
                    TradeTask.OpenGilInput();
                }
                if(ImGui.Button("Set 6"))
                {
                    TradeTask.SetNumericInput(6);
                }
            }, ImGuiColors.DalamudGrey, true)
        );
    }



    internal static AtkUnitBase* GetSpecificYesno(params string[] s)
    {
        for(var i = 1; i < 100; i++)
        {
            try
            {
                var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("SelectYesno", i).Address;
                if(addon == null) return null;
                if(IsAddonReady(addon))
                {
                    var textNode = addon->UldManager.NodeList[15]->GetAsAtkTextNode();
                    var text = MemoryHelper.ReadSeString(&textNode->NodeText).GetText();
                    if(text.EqualsAny(s))
                    {
                        PluginLog.Verbose($"SelectYesno {s.Print()} addon {i}");
                        return addon;
                    }
                }
            }
            catch(Exception e)
            {
                e.Log();
                return null;
            }
        }
        return null;
    }


    public void Dispose()
    {
        Svc.Framework.Update -= Framework_Update;
        Svc.Chat.ChatMessage -= Chat_ChatMessage;
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostRequestedUpdate, "ContextMenu", ContextMenuHandler);
        IsActive[0] = false;
        ECommonsMain.Dispose();
        P = null;
        C = null;
    }
}