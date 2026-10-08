using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using GuGuGaGaTranslator.Core.Config;

namespace GuGuGaGaTranslator.App;

public partial class MainWindow
{
    private FloatingBallWindow? _floatingBall;

    private void ShowMainControls()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void SyncFloatingBall()
    {
        FloatingBallCheck.IsChecked = _session.Config.FloatingBall.Enabled;
        if (!_session.Config.FloatingBall.Enabled)
        {
            _floatingBall?.Close();
            _floatingBall = null;
            return;
        }
        if (_floatingBall is not null) return;
        _floatingBall = new FloatingBallWindow(_session.Config.FloatingBall,
        [
            ("打开主界面", ShowMainControls),
            ("开始翻译", () => OnStart(this, new RoutedEventArgs())),
            ("暂停 / 继续", () => OnPause(this, new RoutedEventArgs())),
            ("停止翻译", () => OnStop(this, new RoutedEventArgs())),
            ("手动框选字幕", () => { if (WindowList.SelectedItem is null) ShowMainControls(); OnRegionAndStart(); }),
            ("切换翻译方向", () => OnMainDirections(this, new RoutedEventArgs())),
            ("添加 / 编辑术语", () => { ShowMainControls(); OnOpenProfilesTab(this, new RoutedEventArgs()); }),
            ("移动译文 / 鼠标穿透", ToggleOverlayEditMode),
            ("服务设置", () => { ShowMainControls(); OnOpenSetup(this, new RoutedEventArgs()); }),
            ("程序 / 词库更新", ShowUpdateControls),
            ("关闭悬浮球", () => _floatingBall?.Close())
        ]);
        _floatingBall.PositionChanged += SaveFloatingBall;
        _floatingBall.Closed += (_, _) =>
        {
            _floatingBall = null;
            if (_closing) return;
            _session.Config.FloatingBall.Enabled = false;
            FloatingBallCheck.IsChecked = false;
            SaveFloatingBall();
        };
        _floatingBall.Show();
        UpdateNotification();
    }

    private void SaveFloatingBall()
    {
        try { _session.SaveConfig(); }
        catch (Exception) { OnNotice("无法保存悬浮球设置，请检查配置目录权限。"); }
    }

    private void OnFloatingBallToggle(object sender, RoutedEventArgs e)
    {
        _session.Config.FloatingBall.Enabled = FloatingBallCheck.IsChecked == true;
        SyncFloatingBall();
        SaveFloatingBall();
    }

    private async void OnQuickKorean(object sender, RoutedEventArgs e) => await QuickDirectionAsync("ko", "ko-KR");

    private void OnMainDirections(object sender, RoutedEventArgs e)
    {
        var point = _floatingBall is { IsVisible: true } ball ? ball.PointToScreen(new Point(0, 58)) : PointToScreen(new Point(320, 200));
        var choice = DirectionChooserWindow.Choose(this, _session.Config.Overlay.LanguagePresets,
            _session.Languages, new Int32Rect((int)point.X, (int)point.Y, 100, 1));
        if (choice is not null) OnLanguageRequested(choice);
    }

    private void OnBottomOptions(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = BottomStripButton };
        var manual = new MenuItem { Header = "手动框选底部对话框（推荐）" };
        manual.Click += (_, _) => OnPickRegion(this, new RoutedEventArgs());
        var automatic = new MenuItem { Header = "使用自动底部预设" };
        automatic.Click += (_, _) =>
        {
            if (_session.Config.Target.ManualRegion && _session.Config.Target.Region is not null)
            {
                OnNotice("已保留手动选区。需要自动预设时，请先清除区域。");
                return;
            }
            OnPickBottomStrip(this, new RoutedEventArgs());
        };
        menu.Items.Add(manual);
        menu.Items.Add(automatic);
        BottomStripButton.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void OnAddQuickTerm(object sender, RoutedEventArgs e)
    {
        var source = QuickTermSource.Text.Trim();
        var target = QuickTermTarget.Text.Trim();
        if (source.Length is 0 or > 200 || target.Length is 0 or > 200)
        {
            OnNotice("请填写原文和译名，各不超过 200 个字符。");
            return;
        }
        var language = _session.Languages;
        if (language.From == "auto")
        {
            OnNotice("添加个人术语前，请选择实际原文语言（中、日、英或韩），避免不同语言混用译名。");
            return;
        }
        var originals = _session.Config.Translation.PersonalTerms;
        var terms = originals.Select(ClonePersonalTerm).ToList();
        var term = terms.FirstOrDefault(item => item.From == language.From && item.To == language.To
            && item.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
        if (term is null)
        {
            if (terms.Count >= 5000) { OnNotice("个人术语已达到 5000 条，请先整理。"); return; }
            term = new PersonalTerm { From = language.From, To = language.To, Source = source };
            terms.Add(term);
        }
        term.Forbidden ??= [];
        if (term.Target.Length > 0 && term.Target != target) term.Forbidden.Add(term.Target);
        term.Target = target;
        term.Forbidden = term.Forbidden.Concat(QuickTermWrong.Text.Split([',', '，', '、', ';', '；'],
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(word => word != target).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        try
        {
            _session.Config.Translation.PersonalTerms = terms;
            _session.SaveConfig();
            _session.ForgetContext();
            QuickTermSource.Clear(); QuickTermTarget.Clear(); QuickTermWrong.Clear();
            UpdateProfileSummary();
            OnNotice($"已保存个人术语：{source} → {target}。当前句重新识别，后续翻译立即使用。");
        }
        catch (Exception)
        {
            _session.Config.Translation.PersonalTerms = originals;
            OnNotice("术语保存失败，请检查配置目录权限。");
        }
    }

    private static PersonalTerm ClonePersonalTerm(PersonalTerm term) => new()
    {
        From = term.From, To = term.To, Source = term.Source, Target = term.Target, Forbidden = [.. term.Forbidden ?? []]
    };

    private void OnManagePersonalTerms(object sender, RoutedEventArgs e)
    {
        var dialog = BuildPersonalTermDialog();
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
            OnNotice($"个人术语已更新，共 {_session.Config.Translation.PersonalTerms.Count} 条；当前句将重新识别。");
    }

    private Window BuildPersonalTermDialog()
    {
        var originals = _session.Config.Translation.PersonalTerms;
        var rows = new ObservableCollection<PersonalTerm>(originals.Select(ClonePersonalTerm));
        var dialog = new Window { Title = "个人术语", Width = 680, Height = 450, MinWidth = 480, MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Theme.Brush("WindowBackgroundBrush", Colors.Black), Foreground = Theme.Brush("TextBrush", Colors.White) };
        var panel = new DockPanel { Margin = new Thickness(14) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false,
            SelectionMode = DataGridSelectionMode.Extended, Background = Theme.Brush("FieldBrush", Colors.Black),
            Foreground = Theme.Brush("TextBrush", Colors.White), RowBackground = Theme.Brush("SurfaceBrush", Colors.Black),
            HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.None };
        var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, Theme.Brush("SurfaceBrush", Colors.Black)));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Brush("TextBrush", Colors.White)));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
        grid.ColumnHeaderStyle = headerStyle;
        foreach (var (header, field, readOnly) in new[] { ("原文语言", "From", true), ("译文语言", "To", true), ("原文", "Source", false), ("译名", "Target", false) })
            grid.Columns.Add(new DataGridTextColumn { Header = header,
                Binding = new Binding(field) { Converter = readOnly ? new PersonalLanguageConverter() : null }, IsReadOnly = readOnly,
                MinWidth = readOnly ? 86 : 140,
                Width = readOnly ? new DataGridLength(86) : new DataGridLength(1, DataGridLengthUnitType.Star) });
        panel.Children.Add(grid);
        var remove = new Button { Content = "删除选中" };
        remove.Click += (_, _) => { foreach (var row in grid.SelectedItems.Cast<PersonalTerm>().ToArray()) rows.Remove(row); };
        var save = new Button { Content = "保存并应用" };
        save.Click += (_, _) =>
        {
            if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) return;
            if (rows.Any(term => string.IsNullOrWhiteSpace(term.Source) || string.IsNullOrWhiteSpace(term.Target)
                || term.Source.Length > 200 || term.Target.Length > 200))
            { MessageBox.Show(dialog, "原文和译名须为 1–200 个字符。"); return; }
            if (rows.GroupBy(term => term.From + "|" + term.To + "|" + term.Source.Trim().ToUpperInvariant()).Any(group => group.Count() > 1))
            { MessageBox.Show(dialog, "同一方向存在重复原文，请先合并。"); return; }
            foreach (var term in rows)
            {
                term.Source = term.Source.Trim(); term.Target = term.Target.Trim();
                var old = originals.FirstOrDefault(item => item.From == term.From && item.To == term.To && item.Source == term.Source);
                if (old is not null && old.Target != term.Target) term.Forbidden.Add(old.Target);
                term.Forbidden = term.Forbidden.Where(word => !string.IsNullOrWhiteSpace(word) && word != term.Target)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            try
            {
                _session.Config.Translation.PersonalTerms = rows.ToList();
                _session.SaveConfig(); _session.ForgetContext();
                dialog.DialogResult = true;
            }
            catch (Exception)
            {
                _session.Config.Translation.PersonalTerms = originals;
                MessageBox.Show(dialog, "无法保存，请检查配置目录权限。");
            }
        };
        var cancel = new Button { Content = "取消", IsCancel = true };
        buttons.Children.Add(remove); buttons.Children.Add(save); buttons.Children.Add(cancel);
        dialog.Content = panel;
        NameScope.SetNameScope(dialog, new NameScope());
        dialog.RegisterName("PersonalTermsGrid", grid);
        dialog.RegisterName("PersonalTermsSave", save);
        dialog.RegisterName("PersonalTermsRemove", remove);
        return dialog;
    }

    private static string DisplayLanguage(string language) => language switch
    {
        "zh-Hans" => "简体中文", "en" => "英语", "ja" => "日语", "ko" => "韩语", "auto" => "自动识别", _ => language
    };

    private sealed class PersonalLanguageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => DisplayLanguage(value?.ToString() ?? "");
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
}
