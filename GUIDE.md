# GuGuGaGaTranslator 改造指南

写给想自己动手改这个项目的人。不需要懂 WPF,照着配方抄就能改;想深入的地方有原理说明。

---

## 1. 先建立心智模型

项目分两块,**职责不重叠**:

| 工程 | 是什么 | 你什么时候动它 |
|---|---|---|
| `src/GuGuGaGaTranslator.Core` | 引擎(纯逻辑):窗口枚举、抓屏、OCR、翻译、轮询流水线、配置读写 | 想改**行为**(识别方式、翻译策略、省 CPU) |
| `src/GuGuGaGaTranslator.App` | 界面(WPF):主窗口、框选遮罩、悬浮层 | 想改**外观**(配色、布局、字号) |

**改任何东西前,先问一句"能不能用配置解决"**。优先级是:

```
改 config.json(不改代码、不用编译)  →  改 App.xaml(只动样式)  →  才去改逻辑代码
```

数据是怎么流的(理解这条线,大部分问题能自己想通):

```
config.json
   ↓  AppSession 组装
识别器(OCR) + 翻译器 + 流水线
   ↓  TranslationPipeline 后台循环
抓屏 → 画面变了吗?没变就跳过 → 变了才 OCR → 这句翻译过了吗?→ 翻译
   ↓  事件抛回 UI 线程
主窗口状态文字 + 悬浮层显示
```

---

## 2. 开发循环:改完怎么看到效果

```powershell
cd X:\gugugaga
.\build.ps1                                        # 编译
.\src\GuGuGaGaTranslator.App\bin\Debug\net10.0-windows10.0.19041.0\gugugaga.exe   # 运行
```

只改了界面想快点编译:

```powershell
dotnet build src\GuGuGaGaTranslator.App\GuGuGaGaTranslator.App.csproj
```

**必知的两个坑:**

1. **程序还开着的时候编译会失败**,报 `MSB3027 / MSB3021 ... because it is being used by another process`。先把 gugugaga 关掉再编译(这个坑我踩过一次,浪费了一轮排查)。
2. **XAML 没有热重载**。这不是网页 —— 改一行颜色也要重新 build 才看得到。

---

## 3. 配方:换配色(改一个文件)

打开 `src/GuGuGaGaTranslator.App/App.xaml`,最上面那段就是全部颜色。格式是 `#AARRGGBB`(前两位是透明度,`FF` 表示不透明)。当前这套是从吉祥物身上取的"深海底 + 鲸鱼蓝 + 头带粉":

```xml
<SolidColorBrush x:Key="WindowBackgroundBrush" Color="#FF0F1430" />  <!-- 窗口底:深海底 -->
<SolidColorBrush x:Key="SurfaceBrush"          Color="#FF161D42" />  <!-- 列表/标签页底 -->
<SolidColorBrush x:Key="FieldBrush"            Color="#FF0B1029" />  <!-- 输入框底 -->
<SolidColorBrush x:Key="BorderBrush"           Color="#FF2C3873" />  <!-- 边框线 -->
<SolidColorBrush x:Key="ButtonFaceBrush"       Color="#FF1E2757" />  <!-- 按钮面(悬停/按下另有两只) -->
<SolidColorBrush x:Key="TextBrush"             Color="#FFEDF1FF" />  <!-- 主文字 -->
<SolidColorBrush x:Key="MutedTextBrush"        Color="#FFA6B0DC" />  <!-- 次要文字 -->
<SolidColorBrush x:Key="AccentBrush"           Color="#FF5B7BFF" />  <!-- 强调色:鲸鱼蓝 -->
<SolidColorBrush x:Key="BowPinkBrush"          Color="#FFFF87B8" />  <!-- 头带粉:点缀用 -->
```

**字体也在这里**,两条链:

```xml
<FontFamily x:Key="UiFont">YouYuan, 幼圆, Microsoft YaHei UI, Microsoft YaHei, Segoe UI</FontFamily>
<FontFamily x:Key="DisplayFont">Arial Rounded MT Bold, Comic Sans MS, YouYuan, 幼圆, Microsoft YaHei UI, Segoe UI</FontFamily>
```

WPF 的 `FontFamily` **支持逗号分隔的回退链**,取第一个装了的 —— 这是"别人机器上没有幼圆也不会变方框"的关键。想知道某台机器最终用上了哪个字体文件:

```powershell
Add-Type -AssemblyName PresentationCore
$f = New-Object System.Windows.Media.Typeface (New-Object System.Windows.Media.FontFamily "YouYuan, 幼圆, Microsoft YaHei UI"),
      ([System.Windows.FontStyles]::Normal), ([System.Windows.FontWeights]::Normal), ([System.Windows.FontStretches]::Normal)
$g = $null; [void]$f.TryGetGlyphTypeface([ref]$g); Split-Path $g.FontUri.LocalPath -Leaf   # 本机输出 SIMYOU.TTF(幼圆)
```

**为什么改这里就能全局生效**:主窗口 XAML 用的是 `{StaticResource ...}` 引用;悬浮层和框选遮罩是无边框窗口、颜色写在 C# 里,它们通过 `Theme.Brush("AccentBrush", ...)` 读同一份资源(见 `src/GuGuGaGaTranslator.App/Theme.cs`)。所以三处外观共用一套颜色,不会各改各的。

想要浅色主题,把上面几行换成例如:

```xml
<SolidColorBrush x:Key="WindowBackgroundBrush" Color="#FFF6F7FA" />
<SolidColorBrush x:Key="SurfaceBrush"          Color="#FFFFFFFF" />
<SolidColorBrush x:Key="FieldBrush"            Color="#FFF2F4F8" />
<SolidColorBrush x:Key="BorderBrush"           Color="#FFD6DAE4" />
<SolidColorBrush x:Key="TextBrush"             Color="#FF1B1E28" />
<SolidColorBrush x:Key="MutedTextBrush"        Color="#FF5A6274" />
<SolidColorBrush x:Key="HintTextBrush"         Color="#FF7A8296" />
<SolidColorBrush x:Key="SourceTextBrush"       Color="#FF3A4152" />
<SolidColorBrush x:Key="AccentBrush"           Color="#FF3F6BD6" />
```

---

## 4. 配方:让控件变好看(圆角、悬停、字体)

控件样式也在 `App.xaml`,那是**隐式样式**(没有 `x:Key`),对它那个类型的所有控件生效 —— 改一处,到处变。

**圆角按钮**必须重写控件模板(WPF 的 Button 默认模板不接受 CornerRadius),把 `Button` 那个 Style 换成:

```xml
<Style TargetType="Button">
  <Setter Property="Padding" Value="12,6" />
  <Setter Property="Margin" Value="0,0,6,0" />
  <Setter Property="MinWidth" Value="76" />
  <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="Button">
        <Border x:Name="Chrome" CornerRadius="6"
                Background="{StaticResource SurfaceBrush}"
                BorderBrush="{StaticResource BorderBrush}" BorderThickness="1"
                Padding="{TemplateBinding Padding}">
          <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property="IsMouseOver" Value="True">
            <Setter TargetName="Chrome" Property="Background" Value="{StaticResource AccentBrush}" />
          </Trigger>
          <Trigger Property="IsPressed" Value="True">
            <Setter TargetName="Chrome" Property="Opacity" Value="0.8" />
          </Trigger>
          <Trigger Property="IsEnabled" Value="False">
            <Setter TargetName="Chrome" Property="Opacity" Value="0.45" />
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
```

**换字体**:`App.xaml` 里的 `UiFont` 资源就是全局 UI 字体,窗口上用 `FontFamily="{StaticResource UiFont}"` 继承下去。想换某个窗口单独一条,直接在那行写死字体名即可 —— 但**记得写回退链**(`"我的字体, Microsoft YaHei UI, Segoe UI"`),否则别人机器上缺字体就会显示方框。
**整体放大**:**不要**改窗口尺寸硬凑,改各处的 `FontSize`,或给 Window 设 `TextElement.FontSize`。
**间距太挤**:`App.xaml` 里各 Style 的 `Margin`/`Padding` 就是间距来源;布局层的大间距在 `MainWindow.xaml` 里的 `Margin="14"` 和 `<UniformGrid Margin=...>`。

**改图标**(整只吉祥物是代码画的):`tools/GuGuGaGaTranslator.Icon/Program.cs` 的 `Draw()` 里就是一个椭圆当身体、两条贝塞尔当尾巴、一段圆弧当头带、两个椭圆当眼睛。改完 `dotnet run --project tools/GuGuGaGaTranslator.Icon` 重新生成 `drawn-mascot.ico`,它会顺手把结果**用字符画打印出来**,不用看图也能确认没画歪。

程序**实际使用的图标**是 `src/GuGuGaGaTranslator.App/Assets/icon.ico`(社区鲸鱼娘,CC BY-NC-SA 4.0,见 `THIRD_PARTY_NOTICES.md`)+ `icon.png`(256 窗口图标)。想换成自己的图:**覆盖这两个同名文件即可**,`GuGuGaGaTranslator.App.csproj` 里的 `<ApplicationIcon>` 和两个 `<Resource>` 指的就是这两个名字,不用改代码。

**改完图标一定要验证 exe 里到底嵌了什么** —— 这里有个坑:

```powershell
gugugaga-probe icon --exe dist\gugugaga.exe --ico src\GuGuGaGaTranslator.App\Assets\icon.ico
```

输出会逐帧列出尺寸、字节数、SHA-256,以及 `allFramesMatch`。**不要用 `Icon.ExtractAssociatedIcon` 去检查**:它走 GDI+,而现代 ico 的每一帧是 PNG 压缩的,GDI+ 解不了 —— 它会把一个 Windows 显示完全正常的图标返回成一团黑色,正好给出相反的结论(本项目就被骗过一次)。直接读 PE 资源比对哈希才是可信的答案。

```powershell
# 资源管理器里图标没变?那是 shell 的图标缓存,不是 exe 的问题:
# 1) 换个路径看一眼(缓存按路径存)  2) 或刷新缓存
Copy-Item dist\gugugaga.exe "$env:TEMP\新名字.exe"
ie4uinit.exe -show
```

**验证界面真的变了**(看不到屏幕时唯一的办法):截图后用颜色普查代替肉眼 ——

```powershell
# PrintWindow 抓的是窗口自己画的内容,不受其他窗口遮挡;屏幕区域抓取会把别的窗口拍进来
gugugaga-probe capture --title GuGuGaGaTranslator --backend printwindow --out shot.png
gugugaga-probe inspect --image shot.png          # ASCII 看布局
```

然后把 PNG 的像素按颜色统计一下,对照 `App.xaml` 里的十六进制值 —— 例如改完主题后 `#5B7BFF`(主按钮)应该出现几千个像素、`#0F1430`(窗口底)占大头。本项目就是这么确认主题生效的:界面看不出问题时,"颜色占比对不上"是最先暴露的地方。

---

## 4.5 界面文案与样式约定(改之前先看这一节)

界面是中文的,术语要一致,不然同一个东西在三处叫三个名字。现在的约定:

**① 术语**:程序内部管识别叫 OCR,但**界面上只写「识别」**。
`OCR` 三个字母只出现在两个引擎的**正式名**里 —— `RapidOCR`(开源项目名)和 `Windows OCR`(系统功能名),因为搜报错、找教程时要用原名的。其余一律说「识别引擎」「识别语言」「识别前放大倍数」「识别 0 次」。

**② 句式**:表单标签统一写成 `名称 —— 说明`,需要取值范围就放括号里。

```text
识别引擎 —— 用哪个程序认字
识别语言 —— 用哪套字形去认字,必须和画面上的文字语言一致
对比度增强 —— 文字和背景颜色太接近时调大 0.2–0.4,正常画面保持 0
```

不要再用 `名称(仅该引擎用):说明` 这种混搭 —— 之前就是这么乱的,同一个页面里 `——`、`:`、`=` 三种分隔符混着用。

**③ 三档文字样式**(都在 `App.xaml`,别再在控件上写死 `FontWeight` / `Foreground`):

| 资源 | 用在哪 | 长相 |
|---|---|---|
| `SectionText` | 小节标题(`① 识别:…`) | 圆体加粗、天蓝色 |
| `FieldLabel` | 控件**上方**的标签 | 灰蓝、11.5px |
| `HintText` | 控件**下方**的说明(通常是动态文案) | 更浅的灰、11.5px |

**④ 动态说明走代码**:例如 `OcrEngineSummary` 的句子在 `MainWindow.UpdateOcrEngineSummary()` 里按引擎拼,样式挂 `HintText`,这样换引擎时说明也跟着换 —— 别把这类句子写死在 XAML 里。

**⑤ 改完怎么核对有没有漏**:一条命令列出界面上真实渲染出来的文字(不靠肉眼翻页面):

```powershell
# 用 UI Automation 把页面上的文字读出来,筛出含某个词的
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$win  = $root.FindFirst('Children', (New-Object System.Windows.Automation.PropertyCondition(
          [System.Windows.Automation.AutomationElement]::ProcessIdProperty, (Get-Process gugugaga).Id)))
$cond = New-Object System.Windows.Automation.PropertyCondition(
          [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
          [System.Windows.Automation.ControlType]::Text)
$win.FindAll('Descendants', $cond) | ForEach-Object { $_.Current.Name } | Where-Object { $_ -match 'OCR|识别' }
```

本项目就是这么确认"界面上还剩哪些 OCR 字样"的 —— 剩下的只有元素名(用户看不见)和两个引擎的正式名。

## 5. 配方:改布局 / 加一个按钮
布局全在 `src/GuGuGaGaTranslator.App/MainWindow.xaml`:左列是选窗口,右列是一个 `TabControl`,五个 `TabItem` 分别是运行 / 识别与翻译 / 悬浮层 / 游戏模式 / 调试。

加一个按钮要**同时改两个文件**,少一个就编译不过:

```xml
<!-- MainWindow.xaml:找到想放的地方,加一行 -->
<Button x:Name="MyButton" Content="我的按钮" Click="OnMyButton" />
```

```csharp
// MainWindow.xaml.cs:加对应的方法,方法名必须和 Click="..." 一模一样
private void OnMyButton(object sender, RoutedEventArgs e)
{
    StatusText.Text = "按钮被点了";
}
```

**关键概念**:`x:Name="MyButton"` 会在代码里生成一个同名字段,所以 `.xaml.cs` 里可以直接写 `MyButton.Content = "..."`;`Click="OnMyButton"` 是把点击事件接到同名方法上。名字对不上,编译器直接报错(这是好事,不会静默失效)。

---

## 6. 配方:悬浮层(透明、字号、位置、描边)

**先用配置,不用改代码。** 界面「悬浮层」页的所有选项都存在 `%APPDATA%\GuGuGaGaTranslator\config.json` 的 `overlay` 段,直接手改也可以(重启程序生效):

```json
"overlay": {
  "fontSize": 30,            // 字号
  "backgroundOpacity": 0,    // 0 = 完全透明、只剩文字;1 = 实心底框
  "textOutline": true,       // 文字描边。背景透明后靠它保证看得清
  "maxLines": 3,             // 最多显示几行,超出省略
  "offsetX": 0,              // 相对所选区域的水平偏移
  "offsetY": 8,              // 垂直偏移,负值往上(可以覆盖到原文上)
  "clickThrough": true,      // 鼠标穿透;想拖动悬浮层就设 false
  "excludeFromCapture": true, // 悬浮层是否对录屏/截图隐身;想录下带译文的画面就设 false
  "showSource": true         // 是否同时显示识别到的原文
}
```

**想加一个新选项**(例如"圆角大小"),三步走 —— 这就是这个项目一致的改法:

```
① src/GuGuGaGaTranslator.Core/Config/AppConfig.cs
   在 OverlayConfig 里加属性:  public double CornerRadius { get; set; } = 6;

② src/GuGuGaGaTranslator.App/MainWindow.xaml        加控件:  <TextBox x:Name="OverlayCornerBox" />
   src/GuGuGaGaTranslator.App/MainWindow.xaml.cs
     LoadConfigIntoUi():      OverlayCornerBox.Text = Text(config.Overlay.CornerRadius);
     ReadUiIntoConfig():      config.Overlay.CornerRadius = Number(OverlayCornerBox.Text, config.Overlay.CornerRadius);

③ src/GuGuGaGaTranslator.App/OverlayWindow.cs
     Configure() 里用上:      _panel.CornerRadius = new CornerRadius(config.CornerRadius);
```

**原理:为什么悬浮层的位置是用 Win32 的 `SetWindowPos` 设的,而不是 WPF 的 `Left/Top`?**
因为你这块屏是 125% 缩放。WPF 的 `Left/Top` 是"逻辑单位",跨不同缩放的显示器时会被换算,导致悬浮层飘到别的地方;而抓屏得到的区域是"物理像素"。所以定位一律走 `OverlayWindowInterop.MoveTo(...)`(物理像素),宽度才用 WPF 的单位。**你自己加定位代码时,也照这个来。**

---

## 6.5 配方:改外观 / 加一套新外观(不用改代码)

翻译框的位置、尺寸、透明度、显隐**全部是数据**:界面「悬浮层」页改,或直接改 `%APPDATA%\GuGuGaGaTranslator\config.json` 的 `overlay` 段。

**加一套新外观**:往 `overlay.presets` 数组里加一条,重启后它就出现在「外观预设」下拉里 —— 不用碰任何 C# 代码:

```json
{
  "name": "我的外观",
  "note": "下拉旁边显示的说明",
  "placement": "over",          // over 盖住原文 / below 下方 / above 上方
  "showSource": false,          // 是否显示识别到的原文
  "showPanel": true,            // 是否显示底框
  "backgroundOpacity": 0.92,
  "fontSize": 30,
  "width": 0,                   // 0 = 宽度跟随所选区域
  "height": 0,                  // 0 = 高度随文字
  "cornerRadius": 10,
  "padding": 18,
  "textAlign": "center",        // left / center / right
  "offsetY": 0
}
```

**加一个语言方向**:往 `overlay.languagePresets` 加一条:

```json
{ "label": "日 → 韩", "from": "ja", "to": "ko", "ocr": "ja" }
```

`ocr` 与 `from` 分开是有意的:一个是"用什么字形认字",一个是"译成什么语言",两者独立。

**要改"位置算法"本身**(例如让面板水平居中):改 `src/GuGuGaGaTranslator.App/OverlayWindow.cs` 的 `PlaceAt`。那里有一条铁律:**尺寸用 WPF 设备无关单位,位置用物理像素**(`OverlayWindowInterop.MoveTo`)。混用会在 125%/150% 缩放的屏幕上偏移 —— 本项目踩过这个坑。

**要加一种抓屏方式**(例如 Windows.Graphics.Capture,以支持被遮挡的窗口):在 `GuGuGaGaTranslator.Core/Capture` 里实现取帧,给 `CaptureBackend` 加一个枚举值,调用点在 `TranslationPipeline` 抓帧那一行。

## 6.6 配方:给一款游戏做专属术语表 / 让译名符合剧情

**先判断要不要写代码**:几乎不用。术语表是**数据**,存在 `translation.gameProfiles` 里,界面「游戏模式」页就能改。代码只在三种情况下才要动:你想换术语注入的位置、想换校正规则、想加一种"学习术语"的自动机制。

### 只想让某个译名不再出错(90% 的情况)

界面 →「游戏模式」→「编辑术语表…」,加一行:

```text
新九人会 = 新九人会 | 禁止: 新九人联盟、新九人协会
```

左边是屏幕上出现的词,右边是你要的译名(两边一样 = 必须原样保留),`禁止` 后面是**绝不允许出现的写法**。保存 → 下一句生效。

为什么这样能治好:模型不是不会翻,是**不认识这部作品**,于是拼出一个合理的名字。提示词里点名(`绝不能写成: …`)把大概率堵住,而 `TermEnforcer` 在译文落地前把漏网的改回来 —— 所以这是**保证**,不是请求。

### 想扩大覆盖(让 AI 补全)

「✨ AI 生成术语表」用当前引擎起草一份。它**故意开着思考模式**(翻译路径上是关掉的):术语表只生成一次、之后每句都在用,值得多等十几秒;为此超时被抬到 240 秒(`TranslatorFactory.CreateForTermSheet`)。

生成结果可能很脏(Markdown 表格、编号列表、JSON、`新九人会（禁止：新九人联盟）`),解析器都认 —— 在 `src/GuGuGaGaTranslator.Core/Translation/TermSheetBuilder.cs` 的 `NormalizeLine` / `ParseJson`。想让它更懂某种格式,加一条规则进去就行。

### 三个想改的地方,对应的文件

| 想改什么 | 改哪里 |
| --- | --- |
| 术语怎么写进提示词(措辞、位置、负面例子) | `OpenAiCompatibleTranslator.DescribeGlossary` / `BuildGalgameSystemPrompt` |
| 译文里的错译怎么被改回来(匹配规则、长词优先、整词边界) | `GuGuGaGaTranslator.Core/Translation/TermEnforcer.cs` |
| AI 生成术语表的提问方式(要几条、要不要"备注: 社区通用") | `TermSheetBuilder.BuildUserPrompt` |

`TermEnforcer.Apply` 的规则只有两条,改起来很直观:**禁止译法 → 既定译名**、**原文原样残留 → 补译**。想加第三条(例如"译名必须全篇一致")就在那里加。

### 不用点界面就能验证

```powershell
# 术语解析 + 校正,给一句模型可能写错的话
gugugaga-probe terms --game limbus-company --sample "N公司的新九人联盟成立了。|N社的使者。"

# 看实际发出去的提示词(规则 + 术语表 + 世界观)
gugugaga-probe translate --text "N社の新九人会が動き出した。" --game limbus-company --show-prompt

# 用配置里的真引擎起草一份术语表,看解析结果和原始回复
gugugaga-probe term-sheet --config --game-name "边狱巴士 / Limbus Company"

# 实时管线里跑一遍:termFixes 里会列出被改回的词
gugugaga-probe watch --title "gugugaga sample" --translator mock --game limbus-company --seconds 5
```

`watch` 加 `--no-enforce` 就能看到"不校正会是什么样",两条输出一对比,就是功能是否真的在起作用的证据。

### 为什么"世界观"那一段往往比长术语表更有效

模型认得出作品,就会自动沿用官方译名里**你没列出来的**词;认不出,它只能按字面拼。所以 `worldview` 写一两句"这是什么作品、什么世界观、主角是谁"的收益通常高于再堆二十条术语。提示词预览窗口里能直接看到这段被放在了哪里。

## 7. 配方:换翻译模型 / 接一个新引擎

**先判断要不要写代码**:OpenAI 兼容协议已经覆盖绝大多数引擎(Ollama、LM Studio、llama.cpp、DeepSeek、智谱、硅基流动,以及各种中转),**多数情况只改三个字段**:

| 引擎 | 接口地址 | 模型名示例 | 备注 |
|---|---|---|---|
| 本地 Ollama | `http://127.0.0.1:11434/v1` | `qwen2.5:7b-instruct` | 离线免费,通用模型口语日译中较弱 |
| 本地 Sakura(推荐) | `http://127.0.0.1:11434/v1` | `sakura-galtransl:7b` | galgame 专用,须把「指令格式」设成 **sakura** |
| DeepSeek | `https://api.deepseek.com` | `deepseek-flash` | 付费但极便宜,需 Key |
| 智谱 GLM | `https://open.bigmodel.cn/api/paas/v4` | `glm-4-flash` | 有免费模型,需 Key |
| 硅基流动 | `https://api.siliconflow.cn/v1` | `Qwen/Qwen2.5-7B-Instruct` | 部分模型免费,需 Key |

**「指令格式」比模型选型更容易踩坑**:`galgame` 是给通用聊天模型的通用指令;`sakura` 是 Sakura-GalTransl 训练时用的固定格式(术语表 → 上一句译文 → 固定指令行)。**用 Sakura 模型却选了 galgame 格式,质量会明显下降。**

**Sakura 的落地方式**(`tools\setup-sakura.ps1` 已经做好):把 GGUF 用 `ollama create` 注册成一个模型,并在 Modelfile 里固化它的 SYSTEM 提示词和推荐推理参数(temperature 0.1 / top_p 0.3)—— 这样程序只管发正文,不用每次拼提示词。协议是 **CC-BY-NC-SA 4.0,禁止商用**。

真要接一个协议不同的引擎,实现 `ITranslator`(`src/GuGuGaGaTranslator.Core/Translation/ITranslator.cs`):

```csharp
public interface ITranslator
{
    string Id { get; }                 // 用于日志、缓存键、界面显示
    bool RequiresNetwork { get; }       // 界面用它提示是否需要联网
    Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default);
}
```

然后在 `TranslatorFactory.Create` 的 `switch` 里加一个分支,并在 `MainWindow.PopulateChoices()` 的 `ProviderCombo.Items` 里加上这个名字(界面的下拉选项是手工列的)。

**注意**:缓存键包含引擎 Id 和语言方向,所以换引擎不会拿到旧引擎的译文。

### 7.1 配方:再接一家传统翻译接口(有道/百度/彩云这类)

这类接口每家一套协议,但**套路完全一样**:继承 `ClassicApiTranslator`(`src/GuGuGaGaTranslator.Core/Translation/ClassicApis.cs`),只需要回答三件事 —— **怎么签名、语言代码怎么写、从响应的哪个字段取译文**。已有的三个就是三个例子:

| 厂商 | 端点 | 怎么证明"我是我" | 语言代码 | 取译文 |
|---|---|---|---|---|
| 有道 | `openapi.youdao.com/api`(表单) | `sha256(appKey + input + salt + curtime + appSecret)`,**input 超过 20 字只取前 10 + 长度 + 后 10** | `ja` / `zh-CHS` | `translation[0]` |
| 百度 | `fanyi-api.baidu.com/api/trans/vip/translate`(表单) | `md5(appid + q + salt + 密钥)` | **`jp`** / `zh` | `trans_result[0].dst` |
| 彩云 | `api.interpreter.caiyunai.com/v1/translator`(JSON) | 请求头 `x-authorization: token <token>` | `ja2zh` 一个字符串 | `target[0]` |

加一家新厂商 = 抄一个最近的类改三处 + 在 `TranslatorFactory` 加一个分支(**不上界面** —— 见下)。凭据字段已经备好:`TranslatorConfig.AppId` / `AppSecret`,`ApiKey` 留给 token 型的服务;这三个字段界面上没有输入框,只能手改 `config.json`,而手改的值**不会被界面保存覆盖**(`ReadUiIntoConfig` 用的是 `with`,没写到的字段原样保留)。

**为什么留在配置里、却不在下拉里**:这类接口收不到术语表和世界观(协议里没有提示词这一层),而这个程序的核心价值就是让译名对得上剧情 —— 摆进下拉等于请人把它关掉。适配器本身是好东西(快、有免费额度),所以代码留着、文档留着,想要的人改一行配置就有。要重新摆上下拉:在 `MainWindow.EngineProviders` 加一行即可。

**签名是对的还是错的,肉眼看不出来**(错了就是一句"签名校验失败"),所以有两件工具必须用上:

```powershell
# 1. 拿官方文档公布的例子核对签名 —— 百度文档给了一组固定示例,必须逐字符一致
gugugaga-probe sign-check

# 2. 本地假接口:它会按同样的算法重算签名,签名不对就回错误码
gugugaga-probe fake-api --provider youdao --port 8787 --secret test-secret --seconds 30
gugugaga-probe translate --translator youdao --app-id test-app-key --app-secret test-secret `
    --endpoint http://127.0.0.1:8787/youdao --text "テスト"
```

`--endpoint`(`TranslatorConfig.EndpointOverride`)是专门为此留的口子:它同时也能把你自己的代理/中转网关塞进这些接口前面。要加一家新的,就在 `Commands.cs` 的 `AnswerYoudao` / `AnswerBaidu` / `AnswerCaiyun` 旁边照抄一个 `AnswerXxx`,把它的校验规则写进去 —— 写这个假接口的过程本身就是把协议读懂的过程。

**别接网页接口**。那些工具里的"搜狗翻译"调的是 `fanyi.sogou.com` 的网页接口(搜狗开放平台已停止服务),参数和 token 靠抓包复刻,网页一改就全废。开放平台虽然要注册,但至少是契约。

---

## 8. 配方:换或加 OCR 引擎

实现 `ITextRecognizer`(`src/GuGuGaGaTranslator.Core/Ocr/ITextRecognizer.cs`),它只有一个方法 `RecognizeAsync(Frame, CancellationToken)`,返回 `OcrResult`(文本 + 行框 + 词框)。然后改 `src/GuGuGaGaTranslator.App/AppSession.cs` 的 `Start()` 里那一行选择实现的地方。

**为什么你可能想换**:系统自带的 Windows OCR 要求装语言包,而且对花体/描边字识别一般。`RapidOCR`(ONNX)这类引擎不需要系统语言包、对游戏字体更稳 —— 接口留在这儿就是为了它(阶段1 计划内)。

---

## 9. 配方:打包给别人

```powershell
.\build.ps1 -Publish -SingleFile            # 单文件、自带运行时 → dist\gugugaga.exe(约 68 MB)
.\build.ps1 -Publish                        # 框架依赖 → dist\ 文件夹(约 26 MB,对方要装 .NET 10 桌面运行时)
.\build.ps1 -Configuration Release -Publish -SingleFile   # 同上但显式 Release
```

- 单文件版**拷过去双击就能跑**,不需要对方装任何东西。
- 想做成带开始菜单/卸载的安装包,用 [Inno Setup](https://jrsoftware.org/isinfo.php) 把 `dist\gugugaga.exe` 包一层即可。
- **分发时提醒对方**:配置存在 `%APPDATA%\GuGuGaGaTranslator\config.json`,对方拿到的是全新配置 —— 需要自己选窗口、框选一次区域,并且要装好对应语言的 OCR。

---

## 10. 自己找代码的两个技巧

1. **按界面文字搜**。想改哪个标签,就搜那句话。例如想知道"框选区域"按钮在哪:
   `grep -rn "框选区域" src/` → 直接定位到 `MainWindow.xaml` 第 N 行和它的处理方法。
2. **按错误原文搜**。编译错误里带文件名和行号,直接跳过去看;运行时问题先看程序状态栏文字,再去「调试」页打开证据落盘,看 `PNG + JSON` 到底抓到了什么。

---

## 11. 不用点界面就能验证(强烈建议用)

改完逻辑别急着靠肉眼验证,这套命令行工具能直接告诉你链路通不通:

```powershell
$probe = '.\tools\GuGuGaGaTranslator.Probe\bin\Debug\net10.0-windows10.0.19041.0\gugugaga-probe.exe'

& $probe ocr-langs                                  # 本机有哪些 OCR 语言可用
& $probe windows --filter 游戏                      # 列出窗口和它们的客户区坐标
& $probe capture --title 游戏 --out frame.png       # 抓一帧存成 PNG(自己打开看就知道区域对不对)
& $probe ocr --image frame.png --lang ja --scale 2  # 对这张图跑识别,打印识别文本和行框
& $probe inspect --image frame.png                  # 亮度统计 + ASCII 字符画(纯文本也能"看"图)
& $probe run --title 游戏 --lang ja --from ja --to zh-Hans   # 走一遍 抓屏→识别→翻译
& $probe watch --title 游戏 --seconds 20 --lang ja --dump .\dumps   # 跑真实循环并逐帧打印决策
```

还有一个**假的游戏窗口**可以当靶子(会自己换台词,不需要装游戏):

```powershell
.\tools\GuGuGaGaTranslator.SampleWindow\bin\Debug\net10.0-windows10.0.19041.0\gugugaga-sample-window.exe `
  --x 260 --y 240 --w 1000 --h 220 --cycle 3 `
  --cycle-lines "Where are you going at this hour?|Just to the convenience store."
```

> 注意:`--cycle-lines` 的值里**有空格,必须用引号包住**。用 `Start-Process` 起它的时候尤其要小心——`Start-Process -ArgumentList` 不会自动加引号,我在这上面栽过一次,窗口在放日文而我以为是英文。

---

## 12. 常见坑清单

| 现象 | 原因 | 怎么办 |
|---|---|---|
| 编译报 `MSB3027/MSB3021 ... being used by another process` | 程序还开着,exe 被锁 | 关掉 gugugaga 再编译 |
| 改了界面没变化 | XAML 没有热重载 | 重新 `.\build.ps1` 再运行 |
| 编译报 `OnXxx 不存在` | XAML 里写了 `Click="OnXxx"` 但代码里没这个方法 | 补上同名方法,或删掉挂载 |
| 编译报某个 `x:Name` 找不到 | XAML 和代码里的名字不一致 | 两处名字必须完全一样 |
| 悬浮层位置偏了 | 用了 WPF 的 `Left/Top` 定位 | 改用 `OverlayWindowInterop.MoveTo`(物理像素) |
| 界面卡顿 | 在前台线程做了 OCR/翻译 | 让流水线在后台跑(`TranslationPipeline` 已经是),别在事件处理里做重活 |
| 换了机器配置没了 | 配置在 `%APPDATA%\GuGuGaGaTranslator\`,不在项目里 | 拷 `config.json` 过去,或重新配 |
| 日文识别不出来 | 没装日语 OCR 语言包(或没用 RapidOCR 引擎) | 把「识别引擎」换成 RapidOCR 离线(自带模型),或装系统语言包 |
| PowerShell 脚本里的中文变乱码,或报「缺少引号 / Unexpected token」 | 脚本是**无 BOM 的 UTF-8**,PowerShell 按系统 ANSI(简中机器是 GBK)去读,一个汉字被拆成两个字符,顺手把后面的引号吃掉了 | 把 .ps1 存成 **UTF-8 with BOM**(本仓库 `build.ps1` / `tools\*.ps1` 都带 BOM)。这是本项目真实踩过的坑 |
| 下载大文件"看起来成功了"其实不完整 | `curl` 中途断开会返回 exit 18(缺字节),脚本若只看"文件存不存在"就会误判 | 下载后**校验字节数或哈希**,并对 `curl` 的退出码 `throw`。`--continue-at -` 可以续传 |
| 抓到的图是黑的 | 窗口被遮挡 / 全屏独占游戏 | 让窗口可见,或把 `target.captureBackend` 改成 `printwindow` 试 |

---

## 13. 动手前的 30 秒自检

```powershell
& $probe ocr-langs                       # 需要的语言可用吗?
& $probe run --title 你的游戏 --lang ja  # 抓屏+识别+翻译都通吗?
```

两条都过了再谈界面美化 —— 否则你会在"界面改对了但没译文"里白折腾。
