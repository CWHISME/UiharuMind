# 思考回传字段名动态探测与 roundtrip 三态化

> **状态**：accepted（2026-10-26）。
>
> **评审**：方案经子代理评审两轮。第一轮（research-thinking-roundtrip-pi-opencode）发现
> 三高严重度问题：`reasoning_text` 不进响应修复器（已修）、观测逐帧落盘写文件风暴（已修：
> 值变更才 Save）、roundtrip 三态化反转预设默认值（已修：sensenova 显式 false）。
> 第二轮（Standards/Spec 两轴）发现：SSE 分支漏传观测回调（已修）、观测落盘零测试与
> 观测值清不出去（已修：加测试 + 编辑窗口改 ModelPath 清值）、并发双写（已修：锁 config）、
> 规格变更缺 ADR（本文）。
> 第三轮（提交前人工复核）发现：① 配置上的 `ThinkingField` 与 `ILlmModel.ThinkingField`
> 同名不同义（前者是原始存储值、后者契约是生效键名），配置一旦被当模型用就会回填空键名
> （已修：存储侧改名 `ObservedThinkingFields`，接口契约由显式实现转给 `EffectiveThinkingField`）；
> ② 观测值记成标量，同端点切 ModelId 会串台（已修：改成按 ModelId 分开记，见决定 4）；
> ③ 回填键名只挡了 `null` 没挡空串（已修：`IsNullOrWhiteSpace` 兜底）。

## 背景

思考回传的既有机制：`OpenAiCompatibleRequestRewriter` 在发请求前改请求体，按 tool_call id
把历史里的思考正文补回去；要不要回传由 `ReplayExistingReasoning`（预设表 + 手动覆盖）
决定；回传键名**写死** `reasoning_content`。

两个问题：

1. **发现字段名的能力缺失**。参考 harness（pi、opencode）不写死键名：pi 流式解析时从
   `["reasoning_content","reasoning","reasoning_text"]` 取第一个非空字段记进思考块签名、回放时
   按同名写回（opencode 则有 `configuredField > observedField > reasoning_content` 三级）。
   我们的响应入口 `OpenAiCompatibleResponseFixer` 在字节层把 `reasoning` 改名成
   `reasoning_content`（SDK 只认后者，不归一思考会被静默丢掉），原名在进系统那一刻就消失；
   而 `reasoning_text` 根本不被识别（预扫描只查 `"reasoning"`），思考直接丢。于是「遇到思考字段
   不是 `reasoning_content` 的服务商（如商汤自研用 `reasoning`）就回传错键或丢思考」。
2. **回传开关只有两态**。预设表里未声明的模型一律不回传，新模型（如目录拉来的 90+）要逐条
   写预设才可能回传，写不动。

## 决定

1. **思考字段名动态探测 + 三级查表**：回填键名 = 运行时观测值 → 预设表 → `reasoning_content`。
   - 观测：`OpenAiCompatibleResponseFixer` 在把 `reasoning`/`reasoning_text` 改名为
     `reasoning_content` 时，把**原名**经 `SseSanitizingStream → SseSanitizingContent →
     OpenAICompatibleHttpHandler` 的 observer 透传到 `RemoteModelManager`，落盘到
     `BaseRemoteModelConfig.ObservedThinkingFields`（`[SettingConfigIgnoreDisplay]` 序列化字段，
     **按 ModelId 分开记**，见决定 4）。流式（SSE）与非流式（application/json）两条响应分支都接。
   - 消费：`EffectiveThinkingField` 三级查表（当前 ModelId 的观测值 → 该 ModelId 的预设 → 默认），
     请求侧 `ScanMessage`/`SerializeReasoning` 用该键名（不再写死 `reasoning_content`）。
   - 落盘节流：`RemoteModelManager.CreateThinkingFieldObserver` 里**值变更才 Save**
     （流式每帧都观测，不节流就是逐帧写配置字典）；比较+赋值+Save 锁在 config 实例上
     （同一模型自始至终同一实例，跨请求并发首写不双落盘）；Save 失败只记日志不打断响应。
   - 之所以必须落盘而不是内存缓存：回填发生在「请求发出时」、观测发生在「响应返回时」，
     内存缓存救不了重启后历史会话的旧思考——启动后的第一批回填用的是默认键名，而这一轮
     新观测只影响下一轮，已存在的旧思考永远错下去。
2. **roundtrip 三态化**：`RemoteModelIdVariant.ReplayExistingReasoning`
   `bool → bool?`：`null`=自动（历史有思考就回传）、`true`/`false`=显式要求/禁止。
   消费点（`RequestRewriter.For`）用 `model is not null && ... is not false` 收集——
   注意 nullable 的 `!= false` 在 null 时得到 null（判不出），不能用作条件。
   UI「跟随预设」改「自动检测」，单选组提示三态（自动/需要/不需要）。
3. **预设表显式声明防反转**（评审发现）：三态化后布尔默认值从 `false` 变 `null`（自动），
   凡此前靠「默认 false」维持不回传的预设会被悄悄反转。逐条核过：
   - `sensenova-6.8-flash-lite`：显式 `false`（官方文档推荐多轮对话不回传 reasoning）+ 预设
     `ThinkingField="reasoning"`（该模型思考字段名）。
   - `DeepSeek` 系、`kimi-k3`：不再显式 `true`——roundtrip 默认 `null`（自动，有正文就回传）
     与 `true` 等价；「没正文也要带字段」由基类按模型 ID 含 `deepseek` 自动判定。
   - `Zhipu`/`Agnes`：未声明，按设计从「不回传」转「自动回传」（见已知代价）。
4. **观测值按 ModelId 分开记，换端点整份作废**（评审第三轮修正）：观测值不是标量而是
   `ModelId → 字段名` 的表。**同一端点的不同模型字段名可以不同**——商汤转售的 DeepSeek 用
   `reasoning_content`、自研 6.8 用 `reasoning`，记成一个标量的话，在同一个配置项里切 ModelId
   就会把 A 模型的观测值发给 B 模型（DeepSeek 收到不认的 `reasoning` 键）。因此：
   - 观测落盘写进 `ObservedThinkingFields[当前 ModelId]`；
   - 编辑窗口保存时若 `ModelPath` 变了，**清整张表**（旧的都属于旧端点）；
   - 同端点切 ModelId 不清理——每个 ModelId 各记各的，天然不串台。
5. **「没有正文也要带字段」独立成一个开关**（第四轮）：`RequireReasoningFieldWhenEmpty`
   与 `ReplayExistingReasoning` 是两件事——那个管「有正文要不要回传」，这个管
   「没正文时字段能不能缺」。DeepSeek 类服务端把后者当硬校验，**字段缺了直接 400**。

   触发场景不是切模型，而是**现在就有**：先用关思考的档位跑出带 `tool_calls` 的历史，再切到
   开思考的档位，那条旧消息没有正文可回传，`ScanMessage` 找不到就整条不带字段 → 400。

   落点：`RequestRewrite.FillEmptyReasoning` → `ScanMessage` 在 `matched == null` 时写空串。
   **只对带 `tool_calls` 的助手消息补**——给普通消息塞空字段是白改请求体，还打乱前缀缓存。
   判定不落配置、不给手动覆盖（用户没法靠观察判断这条硬约束）。来源有两条：预设表显式声明，
   或模型 ID 含 `deepseek` 的兜底——转售的 DeepSeek（CommandCode/OpenCode 的 `deepseek/*`）
   不在预设表里，但硬校验不因转售而消失（与 opencode 的 `modelID.includes("deepseek")` 同口径）。
   预设：DeepSeek 系不再逐条写 `true`（由兜底覆盖）；`kimi-k3` 保持 `false`（pi 对 Moonshot 也没开这条）。

   消费侧再加一道思考档位闸门（第六轮，用户纠正）：这条硬校验只在**本次请求处于思考模式**时成立。
   `EThinkingMode.None` 显式发 `thinking: disabled`，服务端按正文调用处理，不要求 `tool_calls`
   带思考字段——此时补空串是多写字段、白改请求体。因此 `RequestRewrite.FillEmptyReasoning =
   （预设声明 || 模型 ID 含 deepseek） && model.ThinkingMode != None`；`Default` 不干预、
   由模型自己决定是否思考，仍按「可能思考」保守补上。局限：回放时只知道当前请求的档位，
   不知道历史消息当初是否思考产出。

## 取舍

- **探测做成运行时观测，而不是补一张「字段名小表」**：服务商模型太多写不动，观测是
  pi/opencode 同款；已确认过的模型（DeepSeek 的 reasoning_content、sensenova 的 reasoning）
  进预设表，观测只负责未知模型的空白。
- **观测值落盘 vs 内存**：落盘（见决定 1 的理由）；代价是配置存档多一个字段、多一次 Save。
- **`reasoning_details` 结构化思考（OpenRouter 加密块）**：本期不探测，回传仍按普通思考字段。
- **观测失败（Save 抛异常）**：内存值保留、重启后丢，不打断请求——思考回传是增强不是关键路径。
- **不按「产出模型」过滤历史思考**（第五轮，明确否决）：pi 的做法是只回填目标模型自己产的思考，
  别的整块丢掉（`transform-messages.ts` 里按 `provider/api/model` 三项比对）。**我们不跟**——
  `CollectReasoningByCallId` 照旧不看来源，历史里有正文就填，空串只兜「压根没有正文」。

  理由：服务端要的是「原样带回」，真实正文比空串更接近它期望的形态，空串是「最小满足」；
  真要丢掉旧思考，得先有每个模型的 compat 开关兜住「缺字段即 400」，否则是拿 400 换语义干净。
  我们面对 90+ 个目录模型，补不出那张开关表。

  代价：切模型后新模型会看到上一个模型的思考（语义脏，不影响功能）。

## 已知代价

- **`Default` 档仍补空串**：`Default` 不干预、由模型自己决定是否思考，无法从请求体确定服务端是否
  按思考模式处理，故保守补上；模型实际没思考时多一个空字段（比缺字段 400 便宜）。
- **Zhipu/Agnes 预设从「不回传」翻转为「自动回传」**：存量未声明 roundtrip 的配置，
  重启后若历史里有思考就会开始回传。GLM 思考字段名与是否接受回传**未实测**（无余额 key），
  若严格拒绝多字段可能 400。验证代价：带 key 一次 thinking + tool_calls 请求。
- 流式响应的首帧若首次观测到新字段名，`Save()` 的磁盘 IO 落在首帧上，加首 token 延迟
  （毫秒~秒级）；节流已挡掉后续帧，仅一次。
- 观测值清出的唯一入口是编辑窗口改 `ModelPath`；服务商单独改字段名（罕见）会错到被新观测
  覆盖为止。
- `sensenova-6.8-flash-lite` 的思考参数形状（我们发对象 `{"type":"enabled"}`、官方文档是字符串）
  仍未验证——与本文无关的另一条线，开思考模式实测时再看。

## 本期不做

- `reasoning_details` 结构化思考的探测与回传（OpenRouter 加密/总结块）。
- 编辑窗口给观测值（`ObservedThinkingFields`）展示/手动清除入口（当前靠改端点清除）。
- 预设表 `ThinkingField` 随目录候选下发到 UI（ADR 0092 口径：目录不携能力，预设只对已建配置生效）。