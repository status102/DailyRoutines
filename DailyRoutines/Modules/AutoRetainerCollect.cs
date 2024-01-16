using System.Collections.Generic;
using ClickLib;
using ClickLib.Clicks;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.AddonLifecycle;
using Dalamud.Memory;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoRetainerCollectTitle", "AutoRetainerCollectDescription", ModuleCategories.General)]
public class AutoRetainerCollect : IDailyModule
{
    public bool Initialized { get; set; }

    private static bool IsOnProcess;

    public void UI()
    {

    }

    public void Init()
    {
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "Talk", SkipTalk);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerList", OnRetainerList);

        Initialized = true;
    }

    private static void SkipTalk(AddonEvent eventType, AddonArgs addonInfo)
    {
        if (EzThrottler.Throttle("SkipTalkAutoRetainerCollect", 100))
        {
            var bell = Service.Target.Target;
            if (bell == null || (bell.DataId != 2000401 && bell.DataId != 196630)) return;
            Click.SendClick("talk");
        }
    }

    private static unsafe void OnRetainerList(AddonEvent type, AddonArgs args)
    {
        if (!IsOnProcess)
        {
            IsOnProcess = true;

            var retainerManager = RetainerManager.Instance();
            var serverTime = Framework.GetServerTime();
            var completeRetainers = new List<int>();
            for (var i = 0; i < 10; i++)
            {
                var retainerState = retainerManager->GetRetainerBySortedIndex((uint)i)->VentureComplete;
                if (retainerState == 0) continue;
                if (retainerState - serverTime <= 0) completeRetainers.Add(i);
            }

            for (var r = 0; r < completeRetainers.Count; r++)
            {
                EnqueueSingleRetainer(completeRetainers[r]);
                // 防止卡住
                if (r == completeRetainers.Count - 1)
                {
                    P.TaskManager.Enqueue(ExitToRetainerList);
                }
            }

            IsOnProcess = false;
        }
    }

    private static void EnqueueSingleRetainer(int index)
    {
        // 雇员列表是否可用
        P.TaskManager.Enqueue(WaitRetainerListAddon);
        // 点击指定雇员
        P.TaskManager.Enqueue(() => ClickSpecificRetainer(index));
        // 等待选择界面
        P.TaskManager.Enqueue(WaitSelectStringAddon);
        // 点击查看探险情况
        P.TaskManager.Enqueue(CheckVentureState);
        // 重新派遣
        P.TaskManager.Enqueue(ClickVentureReassign);
        // 确认派遣
        P.TaskManager.Enqueue(ClickVentureConfirm);
        // 回到雇员列表
        P.TaskManager.Enqueue(ExitToRetainerList);
        // 雇员列表是否可用
        P.TaskManager.Enqueue(WaitRetainerListAddon);
    }

    private static unsafe bool? WaitRetainerListAddon()
    {
        return TryGetAddonByName<AddonRetainerList>("RetainerList", out var addon) && IsAddonReady(&addon->AtkUnitBase);
    }

    private static bool? ClickSpecificRetainer(int index)
    {
        var handler = new ClickRetainerList();
        handler.Retainer(index);
        return true;
    }

    private static unsafe bool? WaitSelectStringAddon()
    {
        return TryGetAddonByName<AddonSelectString>("SelectString", out var addon) && IsAddonReady(&addon->AtkUnitBase);
    }

    internal static unsafe bool? CheckVentureState()
    {
        if (TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) && IsAddonReady(addon))
        {
            var text = MemoryHelper
                       .ReadSeString(
                           &addon->UldManager.NodeList[2]->GetAsAtkComponentNode()->Component->UldManager.NodeList[6]->
                               GetAsAtkComponentNode()->Component->UldManager.NodeList[3]->GetAsAtkTextNode()->NodeText)
                       .ExtractText().Trim();
            Service.Log.Debug(text);
            if (string.IsNullOrEmpty(text) || text.Contains('～'))
            {
                P.TaskManager.Enqueue(ExitToRetainerList);
                P.TaskManager.Abort();
                IsOnProcess = false;
                return false;
            }

            if (Click.TrySendClick("select_string6")) return true;
        }

        return false;
    }

    private static unsafe bool? ClickVentureReassign()
    {
        if (TryGetAddonByName<AddonRetainerTaskResult>("RetainerTaskResult", out var addon) &&
            IsAddonReady(&addon->AtkUnitBase))
        {
            if (Click.TrySendClick("retainer_venture_result_reassign"))
                return true;
        }

        return false;
    }

    private static unsafe bool? ClickVentureConfirm()
    {
        if (TryGetAddonByName<AddonRetainerTaskAsk>("RetainerTaskAsk", out var addon) &&
            IsAddonReady(&addon->AtkUnitBase))
        {
            if (Click.TrySendClick("retainer_venture_ask_assign"))
                return true;
        }

        return false;
    }

    private static unsafe bool? ExitToRetainerList()
    {
        if (TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) && IsAddonReady(addon))
        {
            if (Click.TrySendClick("select_string13"))
                return true;
        }

        return false;
    }

    public void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(SkipTalk);
        Service.AddonLifecycle.UnregisterListener(OnRetainerList);
        IsOnProcess = false;
        P.TaskManager.Abort();

        Initialized = false;
    }
}
