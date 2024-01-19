using System;
using ClickLib;
using DailyRoutines.Clicks;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.AddonLifecycle;
using Dalamud.Memory;
using ECommons.Automation;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoLoginTitle", "AutoLoginDescription", ModuleCategories.General)]
public class AutoLogin : IDailyModule
{
    public bool Initialized { get; set; }

    private static TaskManager? TaskManager;

    private static bool HasLoginOnce;

    private static string ConfigSelectedServer = string.Empty;
    private static int ConfigSelectedCharaIndex = -1;

    public void Init()
    {
        Service.Config.AddConfig(typeof(AutoLogin), "SelectedServer", "");
        Service.Config.AddConfig(typeof(AutoLogin), "SelectedCharaIndex", "0");

        ConfigSelectedServer = Service.Config.GetConfig<string>(typeof(AutoLogin), "SelectedServer");
        ConfigSelectedCharaIndex = Service.Config.GetConfig<int>(typeof(AutoLogin), "SelectedCharaIndex");

        TaskManager ??= new TaskManager { AbortOnTimeout = true, TimeLimitMS = 20000, ShowDebug = true };
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "_TitleMenu", OnTitleMenu);

        Initialized = true;
    }

    public void UI()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{Service.Lang.GetText("AutoLogin-ServerName")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f);
        if (ImGui.InputText("##AutoLogin-EnterServerName", ref ConfigSelectedServer, 16,
                            ImGuiInputTextFlags.EnterReturnsTrue))
        {
            if (TryGetWorldByName(ConfigSelectedServer, out _))
            {
                Service.Config.UpdateConfig(typeof(AutoLogin), "SelectedServer", ConfigSelectedServer);
                HasLoginOnce = false;
            }
            else
            {
                Service.Chat.PrintError(
                    Service.Lang.GetText("AutoLogin-ServerNotFoundErrorMessage", ConfigSelectedServer));
                ConfigSelectedServer = string.Empty;
            }
        }

        ImGui.SameLine();
        ImGui.Spacing();

        ImGui.SameLine();
        ImGui.Text($"{Service.Lang.GetText("AutoLogin-CharacterIndex")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f);
        if (ImGui.InputInt("##AutoLogin-EnterCharaIndex", ref ConfigSelectedCharaIndex, 1, 1,
                           ImGuiInputTextFlags.EnterReturnsTrue))
        {
            if (ConfigSelectedCharaIndex is < 0 or > 8) ConfigSelectedCharaIndex = 0;
            else
            {
                Service.Config.UpdateConfig(typeof(AutoLogin), "SelectedCharaIndex",
                                            ConfigSelectedCharaIndex.ToString());
                HasLoginOnce = false;
            }
        }

        ImGuiOm.TooltipHover(Service.Lang.GetText("AutoLogin-CharaIndexInputTooltip"));
    }

    private static unsafe void OnTitleMenu(AddonEvent eventType, AddonArgs? addonInfo)
    {
        if (string.IsNullOrEmpty(ConfigSelectedServer) || ConfigSelectedCharaIndex == -1) return;
        if (EzThrottler.Throttle("AutoLogin-OnTitleMenu"))
        {
            if (HasLoginOnce) return;
            if (TryGetAddonByName<AtkUnitBase>("_TitleMenu", out var addon) && IsAddonReady(addon))
            {
                HasLoginOnce = true;
                var handler = new ClickTitleMenuDR();
                handler.Start();

                TaskManager.Enqueue(WaitAddonCharaSelectListMenu);
            }
        }
    }

    private static unsafe bool? WaitAddonCharaSelectListMenu()
    {
        if (TryGetAddonByName<AtkUnitBase>("_CharaSelectListMenu", out var addon) && IsAddonReady(addon))
        {
            var currentServer = addon->GetTextNodeById(8)->NodeText.ExtractText();
            if (string.IsNullOrEmpty(currentServer)) return false;

            Service.Log.Debug($"当前服务器: {currentServer}, 目标服务器: {ConfigSelectedServer}");

            if (currentServer == ConfigSelectedServer)
            {
                var handler = new ClickCharaSelectListMenuDR();
                handler.SelectChara(ConfigSelectedCharaIndex);

                TaskManager.Enqueue(() => Click.TrySendClick("select_yes"));
            }
            else
                TaskManager.Enqueue(WaitCharaSelectWorldServer);

            return true;
        }

        return false;
    }

    private static unsafe bool? WaitCharaSelectWorldServer()
    {
        if (TryGetAddonByName<AtkUnitBase>("_CharaSelectWorldServer", out var addon))
        {
            var stringArray = Framework.Instance()->UIModule->GetRaptureAtkModule()->AtkModule.AtkArrayDataHolder
                .StringArrays[1];
            if (stringArray == null) return false;

            for (var i = 0; i < 16; i++)
            {
                var serverNamePtr = stringArray->StringArray[i];
                if (serverNamePtr == null) continue;

                var serverName = MemoryHelper.ReadStringNullTerminated(new IntPtr(serverNamePtr));
                if (serverName.Trim().Length == 0) continue;

                if (serverName != ConfigSelectedServer) continue;

                var handler = new ClickCharaSelectWorldServerDR();
                handler.SelectWorld(i);

                TaskManager.DelayNext(200);
                TaskManager.Enqueue(WaitAddonCharaSelectListMenu);

                return true;
            }

            Service.Log.Error($"找寻目标服务器失败: {ConfigSelectedServer}");
        }

        return false;
    }

    public void Uninit()
    {
        Service.Config.Save();
        Service.AddonLifecycle.UnregisterListener(OnTitleMenu);
        TaskManager?.Abort();
        HasLoginOnce = false;

        Initialized = false;
    }
}