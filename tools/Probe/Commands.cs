using System.Globalization;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using GuGuGaGaTranslator.Core.Capture;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Imaging;
using GuGuGaGaTranslator.Core.Interop;
using GuGuGaGaTranslator.Core.Ocr;
using GuGuGaGaTranslator.Core.Pipeline;
using GuGuGaGaTranslator.Core.Text;
using GuGuGaGaTranslator.Core.Translation;
using GuGuGaGaTranslator.Ocr.Rapid;
using Windows.Media.Ocr;

namespace GuGuGaGaTranslator.Probe;

/// <summary>The probe's command surface: every command either prints facts as JSON or
/// writes image evidence to disk, so the pipeline can be verified unattended.</summary>
internal static class Commands
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Run one probe command; the exit code is 0 success, 1 failure, 2 usage error.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        DpiAwareness.EnablePerMonitorV2();

        if (args.Length == 0) return Usage();
        var options = Options.Parse(args.Skip(1));

        try
        {
            return args[0] switch
            {
                "ocr-langs" => OcrLanguages(),
                "windows" => Windows(options),
                "capture" => Capture(options),
                "ocr" => await OcrAsync(options).ConfigureAwait(false),
                "inspect" => Inspect(options),
                "run" => await RunOnceAsync(options).ConfigureAwait(false),
                "translate" => await TranslateAsync(options).ConfigureAwait(false),
                "terms" => Terms(options),
                "term-sheet" => await TermSheetAsync(options).ConfigureAwait(false),
                "sign-check" => SignCheck(),
                "icon" => Icon(options),
                "fake-api" => await FakeApiAsync(options).ConfigureAwait(false),
                "watch" => await WatchAsync(options).ConfigureAwait(false),
                "help" or "--help" or "-h" => Usage(),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAILED: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("""
            gugugaga-probe — pipeline verification harness

              ocr-langs                      list OCR languages this machine can use
              windows [--filter <text>]      list candidate target windows
              capture --title <text>         capture a window's client area
                      [--identity <id>] [--backend screen|printwindow]
                      [--out <file.png>] [--region x,y,w,h]
              ocr --image <file.png>         recognize text in a PNG
                      [--lang <tag>] [--scale <factor>] [--gray <0..1>]
              run --title <text>             one capture → recognize → translate, then report
                      [--identity <id>] [--lang <tag>] [--from <tag>] [--to <tag>]
                      [--translator mock|openai-compatible] [--base-url <url>]
                      [--model <name>] [--api-key <key>] [--scale <f>] [--gray <f>]
                      [--region x,y,w,h] [--out <file.png>] [--game <profile id>]
              terms                          parse a term sheet and show what the enforcer rewrites
                      [--text "<sheet>"] [--sheet <file>] [--game <profile id>]
                      [--sample "错译1|错译2"]      (| separates samples)
              term-sheet                     ask the engine for a game's term sheet, then parse it
                      [--game-name <name>] [--sample "line1|line2"] [--sheet <file>]
                      [--prompt-only] + run's engine options
              icon --exe <file> [--ico <file>]   dump the icon resources inside a built exe, and check
                                                 them against an .ico frame by frame
              sign-check                     verify the classic APIs' signing against the published
                                             examples (百度's documented MD5 vector, 有道's rule)
              fake-api --provider youdao|baidu   stand-in provider that validates signatures, so the
                      [--port <n>] [--secret <s>] [--seconds <n>]   protocols can be tested with no account
              watch --title <text>           run the real translation loop and log every decision
                      [--seconds <n>] [--interval <ms>] [--dump <dir>] + run's options
              help                           this text

            Selection: --title matches a window title substring (case-insensitive);
            --identity matches the stable process|class form printed by `windows`.
            """);
        return 2;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"unknown command: {command}");
        return Usage();
    }

    /// <summary>List the installed OCR recognizers and whether each target language can
    /// construct an engine; Japanese OCR is an optional Windows feature.</summary>
    private static int OcrLanguages()
    {
        var available = OcrEngine.AvailableRecognizerLanguages
            .Select(language => new
            {
                language.LanguageTag,
                language.DisplayName,
                language.NativeName,
            })
            .ToArray();

        var targets = new[] { "ja", "en-US", "zh-Hans-CN", "zh-Hant-TW" }
            .Select(tag =>
            {
                var recognizer = WindowsOcrRecognizer.TryCreate(tag);
                return new
                {
                    requested = tag,
                    usable = recognizer is not null,
                    engine = recognizer?.Id,
                };
            })
            .ToArray();

        Console.WriteLine(Serialize(new
        {
            maxImageDimension = OcrEngine.MaxImageDimension,
            availableCount = available.Length,
            available,
            targets,
        }));

        return available.Length == 0 ? 1 : 0;
    }

    private static int Windows(Options options)
    {
        var filter = options.Get("filter");
        var windows = WindowEnumerator.List()
            .Where(window => filter is null
                || window.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || window.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Console.WriteLine(Serialize(new
        {
            count = windows.Length,
            windows = windows.Select(window => new
            {
                window.Identity,
                title = window.Title,
                className = window.ClassName,
                processId = window.ProcessId,
                handle = $"0x{window.Handle:X}",
                client = Rect(window.ClientRect),
                frame = Rect(window.FrameRect),
                window.IsMinimized,
                window.IsOwned,
            }),
        }));

        return windows.Length == 0 ? 1 : 0;
    }

    private static int Capture(Options options)
    {
        var window = SelectWindow(options);
        if (window is null) return 1;

        var region = options.Get("region") is { } text ? ParseRect(text) : (Int32Rect?)null;
        var backend = options.Get("backend")?.Equals("printwindow", StringComparison.OrdinalIgnoreCase) == true
            ? CaptureBackend.PrintWindow
            : CaptureBackend.Screen;

        var frame = region is { } explicitRegion
            ? ScreenCapture.CaptureScreenRegion(explicitRegion)
            : ScreenCapture.CaptureWindowClient(window, backend);

        var outPath = options.Get("out");
        if (outPath is not null) ImageOps.SavePng(frame, outPath);

        var signature = FrameHasher.Compute(frame);
        Console.WriteLine(Serialize(new
        {
            window = new { window.Identity, title = window.Title, handle = $"0x{window.Handle:X}" },
            backend = backend.ToString(),
            frame = new
            {
                width = frame.Width,
                height = frame.Height,
                region = Rect(frame.SourceRegion),
                capturedAt = frame.CapturedAt.ToString("O"),
            },
            pixels = new
            {
                length = frame.Bgra.Length,
                expected = frame.Width * frame.Height * 4,
                stride = frame.Stride,
            },
            signature = new
            {
                rows = signature.Rows,
                rows2 = signature.Rows2,
                rows3 = signature.Rows3,
                rows4 = signature.Rows4,
            },
            savedTo = outPath,
        }));

        return 0;
    }

    private static async Task<int> OcrAsync(Options options)
    {
        var imagePath = options.Get("image") ?? throw new ArgumentException("--image is required");
        var language = options.Get("lang") ?? "ja";
        var scale = options.GetDouble("scale", 2.0);
        var gray = options.GetDouble("gray", 0.0);

        var frame = ImageOps.LoadPng(imagePath);
        var prepared = ImageOps.Upscale(ImageOps.ToGrayscale(frame, gray), scale);

        var recognizer = WindowsOcrRecognizer.TryCreateWithFallback(language, "en-US", "zh-Hans-CN")
            ?? throw new InvalidOperationException("no OCR recognizer is available on this machine");
        var result = await recognizer.RecognizeAsync(prepared).ConfigureAwait(false);

        Console.WriteLine(Serialize(new
        {
            image = imagePath,
            requestedLanguage = language,
            recognizer = new { result.RecognizerId, result.LanguageTag },
            transform = new { scale, gray, preparedWidth = prepared.Width, preparedHeight = prepared.Height },
            durationMs = Math.Round(result.Duration.TotalMilliseconds, 1),
            lineCount = result.Lines.Count,
            text = result.Text,
            lines = result.Lines.Select(line => new
            {
                line.Text,
                box = Rect(line.Box),
                words = line.Words.Select(word => new { word.Text, box = Rect(word.Box) }),
            }),
        }));

        return result.IsEmpty ? 1 : 0;
    }

    /// <summary>Translate text directly, with no window and no capture, so two engines can
    /// be compared on identical input.</summary>
    private static async Task<int> TranslateAsync(Options options)
    {
        var text = options.Get("text") ?? throw new ArgumentException("--text is required");
        var from = options.Get("from", "ja");
        var to = options.Get("to", "zh-Hans");
        var translator = CreateTranslator(options);

        // --game runs the line through a real profile's terms and the enforcer, as the overlay does.
        var profile = options.Get("game") is { } gameId ? LoadProfile(gameId) : null;
        var glossary = profile is null
            ? (options.Get("sheet") is { } sheetPath
                ? GameProfiles.ToGlossary(new GameProfile { Terms = TermSheet.Parse(File.ReadAllText(sheetPath)) })
                : [])
            : GameProfiles.ToGlossary(profile);

        var request = new TranslationRequest
        {
            Text = text,
            From = from,
            To = to,
            Glossary = glossary,
            StyleHint = profile?.StyleHint ?? options.Get("style-hint"),
            Worldview = profile?.Worldview,
            Context = options.Get("history") is { } previous
                ? [new TranslationHistory(string.Empty, previous)]
                : [],
        };

        // --show-prompt prints exactly what the engine would receive, term table included.
        if (options.Get("show-prompt") is not null)
        {
            var (system, user) = OpenAiCompatibleTranslator.PreviewPrompts(request, ParsePromptStyle(options));
            Console.WriteLine(Serialize(new
            {
                profile = profile?.Name,
                glossaryTerms = glossary.Count,
                systemPrompt = system,
                userPrompt = user,
            }));

            return 0;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        string translation;
        try
        {
            // --stream measures what the overlay experiences: time to first visible text.
            if (options.Get("stream") is not null && translator is IStreamingTranslator streaming)
            {
                double firstDelta = 0;
                var deltas = 0;
                translation = await streaming.TranslateAsync(request, partial =>
                {
                    if (deltas++ == 0) firstDelta = watch.Elapsed.TotalMilliseconds;
                }).ConfigureAwait(false);

                watch.Stop();
                Console.WriteLine(Serialize(new
                {
                    translator = translator.Id,
                    promptStyle = options.Get("prompt-style", "galgame"),
                    streaming = true,
                    firstDeltaMs = Math.Round(firstDelta, 1),
                    deltaCount = deltas,
                    totalMs = Math.Round(watch.Elapsed.TotalMilliseconds, 1),
                    languages = new { from, to },
                    text,
                    translation,
                }));

                return 0;
            }

            translation = await translator.TranslateAsync(request).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAILED: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }

        watch.Stop();
        var (enforced, termFixes) = TermEnforcer.Apply(translation, glossary);
        Console.WriteLine(Serialize(new
        {
            translator = translator.Id,
            promptStyle = options.Get("prompt-style", "galgame"),
            languages = new { from, to },
            profile = profile?.Name,
            glossaryTerms = glossary.Count,
            elapsedMs = Math.Round(watch.Elapsed.TotalMilliseconds, 1),
            text,
            translation,
            enforced,
            termFixes = termFixes.Count == 0 ? null : termFixes.Select(fix => $"{fix.From} → {fix.To} ({fix.Reason})").ToArray(),
        }));

        return 0;
    }

    /// <summary>Parse a term sheet and run the enforcer over sample translations, which verifies
    /// the 「禁止译法」 behaviour with no game, screenshot, or network.</summary>
    private static int Terms(Options options)
    {
        var sheetPath = options.Get("sheet");
        var sheet = sheetPath is not null ? File.ReadAllText(sheetPath) : options.Get("text");
        var profile = sheet is null ? LoadProfile(options.Get("game")) : null;

        if (sheet is null && profile is null)
        {
            throw new ArgumentException("--text <术语表文本>、--sheet <文件> 或 --game <档案 id> 需要一个");
        }

        List<string> problems = [];
        var terms = sheet is not null
            ? TermSheet.Parse(sheet, out problems)
            : profile!.Terms;

        var glossary = sheet is not null
            ? terms.Select(term => new GlossaryEntry(term.Source, term.Target, term.Forbidden.Count > 0 ? term.Forbidden.ToArray() : null)).ToList()
            : GameProfiles.ToGlossary(profile);

        var samples = options.Get("sample")?.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            ?? [];

        var results = samples.Select(sample =>
        {
            var (text, fixes) = TermEnforcer.Apply(sample, glossary);
            return new
            {
                input = sample,
                output = text,
                changed = !text.Equals(sample, StringComparison.Ordinal),
                fixes = fixes.Select(fix => $"{fix.From} → {fix.To} ({fix.Reason})").ToArray(),
            };
        }).ToArray();

        Console.WriteLine(Serialize(new
        {
            source = sheetPath ?? (profile is not null ? $"profile:{profile.Id}" : "text"),
            profile = profile?.Name,
            worldview = profile?.Worldview,
            styleHint = profile?.StyleHint,
            parsedTerms = terms.Count,
            parsedForbidden = terms.Sum(term => term.Forbidden.Count),
            problems,
            fingerprint = TermEnforcer.Fingerprint(glossary),
            terms = terms.Select(term => $"{term.Source} = {term.Target}"
                + (term.Forbidden.Count > 0 ? $"  (禁止: {string.Join("、", term.Forbidden)})" : string.Empty)),
            samples = results,
        }));

        return problems.Count > 0 ? 1 : 0;
    }

    /// <summary>Ask the configured engine to draft a term sheet and show what the parser made of
    /// its answer, plus the prompt itself.</summary>
    private static async Task<int> TermSheetAsync(Options options)
    {
        var game = options.Get("game-name") ?? options.Get("game");
        var samples = options.Get("sample")?.Replace("|", "\n");
        var existing = options.Get("sheet") is { } path ? File.ReadAllText(path) : null;

        var prompt = TermSheetBuilder.BuildUserPrompt(game, options.Get("to", "zh-Hans"), samples, existing, options.Get("synopsis"));
        if (options.Get("prompt-only") is not null)
        {
            Console.WriteLine(prompt);
            return 0;
        }

        var translator = options.Get("config") is not null
            ? TranslatorFactory.CreateForTermSheet(new ConfigStore().Load().Translation.Translator)
            : CreateTranslator(options);
        if (translator is not IChatCompleter completer)
        {
            Console.Error.WriteLine($"FAILED: {translator.Id} cannot answer a plain question");
            return 1;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        string reply;
        try
        {
            reply = await completer.CompleteAsync(TermSheetBuilder.SystemPrompt, prompt).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAILED: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }

        watch.Stop();
        var (terms, detected, worldview, problems) = TermSheetBuilder.ParseReply(reply);

        Console.WriteLine(Serialize(new
        {
            translator = translator.Id,
            elapsedMs = Math.Round(watch.Elapsed.TotalMilliseconds, 1),
            detectedGame = detected,
            suggestedWorldview = worldview,
            parsedTerms = terms.Count,
            parsedForbidden = terms.Sum(term => term.Forbidden.Count),
            problems,
            terms = terms.Select(term => term.ToString()),
            reply,
        }));

        return terms.Count == 0 ? 1 : 0;
    }

    /// <summary>Read a game profile out of the live configuration, so the probe measures the
    /// same table the application would send.</summary>
    private static GameProfile LoadProfile(string? id)
    {
        var config = new ConfigStore().Load();
        var profiles = config.Translation.GameProfiles;
        var wanted = string.IsNullOrWhiteSpace(id) ? config.Translation.ActiveProfile : id!;
        var profile = GameProfiles.FindById(wanted, profiles);

        if (profile is null && string.IsNullOrWhiteSpace(id) && profiles.Count > 0) profile = profiles[0];
        if (profile is null)
        {
            throw new ArgumentException(
                $"no game profile '{wanted}' in the configuration (have: {string.Join(", ", profiles.Select(p => p.Id))})");
        }

        return profile;
    }

    // Icon.ExtractAssociatedIcon is avoided: GDI+ cannot decode the PNG-compressed frames modern .ico files use.
    /// <summary>Report the icon resources embedded in a built executable, optionally checked
    /// against an .ico file.</summary>
    private static int Icon(Options options)
    {
        var exe = options.Get("exe") ?? throw new ArgumentException("--exe <path to exe> is required");
        if (!File.Exists(exe)) throw new FileNotFoundException($"no such file: {exe}");

        var frames = ReadIconFrames(exe, out var format);
        var reference = options.Get("ico") is { } icoPath && File.Exists(icoPath)
            ? ReadIcoFile(icoPath)
            : null;

        Console.WriteLine(Serialize(new
        {
            exe = Path.GetFullPath(exe),
            sizeBytes = new FileInfo(exe).Length,
            embeddedFormat = format,
            frameCount = frames.Count,
            frames = frames.Select(frame => new
            {
                size = frame.Size,
                resourceId = frame.Id,
                bytes = frame.Data.Length,
                sha256 = Hash(frame.Data),
                matchesIco = reference is not null
                    ? reference.TryGetValue(frame.Size, out var expected) && Hash(expected) == Hash(frame.Data)
                    : (bool?)null,
            }),
            comparedWith = reference is null ? null : Path.GetFullPath(options.Get("ico")!),
            allFramesMatch = reference is not null
                && frames.Count == reference.Count
                && frames.All(frame => reference.TryGetValue(frame.Size, out var expected) && Hash(expected) == Hash(frame.Data)),
        }));

        return frames.Count > 0 ? 0 : 1;
    }

    private sealed record IconFrame(int Size, int Id, byte[] Data);

    /// <summary>The kernel32 surface needed to read an executable's own resources.</summary>
    private static class IconNative
    {
        internal delegate bool EnumResourceNamesProc(nint module, nint type, nint name, nint param);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint LoadLibraryExW(string file, nint reserved, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumResourceNamesW(nint module, nint type, EnumResourceNamesProc callback, nint param);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint FindResourceW(nint module, nint name, nint type);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nint LoadResource(nint module, nint info);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nint LockResource(nint handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint SizeofResource(nint module, nint info);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FreeLibrary(nint module);
    }

    /// <summary>Read the RT_GROUP_ICON / RT_ICON resources out of an executable or DLL.</summary>
    private static List<IconFrame> ReadIconFrames(string path, out string format)
    {
        const uint LoadLibraryAsDataFile = 0x00000002;
        const int RtIcon = 3;
        const int RtGroupIcon = 14;

        var module = IconNative.LoadLibraryExW(path, IntPtr.Zero, LoadLibraryAsDataFile);
        if (module == IntPtr.Zero) throw new InvalidOperationException($"cannot open {path} as a data file");

        try
        {
            var groups = new List<IntPtr>();
            IconNative.EnumResourceNamesW(module, (IntPtr)RtGroupIcon, (_, _, name, _) =>
            {
                groups.Add(name);
                return true;
            }, IntPtr.Zero);

            if (groups.Count == 0)
            {
                format = "none";
                return [];
            }

            // The group is a GRPICONDIR: a header, then one entry per frame.
            var group = ReadResource(module, RtGroupIcon, groups[0]);
            var count = BitConverter.ToUInt16(group, 4);
            var frames = new List<IconFrame>(count);
            var kinds = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 0; index < count; index++)
            {
                var at = 6 + (14 * index);
                var size = group[at] == 0 ? 256 : group[at];
                var id = BitConverter.ToUInt16(group, at + 12);
                var data = ReadResource(module, RtIcon, (IntPtr)id);
                frames.Add(new IconFrame(size, id, data));
                kinds.Add(data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 ? "PNG" : "DIB");
            }

            format = string.Join("+", kinds.OrderBy(kind => kind, StringComparer.Ordinal));
            return [.. frames.OrderBy(frame => frame.Size)];
        }
        finally
        {
            IconNative.FreeLibrary(module);
        }
    }

    private static byte[] ReadResource(IntPtr module, int type, IntPtr name)
    {
        var info = IconNative.FindResourceW(module, name, (IntPtr)type);
        if (info == IntPtr.Zero) throw new InvalidOperationException($"resource {type}/{name} not found");

        var handle = IconNative.LoadResource(module, info);
        var pointer = IconNative.LockResource(handle);
        var size = (int)IconNative.SizeofResource(module, info);

        var bytes = new byte[size];
        Marshal.Copy(pointer, bytes, 0, size);
        return bytes;
    }

    private static Dictionary<int, byte[]> ReadIcoFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var count = BitConverter.ToUInt16(bytes, 4);
        var frames = new Dictionary<int, byte[]>();

        for (var index = 0; index < count; index++)
        {
            var at = 6 + (16 * index);
            var size = bytes[at] == 0 ? 256 : bytes[at];
            var length = (int)BitConverter.ToUInt32(bytes, at + 8);
            var offset = (int)BitConverter.ToUInt32(bytes, at + 12);
            frames[size] = bytes[offset..(offset + length)];
        }

        return frames;
    }

    private static string Hash(byte[] data) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data))[..16].ToLowerInvariant();

    /// <summary>Check the signing code against the vectors the providers publish: signing is the
    /// one part of a classic API that cannot be tested without an account.</summary>
    private static int SignCheck()
    {
        // 百度's own documentation example: these four constants must produce exactly this signature.
        const string appId = "2015063000000001";
        const string query = "apple";
        const string salt = "1435660288";
        const string key = "12345678";
        const string documented = "f89f9594663708c1605f3d736d01d2d4";

        var baidu = BaiduTranslator.Sign(appId, query, salt, key);
        var baiduOk = baidu.Equals(documented, StringComparison.OrdinalIgnoreCase);

        // 有道 signs the text itself up to twenty characters; past that, the first ten, the length, and the last ten.
        var shortText = "日本語のテスト";
        var longText = "新しい九人会がついに動き出した。ダンテは罪人たちを見渡した。";
        var truncateOk = YoudaoTranslator.SignInput(shortText) == shortText
            && YoudaoTranslator.SignInput(longText) == longText[..10] + longText.Length + longText[^10..];

        // A wrong language tag is a silent mistranslation rather than an error, and the three providers differ most here.
        var languages = new
        {
            note = "同一个 ja→zh-Hans 在三家各自的写法",
            youdao = new { from = YoudaoTranslator.Language("ja"), to = YoudaoTranslator.Language("zh-Hans") },
            baidu = new { from = BaiduTranslator.Language("ja"), to = BaiduTranslator.Language("zh-Hans") },
            caiyun = CaiyunTranslator.Direction("ja", "zh-Hans"),
        };

        Console.WriteLine(Serialize(new
        {
            baidu = new
            {
                example = new { appId, q = query, salt, key },
                documented,
                computed = baidu,
                matches = baiduOk,
            },
            youdao = new
            {
                rule = "sha256(appKey + input + salt + curtime + appSecret)",
                shortInput = YoudaoTranslator.SignInput(shortText),
                longInput = YoudaoTranslator.SignInput(longText),
                matches = truncateOk,
                sampleSign = YoudaoTranslator.Sign("test-app-key", longText, "salt", "1700000000", "test-secret"),
            },
            languages,
        }));

        return baiduOk && truncateOk ? 0 : 1;
    }

    /// <summary>A stand-in 有道 / 百度 endpoint that recomputes each request's signature from the
    /// published rule and refuses bad ones, so the protocols can be tested with no account.</summary>
    private static async Task<int> FakeApiAsync(Options options)
    {
        var provider = options.Get("provider", "youdao").ToLowerInvariant();
        var port = (int)options.GetDouble("port", 8787);
        var secret = options.Get("secret", "test-secret");
        var seconds = options.GetDouble("seconds", 20);

        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        Console.WriteLine($"{{\"fake\":\"{provider}\",\"url\":\"http://127.0.0.1:{port}/{provider}\",\"secret\":\"{secret}\"}}");

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        while (!stop.IsCancellationRequested)
        {
            System.Net.HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                break;
            }

            var form = new Dictionary<string, string>(StringComparer.Ordinal);
            if (context.Request.HasEntityBody)
            {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);

                if (provider == "caiyun")
                {
                    // 彩云 sends JSON rather than a form; the request shape is part of what is being tested.
                    using var document = JsonDocument.Parse(body);
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        form[property.Name] = property.Value.ValueKind == JsonValueKind.Array
                            ? string.Join("\n", property.Value.EnumerateArray().Select(item => item.GetString() ?? string.Empty))
                            : property.Value.ToString();
                    }
                }
                else
                {
                    foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var at = pair.IndexOf('=');
                        if (at < 0) continue;
                        form[Uri.UnescapeDataString(pair[..at])] = Uri.UnescapeDataString(pair[(at + 1)..].Replace('+', ' '));
                    }
                }
            }

            var (reply, accepted) = provider switch
            {
                "baidu" => AnswerBaidu(form, secret),
                "caiyun" => AnswerCaiyun(form, context.Request.Headers["x-authorization"], secret),
                _ => AnswerYoudao(form, secret),
            };

            var bytes = Encoding.UTF8.GetBytes(reply);
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            context.Response.Close();

            Console.WriteLine(Serialize(new
            {
                provider,
                fields = form.Keys.OrderBy(k => k, StringComparer.Ordinal),
                q = form.GetValueOrDefault("q"),
                from = form.GetValueOrDefault("from"),
                to = form.GetValueOrDefault("to"),
                signatureAccepted = accepted,
                reply,
            }));
        }

        return 0;
    }

    private static (string Reply, bool Accepted) AnswerYoudao(IReadOnlyDictionary<string, string> form, string secret)
    {
        var q = form.GetValueOrDefault("q", string.Empty);
        var appKey = form.GetValueOrDefault("appKey", string.Empty);
        var salt = form.GetValueOrDefault("salt", string.Empty);
        var curtime = form.GetValueOrDefault("curtime", string.Empty);
        var sign = form.GetValueOrDefault("sign", string.Empty);

        if (form.GetValueOrDefault("signType", string.Empty) != "v3") return ("{\"errorCode\":\"105\"}", false);

        var expected = YoudaoTranslator.Sign(appKey, q, salt, curtime, secret);
        if (!expected.Equals(sign, StringComparison.OrdinalIgnoreCase)) return ("{\"errorCode\":\"202\"}", false);

        var translated = $"{q}（有道:{form.GetValueOrDefault("from")}→{form.GetValueOrDefault("to")} 签名已校验）";
        return ($"{{\"errorCode\":\"0\",\"query\":\"{q}\",\"translation\":[\"{translated}\"],\"l\":\"{form.GetValueOrDefault("from")}2{form.GetValueOrDefault("to")}\"}}", true);
    }

    /// <summary>Validate one 彩云-shaped request: a JSON body and a token in the <c>x-authorization</c> header.</summary>
    private static (string Reply, bool Accepted) AnswerCaiyun(
        IReadOnlyDictionary<string, string> form,
        string? authorization,
        string secret)
    {
        if (authorization is null || !authorization.Equals($"token {secret}", StringComparison.Ordinal))
        {
            return ("{\"message\":\"Invalid token\"}", false);
        }

        var q = form.GetValueOrDefault("source", string.Empty);
        var direction = form.GetValueOrDefault("trans_type", string.Empty);
        var translated = $"{q}（彩云:{direction} 令牌已校验）";
        return ($"{{\"target\":[\"{translated}\"]}}", true);
    }

    private static (string Reply, bool Accepted) AnswerBaidu(IReadOnlyDictionary<string, string> form, string secret)
    {
        var q = form.GetValueOrDefault("q", string.Empty);
        var appId = form.GetValueOrDefault("appid", string.Empty);
        var salt = form.GetValueOrDefault("salt", string.Empty);
        var sign = form.GetValueOrDefault("sign", string.Empty);

        if (!BaiduTranslator.Sign(appId, q, salt, secret).Equals(sign, StringComparison.OrdinalIgnoreCase))
        {
            return ("{\"error_code\":\"54001\",\"error_msg\":\"Invalid Sign\"}", false);
        }

        var translated = $"{q}（百度:{form.GetValueOrDefault("from")}→{form.GetValueOrDefault("to")} 签名已校验）";
        return ($"{{\"from\":\"{form.GetValueOrDefault("from")}\",\"to\":\"{form.GetValueOrDefault("to")}\","
            + $"\"trans_result\":[{{\"src\":\"{q}\",\"dst\":\"{translated}\"}}]}}", true);
    }

    /// <summary>One pass of the real chain against a live window: capture, recognize, translate,
    /// report.</summary>
    private static async Task<int> RunOnceAsync(Options options)
    {
        var window = SelectWindow(options);
        if (window is null) return 1;

        var setup = BuildPipelineSettings(options, window, dumpDirectory: null);
        var settings = setup.Settings;
        var region = settings.RegionProvider();
        if (region is not { } rect)
        {
            Console.Error.WriteLine("the selected window has no usable client area");
            return 1;
        }

        var frame = ScreenCapture.CaptureScreenRegion(rect);
        var prepared = ImageOps.Upscale(ImageOps.ToGrayscale(frame, options.GetDouble("gray", 0)), options.GetDouble("scale", 2.0));
        var ocrWatch = System.Diagnostics.Stopwatch.StartNew();
        var ocr = await settings.Recognizer.RecognizeAsync(prepared).ConfigureAwait(false);
        ocrWatch.Stop();

        // Repeating inside one process separates inference from model loading.
        var repeat = (int)options.GetDouble("repeat", 1);
        var repeatDurations = new List<double>();
        if (repeat > 1)
        {
            for (var index = 1; index < repeat; index++)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                await settings.Recognizer.RecognizeAsync(prepared).ConfigureAwait(false);
                watch.Stop();
                repeatDurations.Add(Math.Round(watch.Elapsed.TotalMilliseconds, 1));
            }
        }

        var languages = settings.Languages();
        var profile = settings.Profile();
        var sourceText = TextNormalizer.Tidy(ocr.Text);
        var request = new TranslationRequest
        {
            Text = sourceText,
            From = languages.From,
            To = languages.To,
            Glossary = profile.Glossary,
            StyleHint = profile.StyleHint,
            Worldview = profile.Worldview,
        };

        string? translation = null;
        string? translationError = null;
        var translateWatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            translation = sourceText.Length == 0 ? null : await settings.Translator.TranslateAsync(request).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            translationError = $"{exception.GetType().Name}: {exception.Message}";
        }

        translateWatch.Stop();

        // Report the enforcer's work too: the screen shows the corrected text.
        var (enforced, fixes) = TermEnforcer.Apply(translation ?? string.Empty, request.Glossary);
        if (translation is not null) translation = enforced;

        var outPath = options.Get("out");
        if (outPath is not null) ImageOps.SavePng(frame, outPath);

        Console.WriteLine(Serialize(new
        {
            window = new { window.Identity, title = window.Title },
            region = Rect(rect),
            frame = new { width = frame.Width, height = frame.Height },
            recognizer = settings.Recognizer.Id,
            translator = settings.Translator.Id,
            languages = new { from = languages.From, to = languages.To },
            profile = new { terms = profile.Glossary.Count },
            ocrMs = Math.Round(ocr.Duration.TotalMilliseconds, 1),
            ocrFirstCallMs = Math.Round(ocrWatch.Elapsed.TotalMilliseconds, 1),
            ocrRepeatMs = repeatDurations.Count > 0 ? repeatDurations : null,
            translateMs = Math.Round(translateWatch.Elapsed.TotalMilliseconds, 1),
            sourceText,
            translation,
            termFixes = fixes.Count == 0 ? null : fixes.Select(fix => $"{fix.From} → {fix.To} ({fix.Reason})").ToArray(),
            translationError,
            savedTo = outPath,
        }));

        return translation is null ? 1 : 0;
    }

    /// <summary>Run the production <see cref="TranslationPipeline"/> against a live window for a
    /// fixed time, logging one JSON line per decision.</summary>
    private static async Task<int> WatchAsync(Options options)
    {
        var window = SelectWindow(options);
        if (window is null) return 1;

        var seconds = options.GetDouble("seconds", 12);
        var dumpDirectory = options.Get("dump");
        var setup = BuildPipelineSettings(options, window, dumpDirectory);
        var settings = setup.Settings;

        await using var pipeline = new TranslationPipeline(settings);
        pipeline.Updated += update =>
        {
            var line = Serialize(new
            {
                at = update.At.ToString("HH:mm:ss.fff"),
                status = update.Status.ToString(),
                distance = update.SignatureDistance,
                capture = Math.Round(update.CaptureDuration.TotalMilliseconds, 1),
                ocr = Math.Round(update.OcrDuration.TotalMilliseconds, 1),
                translate = Math.Round(update.TranslateDuration.TotalMilliseconds, 1),
                total = Math.Round(update.TotalDuration.TotalMilliseconds, 1),
                text = update.SourceText,
                skipped = update.Skipped.ToString(),
                translation = update.Translation,
                termFixes = update.TermFixes.Count == 0
                    ? null
                    : update.TermFixes.Select(fix => $"{fix.From} → {fix.To}").ToArray(),
                fromCache = update.FromCache,
                error = update.Error,
            }).Replace("\r\n", " ").Replace('\n', ' ');
            Console.WriteLine(line);
        };

        Console.Error.WriteLine($"watching {window.Identity} for {seconds}s (poll {settings.Options.PollIntervalMs}ms)");
        pipeline.Start();

        // Proves the live language switch the overlay's bar performs, with no rebuild.
        var switchAfter = options.GetDouble("switch-after", 0);
        if (switchAfter > 0)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(switchAfter)).ConfigureAwait(false);
                var from = options.Get("switch-from", "en");
                var to = options.Get("switch-to", "zh-Hans");
                setup.SetLanguages(from, to);
                pipeline.InvalidateTranslation();
                Console.Error.WriteLine($"switched direction to {from} -> {to} after {switchAfter}s");
            });
        }

        await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
        await pipeline.StopAsync().ConfigureAwait(false);

        var stats = pipeline.Stats;
        Console.WriteLine(Serialize(new
        {
            summary = new
            {
                stats.Frames,
                stats.Recognitions,
                stats.Translations,
                stats.CacheHits,
                stats.RepeatedLines,
                stats.Errors,
                cachedTranslations = pipeline.Cache.Count,
            },
        }));

        return stats.Errors == 0 ? 0 : 1;
    }

    /// <summary>Build pipeline settings from the command line, plus a mid-run language setter; the
    /// region provider re-resolves the window each iteration, so the region follows a moving window.</summary>
    private static PipelineSetup BuildPipelineSettings(Options options, WindowInfo window, string? dumpDirectory)
    {
        var identity = window.Identity;
        var offset = options.Get("region") is { } text ? ParseRect(text) : (Int32Rect?)null;
        var language = options.Get("lang", "ja");
        var fallbacks = options.Get("fallback")?.Split(',', StringSplitOptions.TrimEntries) ?? ["en-US", "zh-Hans-CN"];
        var engine = options.Get("ocr-engine", "windows");

        ITextRecognizer? recognizer;
        if (engine.Equals("rapidocr", StringComparison.OrdinalIgnoreCase))
        {
            recognizer = RapidOcrRecognizer.Create(new RapidOcrSettings
            {
                ModelDirectory = options.Get("models", RapidOcrRecognizer.DefaultModelDirectory()),
                UseGpu = options.Get("ocr-gpu") is not null,
                // 320 matches the product default: measured identical recognized
                // text at 736, 480, and 320, at 1368 / 622 / 363 ms.
                LimitSideLen = (int)options.GetDouble("ocr-limit", 320),
                ImgResize = (int)options.GetDouble("ocr-resize", 0),
                DoAngle = options.Get("ocr-noangle") is null,
            });
        }
        else if (language.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            var preferred = new[] { "ja", "en-US", "zh-Hans-CN" }.Concat(fallbacks).Distinct(StringComparer.OrdinalIgnoreCase);
            var multi = MultiLanguageRecognizer.TryCreate(preferred);
            recognizer = multi is not null ? multi : MultiLanguageRecognizer.TryCreateSingle(preferred);
        }
        else
        {
            recognizer = WindowsOcrRecognizer.TryCreateWithFallback(language, fallbacks);
        }

        if (recognizer is null) throw new InvalidOperationException("no OCR recognizer is available on this machine");

        // Held in a local so a mid-run switch can replace it.
        var languages = new LanguagePair(options.Get("from", language), options.Get("to", "zh-Hans"));

        // A game profile travels with the run so the enforcer is exercised as the application uses it.
        var profile = options.Get("game") is { } gameId ? LoadProfile(gameId) : null;
        var profileContext = profile is null
            ? new ProfileContext([], options.Get("style-hint"))
            : new ProfileContext(GameProfiles.ToGlossary(profile), profile.StyleHint ?? options.Get("style-hint"), profile.Worldview);

        var settings = new PipelineSettings
        {
            RegionProvider = () =>
            {
                var current = WindowEnumerator.FindByIdentity(identity);
                if (current is null || current.IsMinimized || !current.HasClientArea) return null;
                var client = current.ClientRect;
                return offset is { } local
                    ? new Int32Rect(client.X + local.X, client.Y + local.Y, local.Width, local.Height)
                    : client;
            },
            Recognizer = recognizer,
            Translator = CreateTranslator(options),
            Languages = () => languages,
            Profile = () => profileContext,
            Options = new PipelineOptions
            {
                PollIntervalMs = (int)options.GetDouble("interval", 400),
                OcrScale = options.GetDouble("scale", 2.0),
                OcrGrayscale = options.GetDouble("gray", 0),
                ForceRefreshMs = (int)options.GetDouble("refresh", 5000),
                ChangeThresholdBits = (int)options.GetDouble("threshold", 6),
                HistoryLines = options.Get("no-history") is null ? (int)options.GetDouble("history-lines", 3) : 0,
                MinTextLength = (int)options.GetDouble("min-text", 4),
                ScriptGuard = options.Get("no-script-guard") is null,
                EnforceTerms = options.Get("no-enforce") is null,
            },
            Cache = new TranslationCache(),
            Dumper = dumpDirectory is null ? null : new FrameDumper(dumpDirectory, 50),
        };

        return new PipelineSetup(settings, (from, to) => languages = new LanguagePair(from, to));
    }

    /// <summary>The settings to run with, and a way to change direction while running.</summary>
    private sealed record PipelineSetup(PipelineSettings Settings, Action<string, string> SetLanguages);

    /// <summary>Read the prompt style named on the command line, defaulting to the general one.</summary>
    private static PromptStyle ParsePromptStyle(Options options) =>
        Enum.TryParse<PromptStyle>(options.Get("prompt-style", "galgame"), ignoreCase: true, out var style)
            ? style
            : PromptStyle.Galgame;

    /// <summary>Build the translator named on the command line; the default is the offline stand-in.</summary>
    private static ITranslator CreateTranslator(Options options)
    {
        // --config runs against whatever engine the application is configured with, key and all.
        if (options.Get("config") is not null) return TranslatorFactory.Create(new ConfigStore().Load().Translation.Translator);

        return TranslatorFactory.Create(new TranslatorConfig
        {
            Provider = options.Get("translator", "mock"),
            BaseUrl = options.Get("base-url", "http://localhost:11434/v1"),
            Model = options.Get("model", "qwen2.5:7b-instruct"),
            ApiKey = options.Get("api-key", string.Empty),
            AppId = options.Get("app-id", string.Empty),
            AppSecret = options.Get("app-secret", string.Empty),
            Domain = options.Get("domain"),
            EndpointOverride = options.Get("endpoint"),
            TimeoutSeconds = (int)options.GetDouble("timeout", 180),
            PromptStyle = options.Get("prompt-style", "galgame"),
            DisableThinking = options.Get("thinking") is null,
        });
    }

    /// <summary>Render an image as text: luminance statistics plus an ASCII map, which is how a run
    /// gets checked when the driving model cannot view images at all.</summary>
    private static int Inspect(Options options)
    {
        Frame frame;
        string origin;
        if (options.Get("image") is { } imagePath)
        {
            frame = ImageOps.LoadPng(imagePath);
            origin = imagePath;
        }
        else
        {
            var window = SelectWindow(options);
            if (window is null) return 1;
            var region = options.Get("region") is { } text ? ParseRect(text) : window.ClientRect;
            frame = ScreenCapture.CaptureScreenRegion(region);
            origin = $"live capture of {window.Identity}";
        }

        var columns = (int)options.GetDouble("cols", 110);
        var rows = (int)options.GetDouble("rows", 26);

        long total = 0;
        var min = 255;
        var max = 0;
        var bright = 0;
        var transparent = 0;
        for (var i = 0; i < frame.Bgra.Length; i += 4)
        {
            var luminance = (int)((0.114 * frame.Bgra[i]) + (0.587 * frame.Bgra[i + 1]) + (0.299 * frame.Bgra[i + 2]));
            total += luminance;
            if (luminance < min) min = luminance;
            if (luminance > max) max = luminance;
            if (luminance > 160) bright++;
            if (frame.Bgra[i + 3] == 0) transparent++;
        }

        var pixels = frame.PixelCount;
        Console.WriteLine(Serialize(new
        {
            origin,
            frame = new { width = frame.Width, height = frame.Height },
            luminance = new
            {
                min,
                max,
                mean = Math.Round(total / (double)pixels, 2),
                brightPixels = bright,
                brightRatio = Math.Round(bright / (double)pixels, 5),
                zeroAlphaRatio = Math.Round(transparent / (double)pixels, 4),
            },
        }));

        Console.WriteLine($"ascii map ({columns}×{rows}, ' ' dark → '@' bright):");
        foreach (var line in ToAscii(frame, columns, rows)) Console.WriteLine(line);
        return 0;
    }

    private static IEnumerable<string> ToAscii(Frame frame, int columns, int rows)
    {
        const string ramp = " .:-=+*#%@";
        columns = Math.Max(8, columns);
        rows = Math.Max(4, rows);

        for (var row = 0; row < rows; row++)
        {
            var builder = new StringBuilder(columns);
            var y0 = row * frame.Height / rows;
            var y1 = Math.Max(y0 + 1, (row + 1) * frame.Height / rows);

            for (var column = 0; column < columns; column++)
            {
                var x0 = column * frame.Width / columns;
                var x1 = Math.Max(x0 + 1, (column + 1) * frame.Width / columns);

                long sum = 0;
                var count = 0;
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        var offset = ((y * frame.Width) + x) * 4;
                        sum += (int)((0.114 * frame.Bgra[offset]) + (0.587 * frame.Bgra[offset + 1]) + (0.299 * frame.Bgra[offset + 2]));
                        count++;
                    }
                }

                var mean = count == 0 ? 0 : sum / (double)count;
                var index = (int)Math.Round(mean / 255 * (ramp.Length - 1));
                builder.Append(ramp[Math.Clamp(index, 0, ramp.Length - 1)]);
            }

            yield return builder.ToString();
        }
    }

    private static WindowInfo? SelectWindow(Options options)
    {
        var identity = options.Get("identity");
        var title = options.Get("title");
        if (identity is null && title is null)
            throw new ArgumentException("--title or --identity is required");

        var windows = WindowEnumerator.List();
        var window = identity is not null
            ? windows.FirstOrDefault(candidate => candidate.Identity == identity)
            : windows.FirstOrDefault(candidate => candidate.Title.Contains(title!, StringComparison.OrdinalIgnoreCase));

        if (window is not null) return window;

        Console.Error.WriteLine($"no window matched ({(title is null ? $"identity {identity}" : $"title ~ {title}")}); candidates:");
        foreach (var candidate in windows.Take(25)) Console.Error.WriteLine($"  {candidate.Identity}  {candidate.Title}");
        return null;
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);

    private static object Rect(Int32Rect rect) => new { x = rect.X, y = rect.Y, width = rect.Width, height = rect.Height };

    private static Int32Rect ParseRect(string text)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4) throw new ArgumentException($"--region wants x,y,w,h but got '{text}'");
        return new Int32Rect(
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(parts[2], CultureInfo.InvariantCulture),
            int.Parse(parts[3], CultureInfo.InvariantCulture));
    }

    /// <summary>Minimal <c>--key value</c> parsing: enough for a harness, no dependency.</summary>
    private sealed class Options
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        public static Options Parse(IEnumerable<string> args)
        {
            var options = new Options();
            string? pending = null;
            foreach (var arg in args)
            {
                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    if (pending is not null) options._values[pending] = "true";
                    pending = arg[2..];
                }
                else if (pending is not null)
                {
                    options._values[pending] = arg;
                    pending = null;
                }
            }

            if (pending is not null) options._values[pending] = "true";
            return options;
        }

        public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

        public string Get(string key, string fallback) => Get(key) ?? fallback;

        public double GetDouble(string key, double fallback) =>
            _values.TryGetValue(key, out var value) && double.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
    }
}
