using System.Collections.Generic;
using System.Numerics;
using System.Text.RegularExpressions;
using ClickLib;
using DailyRoutines.Clicks;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using DailyRoutines.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Colors;
using Dalamud.Memory;
using ECommons.Automation;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoSubmarineCollectTitle", "AutoSubmarineCollectDescription", ModuleCategories.General)]
public unsafe partial class AutoSubmarineCollect : DailyModuleBase
{
    private static AtkUnitBase* AddonSelectString => (AtkUnitBase*)Service.Gui.GetAddonByName("SelectString");

    private static readonly HashSet<uint> CompanyWorkshopZones = [423, 425, 425, 653, 984];
    private static string RequisiteMaterialsName = string.Empty;
    private static int? RequisiteMaterials;

    public override void Init()
    {
        TaskManager ??= new TaskManager { AbortOnTimeout = true, TimeLimitMS = 30000, ShowDebug = false };
        Overlay ??= new Overlay(this);
        Overlay.Flags |= ImGuiWindowFlags.NoMove;

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "SelectYesno", AlwaysYes);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "AirShipExplorationResult", OnExplorationResult);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "SelectString", OnAddonSelectString);

        Service.Chat.ChatMessage += OnErrorText;

        RequisiteMaterialsName = Service.Data.GetExcelSheet<Item>().GetRow(10373).Name.RawString;
    }

    public override void OverlayUI()
    {
        if (AddonSelectString == null)
        {
            Overlay.IsOpen = false;
            return;
        }

        var pos = new Vector2(AddonSelectString->GetX() + 6, AddonSelectString->GetY() - ImGui.GetWindowSize().Y + 6);
        ImGui.SetWindowPos(pos);

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudYellow, Service.Lang.GetText("AutoSubmarineCollectTitle"));

        ImGui.SameLine();

        ImGui.BeginDisabled(TaskManager.IsBusy);
        if (ImGui.Button(Service.Lang.GetText("Start"))) GetSubmarineInfos();
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("Stop"))) TaskManager.Abort();

        RequisiteMaterials ??= InventoryManager.Instance()->GetInventoryItemCount(10373);

        ImGui.SameLine();
        ImGui.Text($"{RequisiteMaterialsName}:");

        ImGui.SameLine();
        ImGui.TextColored(RequisiteMaterials < 20 ? ImGuiColors.DPSRed : ImGuiColors.HealerGreen,
                          RequisiteMaterials.ToString());

        if (EzThrottler.Throttle("AutoSubmarineCollectOverlay-RequestItemAmount", 1000))
        {
            var inventoryManager = InventoryManager.Instance();
            RequisiteMaterials = inventoryManager->GetInventoryItemCount(10373);
        }
    }

    private void OnAddonSelectString(AddonEvent type, AddonArgs args)
    {
        if (!EzThrottler.Throttle("AutoSubmarineCollectOverlay")) return;
        if (!CompanyWorkshopZones.Contains(Service.ClientState.TerritoryType)) return;

        Overlay.IsOpen = false;

        if (AddonSelectString == null) return;
        var title = MemoryHelper.ReadStringNullTerminated((nint)AddonSelectString->AtkValues[2].String);
        if (string.IsNullOrWhiteSpace(title) || !title.Contains("请选择潜水艇")) return;
        Overlay.IsOpen = true;
    }

    private void AlwaysYes(AddonEvent type, AddonArgs args)
    {
        if (!TaskManager.IsBusy) return;
        Click.SendClick("select_yes");
    }

    // 报错处理
    private void OnErrorText(
        XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (!TaskManager.IsBusy) return;

        var content = message.ExtractText();
        if (content.Contains("需要修理配件"))
        {
            TaskManager.Abort();
            TaskManager.Enqueue(ReadyToRepairSubmarines);
            return;
        }

        if (content.Contains("没有修理所必需的")) TaskManager.Abort();
    }

    // 航程结果 -> 再次出发
    private void OnExplorationResult(AddonEvent type, AddonArgs args)
    {
        if (TryGetAddonByName<AtkUnitBase>("AirShipExplorationResult", out var addon) &&
            IsAddonAndNodesReady(addon))
        {
            AddonManager.Callback(addon, true, 1);

            if (TaskManager.IsBusy) addon->IsVisible = false;
        }
    }

    private bool? GetSubmarineInfos()
    {
        if (Service.Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
            Service.Condition[ConditionFlag.WatchingCutscene78]) return false;

        // 魔导机械修理材料/桶装青磷水不足
        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager->GetInventoryItemCount(10373) < 20)
        {
            Service.Chat.Print(Service.Lang.GetSeString("AutoSubmarineCollect-LackSpecificItems",
                                                        SeString.CreateItemLink(
                                                            10373)));
            TaskManager.Abort();
            return true;
        }

        if (inventoryManager->GetInventoryItemCount(10155) < 15)
        {
            Service.Chat.Print(Service.Lang.GetSeString("AutoSubmarineCollect-LackSpecificItems",
                                                        SeString.CreateItemLink(
                                                            10155)));
            TaskManager.Abort();
            return true;
        }

        if (TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) && IsAddonAndNodesReady(addon))
        {
            if (!TryScanSelectStringText(addon, "探索完成", out var index))
            {
                TaskManager.Abort();
                return true;
            }

            if (!Click.TrySendClick($"select_string{index + 1}")) return false;

            // 会有 Toast
            TaskManager.DelayNext(2000);
            TaskManager.Enqueue(CommenceSubmarineVoyage);

            return true;
        }

        return false;
    }

    private bool? CommenceSubmarineVoyage()
    {
        if (TryGetAddonByName<AtkUnitBase>("AirShipExplorationDetail", out var addon) &&
            IsAddonAndNodesReady(addon))
        {
            AddonManager.Callback(addon, true, 0);
            addon->Close(true);

            // 一轮结束, 清理任务; 会有动画
            TaskManager.Abort();
            TaskManager.DelayNext(3000);
            TaskManager.Enqueue(GetSubmarineInfos);

            return true;
        }

        return false;
    }

    private bool? ReadyToRepairSubmarines()
    {
        if (TryGetAddonByName<AtkUnitBase>("AirShipExplorationDetail", out var addon) &&
            IsAddonAndNodesReady(addon))
        {
            AddonManager.Callback(addon, true, -1);
            addon->Close(true);
        }

        if (AddonSelectString == null || !IsAddonAndNodesReady(AddonSelectString)) return false;
        if (!ClickManager.SelectString("修理")) return false;

        TaskManager.Enqueue(() => AddonSelectString->Close(true));
        TaskManager.Enqueue(RepairSubmarines);

        return true;
    }

    private bool? RepairSubmarines()
    {
        if (Service.Gui.GetAddonByName("SelectYesno") != nint.Zero) return false;
        if (TryGetAddonByName<AtkUnitBase>("CompanyCraftSupply", out var addon) &&
            IsAddonAndNodesReady(addon))
        {
            var handler = new ClickCompanyCraftSupplyDR();

            for (var i = 0; i < 4; i++)
            {
                var endurance = addon->AtkValues[3 + (8 * i)].UInt;
                if (endurance <= 0)
                {
                    AgentManager.SendEvent(AgentId.SubmersibleParts, 0, 3, 0, i, 0, 0, 0);
                    return false;
                }
            }

            TaskManager.DelayNext(100);
            TaskManager.Enqueue(handler.Close);
            TaskManager.Enqueue(() => addon->Close(true));

            TaskManager.DelayNext(100);
            TaskManager.Enqueue(ClickPreviousVoyageLog);

            return true;
        }

        return false;
    }

    private bool? ClickPreviousVoyageLog()
    {
        if (TryGetAddonByName<AtkUnitBase>("AirShipExplorationDetail", out var detailAddon) &&
            IsAddonAndNodesReady(detailAddon))
        {
            TaskManager.Abort();
            TaskManager.Enqueue(CommenceSubmarineVoyage);

            return true;
        }

        if (AddonSelectString == null || !IsAddonAndNodesReady(AddonSelectString)) return false;

        if (!ClickManager.SelectString("上次的远航报告")) return false;
        AddonSelectString->Close(true);

        TaskManager.DelayNext(100);
        TaskManager.Enqueue(CommenceSubmarineVoyage);
        return true;
    }

    public override void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(OnExplorationResult);
        Service.AddonLifecycle.UnregisterListener(AlwaysYes);
        Service.Chat.ChatMessage -= OnErrorText;
        Service.AddonLifecycle.UnregisterListener(OnAddonSelectString);

        base.Uninit();
    }


    [GeneratedRegex("探索机体数：\\d+/(\\d+)")]
    private static partial Regex SubmarineInfoRegex();
}
