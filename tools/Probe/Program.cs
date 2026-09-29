namespace GuGuGaGaTranslator.Probe;

/// <summary>
/// Entry point of the verification harness. The harness exists so the pipeline
/// can be proven on a real window without a human watching the screen: every
/// command either prints facts as JSON or writes image evidence to disk.
/// </summary>
internal static class Program
{
    /// <summary>STA is required by the WPF imaging types the capture commands use.</summary>
    [STAThread]
    private static async Task<int> Main(string[] args) => await Commands.RunAsync(args);
}
