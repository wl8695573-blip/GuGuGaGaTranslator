using GuGuGaGaTranslator.Core.Config;

namespace GuGuGaGaTranslator.Core.Translation;

/// <summary>The terms a fresh install ships for 《边狱巴士》/ Limbus Company, taken from the community's
/// 零协会 localization and carried in all three supported source languages at once. Parsed at startup, so
/// the data stays editable as text instead of living as a hand-built object graph.</summary>
internal static class LimbusProfile
{
    /// <summary>English rows plus Japanese rows, each tagged with the language its source is written in.</summary>
    public const string Terms =
"""
# ===== 《边狱巴士》Limbus Company · 中 / 日 / 英 三语术语表 =====
# 每行:原文 = 零协中文译名。行首 en: / ja: 标明原文是哪国语言。
# 翻译时按当前方向自动取用:译成中文取源语言那一列;中文译成外语则反过来用;
# 两种外语互译时,以中文译名为枢轴自动配对。未标语言的行按「任意外语 → 中文」处理。
# 「禁止:」是硬替换:译文里出现这些写法会被自动改回。只放永远算错的写法。

# ======================== 英语原文 ========================
# ===== 《边狱巴士》术语表 · 英文原文 → 零协中文 =====
# 原文列 = 游戏英文版屏幕上的写法;中文列 = 零协会汉化通行译名
# 「禁止:」是硬替换,只放永远算错的写法

# —— 十二罪人与主角 ——
en: Yi Sang = 李箱 | 禁止: 李相、伊桑
en: Faust = 浮士德 | 禁止: 法乌斯特
en: Don Quixote = 堂吉诃德 | 禁止: 唐吉诃德、堂吉珂德
en: Ryōshū = 良秀 | 禁止: 凉秀、辽修、良修
en: Meursault = 默尔索 | 禁止: 穆尔索、梅尔索、莫尔索
en: Hong Lu = 鸿璐 | 禁止: 洪露、红露、鸿露
en: Heathcliff = 希斯克利夫 | 禁止: 希斯克里夫、西斯克利夫
en: Ishmael = 以实玛利 | 禁止: 伊斯梅尔、伊什梅尔、以赛玛利
en: Rodion = 罗佳 | 禁止: 罗迪昂、罗季昂、罗迪恩
en: Sinclair = 辛克莱 | 禁止: 辛克雷、辛克莱尔
en: Outis = 奥提斯 | 禁止: 奥德修斯、奥蒂斯、欧提斯
en: Gregor = 格里高尔 | 禁止: 格雷戈尔、格里格
en: Dante = 但丁 | 禁止: 丹特、丹堤
en: Vergilius = 维吉里乌斯 | 禁止: 维吉尔、维尔吉利乌斯
en: Charon = 卡戎 | 禁止: 卡隆、夏隆
en: Sinners = 罪人 | 禁止: 囚犯、罪徒
en: Mephistopheles = 梅菲斯托费勒斯 | 禁止: 梅菲斯特、墨菲斯托

# —— 主要角色 / NPC ——
en: Sancho = 桑丘
en: Dulcinea = 杜尔西内娅
en: Sansón = 参孙
en: The Barber = 理发师
en: The Priest = 神父
en: Cassetti = 卡塞蒂
en: Kromer = 克罗默
en: Demian = 德米安
en: Hohenheim = 霍恩海姆
en: Ahab = 亚哈
en: Queequeg = 魁魁格
en: Starbuck = 斯达巴克
en: Pip = 比普
en: Catherine = 凯瑟琳
en: Hindley = 亨德利
en: Linton = 林顿
en: Nelly = 耐莉
en: Moses = 摩西
en: Ezra = 以斯拉
en: Sonya = 索尼亚
en: Jia Xichun = 贾惜春
en: Jia Baoyu = 贾宝玉
en: Lin Daiyu = 林黛玉
en: Xue Baochai = 薛宝钗
en: Dongbaek = 冬柏
en: Dongrang = 东朗
en: Gubo = 仇甫
en: Araya = 阿赖耶
en: Erlking Heathcliff = 魔王希斯克利夫
en: The Time Ripper = 时间杀人魔
en: League of Nine = 九人会 | 禁止: 九人联盟、新九人会、九人协会

# —— 边狱公司与内部部门 ——
en: Limbus Company = 边狱公司 | 禁止: 林布斯、边狱企业
en: The LCB = LCB
en: LCCB = 先遣部队
en: LCCA = 收尾部队
en: LCD = LCD
en: LCE = LCE
en: Limbus Kindergarten = 边狱幼儿园
en: The Backdoor = 后门 | 禁止: 暗门
en: PDA Device = PDA终端

# —— 都市结构 ——
en: The City = 都市 | 禁止: 城市
en: District = 区
en: The Nests = 巢 | 禁止: 巢穴、鸟巢、巢都
en: The Backstreets = 后巷 | 禁止: 后街、背街
en: The Outskirts = 郊区 | 禁止: 郊外
en: The Great Lake = 大湖
en: Night in the Backstreets = 后巷深宵
en: The Head = 首脑 | 禁止: 元首、头目
en: The Claw = 爪牙 | 禁止: 爪子、利爪
en: Arbiter = 调律者 | 禁止: 仲裁者、调停者、裁定者
en: Taboo = 禁忌
en: The Wings = 翼 | 禁止: 翅膀、羽翼
en: Singularities = 奇点 | 禁止: 奇异点、奇异性
en: The Golden Bough = 金枝 | 禁止: 黄金枝、金树枝、黄金树枝
en: Relic = 遗物
en: The Sign = 印记
en: Smoke War = 烟霾战争 | 禁止: 烟雾战争
en: Concept Incinerator = 概念焚化炉
en: The Library = 图书馆
en: Lobotomy Corp. = 脑叶公司
en: Lobotomy Corp. Branch = 脑叶公司支部
en: Seed of Light = 光之种
en: White Nights and Dark Days = 白夜黑昼

# —— 公司 / 协会 / 事务所 / 工坊 ——
en: A Corp. = A公司 | 禁止: A社
en: G Corp. = G公司 | 禁止: G社
en: H Corp. = H公司 | 禁止: H社
en: K Corp. = K公司 | 禁止: K社
en: L Corp. = L公司 | 禁止: L社
en: M Corp. = M公司 | 禁止: M社
en: N Corp. = N公司 | 禁止: N社、恩公司
en: P Corp. = P公司 | 禁止: P社
en: R Corp. = R公司 | 禁止: R社
en: S Corp. = S公司 | 禁止: S社
en: T Corp. = T公司 | 禁止: T社
en: U Corp. = U公司 | 禁止: U社
en: W Corp. = W公司 | 禁止: W社
en: X Corp. = X公司 | 禁止: X社
en: The Associations = 协会
en: Hana Association = 하나协会
en: Zwei Association = Zwei协会
en: Shi Association = し协会
en: Cinq Association = Cinq协会
en: Liu Association = 六协会
en: Seven Association = Seven协会
en: Devyat' Association = Девять协会
en: Dieci Association = Dieci协会
en: Öufi Association = Öufi协会
en: Fixer = 收尾人 | 禁止: 修理工、修理者、调停人
en: Color Fixer = 特色 | 禁止: 彩色收尾人
en: Offices = 事务所 | 禁止: 办公室
en: Workshops = 工坊 | 禁止: 车间、工作室
en: Syndicate = 帮派 | 禁止: 辛迪加
en: Kurokumo Clan = 黑云会
en: Blade Lineage = 剑契组
en: Yurodiviye = 圣愚
en: The House of Spiders = 蜘蛛巢
en: Heishou Pack = 黑兽
en: Technology Liberation Alliance = 技术解放联盟
en: The Dead Rabbits = 死兔帮
en: Tieqiu Crew = 铁工会
en: Tingtang Gang = 豆豆帮
en: Twinhook Pirates = 双钩海盗团
en: Los Mariachis = 流浪乐队
en: Night Awls = 夜锥组
en: The Wild Hunt = 狂猎
en: La Manchaland = 拉·曼却领
en: Nagel und Hammer = 钉与锤
en: The Pequod = 裴廓德号
en: Wuthering Heights = 呼啸山庄
en: Dawn Office = 黎明事务所
en: Molar Office = 臼齿事务所
en: Full-Stop Office = 句点事务所
en: Hook Office = 吊钩事务所
en: Rosespanner Workshop = 玫瑰扳手工坊

# —— 手指 ——
en: The Fingers = 手指
en: The Index = 食指
en: The Thumb = 拇指
en: The Middle = 中指
en: The Ring = 环指 | 禁止: 无名指、戒指
en: The Pinky = 小指
en: Nursefather = 父辈
en: Apprentice = 子辈
en: Proxy = 代行者
en: Proselyte = 苦行者
en: Docent = 讲解员
en: Maestro = 大师
en: Big Sister = 幼姊
en: Little Sister = 幼妹
en: Great Brother = 长兄
en: Sottocapo = 二老板
en: Consigliere = 顾问

# —— 机制与设定 ——
en: Abnormalities = 异想体 | 禁止: 异常体、异变体、怪物体
en: Distortions = 扭曲 | 禁止: 畸变、失真
en: Qliphoth Deterrence = 逆卡巴拉抑制力
en: Qliphoth Counter = 逆卡巴拉计数器
en: Qliphoth Meltdown = 逆卡巴拉熔毁
en: Cognition Filter = 认知滤网
en: Enkephalin = 脑啡肽 | 禁止: 脑磷脂、恩凯法林
en: Canned Experience = 经验罐头
en: Identities = 人格 | 备注: 指罪人可替换的人格(抽卡单位),不是「身份」
en: Identity = 人格 | 备注: 同上
en: Identity Uptying = 人格同步
en: Thread = 纺锤 | 禁止: 线程
en: Threadspinning = 异想解析
en: Resonance = 共鸣
en: Attunement = 感应度
en: fathoms of the ego = 自我心道
en: Mirror Worlds = 镜像世界
en: Mirror Dungeons = 镜像迷宫
en: Refraction Railway = 折射轨道 | 禁止: 折射铁路、反射铁道
en: Luxcavation Dungeons = 采光迷宫
en: Dungeon of the Fathoms = 心象迷宫
en: Bloodfiends = 血魔 | 禁止: 血鬼、吸血鬼
en: Kindred = 眷属 | 禁止: 血亲、亲属
en: Bloodbag = 血袋
en: Pallidified = 白化
en: Seaborn = 海嗣
en: Sea Terror = 恐鱼
en: E.G.O = E.G.O | 禁止: EGO | 备注: 保留缩写,不译成「自我」
en: E.G.O Gifts = E.G.O饰品
en: E.G.O Corrosion = E.G.O侵蚀
en: Extraction Ticket = 提取券

# —— 战斗数值 ——
en: Sanity = 理智值 | 禁止: 精神值
en: Lunacy = 狂气 | 禁止: 疯狂值
en: Clash = 拼点 | 禁止: 对拼、冲突
en: Stagger = 混乱 | 禁止: 硬直、踉跄、眩晕
en: Tremor = 震颤
en: Rupture = 破裂
en: Sinking = 沉沦 | 禁止: 下沉
en: Bleed = 流血
en: Burn = 烧伤 | 禁止: 燃烧
en: Poise = 呼吸法 | 禁止: 姿态、平衡
en: Charge = 充能 | 禁止: 充电
en: Haste = 迅捷 | 禁止: 急速
en: Bind = 束缚
en: Fragile = 易损
en: Protection = 守护

# ======================== 日语原文 ========================
# 原文列 = 日文版屏幕上的写法;中文列 = 零协会汉化通行译名
# 来源:日文 Limbus 攻略 wiki(lcbwiki)、ProjectMoon 大辞典、囚人称呼整理
#
# 最容易翻错的地方(日文和中文译名完全不同):
#   罪人 → 日文叫「囚人」   异想体 → 日文叫「幻想体」
#   扭曲 → 日文叫「ねじれ」  血魔 → 日文叫「血鬼」
#   Limbus Company → 日文叫「リンバス・カンパニー」
# 标了「待核对」的行:原文写法我没找到实证,请对着「运行」页的「识别到的原文」改准。
# (原文写错不会出错,只是这条不生效。)

# —— 十二罪人与主角(日文写法已核对)——
ja: ダンテ = 但丁 | 禁止: 丹特、丹堤
ja: イサン = 李箱 | 禁止: 李相、伊桑
ja: ファウスト = 浮士德 | 禁止: 法乌斯特
ja: ドンキホーテ = 堂吉诃德 | 禁止: 唐吉诃德、堂吉珂德
ja: 良秀 = 良秀 | 禁止: 凉秀、辽修、良修
ja: ムルソー = 默尔索 | 禁止: 穆尔索、梅尔索
ja: ホンル = 鸿璐 | 禁止: 洪露、红露、鸿露
ja: ヒースクリフ = 希斯克利夫 | 禁止: 希斯克里夫、西斯克利夫
ja: イシュメール = 以实玛利 | 禁止: 伊斯梅尔、伊什梅尔
ja: ロージャ = 罗佳 | 禁止: 罗迪昂、罗季昂
ja: シンクレア = 辛克莱 | 禁止: 辛克雷、辛克莱尔
ja: ウーティス = 奥提斯 | 禁止: 奥德修斯、奥蒂斯
ja: グレゴール = 格里高尔 | 禁止: 格雷戈尔、格里格
ja: 囚人 = 罪人 | 禁止: 囚犯、罪徒
ja: 管理人 = 管理人 | 备注: 日文对玩家的称呼,零协中文同样用「管理人」
ja: リンバス・カンパニー = 边狱公司 | 禁止: 林布斯
ja: メフィストフェレス = 梅菲斯托费勒斯 | 禁止: 梅菲斯特、墨菲斯托

# —— 都市与世界(日文写法已核对)——
ja: 都市 = 都市 | 禁止: 城市
ja: 巣 = 巢 | 禁止: 巢穴、鸟巢
ja: 裏路地 = 后巷 | 禁止: 后街、背街
ja: 大湖 = 大湖
ja: 翼 = 翼 | 禁止: 翅膀、羽翼
ja: 特異点 = 奇点 | 禁止: 奇异点
ja: 事務所 = 事务所 | 禁止: 办公室
ja: 協会 = 协会
ja: 工房 = 工坊 | 禁止: 车间、工作室
ja: フィクサー = 收尾人 | 禁止: 修理工、修理者
ja: バトラー = 管家
ja: 図書館 = 图书馆
ja: アンジェラ = 安吉拉
ja: 残響楽団 = 残响乐团
ja: 九人会 = 九人会 | 禁止: 九人联盟、新九人会

# —— 异想体 / 扭曲 / 血魔(日文写法已核对)——
ja: 幻想体 = 异想体 | 禁止: 异常体、异变体
ja: ねじれ = 扭曲 | 禁止: 畸变、失真
ja: 血鬼 = 血魔 | 禁止: 吸血鬼
ja: 血鬼の狩人 = 血魔猎人
ja: 大湖の鯨捕り = 捕鲸人

# —— 战斗与人格(日文写法已核对)——
ja: 人格 = 人格 | 禁止: 身份 | 备注: 指罪人可替换的人格(抽卡单位)
ja: E.G.O = E.G.O | 禁止: EGO | 备注: 保留缩写,不译成「自我」
ja: ドーセント = 讲解员
ja: 野獣派 = 野兽派
ja: 立体派 = 立体派
ja: 点描派 = 点彩派
ja: 中指 = 中指
ja: セブン協会 = Seven协会
ja: トレス協会 = Tres协会

# —— 待核对(我手上没有日文实证,请对着屏幕改准原文)——
ja: 黄金の枝 = 金枝 | 备注: 待核对(Golden Bough 的日文写法)
ja: クリフォト = 逆卡巴拉 | 备注: 待核对(Qliphoth)
ja: エンケファリン = 脑啡肽 | 备注: 待核对(Enkephalin)
ja: 正気度 = 理智值 | 备注: 待核对(Sanity)
ja: 狂気 = 狂气 | 备注: 待核对(Lunacy)
ja: ロボトミー・コーポレーション = 脑叶公司 | 备注: 待核对
ja: 後ろの扉 = 后门 | 备注: 待核对(The Backdoor)
ja: 鏡世界 = 镜像世界 | 备注: 待核对(Mirror Worlds)
ja: 鏡ダンジョン = 镜像迷宫 | 备注: 待核对(Mirror Dungeons)
ja: 色彩 = 特色 | 备注: 待核对(Color Fixer)
ja: 首脳 = 首脑 | 备注: 待核对(The Head)
ja: 爪 = 爪牙 | 备注: 待核对(The Claw)
ja: 調律者 = 调律者 | 备注: 待核对(Arbiter)
""";

    public const string Worldview = "《边狱巴士》(Limbus Company,Project Moon 出品)的对话。背景是「都市」——由 26 家巨型企业(「翼」)与各自管辖的「巢」、以及巢之外的「后巷」构成的巨型都市国家。玩家扮演管理人但丁,带领十二位「罪人」乘坐巴士「梅菲斯托费勒斯」,在都市各处崩塌的脑叶公司支部里回收「金枝」。专有名词一律沿用社区通行的零协会译名,不要另造:Outis 是「奥提斯」而不是「奥德修斯」;Identity 是「人格」而不是「身份」;The City 是「都市」而不是「城市」;Fixer 是「收尾人」;Association 是「协会」;Abnormality 是「异想体」(日文原文写作「幻想体」);Distortion 是「扭曲」(日文原文写作「ねじれ」);Bloodfiend 是「血魔」(日文原文写作「血鬼」);Wing 是「翼」;Nest 是「巢」。英文的「N Corp.」一律译作「N公司」,不要写成「N社」。日文原文里罪人叫「囚人」、公司名写作「リンバス・カンパニー」,译文都要按中文译名走。";

    public const string StyleHint = "罪人之间以名字或绰号互称,语气现代、口语化,夹带黑色幽默与讽刺,不要文言或书面官腔。各人的语气差别很大,不要写成同一个腔调:堂吉诃德热情外放、感叹号极多;默尔索平铺直叙、几乎不用语气词(不要给他加「啊/吧/呢」);良秀句子极短、很少说「我们」;辛克莱犹豫、多用省略号;罗佳爱用反问;但丁是管理人,台词短而克制。";
}
