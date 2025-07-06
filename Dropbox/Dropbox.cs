
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text;
using Dalamud.Memory;
using ECommons.SimpleGui;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using ECommons.Configuration;
using ECommons.Automation;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.EzSharedDataManager;
using ECommons.Automation.LegacyTaskManager;
using ECommons.UIHelpers.AddonMasterImplementations;
using ECommons.Automation.UIInput;
using ECommons.Reflection;
using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.GameFunctions;
using ECommons.GameHelpers;

namespace Dropbox
{
    public unsafe class Dropbox : IDalamudPlugin
    {
        public string Name => "Dropbox";
        public string TradePartnerName = "";
        internal static Config C;
        internal static Dropbox P;
        const string ThrottleName = "TradeArtificialThrottle";
        public uint[] TradeableItems;
        public Memory Memory;
        public bool[] IsActive;

        internal TaskManager TaskManager;

        string TradeText => Svc.Data.GetExcelSheet<Addon>().GetRow(102223).Text.ExtractText();

        public Dropbox(IDalamudPluginInterface i)
        {
            P = this;
            ECommonsMain.Init(i, this, Module.DalamudReflector);
            TaskManager = new()
            {
                AbortOnTimeout = true,
                TimeLimitMS = 60000,
            };
            Svc.Framework.Update += Framework_Update;
            C = EzConfig.Init<Config>();
            EzConfigGui.Init(Draw);
            EzCmd.Add("/dropbox", CommandHandler, """
                Opens the Dropbox UI.
                /dropbox tab <tabname> - Opens the UI on a specific tab (Main, Item Trade Queue, Whitelist, Debug)
                /dropbox toggle - Toggles the UI.
                """);
            Svc.Chat.ChatMessage += Chat_ChatMessage;
            if (!C.PermanentActive)
            {
                C.Active = false;
            }
            Svc.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "ContextMenu", ContextMenuHandler);
            TradeableItems = Svc.Data.GetExcelSheet<Item>().Where(x => !x.IsUntradable).Select(x => x.RowId).ToArray();
            Memory = new();
            IsActive = EzSharedData.GetOrCreate<bool[]>("Dropbox.IsProcessingTasks", [false]);
            new IPCProvider();
            DalamudReflector.RegisterOnInstalledPluginsChangedEvents(() => PluginLog.Information("Changed!"));
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
            if (((int)type).EqualsAny(313, 569))
            {
                var mStr = message.ToString();
                if (mStr.StartsWith("Trade request sent to") || mStr.EndsWith("wishes to trade with you."))
                {
                    PluginLog.Debug("Detected trade request");
                    foreach (var payload in message.Payloads)
                    {
                        if (payload.Type == PayloadType.Player)
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
            if (type == XivChatType.SystemMessage)
            {
                var msg = message.ToString();
                if (msg.Equals("Trade complete."))
                {
                    if (!C.Silent) Notify.Info($"You finished trade with {TradePartnerName}");
                    TradePartnerName = "";
                }
                else if (msg.Equals("Trade canceled."))
                {
                    if (!C.Silent) Notify.Info("Trade canceled");
                    TradePartnerName = "";
                }
            }
        }

        private bool YesAlreadyStopRequired = false;
        private void Framework_Update(object framework)
        {
            if (TaskManager.IsBusy)
            {
                if(EzSharedData.TryGet<HashSet<string>>("YesAlready.StopRequests", out var data))
                {
                    YesAlreadyStopRequired = true;
                    data.Add(Svc.PluginInterface.InternalName);
                }
            }
            else if(YesAlreadyStopRequired)
            {
                if (EzSharedData.TryGet<HashSet<string>>("YesAlready.StopRequests", out var data))
                {
                    data.Remove(Svc.PluginInterface.InternalName);
                }
                YesAlreadyStopRequired = false;
            }
            IsActive[0] = TaskManager.IsBusy;
            if((Utils.CanAutoTrade() || TaskManager.IsBusy) && Svc.Condition[ConditionFlag.TradeOpen])
            {
                {
                    if (TryGetAddonByName<AtkUnitBase>("Trade", out var addon) && IsAddonReady(addon))
                    {
                        //InternalLog.Information($"My: {GetMyTradeItemCount()}, other: {GetOtherTradeItemCount(addon)}");
                        var check = addon->UldManager.NodeList[31]->GetAsAtkComponentNode()->Component->UldManager.NodeList[0]->GetAsAtkImageNode();
                        var ready = check->AtkResNode.Color.A == 0xFF;

                        if (C.AutoConfirmGil > 0)
                        {
                            var gilOffered = MemoryHelper.ReadSeString(&addon->UldManager.NodeList[6]->GetAsAtkTextNode()->NodeText).ExtractText().ReplaceByChar(" ,.", "", true);
                            if (uint.TryParse(gilOffered, out var gil) && gil >= C.AutoConfirmGil)
                            {
                                //InternalLog.Information($"Gil is 1m");
                                ready = true;
                            }
                        }

                        if (C.AutoConfirm5)
                        {
                            if (GetMyTradeItemCount() == 5) ready = true;
                            if (GetOtherTradeItemCount(addon) == 5) ready = true;
                        }

                        var tradeButton = (AtkComponentButton*)(addon->UldManager.NodeList[3]->GetComponent());

                        if (TradeTask.IsActive) ready = TradeTask.ConfirmAllowed;

                        if (ready)
                        {
                            if (EzThrottler.Check(ThrottleName) && FrameThrottler.Check(ThrottleName) && tradeButton->IsEnabled && EzThrottler.Throttle("Delay", 200) && EzThrottler.Throttle("ReadyTrade", 2000))
                            {
                                PluginLog.Information($"Locking trade");
                                if (!C.NoOp) tradeButton->ClickAddonButton(addon);
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
                    if (addon != null && EzThrottler.Throttle("Delay", 200) && EzThrottler.Throttle("SelectYes", 2000))
                    {
                        PluginLog.Information($"Confirming trade");
                        if (!C.NoOp) new AddonMaster.SelectYesno(addon).Yes();
                    }
                }
            }
        }

        int GetOtherTradeItemCount(AtkUnitBase* addon)
        {
            int ret = 0;
            for(int i = 0; i < 5; i++)
            {
                var slot = addon->UldManager.NodeList[15 + i];
                if (slot->GetAsAtkComponentNode()->Component->UldManager.NodeList[0]->IsVisible())
                {
                    ret++;
                }
            }
            return ret;
        }

        int GetMyTradeItemCount()
        {
            int ret = 0;
            var inv = InventoryManager.Instance()->GetInventoryContainer(InventoryType.HandIn);
            for (int i = 0; i < 5; i++)
            {
                if (inv->GetInventorySlot(i)->ItemId != 0) ret++;
            }
            if (TryGetAddonByName<byte>("InputNumeric", out _)) ret--;
            return ret;
        }

        private void CommandHandler(string command, string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                EzConfigGui.Open();
                return;
            }

            var args = arguments.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

            if (args[0].EqualsIgnoreCase("tab"))
            {
                var tabArgs = arguments.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (tabArgs.Length < 2)
                {
                    Notify.Info("Usage: /dropbox tab <tabname>");
                    return;
                }

                string tabInput = tabArgs[1];
                var tabList = new[] { "Main", "Item Trade Queue", "Whitelist", "Debug" };
                var matchedTab = tabList.FirstOrDefault(t => t.Equals(tabInput, StringComparison.OrdinalIgnoreCase));
                if (matchedTab == null)
                {
                    Notify.Error($"Unknown tab '{tabInput}'. Available tabs: {string.Join(", ", tabList)}");
                    return;
                }

                OpenTab = matchedTab;
                EzConfigGui.Open();
                return;
            }
            else if (args[0].EqualsIgnoreCase("toggle"))
            {
                EzConfigGui.Toggle();
                return;
            }
            Notify.Error($"Invalid command: {arguments}");
        }

        private string OpenTab = null;

        void Draw()
        {
            ImGuiEx.EzTabBar("Tabs", null, OpenTab,
                ("Main", () =>
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
                    ImGui.SliderInt("Delay between trades, frames", ref C.TradeDelay, 4, 60, null, ImGuiSliderFlags.Logarithmic);
                    ImGuiEx.SetNextItemWidthScaled(100);
                    ImGui.SliderInt("Trade open command throttle, ms", ref C.TradeThrottle, 1000, 5000);
                    //ImGui.Checkbox("Enable busy while trading", ref C.Busy);
                    ImGui.Checkbox("Auto-clear focus target when finished trade", ref C.AutoClear);
                    ImGui.Separator();
                    ImGui.Checkbox($"Not operational", ref C.NoOp);
                }, null, true),
                ("Item Trade Queue", ItemQueueUI.Draw, null, true),
                ("Whitelist", () =>
                {
                    ImGui.Checkbox("Accept trades only from whitelisted accounts", ref C.WhitelistMode);
                    if(ImGuiEx.IconButtonWithText(FontAwesomeIcon.Plus, "Add target", Svc.Targets.Target is IPlayerCharacter))
                    {
                        var pc = (IPlayerCharacter)Svc.Targets.Target;
                        C.WhitelistedAccounts[pc.GetRealAccountId()] = pc.GetNameWithWorld();
                    }
                    if(ImGuiEx.BeginDefaultTable(["~Account", "##control"], false))
                    {
                        foreach(var x in C.WhitelistedAccounts)
                        {
                            ImGui.PushID(x.Key.ToString());
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGuiEx.TextV($"Account {x.Key} ({x.Value})");
                            ImGui.TableNextColumn();
                            if(ImGuiEx.IconButton(FontAwesomeIcon.Trash))
                            {
                                new TickScheduler(() => C.WhitelistedAccounts.Remove(x.Key));
                            }
                            ImGui.PopID();
                        }

                        ImGui.EndTable();
                    }
                }, C.WhitelistMode?null:ImGuiColors.DalamudGrey, true),
                InternalLog.ImGuiTab(),
                ("Debug", () =>
                {
                    if (ImGui.CollapsingHeader("Tasks"))
                    {
                        P.TaskManager.TaskStack.Print("\n");
                        if (ImGui.Button("Step on")) P.TaskManager.SetStepMode(true);
                        if (ImGui.Button("Step off")) P.TaskManager.SetStepMode(false);
                        if (ImGui.Button("Step")) P.TaskManager.Step();
                    }
                    ImGui.InputInt("Maxgil", ref TradeTask.MaxGil.ValidateRange(1, 1000000));
                    EzThrottler.ImGuiPrintDebugInfo();
                    FrameThrottler.ImGuiPrintDebugInfo();
                    if (ImGui.Button("Open"))
                    {
                        TradeTask.OpenGilInput();
                    }
                    if(ImGui.Button("Set 6"))
                    {
                        TradeTask.SetNumericInput(6);
                    }
                }, ImGuiColors.DalamudGrey, true)
            );
            OpenTab = null;
        }

        internal static AtkUnitBase* GetSpecificYesno(params string[] s)
        {
            for (int i = 1; i < 100; i++)
            {
                try
                {
                    var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("SelectYesno", i);
                    if (addon == null) return null;
                    if (IsAddonReady(addon))
                    {
                        var textNode = addon->UldManager.NodeList[15]->GetAsAtkTextNode();
                        var text = MemoryHelper.ReadSeString(&textNode->NodeText).ExtractText();
                        if (text.EqualsAny(s))
                        {
                            PluginLog.Verbose($"SelectYesno {s.Print()} addon {i}");
                            return addon;
                        }
                    }
                }
                catch (Exception e)
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
}