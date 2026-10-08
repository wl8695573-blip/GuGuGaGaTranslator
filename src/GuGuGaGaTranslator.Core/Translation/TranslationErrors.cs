using System.Net;
using System.Net.Http;
using GuGuGaGaTranslator.Core.Capture;

namespace GuGuGaGaTranslator.Core.Translation;

public sealed class UnsupportedTranslationDirectionException : InvalidOperationException
{
    public UnsupportedTranslationDirectionException() : base("彩云不支持此翻译方向，请改用有道、百度或支持四语的兼容模型服务。") { }
}

public static class TranslationErrors
{
    public static string Describe(Exception error) => error switch
    {
        CaptureUnavailableException => "目标窗口画面不可读取。请恢复窗口，尝试窗口化或无边框模式；兼容屏幕捕获需保持选区无遮挡。",
        UnsupportedTranslationDirectionException => "彩云不支持此翻译方向，请改用有道、百度或支持四语的兼容模型服务。",
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => "服务拒绝访问，请检查密钥和模型权限。",
        HttpRequestException { StatusCode: HttpStatusCode.PaymentRequired } => "服务账户余额不足或调用权限受限。",
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } => "接口或模型不存在，请检查服务地址和模型名。",
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "服务限流或额度不足，请稍后重试或检查服务账户。",
        HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError } => "翻译服务暂不可用，请稍后重试。",
        HttpRequestException => "无法连接翻译服务，请检查网络、接口地址或本地服务是否启动。",
        OperationCanceledException => "请求超时，请检查服务状态或增加超时时间。",
        _ => "处理失败，请检查识别设置和翻译服务；可导出诊断记录。"
    };
}
