using System.Windows;
using System.Windows.Controls;
using GuGuGaGaTranslator.Core.Translation;

namespace GuGuGaGaTranslator.App;

public partial class MainWindow
{
    private CancellationTokenSource? _modelListCancellation;
    private async void OnReadModels(object sender, RoutedEventArgs e)
    {
        ReadModelsButton.IsEnabled = false;
        AvailableModelsCombo.Visibility = Visibility.Collapsed;
        var address = BaseUrlBox.Text.Trim();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _modelListCancellation = cancellation;
        ModelsStatusText.Text = "正在读取模型列表…";
        try
        {
            var models = await ModelServices.ListModelsAsync(address, ApiKeyBox.Password, cancellation.Token);
            if (_closing || address != BaseUrlBox.Text.Trim()) return;
            AvailableModelsCombo.ItemsSource = models;
            AvailableModelsCombo.Visibility = models.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ModelsStatusText.Text = models.Count > 0 ? $"读取到 {models.Count} 个模型。请选择支持文本聊天的模型，选择后测试连接。" : "列表为空，请在服务商控制台确认权限，或手动填写模型名。";
        }
        catch (OperationCanceledException) { if (!_closing) ModelsStatusText.Text = "读取已取消或超时，仍可手动填写模型名。"; }
        catch (Exception error) { if (!_closing) ModelsStatusText.Text = error.Message; }
        finally { _modelListCancellation = null; if (!_closing) ReadModelsButton.IsEnabled = true; }
    }
    private void OnAvailableModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AvailableModelsCombo.SelectedItem is string model) { ModelBox.Text = model; UpdateEngineSummary(); }
    }
}
