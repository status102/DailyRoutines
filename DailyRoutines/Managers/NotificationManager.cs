using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Timers;
using System.Windows.Forms;
using Timer = System.Timers.Timer;

namespace DailyRoutines.Managers;

public class NotificationManager
{
    private class ToastMessage(string? title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        public string? Title { get; set; } = title;
        public string Message { get; set; } = message;
        public ToolTipIcon Icon { get; set; } = icon;
    }

    private static NotifyIcon? icon;
    private readonly Queue<ToastMessage> messagesQueue = new();
    private readonly Timer timer = new(5000);
    private bool isTimerScheduled;

    public void Init()
    {
        timer.AutoReset = false;
        timer.Elapsed += TimerElapsed;
    }

    private void TimerElapsed(object? sender, ElapsedEventArgs e)
    {
        switch (messagesQueue.Count)
        {
            case > 1:
                ShowBalloonTip(new ToastMessage(
                                   "",
                                   Service.Lang.GetText("NotificationManager-ReceiveMultipleMessages",
                                                        messagesQueue.Count)));
                break;
            case 1:
                ShowBalloonTip(messagesQueue.Dequeue());
                break;
        }

        isTimerScheduled = false;
        messagesQueue.Clear();

        if (messagesQueue.Count == 0)
            DestroyIcon();
        else
            ScheduleTimer();
    }

    public void Show(string title, string content, ToolTipIcon toolTipIcon = ToolTipIcon.Info)
    {
        if (icon is not { Visible: true }) CreateIcon();
        messagesQueue.Enqueue(new ToastMessage(title, content, toolTipIcon));

        if (!isTimerScheduled)
        {
            ShowBalloonTip(messagesQueue.Dequeue());
            ScheduleTimer();
        }
    }

    private void ScheduleTimer()
    {
        timer.Stop();
        timer.Start();
        isTimerScheduled = true;
    }

    private void ShowBalloonTip(ToastMessage message)
    {
        icon.ShowBalloonTip(
            5000, string.IsNullOrEmpty(message.Title) ? P.Name : SanitizeManager.Sanitize(message.Title),
            SanitizeManager.Sanitize(message.Message), message.Icon);
    }

    private void CreateIcon()
    {
        DestroyIcon();
        icon = new NotifyIcon
        {
            Icon = new Icon(Path.Join(P.PluginInterface.AssemblyLocation.DirectoryName, "Assets", "FFXIVICON.ico")),
            Text = P.Name,
            Visible = true
        };
    }

    private void DestroyIcon()
    {
        if (icon != null)
        {
            icon.Visible = false;
            icon.Dispose();
            icon = null;
        }
    }

    public void Dispose()
    {
        timer.Elapsed -= TimerElapsed;
        timer.Dispose();
        DestroyIcon();
    }
}
