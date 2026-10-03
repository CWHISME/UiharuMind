using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 群流水与成员会话之间的换算（ADR 0046）。全是纯函数：
/// <see cref="GroupChatCoordinator"/> 决定「什么时候、交给谁」，这里只回答「交什么、算什么」。
/// </summary>
public static class GroupTranscript
{
    private const int MaxStripPasses = 3; //循环剥前缀上限：脏数据一般叠一两层，三层封顶，再多当正文

    /// <summary>
    /// 群发言交给成员时打头的一行（投递与插话都带）。投递在成员会话里是 user 消息，光看「[名字]: 内容」，
    /// 模型会当成用户单独发给他的——用户在群里 @ 他一句，他就在回复里直接答，群里没人看见。界面不画这一行
    /// </summary>
    public const string FeedHeader = "来自群聊：\n\n";

    private static readonly string FeedHeaderLine = FeedHeader.TrimEnd(); //拆段时认的那一行

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
            text.Append(text.Length == 0 ? FeedHeader : "\n\n");
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
    /// 交给成员的一条投递消息：正文在前、图片在后，标上群投递。
    /// 时间盖创建这一刻：不盖的话落盘只能补回落值（一轮开跑或落盘那一刻），
    /// 中途插话会被标成与实际差几分钟的时间。排序由落盘时夹下限兜底
    /// </summary>
    /// <param name="text">投递正文（已带发言人前缀）</param>
    /// <param name="images">转交给他的图片；他看不了图时传空</param>
    /// <returns>新消息（每人一份：同一实例进了几个人的历史，一处改注解就串到别人那里）</returns>
    public static ChatMessage DeliveryMessage(string text, IEnumerable<DataContent> images)
    {
        List<AIContent> contents = [new TextContent(text)];
        contents.AddRange(images);
        ChatMessage message = new(ChatRole.User, contents)
        {
            CreatedAt = DateTimeOffset.Now,
        };
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
            if (ChatMessageAnnotations.GroupAvatarPostOf(post) == memberSessionId) continue; //化身自己的话
            if (ChatMessageAnnotations.GroupAwayReceiptOf(post) != null) continue; //离席回执只给人看
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
    public static string FormatPost(string? speaker, string body) => GroupSpeakerLine.Format(speaker, body);

    /// <summary>
    /// 一条群发言插给正在说的人时的样子：与投递同样打头一行，再是带发言人前缀的一段
    /// </summary>
    /// <param name="speaker">发言人显示名</param>
    /// <param name="body">正文</param>
    /// <returns>插话正文</returns>
    public static string FormatInjection(string? speaker, string body) => FeedHeader + FormatPost(speaker, body);

    /// <summary>
    /// <see cref="FormatInjection"/> 的反向：一条插话（单条发言）拆回发言人与正文
    /// </summary>
    /// <param name="text">插话正文</param>
    /// <returns>发言人与正文；不是这个格式时发言人为 null、正文原样</returns>
    public static GroupDeliverySegment ParsePost(string text)
    {
        if (text.StartsWith(FeedHeader, StringComparison.Ordinal)) text = text[FeedHeader.Length..];
        return GroupSpeakerLine.TryReadBracketed(text, out string speaker, out string body)
            ? new GroupDeliverySegment(speaker, body)
            : new GroupDeliverySegment(null, text);
    }

    /// <summary>
    /// 剥掉模型自加的发言人前缀。场景段告诉成员「群里会自动标上你的名字，直接写正文」
    /// （不再写成「不要加前缀」的禁令——那句本身又把格式念了一遍），但弱模型仍常照着投递格式仿写（`[一方通行]: 嗯。`）。整段以 `[名]:` 开头时，
    /// markdown 会把它当链接引用定义整段吞掉——气泡空白、复制却有字；投递时再包一层还会变双前缀。
    /// 只认发言人自己的名字，不做通用 `[...]:` 猜测，避免误伤正文里合法的中括号开头。
    /// </summary>
    /// <param name="body">正文</param>
    /// <param name="speaker">发言人显示名；为空时原样返回</param>
    /// <returns>剥掉自加前缀（循环剥多层脏数据）后的正文；剥空时返回空串，由调用方按空处理</returns>
    public static string StripSpeakerPrefix(string body, string? speaker)
    {
        if (string.IsNullOrEmpty(body) || string.IsNullOrWhiteSpace(speaker)) return body;

        GroupSpeakerName name = new(speaker);
        if (name.Full.Length == 0) return body;

        string result = body;
        for (int i = 0; i < MaxStripPasses; i++)
        {
            string? rest = StripOnce(result.TrimStart(), name);
            if (rest == null) break;
            result = rest;
        }

        return StripBracketedLinePrefixes(result, name);
    }

    // 实测白井黑子第一段照常说，第二段开头写「[黑子]:」——把投递格式当成了分段标记。
    // 行中只剥括号式：裸名式在行首多半是在跟人说话
    private static string StripBracketedLinePrefixes(string text, GroupSpeakerName name)
    {
        if (!text.Contains('\n')) return text;

        string[] lines = text.Split('\n');
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].TrimStart();
            if (line.Length == 0 || (line[0] != '[' && line[0] != '【')) continue;
            if (StripOnce(line, name) is { } rest) lines[i] = rest;
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// 剥成员发言开头的自加前缀：自己的名字之外，还认开头的「[用户名]:」。
    /// 实测 Agnes 的千空两条发言以「[黑猫]:」（用户的名字）开头，是把投递格式里用户那条的写法仿了过来。
    /// 用户名只认括号式全名：「黑猫：」在句首多半是在对用户说话
    /// </summary>
    /// <param name="body">正文</param>
    /// <param name="speaker">发言成员的名字</param>
    /// <param name="userName">用户的名字；为空时只剥自己的</param>
    /// <returns>剥掉自加前缀后的正文</returns>
    public static string StripMemberPrefix(string body, string? speaker, string? userName)
    {
        string result = StripSpeakerPrefix(body, speaker);
        if (string.IsNullOrWhiteSpace(userName)) return result;

        GroupSpeakerName user = new(userName);
        return GroupSpeakerLine.TryReadBracketed(result.TrimStart(), out string name, out string rest) && user.IsExactly(name)
            ? StripSpeakerPrefix(rest, speaker)
            : result;
    }

    /// <summary>
    /// 就地剥掉一条助手消息开头自加的发言人前缀（落盘前用，成员会话里存的就是干净的那份）。
    /// 只动第一段有字的正文；带工具调用的旁白一样剥，它也会画成气泡
    /// </summary>
    /// <param name="message">模型刚回的消息</param>
    /// <param name="speaker">这个成员自己的名字</param>
    /// <param name="userName">用户的名字，见 <see cref="StripMemberPrefix"/>；为空时只剥自己的</param>
    /// <returns>剥过为 true</returns>
    public static bool StripOwnPrefix(ChatMessage message, string? speaker, string? userName = null)
    {
        if (message.Role != ChatRole.Assistant) return false;

        TextContent? first = message.Contents.OfType<TextContent>().FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Text));
        if (first == null) return false;

        string stripped = StripMemberPrefix(first.Text, speaker, userName);
        if (stripped.Length == first.Text.Length) return false;

        first.Text = stripped;
        return true;
    }

    // 括号里是他的任一叫法（全名、本名、别名或其中连续的两字起：原作角色常拿自称当前缀，如白井黑子写「[黑子]:」）；
    // 裸名式只认完整叫法：「黑子：」在句首更可能是在对人说话
    private static string? StripOnce(string text, GroupSpeakerName name)
    {
        if (text.Length > 0 && (text[0] == '[' || text[0] == '【'))
            return GroupSpeakerLine.TryReadBracketed(text, out string token, out string rest) && name.IsCalledBy(token) ? rest : null;

        foreach (string exact in name.ExactNames)
        {
            if (GroupSpeakerLine.TryReadBare(text, exact, out string rest)) return rest;
        }

        return null;
    }

    /// <summary>对话形态的成员表示「这次不接话」的回复：不进群（场景段里告诉了他）。智能体形态不用它，不调群发言工具就是不接话</summary>
    public const string PassReply = "[沉默]";

    /// <summary>
    /// 对话形态成员的这句回复是不是「不接话」。容忍模型常见的几种写法（全角括号、不带括号、末尾句号）
    /// </summary>
    /// <param name="text">剥过发言人前缀的正文</param>
    /// <returns>是为 true</returns>
    public static bool IsPass(string text)
    {
        string value = text.Trim().TrimEnd('。', '.', '！', '!');
        return value is PassReply or "【沉默】" or "沉默";
    }

    /// <summary>
    /// 补位轮的提示（ADR 0049 修订）：激进档一波静下来后，给还有没看过的发言的人各补一次，随投递附在末尾。
    /// 界面随每轮重锚一并摘掉，不画出来
    /// </summary>
    public static string CatchUpHint => $"（这一波大家说完了，上面是你还没看过的：有新角度就说，没有就只回复「{PassReply}」。）";

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
    /// </summary>
    /// <param name="personaCoda">人格锚点（<c>CharacterData.GetPersonaCoda</c>）；空串时只提群里的说话尺度</param>
    /// <returns>重锚句</returns>
    public static string VoiceReminder(string personaCoda)
    {
        string coda = personaCoda.Trim();
        if (coda.Length > 0 && !"。！？.!?".Contains(coda[^1])) coda += "。";
        return $"{VoiceReminderOpening}{coda}群里照你自己的说话方式说。）";
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
        StringBuilder text = new();
        text.Append($"你在群聊「{scene.GroupName}」里。在场的有：{BuildRoster(scene.UserName, scene.Others)}。你是{scene.SelfName}。");
        if (scene.HostName == scene.SelfName)
            text.Append("\n你是本群主持人：用户没有 @ 任何人时，先用 @名字 点名最合适的一两位来回应，而不是自己直接回答；" +
                        "点完名这一轮就结束，要查的资料留到之后再查，别让大家干等。");
        else if (scene.HostName != null)
            text.Append($"\n本群主持人是{scene.HostName}。");

        // 规矩一条一行、一个意思只说一次：挤成长句连排时分不出主次；同一个意思分几条说，模型反而分不出哪条最要紧
        // 回复就是发言（ADR 0060 修订）：只经工具说话时，模型被用户直接问到就照常在正文里答，群里谁也看不见
        text.Append("\n\n- 格式：群里的发言会按「[名字]: 内容」交给你；你每次说完的正文就是你在群里说的话，" +
                    "群里会自动标上你的名字，直接写正文就行。想请某位成员接话，写 @名字。");
        if (scene.HasGroupPostTool)
            text.Append($"\n- 中途说话：事情做到一半想先说一句（比如先说接哪一块），调用 {GroupPostTool.ToolName}；" +
                        "它发过的话，最后的正文里不必再说一遍。");
        // 不给尺度，群里的话会被当成交付物写，一人写长报告、别人跟着学
        // 口吻交给各自的卡：「像平常聊天」实测把 OP-01 的协议、原作角色的口吻一并抹成同一种聊天腔
        text.Append("\n- 尺度：说你自己的看法，用你自己的说话方式；发到群聊的信息不要过长，不写成报告。");
        if (scene.SharesDraftRoom) text.Append("材料长（清单、对比、摘录）就写成草稿目录里的文件，群里只说结论、附上路径。");
        // 实测：过程话写成调工具前的开场白；以「让我去翻一下……」收尾，这一轮就此结束、群里等不到下文
        // if (scene.HasGroupPostTool)
        //     text.Append("\n- 一轮怎么算：说了要去查，就在这一轮里查完再说，别拿「我去翻一下」收尾；" +
        //                 "一轮里可以说几次，比如先说接哪一块、做完再报结果。");
        // 实测：没新信息时人人把现状重申一遍、几个人核同一份事实、一句问用户的话被轮着再催七遍
        text.Append("\n- 只说新的：别人说过的、正在查在核的、你自己刚说过的，都不再重复；和已有的差不多就只说不一样的那一点，" +
                    $"认同一句话带过。已经有人问了{scene.UserName}、正等着回答的事，别再催一遍。");
        text.Append($"\n- 不接话：没什么要补充时只回复「{PassReply}」，这句不会发到群里；不必为表态「收到」「我也等着」专门说一句。");
        if (scene.SharesDraftRoom)
        {
            // 讨论 → 拍板 → 开工（方案 v6 §2.6⑤），拍板归用户。系统不解析用户那句话，判断交给模型
            // text.Append("\n- 方案由用户拍板：用户明确说定（比如「就这么做」「开始吧」）或点名让你去做之前，只讨论、查证，" +
            //             "不改工作区的文件、不跑会改东西的命令；拿不准就问一句。");
            // 实测：并行群里用户一句「开工」没点名，六人同时改同一份文档、三人同时实现同一处代码
            if (scene.HasGroupPostTool)
                text.Append($"\n- {scene.UserName}让大家开工却没点名谁做时，先调用 {GroupPostTool.ToolName} 说一句你接哪一块；" +
                            "避免和别人认领的撞了：谁先认领谁动手，没接到的只审不改。");
            text.Append("\n- 草稿目录是全群共用的：新建前先看有没有同名的，别盖掉别人的。");
        }

        return text.ToString();
    }

    /// <summary>
    /// 在场名单：没填作品的先摆（紧跟用户），同作品的再合并成段（首次出现的顺序）。
    /// 这个顺序是故意的：分组没有右边界，裸名跟在后面会被误读成组里人；
    /// 裸名在前则永远出现在任何 <c>《》</c> 之前，不可能被吞。没人填作品时退化成旧样子。
    /// </summary>
    private static string BuildRoster(string userName, IReadOnlyList<GroupMemberPresence> others)
    {
        string members = JoinMembers(others);
        return members.Length == 0 ? $"{userName}（用户）" : $"{userName}（用户）、" + members;
    }

    /// <summary>
    /// 成员名单（不含用户），排法见 <see cref="BuildRoster"/>
    /// </summary>
    /// <param name="others">成员（名字 + 作品）</param>
    /// <returns>顿号连起来的名单；没人为空串</returns>
    internal static string JoinMembers(IReadOnlyList<GroupMemberPresence> others)
    {
        List<string> bare = [];
        List<(string Works, List<string> Names)> groups = [];
        Dictionary<string, int> indexOf = new(StringComparer.Ordinal);
        foreach (GroupMemberPresence member in others)
        {
            string works = member.Works.Trim();
            if (works.Length == 0)
            {
                bare.Add(member.Name);
            }
            else if (indexOf.TryGetValue(works, out int index))
            {
                groups[index].Names.Add(member.Name);
            }
            else
            {
                indexOf[works] = groups.Count;
                groups.Add((works, [member.Name]));
            }
        }

        List<string> segments = [.. bare];
        segments.AddRange(groups.Select(x => $"《{x.Works}》：" + string.Join("、", x.Names)));
        return string.Join("、", segments);
    }

    /// <summary>
    /// 把一条投递拆回各人的发言（呈现用；存储与供给仍是合成的一条）。
    /// 只认 <paramref name="speakerNames"/> 里的名字开头的 <c>[名字]: </c> 行——正文里别的中括号不会被误拆。
    /// 第一个发言人之前的（旧数据首次投递开头的场景说明）拆成无发言人的一段；
    /// 每轮重锚与补位提示是说给模型的，不拆出来
    /// </summary>
    /// <param name="text">投递正文</param>
    /// <param name="speakerNames">可能出现的发言人（成员与用户）</param>
    /// <returns>各段，按原顺序；认不出任何发言人时整条是一段无发言人的</returns>
    public static IReadOnlyList<GroupDeliverySegment> SplitDelivery(string text, IReadOnlyCollection<string> speakerNames)
    {
        string body = text;

        int reminder = body.LastIndexOf("\n\n" + VoiceReminderOpening, StringComparison.Ordinal);
        if (reminder >= 0 && body.EndsWith('）')) body = body[..reminder];

        List<GroupDeliverySegment> segments = [];
        string? speaker = null;
        StringBuilder current = new();
        foreach (string line in body.Split('\n'))
        {
            if (line == FeedHeaderLine) continue; //说给模型的，不画
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
        if (!GroupSpeakerLine.TryReadBracketed(line, out string candidate, out rest) || !speakerNames.Contains(candidate)) return false;

        name = candidate;
        return true;
    }
}

/// <summary>群场景段里的一位在场成员</summary>
/// <param name="Name">显示名</param>
/// <param name="Works">出自哪部作品；没填为空串，名单里裸列</param>
public sealed record GroupMemberPresence(string Name, string Works);

/// <summary>群场景段的要素</summary>
/// <param name="GroupName">群名</param>
/// <param name="SelfName">这个成员的名字</param>
/// <param name="Others">其余在场成员（名字 + 作品，同作品的介绍时合并）</param>
/// <param name="UserName">用户的名字</param>
/// <param name="HasGroupPostTool">他有没有群发言工具（智能体形态）：中途说话用它，回复正文照样是发言（ADR 0060 修订）</param>
/// <param name="HostName">主持人的名字；没有为 null</param>
/// <param name="SharesDraftRoom">他有没有草稿目录（智能体形态且开着文件或命令行）：有就是全群共用的那一间，
/// 也说明他动得了工作区，场景段要讲清拍板之前不动手</param>
public sealed record GroupScene(string GroupName, string SelfName, IReadOnlyList<GroupMemberPresence> Others,
    string UserName, bool HasGroupPostTool, string? HostName, bool SharesDraftRoom = false);

/// <summary>投递拆回来的一段</summary>
/// <param name="Speaker">发言人显示名；场景说明、主持人提示等无发言人为 null</param>
/// <param name="Body">正文</param>
public readonly record struct GroupDeliverySegment(string? Speaker, string Body);
