using System.Windows;
using System.Windows.Controls;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Translation;

namespace GuGuGaGaTranslator.App;

/// <summary>The editor behind 「游戏专属模式」: name the work, describe it, list
/// its terms, and let the model draft the list.</summary>
public partial class GameProfileWindow : Window
{
    private sealed record LanguageChoice(string Value, string Label)
    {
        /// <inheritdoc />
        public override string ToString() => Label;
    }

    private static readonly LanguageChoice[] Languages =
    [
        new(string.Empty, "跟随主界面 —— 沿用「识别与翻译」里的语言设置"),
        new("auto", LanguageNames.Label("auto")),
        new("ja", LanguageNames.Label("ja")),
        new("ko", LanguageNames.Label("ko")),
        new("en", LanguageNames.Label("en")),
        new("zh-Hans", LanguageNames.Label("zh-Hans")),
        new("zh-Hant", LanguageNames.Label("zh-Hant")),
    ];

    private static readonly LanguageChoice[] OcrLanguages =
    [
        new(string.Empty, "跟随主界面 —— 沿用「识别与翻译」里的识别语言"),
        new("auto", LanguageNames.Label("auto")),
        new("zh-Hans-CN", LanguageNames.Label("zh-Hans-CN")),
        new("zh-Hant-TW", LanguageNames.Label("zh-Hant-TW")),
        new("ja", LanguageNames.Label("ja")),
        new("en-US", LanguageNames.Label("en-US")),
        new("ko", LanguageNames.Label("ko")),
    ];

    private readonly AppSession _session;
    private readonly GameProfile _profile;

    /// <summary>Create the editor.</summary>
    public GameProfileWindow(AppSession session, GameProfile? profile)
    {
        _session = session;
        InitializeComponent();

        var creating = profile is null;
        _profile = profile is null
            ? new GameProfile { Id = string.Empty }
            : new GameProfile
            {
                Id = profile.Id,
                Name = profile.Name,
                Note = profile.Note,
                WindowHints = [.. profile.WindowHints],
                From = profile.From,
                To = profile.To,
                OcrLanguage = profile.OcrLanguage,
                Worldview = profile.Worldview,
                StyleHint = profile.StyleHint,
                Terms = profile.Terms.Select(term => new GameTerm
                {
                    Source = term.Source,
                    Target = term.Target,
                    Forbidden = [.. term.Forbidden],
                    Note = term.Note,
                }).ToList(),
            };

        Title = creating ? "新建游戏档案" : $"游戏档案 · {profile!.Name}";

        FromCombo.ItemsSource = Languages;
        ToCombo.ItemsSource = Languages;
        OcrCombo.ItemsSource = OcrLanguages;

        NameBox.Text = _profile.Name;
        HintsBox.Text = string.Join(", ", _profile.WindowHints);
        WorldviewBox.Text = _profile.Worldview ?? string.Empty;
        StyleBox.Text = _profile.StyleHint ?? string.Empty;
        TermsBox.Text = _profile.TermsAsText();
        GameNameBox.Text = _profile.Name;
        Select(FromCombo, _profile.From);
        Select(ToCombo, _profile.To);
        Select(OcrCombo, _profile.OcrLanguage);
    }

    public GameProfile? Result { get; private set; }

    public bool Created { get; private set; }

    /// <summary>Start drafting a term sheet as soon as the window appears; set by 「✨ AI 生成术语表…」 so that button is one click.</summary>
    public bool GenerateOnLoad
    {
        get => _generateOnLoad;
        set
        {
            _generateOnLoad = value;
            if (value) Loaded += OnLoadedGenerate;
        }
    }

    private bool _generateOnLoad;

    private void OnLoadedGenerate(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedGenerate;
        if (IsLoaded) OnGenerate(this, new RoutedEventArgs());
    }

    private static void Select(ComboBox combo, string? value)
    {
        var match = ((LanguageChoice[])combo.ItemsSource)
            .FirstOrDefault(choice => choice.Value.Equals(value ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        combo.SelectedItem = match ?? ((LanguageChoice[])combo.ItemsSource)[0];
    }

    private static string ValueOf(ComboBox combo) => (combo.SelectedItem as LanguageChoice)?.Value ?? string.Empty;

    /// <summary>Read the form back into the working copy.</summary>
    private bool ReadForm()
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            SaveStatus.Text = "请先填「名称」。";
            return false;
        }

        var terms = TermSheet.Parse(TermsBox.Text, out var problems);
        if (problems.Count > 0)
        {
            var proceed = MessageBox.Show(
                this,
                $"术语表里有 {problems.Count} 行读不出来:\n\n{string.Join("\n", problems.Take(6))}\n\n其余 {terms.Count} 条仍然要保存吗?",
                "术语表",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (proceed != MessageBoxResult.OK) return false;
        }

        _profile.Name = name;
        _profile.Note = string.IsNullOrWhiteSpace(_profile.Note) ? null : _profile.Note;
        _profile.WindowHints = HintsBox.Text
            .Split([',', '，', ';', '；', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _profile.From = NullIfEmpty(ValueOf(FromCombo));
        _profile.To = NullIfEmpty(ValueOf(ToCombo));
        _profile.OcrLanguage = NullIfEmpty(ValueOf(OcrCombo));
        _profile.Worldview = NullIfEmpty(WorldviewBox.Text);
        _profile.StyleHint = NullIfEmpty(StyleBox.Text);

        // 改过译名的术语不再保留旧写法作为禁用译法:那会把编辑器上一次的答案判成错误,
        // 把每一句正确的译文都改回去。
        foreach (var term in terms) term.Forbidden.RemoveAll(variant => variant.Equals(term.Target, StringComparison.OrdinalIgnoreCase));
        _profile.Terms = terms;

        return true;
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!ReadForm()) return;

        var profiles = _session.Config.Translation.GameProfiles;
        if (string.IsNullOrWhiteSpace(_profile.Id))
        {
            _profile.Id = GameProfiles.MakeId(_profile.Name, profiles.Select(existing => existing.Id));
            profiles.Add(_profile);
            Created = true;
        }
        else
        {
            var index = profiles.FindIndex(existing => existing.Id.Equals(_profile.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) profiles.Add(_profile);
            else profiles[index] = _profile;
        }

        _session.SaveConfig();
        Result = _profile;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnFillSample(object sender, RoutedEventArgs e)
    {
        var lines = _session.RecentSources;
        if (lines.Count == 0)
        {
            SaveStatus.Text = "还没有识别到原文:先开始翻译一会儿,或者自己粘贴几句台词。";
            return;
        }

        SampleBox.Text = string.Join("\n", lines);
        SaveStatus.Text = $"已填入最近识别到的 {lines.Count} 行原文。";
    }

    private async void OnGenerate(object sender, RoutedEventArgs e)
    {
        GenerateButton.IsEnabled = false;

        // 等待时间很长(在线模型开着思考模式),所以用计秒提示而不是留一句静止的话。
        var started = DateTime.Now;
        var ticker = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        ticker.Tick += (_, _) =>
            GenerateStatus.Text = $"正在让 AI 起草术语表… 已等 {(DateTime.Now - started).TotalSeconds:0} 秒"
                + "(在线模型开着思考模式,通常 20–90 秒;术语表只生成一次,之后每句都在用)";
        ticker.Start();

        GenerateStatus.Text = "正在让 AI 起草术语表… 已等 0 秒";

        try
        {
            // 名称栏兼作游戏名:它们是同一个字符串,再问一次就是重新猜用户的意图。
            var name = string.IsNullOrWhiteSpace(GameNameBox.Text) ? NameBox.Text.Trim() : GameNameBox.Text.Trim();
            var existing = _profile.TermsAsText();
            var (terms, detected, suggested, problems, reply) = await _session
                .GenerateTermSheetAsync(name, SampleBox.Text, existing, SynopsisBox.Text)
                .ConfigureAwait(true);

            if (terms.Count == 0)
            {
                GenerateStatus.Text = "AI 没有返回可解析的术语。原始回复:"
                    + Environment.NewLine + Shorten(reply, 900)
                    + (problems.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, problems.Take(4)) : string.Empty);
                return;
            }

            var (merged, added) = TermSheetBuilder.Merge(_profile.Terms, terms);
            _profile.Terms = merged;
            TermsBox.Text = _profile.TermsAsText();

            if (!string.IsNullOrWhiteSpace(detected))
            {
                if (NameBox.Text.Trim().Length == 0) NameBox.Text = detected;
                GameNameBox.Text = detected;
            }

            // 模型写的世界观每次都会随每句话发送,所以已有的(用户自己的措辞)绝不覆盖,只补空缺。
            var filledWorldview = false;
            if (!string.IsNullOrWhiteSpace(suggested) && WorldviewBox.Text.Trim().Length == 0)
            {
                WorldviewBox.Text = suggested;
                filledWorldview = true;
            }

            var bans = terms.Sum(term => term.Forbidden.Count);
            GenerateStatus.Text = $"AI 返回 {terms.Count} 条(新增 {added} 条,补全 {terms.Count - added} 条的禁用译法),"
                + $"其中 {bans} 条带禁止译法,用时 {(DateTime.Now - started).TotalSeconds:0} 秒。"
                + (string.IsNullOrWhiteSpace(detected) ? string.Empty : $" 识别到的作品:{detected}")
                + (filledWorldview ? " 已顺手把「世界观」那一栏填上 AI 的建议,可以自己改短。" : string.Empty)
                + (problems.Count > 0 ? $" 有 {problems.Count} 行没读懂。" : string.Empty)
                + " 检查一遍再保存 —— 尤其是译名,AI 偶尔会把几年前的旧译名当成官方译名。";
        }
        catch (Exception exception)
        {
            GenerateStatus.Text = $"生成失败:{exception.GetType().Name}: {exception.Message}";
        }
        finally
        {
            ticker.Stop();
            GenerateButton.IsEnabled = true;
        }
    }

    private static string Shorten(string text, int max) =>
        string.IsNullOrEmpty(text) ? "(空)" : text.Length <= max ? text : text[..max] + "…";
}
