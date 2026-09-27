using System.Text;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 群流水与成员会话之间的换算（ADR 0046）。全是纯函数：
/// <see cref="GroupChatCoordinator"/> 决定「什么时候、交给谁」，这里只回答「交什么、算什么」。
/// </summary>
public static class GroupTranscript
{
    private const int MaxStripPasses = 3; //循环剥前缀上限：脏数据一般叠一两层，三层封顶，再多当正文

    /// <summary>
    /// 把游标之后、这个成员还没听过的群发言合成<b>一条</b>投递正文，逐条标发言人。
    ///
    /// 合成一条是为了守住 user / assistant 严格交替——部分本地对话模板遇到连续的 user 消息会直接报错。
    /// 跳过两类：他自己的发言（本来就在他自己的会话里），以及已经插话插给他的。
    /// </summary>
    /// <param name="groupLog">群流水</param>
    /// <param name="cursor">这个成员的游标：之前的都已交给过他</param>
    /// <param name="memberSessionId">成员会话标识</param>
    /// <param name="injected">已经插话插给他的群流水下标；没有为 null</param>
    /// <returns>投递正文；没有新话可交为 null</returns>
    public static string? BuildDelivery(IReadOnlyList<ChatMessage> groupLog, int cursor, string memberSessionId,
        IReadOnlySet<int>? injected = null)
    {
        StringBuilder text = new();
        foreach ((ChatMessage post, string body) in Undelivered(groupLog, cursor, memberSessionId, injected))
        {
            if (text.Length > 0) text.Append("\n\n");
            text.Append(FormatPost(post.AuthorName, body));
        }

        return text.Length == 0 ? null : text.ToString();
    }

    /// <summary>
    /// 与 <see cref="BuildDelivery"/> 同一批发言里带的图片。正文里已有它们的路径引用，
    /// 图片本身只转交给看得了图的成员（由调度器按人判断）
    /// </summary>
    /// <param name="groupLog">群流水</param>
    /// <param name="cursor">这个成员的游标</param>
    /// <param name="memberSessionId">成员会话标识</param>
    /// <param name="injected">已经插话插给他的群流水下标；没有为 null</param>
    /// <returns>图片，按发言顺序；没有为空</returns>
    public static IReadOnlyList<DataContent> DeliveryImages(IReadOnlyList<ChatMessage> groupLog, int cursor,
        string memberSessionId, IReadOnlySet<int>? injected = null) =>
        Undelivered(groupLog, cursor, memberSessionId, injected).SelectMany(x => ImagesOf(x.Post)).ToList();

    /// <summary>
    /// 交给成员的一条投递消息：正文在前、图片在后，标上群投递
    /// </summary>
    /// <param name="text">投递正文（已带发言人前缀）</param>
    /// <param name="images">转交给他的图片；他看不了图时传空</param>
    /// <returns>新消息（每人一份：同一实例进了几个人的历史，一处改注解就串到别人那里）</returns>
    public static ChatMessage DeliveryMessage(string text, IEnumerable<DataContent> images)
    {
        List<AIContent> contents = [new TextContent(text)];
        contents.AddRange(images);
        ChatMessage message = new(ChatRole.User, contents);
        ChatMessageAnnotations.MarkGroupDelivery(message);
        return message;
    }

    /// <summary>一条群发言带的图片</summary>
    /// <param name="post">群发言</param>
    /// <returns>图片；没有为空</returns>
    public static IEnumerable<DataContent> ImagesOf(ChatMessage post) =>
        post.Contents.OfType<DataContent>().Where(x => x.HasTopLevelMediaType("image"));

    // 游标之后、他没听过的群发言：跳过他自己的、已经插话插给他的、剥完前缀没字的
    private static IEnumerable<(ChatMessage Post, string Body)> Undelivered(IReadOnlyList<ChatMessage> groupLog,
        int cursor, string memberSessionId, IReadOnlySet<int>? injected)
    {
        for (int i = Math.Max(0, cursor); i < groupLog.Count; i++)
        {
            ChatMessage post = groupLog[i];
            if (ChatMessageAnnotations.GroupSpeakerSessionOf(post) == memberSessionId) continue;
            if (injected?.Contains(i) == true) continue;

            string body = StripSpeakerPrefix(post.Text.Trim(), post.AuthorName);
            if (body.Length > 0) yield return (post, body);
        }
    }

    /// <summary>
    /// 一条群发言交给别人时的样子。结构化地知道是谁说的（是谁的那一轮），
    /// 所以只在<b>交给模型</b>时拼成前缀，不从正文里反解析（方案 §6.5 的坑）。
    /// </summary>
    /// <param name="speaker">发言人显示名</param>
    /// <param name="body">正文</param>
    /// <returns>带发言人前缀的一段</returns>
    public static string FormatPost(string? speaker, string body) =>
        $"[{(string.IsNullOrWhiteSpace(speaker) ? "?" : speaker)}]: {body}";

    /// <summary>
    /// <see cref="FormatPost"/> 的反向：一条插话（单条发言）拆回发言人与正文
    /// </summary>
    /// <param name="text">插话正文</param>
    /// <returns>发言人与正文；不是这个格式时发言人为 null、正文原样</returns>
    public static GroupDeliverySegment ParsePost(string text)
    {
        int close = text.IndexOf("]: ", StringComparison.Ordinal);
        if (!text.StartsWith('[') || close <= 1) return new GroupDeliverySegment(null, text);
        return new GroupDeliverySegment(text[1..close], text[(close + 3)..]);
    }

    /// <summary>
    /// 剥掉模型自加的发言人前缀。场景段要求成员「直接说，不要自己加 [名字]: 前缀」，
    /// 但弱模型常照着投递格式仿写（`[一方通行]: 嗯。`）。整段以 `[名]:` 开头时，
    /// markdown 会把它当链接引用定义整段吞掉——气泡空白、复制却有字；投递时再包一层还会变双前缀。
    /// 只认发言人自己的名字，不做通用 `[...]:` 猜测，避免误伤正文里合法的中括号开头。
    /// </summary>
    /// <param name="body">正文</param>
    /// <param name="speaker">发言人显示名；为空时原样返回</param>
    /// <returns>剥掉自加前缀（循环剥多层脏数据）后的正文；剥空时返回空串，由调用方按空处理</returns>
    public static string StripSpeakerPrefix(string body, string? speaker)
    {
        if (string.IsNullOrEmpty(body) || string.IsNullOrWhiteSpace(speaker)) return body;

        string name = speaker.Trim();
        if (name.Length == 0) return body;

        string result = body;
        for (int i = 0; i < MaxStripPasses; i++)
        {
            string? rest = StripOnce(result.TrimStart(), name);
            if (rest == null) break;
            result = rest;
        }

        return result;
    }

    /// <summary>
    /// 就地剥掉一条助手消息开头自加的发言人前缀（落盘前用，成员会话里存的就是干净的那份）。
    /// 只动第一段有字的正文；带工具调用的旁白一样剥，它也会画成气泡
    /// </summary>
    /// <param name="message">模型刚回的消息</param>
    /// <param name="speaker">这个成员自己的名字</param>
    /// <returns>剥过为 true</returns>
    public static bool StripOwnPrefix(ChatMessage message, string? speaker)
    {
        if (message.Role != ChatRole.Assistant) return false;

        TextContent? first = message.Contents.OfType<TextContent>().FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Text));
        if (first == null) return false;

        string stripped = StripSpeakerPrefix(first.Text, speaker);
        if (stripped.Length == first.Text.Length) return false;

        first.Text = stripped;
        return true;
    }

    private static string? StripOnce(string text, string name)
    {
        // 中括号式：[名]: / [名]：/【名】: /【名】：，括号与冒号之间允许空格
        if (text.Length > 0 && (text[0] == '[' || text[0] == '【'))
        {
            char close = text[0] == '[' ? ']' : '】';
            int end = text.IndexOf(close);
            if (end > 1 && string.Equals(text.Substring(1, end - 1).Trim(), name, StringComparison.Ordinal))
            {
                string after = text.Substring(end + 1).TrimStart();
                if (after.Length > 0 && (after[0] == ':' || after[0] == '：'))
                    return after.Substring(1).TrimStart();
            }

            return null;
        }

        // 裸名式：名: / 名：
        if (text.StartsWith(name, StringComparison.Ordinal))
        {
            string after = text.Substring(name.Length).TrimStart();
            if (after.Length > 0 && (after[0] == ':' || after[0] == '：'))
                return after.Substring(1).TrimStart();
        }

        return null;
    }

    /// <summary>成员表示「这次不接话」的回复：不进群（场景段里告诉了他）</summary>
    public const string PassReply = "[跳过]";

    /// <summary>
    /// 这句回复是不是「不接话」。容忍模型常见的几种写法（全角括号、不带括号、末尾句号）
    /// </summary>
    /// <param name="text">剥过发言人前缀的正文</param>
    /// <returns>是为 true</returns>
    public static bool IsPass(string text)
    {
        string value = text.Trim().TrimEnd('。', '.', '！', '!');
        return value is PassReply or "【跳过】" or "跳过";
    }

    /// <summary>
    /// 主持人的冷启动提示（ADR 0049 决策 4）：用户这句没 @ 任何人时，随投递附在末尾。
    /// 放在投递里而不是系统提示——它只在这一刻成立，是动态状态
    /// </summary>
    public const string HostColdStartHint =
        "（你是本群主持人。上面用户那句没有 @ 任何人：先用 @名字 点名最合适的一两位来回应，不要自己直接回答；" +
        "点完名这一轮就结束，要查的资料留到之后再查，别让大家干等。）";

    /// <summary>
    /// 群里那一轮被停或失败后，再被叫醒又没有新话时交给他的一句。他可能已在私聊里接着做完了——
    /// 那份结果群里没人看见，所以请他说一句
    /// </summary>
    public const string ResumeNote = "（你上一轮在群里被打断了：接着做完；要是已经做完了，把结果跟大家说一句。）";

    /// <summary>每轮重锚的开头：界面据此认出它、不画出来</summary>
    public const string VoiceReminderOpening = "（说话前记着：";

    /// <summary>
    /// 每轮重锚（提案 v8 §7.4）：投递末尾贴一句本人的口吻。系统提示末尾的人格锚点到了长群聊里，
    /// 离开口处隔着几十条群友的长发言，拉不住口吻，人人滑成同一种报告腔；贴在投递里紧挨着开口处。
    /// 随投递落盘、之后不再改，前缀缓存不受影响；每次唤醒只多一行
    /// </summary>
    /// <param name="personaCoda">人格锚点（<c>CharacterData.GetPersonaCoda</c>）；空串时只提群里的说话尺度</param>
    /// <returns>重锚句</returns>
    public static string VoiceReminder(string personaCoda)
    {
        string coda = personaCoda.Trim();
        if (coda.Length > 0 && !"。！？.!?".Contains(coda[^1])) coda += "。";
        return $"{VoiceReminderOpening}{coda}群里说话像聊天，平常两三句。）";
    }

    /// <summary>
    /// 私聊说明：用户在成员会话里直接发的话，发给模型时前面带这一句——否则模型分不清这句是私聊还是群里说的，
    /// 会照群聊口吻回（实测会 @ 人）。界面显示时摘掉
    /// </summary>
    public const string PrivateNote =
        "（私聊：这句是用户单独对你说的，不在群里；你这一轮的回复只有用户看得到，不会发到群里。）";

    /// <summary>给私聊正文加上私聊说明</summary>
    /// <param name="text">用户打的原话</param>
    /// <returns>发给模型的正文</returns>
    public static string WithPrivateNote(string text) => PrivateNote + "\n\n" + text;

    /// <summary>摘掉私聊说明（没有就原样返回）</summary>
    /// <param name="text">消息正文</param>
    /// <returns>用户打的原话</returns>
    public static string StripPrivateNote(string text) =>
        text.StartsWith(PrivateNote, StringComparison.Ordinal) ? text[PrivateNote.Length..].TrimStart('\n') : text;

    /// <summary>
    /// 群场景段正文（ADR 0048），进成员的系统提示，不含标题。只写建群后不常变的事实与说话规矩：
    /// 群名、在场名单、自己是谁、主持人、发言格式与 @ 用法。改了其中任何一样，成员下一轮开跑时重建一次装配。
    /// 动态状态（这句没 @ 人、这句是私聊）不进这里，随消息给——那是每轮改前缀
    /// </summary>
    /// <param name="scene">场景要素</param>
    /// <returns>场景段正文</returns>
    public static string BuildScene(GroupScene scene)
    {
        string others = scene.OtherNames.Count == 0 ? "" : "、" + string.Join("、", scene.OtherNames);
        StringBuilder text = new();
        text.Append($"你在群聊「{scene.GroupName}」里。在场的有：{scene.UserName}（用户）{others}。你是{scene.SelfName}。");
        if (scene.HostName == scene.SelfName)
            text.Append("\n你是本群主持人：用户没有 @ 任何人时，先用 @名字 点名最合适的一两位来回应，而不是自己直接回答。");
        else if (scene.HostName != null)
            text.Append($"\n本群主持人是{scene.HostName}。");

        // 规矩一条一行：挤成长句连排时，前缀、@、过程话几条缠在一起，分不出主次
        text.Append("\n\n- 群里的发言会按「[名字]: 内容」的格式交给你；你每次说完的回复正文，就是你在群里说的话——" +
                    "直接说，不要自己加「[名字]:」前缀。想请某位成员接话，在发言里写 @名字。");
        // 不给尺度，群里的话会被当成交付物写，一人写长报告、别人跟着学
        text.Append("\n- 群里说话就像平常聊天：一次两三句，说你自己的看法，不写成报告。");
        if (scene.SharesDraftRoom)
            text.Append("要摆的材料长（清单、对比、摘录），写成草稿目录里的文件，群里只说结论、附上文件路径。");
        text.Append("\n- 调用工具时顺手写的话（比如「先查一下」）只留在你这里，群里看不到；要对大家说的，等工具用完再说。");
        if (scene.CanPostMidTurn)
            text.Append("想在这一轮中途先对大家说一句，可以调用 SendMessage，to 写 group；发出去的那几条就是你在群里说的话，" +
                        "同一条回复里的其余正文不会重复贴，之后每次说完的正文照常贴到群里。");
        // 实测（Hello World 首跑）：审查者各交一份几乎一样的清单，没新信息时人人把现状重申一遍
        text.Append("\n- 说过的点不复述，别人说过的、你自己刚说过的都算：你要说的跟已有的差不多，就只说不一样的那一点；认同就一句话带过。");
        text.Append($"\n- 没什么要补充、不用接话时，只回复「{PassReply}」：这句不会发到群里。不必为表态「收到」「我也等着」、" +
                    "或把大家都知道的现状再说一遍而专门说一句。");
        text.Append("\n- 用户也会单独找你私聊，那种消息开头标着「私聊」，回复只有用户看得到。");
        if (scene.SharesDraftRoom)
        {
            // 讨论 → 拍板 → 开工（方案 v6 §2.6⑤），拍板归用户。系统不解析用户那句话，判断交给模型
            text.Append("\n- 方案由用户拍板：用户明确说定（比如「就这么做」「开始吧」）或点名让你去做之前，只讨论、查证，" +
                        "草稿写在草稿目录里，不改工作区的文件、不跑会改东西的命令。拿不准用户是不是已经拍板，就问一句。");
            text.Append("\n- 你的草稿目录是全群共用的，别的成员也往里写：文件名起得具体些，新建前先看有没有同名的，别盖掉别人的。");
        }
        return text.ToString();
    }

    /// <summary>
    /// 把一条投递拆回各人的发言（呈现用；存储与供给仍是合成的一条）。
    /// 只认 <paramref name="speakerNames"/> 里的名字开头的 <c>[名字]: </c> 行——正文里别的中括号不会被误拆。
    /// 第一个发言人之前的（旧数据首次投递开头的场景说明）与末尾的主持人提示拆成无发言人的一段；
    /// 每轮重锚是说给模型的，不拆出来
    /// </summary>
    /// <param name="text">投递正文</param>
    /// <param name="speakerNames">可能出现的发言人（成员与用户）</param>
    /// <returns>各段，按原顺序；认不出任何发言人时整条是一段无发言人的</returns>
    public static IReadOnlyList<GroupDeliverySegment> SplitDelivery(string text, IReadOnlyCollection<string> speakerNames)
    {
        string body = text;
        string? tail = null;
        if (body.EndsWith(HostColdStartHint, StringComparison.Ordinal))
        {
            body = body[..^HostColdStartHint.Length].TrimEnd();
            tail = HostColdStartHint;
        }

        int reminder = body.LastIndexOf("\n\n" + VoiceReminderOpening, StringComparison.Ordinal);
        if (reminder >= 0 && body.EndsWith('）')) body = body[..reminder];

        List<GroupDeliverySegment> segments = [];
        string? speaker = null;
        StringBuilder current = new();
        foreach (string line in body.Split('\n'))
        {
            if (TryReadSpeakerLine(line, speakerNames, out string? name, out string rest))
            {
                Flush();
                speaker = name;
                current.Append(rest);
                continue;
            }

            if (current.Length > 0) current.Append('\n');
            current.Append(line);
        }

        Flush();
        if (tail != null) segments.Add(new GroupDeliverySegment(null, tail));
        return segments;

        void Flush()
        {
            string value = current.ToString().Trim();
            if (value.Length > 0) segments.Add(new GroupDeliverySegment(speaker, value));
            current.Clear();
        }
    }

    private static bool TryReadSpeakerLine(string line, IReadOnlyCollection<string> speakerNames,
        out string? name, out string rest)
    {
        name = null;
        rest = string.Empty;
        if (!line.StartsWith('[')) return false;

        int close = line.IndexOf("]: ", StringComparison.Ordinal);
        if (close <= 1) return false;

        string candidate = line[1..close];
        if (!speakerNames.Contains(candidate)) return false;

        name = candidate;
        rest = line[(close + 3)..];
        return true;
    }

    /// <summary>
    /// 从某个下标起，有没有一句用户发言没 @ 任何成员——主持人据此决定要不要先点名
    /// </summary>
    /// <param name="groupLog">群流水</param>
    /// <param name="fromIndex">起点下标</param>
    /// <param name="roster">成员名单</param>
    /// <returns>有为 true</returns>
    public static bool HasUnaddressedUserPost(IReadOnlyList<ChatMessage> groupLog, int fromIndex,
        IReadOnlyList<GroupRosterEntry> roster)
    {
        for (int i = Math.Max(0, fromIndex); i < groupLog.Count; i++)
        {
            ChatMessage post = groupLog[i];
            if (ChatMessageAnnotations.GroupSpeakerSessionOf(post) != null) continue;
            if (GroupMentions.Parse(post.Text, roster).Count == 0) return true;
        }

        return false;
    }
}

/// <summary>群场景段的要素</summary>
/// <param name="GroupName">群名</param>
/// <param name="SelfName">这个成员的名字</param>
/// <param name="OtherNames">其余成员的名字</param>
/// <param name="UserName">用户的名字</param>
/// <param name="CanPostMidTurn">他有没有 SendMessage 可用（智能体形态且开着委派）</param>
/// <param name="HostName">主持人的名字；没有为 null</param>
/// <param name="SharesDraftRoom">他有没有草稿目录（智能体形态且开着文件或命令行）：有就是全群共用的那一间，
/// 也说明他动得了工作区，场景段要讲清拍板之前不动手</param>
public sealed record GroupScene(string GroupName, string SelfName, IReadOnlyList<string> OtherNames,
    string UserName, bool CanPostMidTurn, string? HostName, bool SharesDraftRoom = false);

/// <summary>投递拆回来的一段</summary>
/// <param name="Speaker">发言人显示名；场景说明、主持人提示等无发言人为 null</param>
/// <param name="Body">正文</param>
public readonly record struct GroupDeliverySegment(string? Speaker, string Body);
