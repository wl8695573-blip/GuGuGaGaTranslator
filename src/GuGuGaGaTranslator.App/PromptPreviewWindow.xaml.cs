using System.Windows;
using GuGuGaGaTranslator.Core.Translation;

namespace GuGuGaGaTranslator.App;

/// <summary>Shows the two messages the translator would send for one line.</summary>
public partial class PromptPreviewWindow : Window
{
    /// <summary>Create the preview.</summary>
    public PromptPreviewWindow(GameProfile? profile, string languages, string system, string user)
    {
        InitializeComponent();

        ProfileText.Text = profile is null
            ? $"当前:通用翻译 · {languages} —— 没有术语表。专有名词只能靠模型自己判断。"
            : $"当前:{profile.Name} · {languages} —— {profile.Terms.Count} 条术语、"
                + $"{profile.Terms.Sum(term => term.Forbidden.Count)} 条禁用译法"
                + (string.IsNullOrWhiteSpace(profile.Worldview) ? " · 未写世界观" : " · 含世界观描述");

        SystemBox.Text = system;
        UserBox.Text = user;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText($"=== SYSTEM ===\n{SystemBox.Text}\n\n=== USER ===\n{UserBox.Text}");
            ProfileText.Text += "   (已复制到剪贴板)";
        }
        catch (Exception exception)
        {
            ProfileText.Text += $"   (复制失败:{exception.Message})";
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
