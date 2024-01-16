using System;
using System.Collections.Generic;
using System.Linq;
using ClickLib;
using ClickLib.Bases;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.AddonLifecycle;
using ECommons.Reflection;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace DailyRoutines.Modules;


[ModuleDescription("AutoMiniCactpotTitle", "AutoMiniCactpotDescription", ModuleCategories.GoldSaucer)]
public class AutoMiniCactpot : IDailyModule
{
    public bool Initialized { get; set; }

    // 从左上到右下 From Top Left to Bottom Right  (Node ID - Callback Index)
    private static readonly Dictionary<uint, uint> BlockNodeIds = new()
    {
        { 30, 0 },
        { 31, 1 },
        { 32, 2 },
        { 33, 3 },
        { 34, 4 },
        { 35, 5 },
        { 36, 6 },
        { 37, 7 },
        { 38, 8 }
    };

    // 从左下到右上 From Bottom Left to Top Right
    private static readonly uint[] LineNodeIds = { 28, 27, 26, 21, 22, 23, 24, 25 };

    public void UI()
    {
        
    }

    public void Init()
    {
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "Talk", OnAddonTalk);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "LotteryDaily", OnAddonSetup);

        Initialized = true;
    }

    private static void OnAddonTalk(AddonEvent type, AddonArgs args)
    {
        if (EzThrottler.Throttle("SkipTalkAutoMiniCactpot", 100))
        {
            var broker = Service.Target.Target;
            if (broker == null || broker.DataId != 1010445) return;
            Click.SendClick("talk");
        }
    }

    private static unsafe void OnAddonSetup(AddonEvent type, AddonArgs args)
    {
        // 装了 ezMiniCactpot -> 取插件算出来的相对优解
        if (IsEzMiniCactpotInstalled())
        {
            P.TaskManager.Enqueue(WaitLotteryDailyAddon);
            P.TaskManager.Enqueue(ClickRecommendBlock);
            P.TaskManager.Enqueue(WaitLotteryDailyAddon);
            P.TaskManager.Enqueue(ClickRecommendBlock);
            P.TaskManager.Enqueue(WaitLotteryDailyAddon);
            P.TaskManager.Enqueue(ClickRecommendBlock);
            P.TaskManager.Enqueue(WaitLotteryDailyAddon);
            P.TaskManager.Enqueue(ClickRecommendLine);
            P.TaskManager.Enqueue(WaitLotteryDailyAddon);
            P.TaskManager.Enqueue(ClickExit);
        }
        else // 没装 -> 随机取
        {
            P.TaskManager.Enqueue(WaitLotteryDailyAddon);
            P.TaskManager.Enqueue(ClickRandomBlocks);
            P.TaskManager.Enqueue(WaitLotteryDailyAddon);
            P.TaskManager.Enqueue(ClickRandomLine);
            P.TaskManager.Enqueue(WaitLotteryDailyAddon);
            P.TaskManager.Enqueue(ClickExit);
        }
    }

    private static unsafe bool? ClickRandomBlocks()
    {
        if (TryGetAddonByName<AddonLotteryDaily>("LotteryDaily", out var addon) && IsAddonReady(&addon->AtkUnitBase))
        {
            var ui = &addon->AtkUnitBase;
            var rnd = new Random();
            var selectedBlocks = BlockNodeIds.Keys.OrderBy(x => rnd.Next()).Take(4).ToArray();
            var clickHandler = new ClickLotteryDaily((nint)ui);
            foreach (var id in selectedBlocks)
            {
                var blockButton = ui->GetComponentNodeById(id);
                if (blockButton == null) continue;

                clickHandler.ClickBlockButton(BlockNodeIds[id]);
            }

            return true;
        }
        return false;
    }

    private static unsafe bool? ClickRandomLine()
    {
        if (TryGetAddonByName<AddonLotteryDaily>("LotteryDaily", out var addon) && IsAddonReady(&addon->AtkUnitBase))
        {
            var ui = &addon->AtkUnitBase;
            var rnd = new Random();
            var selectedLine = LineNodeIds.OrderBy(x => rnd.Next()).LastOrDefault();
            var clickHandler = new ClickLotteryDaily((nint)ui);

            var radioButton = ui->GetComponentNodeById(selectedLine);
            if (radioButton == null) return false;

            clickHandler.ClickLineButton((AtkComponentRadioButton*)radioButton);
            clickHandler.ClickConfirmButton();

            return true;
        }
        return false;
    }

    private static unsafe bool? ClickExit()
    {
        if (TryGetAddonByName<AddonLotteryDaily>("LotteryDaily", out var addon) && IsAddonReady(&addon->AtkUnitBase))
        {
            var ui = &addon->AtkUnitBase;
            var clickHandler = new ClickLotteryDaily((nint)ui);
            clickHandler.ClickExitButton();

            return true;
        }
        return false;
    }

    private static unsafe bool? ClickRecommendBlock()
    {
        if (TryGetAddonByName<AddonLotteryDaily>("LotteryDaily", out var addon) && IsAddonReady(&addon->AtkUnitBase))
        {
            var ui = &addon->AtkUnitBase;
            var clickHandler = new ClickLotteryDaily((nint)ui);
            foreach (var block in BlockNodeIds)
            {
                var node = ui->GetComponentNodeById(block.Key)->AtkResNode;
                if (node is { MultiplyBlue: 0, MultiplyRed: 0, MultiplyGreen: 100 })
                {
                    clickHandler.ClickBlockButton(block.Value);
                    break;
                }
            }

            return true;
        }
        return false;
    }

    private static unsafe bool? ClickRecommendLine()
    {
        if (TryGetAddonByName<AddonLotteryDaily>("LotteryDaily", out var addon) && IsAddonReady(&addon->AtkUnitBase))
        {
            var ui = &addon->AtkUnitBase;
            var clickHandler = new ClickLotteryDaily((nint)ui);
            foreach (var block in LineNodeIds)
            {
                var node = ui->GetComponentNodeById(block)->AtkResNode;
                var button = (AtkComponentRadioButton*)ui->GetComponentNodeById(block);
                if (node is { MultiplyBlue: 0, MultiplyRed: 0, MultiplyGreen: 100 })
                {
                    clickHandler.ClickLineButton(button);
                    break;
                }
            }

            clickHandler.ClickConfirmButton();
            return true;
        }
        return false;
    }

    internal static bool IsEzMiniCactpotInstalled()
    {
        return DalamudReflector.TryGetDalamudPlugin("ezMiniCactpot", out _, true, true);
    }

    private static unsafe bool? WaitLotteryDailyAddon()
    {
        if (TryGetAddonByName<AddonLotteryDaily>("LotteryDaily", out var addon) && IsAddonReady(&addon->AtkUnitBase))
        {
            var ui = &addon->AtkUnitBase;
            return !ui->GetImageNodeById(4)->AtkResNode.IsVisible && !ui->GetTextNodeById(3)->AtkResNode.IsVisible &&
                   !ui->GetTextNodeById(2)->AtkResNode.IsVisible;
        }

        return false;
    }

    public void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(OnAddonTalk);
        Service.AddonLifecycle.UnregisterListener(OnAddonSetup);

        Initialized = false;
    }
}

public class ClickLotteryDaily(nint addon = default)
    : ClickBase<ClickLotteryDaily, AddonLotteryDaily>("LotteryDaily", addon)
{
    public void ClickBlockButton(uint index)
    {
        FireCallback(1, index);
    }

    public unsafe void ClickLineButton(AtkComponentRadioButton* button)
    {
        ClickAddonRadioButton(button, 8);
    }

    public void ClickConfirmButton()
    {
        FireCallback(2, 0);
    }

    public void ClickExitButton()
    {
        FireCallback(-1);
    }
}

