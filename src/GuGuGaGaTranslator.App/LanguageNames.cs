namespace GuGuGaGaTranslator.App;

/// <summary>
/// How languages are written in every menu and list, in one place: <c>中文名 (标签)</c>.
/// </summary>
internal static class LanguageNames
{
    /// <summary>The Chinese name for a language tag, or the tag when it is unknown.</summary>
    public static string Name(string tag) => tag.ToLowerInvariant() switch
    {
        "auto" => "自动",
        "ja" or "ja-jp" or "japanese" => "日语",
        "en" or "en-us" or "en-gb" or "english" => "英语",
        "zh" or "zh-hans" or "zh-hans-cn" or "zh-cn" or "chinese" => "简体中文",
        "zh-hant" or "zh-hant-tw" or "zh-tw" or "traditional chinese" => "繁体中文",
        "ko" or "ko-kr" or "korean" => "韩语",
        "fr" or "fr-fr" => "法语",
        "de" or "de-de" => "德语",
        "es" or "es-es" => "西班牙语",
        "ru" or "ru-ru" => "俄语",
        "pt" or "pt-br" or "pt-pt" => "葡萄牙语",
        "it" or "it-it" => "意大利语",
        "th" or "th-th" => "泰语",
        "vi" or "vi-vn" => "越南语",
        "id" or "id-id" => "印尼语",
        "nl" or "nl-nl" => "荷兰语",
        "pl" or "pl-pl" => "波兰语",
        "tr" or "tr-tr" => "土耳其语",
        "ar" or "ar-sa" => "阿拉伯语",
        "hi" or "hi-in" => "印地语",
        _ => tag,
    };

    /// <summary>The display form: 「日语 (ja)」, or the tag alone when there is no Chinese name.</summary>
    public static string Label(string tag)
    {
        var name = Name(tag);
        return name.Equals(tag, StringComparison.OrdinalIgnoreCase) ? tag : $"{name} ({tag})";
    }

    /// <summary>The display form with a note after it: 「日语 (ja) —— 未安装语言包」.</summary>
    public static string Label(string tag, string? note) =>
        string.IsNullOrWhiteSpace(note) ? Label(tag) : $"{Label(tag)} —— {note.Trim()}";
}
