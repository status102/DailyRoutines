using System.Numerics;
using ClickLib.Clicks;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using DailyRoutines.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Colors;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoDesynthesizeItemsTitle", "AutoDesynthesizeItemsDescription", ModuleCategories.界面操作)]
public unsafe class AutoDesynthesizeItems : DailyModuleBase
{
    private static bool ConfigSkipWhenHQ;

    public override void Init()
    {
        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 10000, ShowDebug = false };
        Overlay ??= new Overlay(this);

        AddConfig("SkipWhenHQ", ConfigSkipWhenHQ);
        ConfigSkipWhenHQ = GetConfig<bool>("SkipWhenHQ");

        Service.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, "SalvageDialog", OnAddon);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SalvageItemSelector", OnAddonList);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "SalvageItemSelector", OnAddonList);
    }

    public override void OverlayUI()
    {
        var addon = (AtkUnitBase*)Service.Gui.GetAddonByName("SalvageItemSelector");
        if (addon == null) return;

        var pos = new Vector2(addon->GetX() - ImGui.GetWindowSize().X, addon->GetY() + 6);
        ImGui.SetWindowPos(pos);

        ImGui.TextColored(ImGuiColors.DalamudYellow, Service.Lang.GetText("AutoDesynthesizeItemsTitle"));

        ImGui.Separator();

        ImGui.BeginDisabled(TaskHelper.IsBusy);
        if (ImGui.Checkbox(Service.Lang.GetText("AutoDesynthesizeItems-SkipHQ"), ref ConfigSkipWhenHQ))
            UpdateConfig("SkipWhenHQ", ConfigSkipWhenHQ);

        if (ImGui.Button(Service.Lang.GetText("Start"))) StartDesynthesize();
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("Stop"))) TaskHelper.Abort();
    }

    private void OnAddonList(AddonEvent type, AddonArgs args)
    {
        Overlay.IsOpen = type switch
        {
            AddonEvent.PostSetup => true,
            AddonEvent.PreFinalize => false,
            _ => Overlay.IsOpen,
        };
    }

    private static void OnAddon(AddonEvent type, AddonArgs args)
    {
        var addon = (AddonSalvageDialog*)args.Addon;
        if (addon == null) return;

        var handler = new ClickSalvageDialog();
        handler.CheckBox();
        handler.Desynthesize();
    }

    private bool? StartDesynthesize()
    {
        if (Flags.OccupiedInEvent) return false;
        if (TryGetAddonByName<AtkUnitBase>("SalvageItemSelector", out var addon) &&
            IsAddonAndNodesReady(addon))
        {
            var itemAmount = addon->AtkValues[9].Int;
            if (itemAmount == 0)
            {
                TaskHelper.Abort();
                return true;
            }

            for (var i = 0; i < itemAmount; i++)
            {
                var itemName = MemoryHelper.ReadStringNullTerminated((nint)addon->AtkValues[(i * 8) + 14].String);
                if (ConfigSkipWhenHQ)
                {
                    if (itemName.Contains('')) // HQ 符号
                        continue;
                }

                var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Salvage);
                if (agent == null) return false;

                AgentHelper.SendEvent(agent, 0, 12, i);

                TaskHelper.DelayNext(1500);
                TaskHelper.Enqueue(StartDesynthesize);
                return true;
            }
        }

        return false;
    }

    public override void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(OnAddonList);
        Service.AddonLifecycle.UnregisterListener(OnAddon);

        base.Uninit();
    }
}
