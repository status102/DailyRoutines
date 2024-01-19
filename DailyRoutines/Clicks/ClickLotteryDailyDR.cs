using ClickLib.Bases;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DailyRoutines.Clicks;

public class ClickLotteryDailyDR(nint addon = default) : ClickBase<ClickLotteryDailyDR, AddonLotteryDaily>("LotteryDaily", addon)
{
    public void Block(uint index) => FireCallback(1, index);

    public unsafe void Line(AtkComponentRadioButton* button) => ClickAddonCheckBox((AtkComponentCheckBox*)button, 8);

    public void Confirm() => FireCallback(2, 0);

    public void Exit() => FireCallback(-1);
}