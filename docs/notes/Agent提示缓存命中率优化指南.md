# Agent 提示缓存命中率优化指南

面向自研 harness / 自建网关的实战整理。覆盖缓存原理、通用前缀布局、会话亲和机制，以及各供应商与网关的特殊要求。

---

## 〇、先弄清一件事：缓存到底缓存了什么

Prompt Cache **不是**"记住上次的答案"。它复用的是模型处理完重复前缀后留下的计算状态（KV Cache），新问题依然要重新推理、新答案依然逐 token 生成。省掉的只是那段重复前缀的 Prefill 计算。

三个直接推论：

- **匹配从第一个 token 开始，在第一个差异处停止。** 前缀里任何一处字节不同，后面全部作废。
- **只有"前缀"能复用。** 变化的内容必须沉到尾部。
- **命中率是可观测的。** 看 `usage.prompt_tokens_details.cached_tokens`，盯 `cached_tokens / prompt_tokens` 这个比值，不要只看总量。

**为什么这件事在 Agent 场景是数量级的**：一个几十万 token 的 session 跑到下一轮，真正新增的可能只有几千 token——system prompt、工具定义、代码上下文、历史对话基本没变。稳定前缀 20,000 token × 50 轮 = 100 万 token 的重复输入。命中率从 70% 掉到 20%，账单立刻看得出来。

---

## 一、三种缓存机制的区别

| 类型 | 谁来标记 | 最小缓存长度 | 缓存读取计价 | 多副本路由 |
|---|---|---|---|---|
| 显式断点型 | 开发者在内容块上打标 | 约 1024 token | 输入的约 0.1 倍 | 断点信息随请求走，天然友好 |
| 自动前缀型 | 服务端自动检测 | 约 1024 token | 输入的约 0.25–0.5 倍 | **需要粘性路由或路由键** |
| 显式缓存对象型 | 先创建缓存对象再引用 | 通常更高，按模型分档 | 约 0.25 倍 + 存储费 | 对象有 id，天然友好 |

关键分歧在最后一行：**自动前缀型（DeepSeek、Meta、多数 OpenAI 兼容服务）在服务端多副本部署下，请求被负载均衡随机打到不同机器，命中率会碎成渣。** 这就是"需要额外传 session ID"的根本原因——不是为了鉴权，是为了做路由亲和。

---

## 二、通用前缀布局（对所有供应商都成立）

### 2.1 布局原则

```
[ 系统指令 / 工具定义 / 项目上下文 ]  ← 稳定，放最前
[ 历史对话（旧的在前，新的在后） ]
[ 本轮用户输入 / 时间戳 / request id ] ← 易变，放最后
                                       ↑ 缓存边界
```

常见反例：把毫秒级时间戳、request ID、动态 skills 列表插在 system prompt 前面——每轮前缀都不同，命中率直接归零。

### 2.2 必须遵守的纪律

- **工具定义顺序禁止在调用之间重排。** 工具 schema 是付费输入，也是前缀的一部分。哪怕内容一致，顺序变了也全断。
- **工具定义不要动态增删。** 有实测通过工具白名单把 payload 从 161KB 压到 24KB，顺带稳定了前缀。
- **提示词版本写进缓存键。** `support-agent:v4`，方便对比新旧版本命中率、也方便回滚。注意改 key 不等于清空缓存，旧条目仍会存在到自然过期。
- **TTL 心里有数。** 默认通常 5–10 分钟，部分平台可申请 24h。跨零点日期变更会让 system prompt 里的日期变化 → 前缀失效 → TTFT 暴涨，这是经典故障。
- **别把敏感信息放进缓存键。** key 会被当作路由标识传递。

### 2.3 缓存键的粒度：不要过度分片

```
✅ prompt_cache_key = "support-agent:v3"                    # 共享同一套公共前缀
✅ prompt_cache_key = f"support-agent:v3:session:{sid}"     # 前缀含会话独有历史
❌ prompt_cache_key = str(uuid.uuid4())                     # 每请求随机，破坏路由分组
```

每个 key 的总流量最好控制在约 15 req/min 以内；更高流量时按租户、会话或稳定哈希分片。

**过度分片是最隐蔽的杀手**：一个用户一个 key、一个会话一个 key，会把请求打散到不同机器上。实测去掉粘性路由后，同一套流量命中率从 70% 出头掉到 20% 以下。

---

## 三、会话亲和：自研 harness 必须自己实现

### 3.1 通用做法

用 HTTP 头或请求体字段承载会话标识，服务端据此把同一会话的请求调度到同一批机器。主流约定：

| 信号 | 载体 | 常见发送方 |
|---|---|---|
| `X-Session-Id` | Header | OpenCode、Kilo Code、通用客户端 |
| `X-Opencode-Session` | Header | OpenCode 系 |
| `X-Claude-Code-Session-Id` | Header | Claude Code |
| `Session-Id` / `session_id` | Header / Body | Codex CLI、OpenRouter 约定 |
| `X-Litellm-Session-Id` | Header | LiteLLM 约定 |
| `metadata.user_id` | Body（Anthropic） | Claude Code 内嵌会话 UUID |
| `prompt_cache_key` | Body（OpenAI 兼容） | Zed 等按会话复用缓存键的客户端 |

**最佳实践**：同一会话内保持不变；不同用户、不同会话用不同值；用业务侧稳定生成的标识，不要每次请求重新生成；避免大量高并发用户共用一个 ID（会削弱调度亲和，也有上下文串扰风险）。

### 3.2 无显式信号时的兜底派生

```
session_id = stable_uuid( sha256(system + tools + first_user_message) )
```

这个式子同时保证"前缀相同 → ID 相同"和"前缀不同 → ID 不同"。后续轮次复用同一个上游会话，只在 system prompt、工具集或压缩后上下文变化时轮换。

### 3.3 两个极端都致命

- 每轮新生成 ID → 永远冷启动，等于没缓存。
- 所有会话写死一个固定值 → 不同前缀的请求挤进同一个缓存桶，互相踢掉，命中率反而崩、延迟上去、钱也跟着上去。

---

## 四、各供应商 / 网关的特殊要求

### 4.1 OpenAI（官方 API）

- 缓存**自动生效**，无需开关；`prompt_cache_key` 是可选的路由提示，不是命中本身的条件。
- 高流量时加一个命名共享前缀的稳定 key（如 `customer-support-v2`）能提升命中率。
- `prompt_cache_retention`：`"in_memory"`（默认快路径）或 `"24h"`（跨空闲期保温），是提示而非保证。
- **Responses API 的额外优势**：推理项可以跨轮保留（自动，或用 `reasoning.encrypted_content` 走无状态/ZDR 场景），Chat Completions 每轮丢弃推理项、下轮重建。多轮复杂任务是质变。
- 注意 GPT-5.4 起 Chat Completions 只在 `reasoning_effort: none` 下允许工具调用，GPT-6 Astra 完全不能在 Chat Completions 调工具。

### 4.2 Anthropic（Claude）

- 显式断点：在内容块上打 `cache_control: {"type": "ephemeral"}`，可带 `ttl`。
- 最小缓存长度约 1024 token，短于阈值不缓存。
- 工具定义、system 都要按稳定顺序；`tools` 顺序变化即断。
- 亲和信号走 `metadata.user_id`（字符串，可序列化 `device_id / account_uuid / session_id`）。
- 开启 extended thinking 时 `max_tokens > thinking.budget_tokens`，且会丢掉 `temperature` / `top_p`——参数变化也会影响前缀一致性。

### 4.3 DeepSeek

- **严格字节级前缀匹配，无容错。** 所有供应商里对前缀最苛刻的一家。
- 已知坑：opencode 组装的请求前缀里带毫秒级时间戳、skills 列表等动态内容，导致 Command Code 网关侧 deepseek-v4-flash 命中率只有约 50%，每轮约 5 万 token 全量重算、费用约 2 倍；opencode-go 直连实测 99%+。
- 官方 CLI 的做法是把易变动量块挪到请求尾部（v0.33.0 的 Major Cache Overhaul），第三方客户端改不了别人的请求结构时，这个组合基本无解。
- `prompt_cache: "off"` 只是策略提示，实测在 off 下仍会返回模型侧缓存命中，不能保证零缓存。

### 4.4 Meta Muse Spark

- **隐式缓存，没有开关可开**，但实测**并不稳定**。
- 实测波动：1.2 有一轮 4,026 token 中命中 0；1.1 在 OpenRouter 平均命中率 64.82%；1.3 在部分网关上近期为 0%、24 小时均值 0.5%。
- 定价上缓存极便宜（标准档 $0.15/M，比输入便宜 88%；Contributor 档 $0.002/M），所以命中率差异直接决定成本是否成立。
- 结论：**这个模型的低命中有一部分是上游天花板，不是 harness 能全补回来的。** 先测基线再优化。

### 4.5 OpenRouter

- **`session_id` 参数**（≤256 字符）就是为粘性路由设计的：作为 sticky routing key，把同一会话的请求路由到同一 provider，最大化 prompt cache 命中，同时用于用量分组。
- **Responses API 是无状态的**：带 `store: true` 或非空 `previous_response_id` 会被 **400 拒绝**。Responses 最大的两个优势（服务端状态、跨轮推理保留）在这里直接没有。
- 非标准参数会被忽略而不是报错，改模型时注意静默降级。

### 4.6 Command Code（自建 harness / 中转场景重点）

**缓存按会话粒度运作**，官方 CLI 能把命中率长期做到 95%–99%，靠的是自己组装请求 + 会话亲和。自研 harness 要复刻这套：

四层优先级（社区实现通行做法）：

1. **显式会话头** — `x-session-id` / `x-claude-code-session-id` / `session_id`，长度 ≥ 8 才被采纳，格式化后作为上游会话 ID
2. **显式 `prompt_cache_key`**（长度 ≥ 8），优先于派生启发式
3. **前缀派生** — `sha256(system + tools + first_user_message)` 生成稳定 UUID；会话跨轮复用，仅在 system / 工具集 / 压缩上下文变化时轮换
4. **内容级 `cache_control` 断点** — 含 ttl，原样透传

**信封要点**（抓包定案）：

- `/alpha/generate` 是主通道（Go 档唯一可用），请求体为 CLI 信封：`{config, memory, taste, skills, permissionMode, threadId, params}`，另有可选的 `mode` 与 `promptCache`。
- **`threadId` 与 `x-session-id` 同值。**
- `memory` / `taste` / `skills` 在第三方实现里是硬编码 `null` 死键。
- `config` 块九字段：`workingDir / date / environment / structure / isGitRepo / currentBranch / mainBranch / gitStatus / recentCommits`。其中 **`date`、`gitStatus`、`recentCommits` 每轮都在变**——它们的位置直接决定字节级前缀能不能对上。

**`promptCache` 字段的陷阱**：正常 agent 循环轮次**从不设置**这个字段；只有旁路调用（图片描述、上下文压缩、标题生成）才发 `"off"`。在主循环里带上它等于亲手关掉缓存。

**指纹层**：设备指纹由 API key 确定性派生并按 CLI 算法哈希，同一 key 永远报告同一台设备；**跨进程重启需要配置固定的 `FINGERPRINT_SEED` 才能保持会话身份**（与是否启用指纹上报相互独立）。另有 `/alpha/fingerprint/record` 与 `/alpha/lifecycle-events` 心跳预请求（约 8h + 2h 抖动周期）。

**端点差异**：`/provider/v1/chat/completions`（Pro+ 文档化端点）能显式传的只有 `prompt_cache_key`，它与会话头是两套独立机制，**官方明确说"不保证 provider 端缓存命中"**。带 `threadId` 的完整信封只在 `/alpha/generate`。先确认自己打的是哪个端点，能做的优化层级完全不同。

**已知组合坑**：Command Code 的 DeepSeek 模型放进 opencode / OpenChamber 只剩约 50% 命中率，官方 CLI 91–98%，直连 99%+。官方修复只覆盖自己的 CLI 请求组装。

### 4.7 opencode 系

- 要求每个请求带稳定的会话 ID，放在 **`x-opencode-session`** 头里；2026-09-05 起缺失该头的请求会被直接拒绝，返回 **400 MissingSessionID**。
- 插件侧通常提供两种模式：`uuid`（每会话一个随机 UUID，不把内部会话 ID 发给第三方）与 `session-id`（显式发送内部会话 ID）。
- **不要为图省事写死一个固定 header**——所有会话共用一个 ID，不同前缀被塞进同一个缓存桶，命中率掉、延迟上、钱也上。

### 4.8 Codex CLI

- 新版硬绑 `/v1/responses`，2026 年 2 月起 `wire_api` 只剩 `responses`，`chat/completions` 已被移除。
- 上游只提供 chat 格式时必须上协议转换层，但要清楚这是**有损翻译**，工具调用与推理字段最容易出问题。

### 4.9 LiteLLM 与各类网关桥接

- 对没有原生 Responses 实现的上游（`custom_openai/`、`openai_like/`、`baseten/` 等），`/v1/responses` 默认**桥接回 `/v1/chat/completions`**，Responses 独有字段被"近似或丢弃"。
- 你以为在用 Responses，底下跑的还是 chat，还多一层转换失真。要直通需在 `model_info` 里声明 `supported_endpoints: ["/v1/responses"]`。
- `previous_response_id` 在无状态后端上会被原样转发、然后被后端 400 拒绝。

### 4.10 国内模型 / 自建推理

- DeepSeek、Kimi、MiniMax、GLM、Qwen 等对外基本只暴露 chat 格式；中转站通常标注 `/v1/responses` 仅部分 OpenAI 新模型支持，建议用通用格式。
- 自建 vLLM / SGLang 这类自动前缀型服务，**必须自己在网关层做一致性哈希粘性路由**（按会话 id 或 agent 配置 id）。这是唯一能救多副本命中率的手段。

---

## 五、自研 harness 落地清单

按优先级排：

- [ ] **选对端点**。能发完整信封的 `/alpha/generate` 与只接受 chat 格式的 `/provider/v1/*`，可优化的层级完全不同。
- [ ] **生成并持久化会话 ID**，一个会话一个，跨轮不变；前缀变化时按规则轮换。
- [ ] 兜底派生：`sha256(system + tools + first_user_message)` → 稳定 UUID。
- [ ] **前缀审计**：把所有每轮变化的字段（日期、时间戳、git status、recent commits、request id、动态 skills 列表）全部挪到请求尾部。
- [ ] **工具定义顺序冻结**。序列化前排序一次，之后永不重排；不要每轮动态增删工具。
- [ ] 提示词版本写进缓存键（`agent:v4`），便于 A/B 与回滚。
- [ ] 主循环**不要**设 `promptCache: "off"`。
- [ ] 跨进程重启时固定 `FINGERPRINT_SEED`（如适用）。
- [ ] 图片/截图预算控制：base64 图片按文本分词极贵（单张 2.76MB 截图实测约 1.92M tokens），旧图替换成占位符能同时压成本和稳定前缀。
- [ ] **埋点**：每轮记录 `cached_tokens / prompt_tokens`，按会话和按模型分别监控。

## 六、排障顺序

命中率异常时按这个顺序查，成本从低到高：

1. `cached_tokens` 是不是压根没上报？（换成用量面板或上游响应的原始 usage 字段确认）
2. 前缀里有没有时间戳 / 日期 / request id？挪到最后再测。
3. 工具定义顺序在两轮之间是否一致？抓两轮请求 diff 一下。
4. 会话 ID 是否稳定？是不是每轮重新生成，或反过来所有会话共用一个？
5. 该供应商是不是自动前缀型 + 多副本？有没有粘性路由手段？（OpenRouter 用 `session_id`，自建用一致性哈希）
6. 是不是踩了桥接降级？（Responses → Chat Completions 被网关转换）
7. 最后才怀疑模型侧天花板——先查该模型在网关上的公开命中率基线（Muse Spark 这类已知波动极大）。

## 七、一句话总结

命中率的胜负手不在"传没传 session ID"，而在**请求组装权在谁手上**。能自己排前缀顺序、自己冻结工具定义、自己维护会话亲和的 harness，才能吃到缓存红利；把 key 塞进别人组装好的客户端里，光补一个 ID 补不回那几十个点。
