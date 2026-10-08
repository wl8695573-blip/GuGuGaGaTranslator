using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GuGuGaGaTranslator.App;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Interop;
using GuGuGaGaTranslator.Core.Updates;

internal static partial class Program
{
    private static void InvokeClick(object window, string method) => window.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);

    private static void CheckLcta15(string scratch)
    {
        var config = new FloatingBallConfig { X = 240, Y = 200 };
        var chosen = 0;
        var saved = 0;
        var ball = new FloatingBallWindow(config, [("测试动作", () => chosen++)]);
        ball.PositionChanged += () => saved++;
        try
        {
            ball.Show(); Pump();
            var handle = Field<Thumb>(ball, "_dragHandle");
            var menu = Field<ContextMenu>(ball, "_menu");
            handle.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            handle.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Pump();
            Check(menu.IsOpen, "floating ball short click opens its menu");
            ((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(chosen == 1, "floating ball menu invokes selected action once");
            menu.IsOpen = false;
            var before = ball.PointToScreen(new Point());
            handle.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            handle.RaiseEvent(new DragDeltaEventArgs(35, 20) { RoutedEvent = Thumb.DragDeltaEvent });
            handle.RaiseEvent(new DragCompletedEventArgs(35, 20, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Pump();
            var after = ball.PointToScreen(new Point());
            var dpi = VisualTreeHelper.GetDpi(ball);
            Check(after.X == before.X + Math.Round(35 * dpi.DpiScaleX)
                && after.Y == before.Y + Math.Round(20 * dpi.DpiScaleY)
                && config.X == (int)after.X && config.Y == (int)after.Y && saved == 1 && !menu.IsOpen,
                "floating ball drag moves at current DPI, saves position and does not open menu");
            handle.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            handle.RaiseEvent(new DragDeltaEventArgs(-100000, -100000) { RoutedEvent = Thumb.DragDeltaEvent });
            handle.RaiseEvent(new DragCompletedEventArgs(-100000, -100000, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Pump();
            var bounded = ball.PointToScreen(new Point());
            var area = OverlayWindowInterop.WorkAreaAt(new Int32Rect((int)bounded.X, (int)bounded.Y, 58, 58));
            Check(bounded.X >= area.X && bounded.Y >= area.Y, "floating ball released outside desktop returns to work area");
            ((Button)((Grid)ball.Content).Children[2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!ball.IsVisible && !menu.IsOpen, "floating ball close button closes its menu and window");
        }
        finally { ball.Close(); }

        var directory = Path.Combine(scratch, "lcta15-ui-" + Guid.NewGuid().ToString("N"));
        var session = new AppSession(directory);
        session.LoadConfig();
        session.Config.FloatingBall.X = 240;
        session.Config.FloatingBall.Y = 200;
        foreach (var property in session.Config.Hotkeys.GetType().GetProperties())
            if (property.PropertyType == typeof(string)) property.SetValue(session.Config.Hotkeys, "");
        var main = new MainWindow(session) { Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            main.Show(); Pump();
            var first = Field<FloatingBallWindow>(main, "_floatingBall");
            Check(first.IsVisible, "main window opens floating ball when enabled");
            first.Close(); Pump();
            Check(main.IsVisible && !session.Config.FloatingBall.Enabled
                && ((CheckBox)main.FindName("FloatingBallCheck")).IsChecked == false,
                "closing floating ball preserves main window and disables saved option");
            ((CheckBox)main.FindName("FloatingBallCheck")).IsChecked = true;
            InvokeClick(main, "OnFloatingBallToggle"); Pump();
            var second = Field<FloatingBallWindow>(main, "_floatingBall");
            Check(second.IsVisible && !ReferenceEquals(first, second), "main switch reopens a closed floating ball");
            var termOffer = typeof(MainWindow).GetField("_termUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!;
            termOffer.SetValue(main, new TermUpdate("2026.10.8.2", new Uri(UpdateClient.RawRoot + "limbus-company.ggprofile.json"), new string('0', 64)));
            Invoke(main, "UpdateNotification");
            Check(((Button)main.FindName("UpdateBadgeButton")).Visibility == Visibility.Visible
                && Field<TextBlock>(second, "_updateMark").Visibility == Visibility.Visible,
                "update offer remains visible in main header and floating ball");
            InvokeClick(main, "OnShowUpdates");
            Check(((TabControl)main.FindName("SettingsTabs")).SelectedIndex == 5, "update notification opens update settings");
            termOffer.SetValue(main, null);
            Invoke(main, "UpdateNotification");
            Check(Field<TextBlock>(second, "_updateMark").Visibility == Visibility.Collapsed,
                "applied update clears floating ball notification");

            ((TextBox)main.FindName("QuickTermSource")).Text = "Outis";
            ((TextBox)main.FindName("QuickTermTarget")).Text = "个人奥提斯";
            InvokeClick(main, "OnAddQuickTerm");
            Check(session.Config.Translation.PersonalTerms.Count == 0, "quick term requires an explicit source language");
            session.ApplyLanguagePresetAsync(new() { From = "en", To = "zh-Hans", Ocr = "en-US" }).GetAwaiter().GetResult();
            ((TextBox)main.FindName("QuickTermWrong")).Text = "奥蒂斯";
            InvokeClick(main, "OnAddQuickTerm");
            Check(session.CurrentProfile.Glossary.Single(term => term.Source == "Outis").Target == "个人奥提斯"
                && session.Config.Translation.PersonalTerms.Single().Forbidden.Contains("奥蒂斯"),
                "quick term immediately changes active glossary and records wrong translation");
            ((TextBox)main.FindName("QuickTermSource")).Text = "Outis";
            ((TextBox)main.FindName("QuickTermTarget")).Text = "奥提斯";
            InvokeClick(main, "OnAddQuickTerm");
            Check(session.Config.Translation.PersonalTerms.Count == 1
                && session.Config.Translation.PersonalTerms.Single().Forbidden.Contains("个人奥提斯"),
                "quick term edits existing row and preserves previous target for correction");
            CheckPersonalTermManager(main, session);

            session.Config.Target.Region = new RegionRect(10, 400, 600, 100);
            session.Config.Target.ManualRegion = true;
            session.Config.Target.RegionSettingsVersion = 1;
            InvokeClick(main, "OnBottomOptions"); Pump();
            var menu = ((Button)main.FindName("BottomStripButton")).ContextMenu;
            ((MenuItem)menu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.IsOpen = false;
            Check(session.Config.Target.Region == new RegionRect(10, 400, 600, 100) && session.Config.Target.ManualRegion,
                "automatic bottom preset never overwrites manual subtitle area");
            session.SaveConfig();
        }
        finally { main.Close(); session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        var restored = new AppSession(directory);
        try
        {
            restored.LoadConfig();
            Check(restored.Config.Target.ManualRegion && restored.Config.Target.Region == new RegionRect(10, 400, 600, 100),
                "manual subtitle area survives restarting session");
            Check(restored.Config.Translation.PersonalTerms.Single().Target == "奥提斯", "quick term survives restarting session");
        }
        finally { restored.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private static void CheckPersonalTermManager(MainWindow main, AppSession session)
    {
        var build = typeof(MainWindow).GetMethod("BuildPersonalTermDialog", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var cancel = (Window)build.Invoke(main, null)!;
        cancel.Left = cancel.Top = -10000;
        cancel.ShowInTaskbar = false;
        cancel.WindowStartupLocation = WindowStartupLocation.Manual;
        cancel.Loaded += (_, _) => cancel.Dispatcher.BeginInvoke(new Action(() =>
        {
            var grid = (DataGrid)cancel.FindName("PersonalTermsGrid");
            ((PersonalTerm)grid.Items[0]).Target = "未保存译名";
            cancel.Close();
        }));
        cancel.ShowDialog();
        Check(session.Config.Translation.PersonalTerms.Single().Target == "奥提斯", "canceling personal term edit preserves active glossary");
        var edit = (Window)build.Invoke(main, null)!;
        edit.Left = edit.Top = -10000;
        edit.ShowInTaskbar = false;
        edit.WindowStartupLocation = WindowStartupLocation.Manual;
        edit.Loaded += (_, _) => edit.Dispatcher.BeginInvoke(new Action(() =>
        {
            var grid = (DataGrid)edit.FindName("PersonalTermsGrid");
            var row = (PersonalTerm)grid.Items[0];
            row.Target = "更新奥提斯";
            ((Button)edit.FindName("PersonalTermsSave")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        Check(edit.ShowDialog() == true && session.CurrentProfile.Glossary.Single(term => term.Source == "Outis").Target == "更新奥提斯",
            "saving personal term manager immediately applies edited target");
        var remove = (Window)build.Invoke(main, null)!;
        remove.Left = remove.Top = -10000;
        remove.ShowInTaskbar = false;
        remove.WindowStartupLocation = WindowStartupLocation.Manual;
        remove.Loaded += (_, _) => remove.Dispatcher.BeginInvoke(new Action(() =>
        {
            var grid = (DataGrid)remove.FindName("PersonalTermsGrid");
            grid.SelectedItem = grid.Items[0];
            ((Button)remove.FindName("PersonalTermsRemove")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Button)remove.FindName("PersonalTermsSave")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        Check(remove.ShowDialog() == true && session.Config.Translation.PersonalTerms.Count == 0,
            "personal term manager deletes selected entries and saves removal");
        ((TextBox)main.FindName("QuickTermSource")).Text = "Outis";
        ((TextBox)main.FindName("QuickTermTarget")).Text = "奥提斯";
        InvokeClick(main, "OnAddQuickTerm");
    }
}
