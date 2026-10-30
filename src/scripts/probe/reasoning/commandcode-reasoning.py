#!/usr/bin/env python3
"""量一件事：这个模型在 CommandCode 上到底有没有「思考票据」，以及它长什么样。

为什么需要专门量：加密思考不进界面，我们的适配层又只认 reasoning_content
（reasoning_details 在 Microsoft.Extensions.AI.OpenAI 里根本没被读），于是
「服务端发了、我们丢了」与「服务端根本没发」在应用里长得一模一样，
只有 usage 里那个 reasoning_tokens 在动。有些闭源模型正是这种。

七步，依次回答七个问题：

  1. 它声明了哪些端点（/responses 在不在）
  2. /chat/completions 的响应里有没有 reasoning_content / reasoning_details
  3. 把这些字段原样塞回下一轮 chat，网关收不收（chat 侧回填的可行性）
  4. /responses + include:["reasoning.encrypted_content"] 下不下发 encrypted_content
  5. 把第一轮的 output 原样塞回第二轮 responses，能不能过
  6. /responses + summary:"auto" 能不能拿到<b>可读摘要</b>（不开就只有密文）
  7. chat 侧有没有等价的参数能要摘要

第 6、7 步是后补的：OpenAI Responses API 的可读推理摘要在 Responses 侧要显式开
（summary:"auto" 或 ReasoningSummaryVerbosity），不显式请求就只给密文。
之前那轮探测只带了 include、没带 summary，所以「summary 是空数组」不能证明
「模型没有摘要」——可能是压根没要。这两步就是补这个洞。

用法:
    CMD_API_KEY=... CMD_MODEL=<模型id> python3 src/scripts/probe/reasoning/commandcode-reasoning.py
    CMD_API_KEY=... python3 src/scripts/probe/reasoning/commandcode-reasoning.py <模型id>
    CMD_API_KEY=... python3 src/scripts/probe/reasoning/commandcode-reasoning.py <模型id> --only=2,3

只发五六次最小请求；密钥只在本进程的环境变量里，不落盘、不写日志。
"""
import json
import os
import sys
import urllib.error
import urllib.request

BASE = os.environ.get("CMD_BASE", "https://api.commandcode.ai/provider")
# 第一个位置参数是模型名；--only=<步号,步号> 只跑指定的步（其余步骤已量过时不重复消耗配额）
_only = ""
_args = []
for _arg in sys.argv[1:]:
    if _arg.startswith("--only="):
        _only = _arg[len("--only="):]
    else:
        _args.append(_arg)
MODEL = _args[0] if _args else os.environ.get("CMD_MODEL", "")
KEY = os.environ.get("CMD_API_KEY")
PROMPT = "算一下 17*23，先想清楚再回答，最后只给结论。"
FOLLOW_UP = "刚才那个结果再加 1 是多少？"

# 步骤号 → 函数名。--only 里的数字对应脚本头部的说明序号
_STEPS = {
    "1": "step_endpoints",
    "2": "step_chat",
    "2b": "step_chat_stream",
    "3": "step_chat_replay",
    "3b": "step_foreign_replay",
    "3c": "step_replay_cost",
    "4": "step_responses",
    "5": "step_responses_replay",
    "6": "step_responses_summary",
    "7": "step_chat_summary",
}
_ONLY = set(_only.split(",")) if _only else None


def want(step):
    """这个步骤要不要跑"""
    return _ONLY is None or step in _ONLY

# 缺省的 urllib UA 会被 Cloudflare 挡成 error code: 1010（按浏览器指纹拒），写成普通客户端的
HEADERS = {
    "Content-Type": "application/json",
    "Authorization": "Bearer " + (KEY or ""),
    "User-Agent": "uiharumind-probe/1.0",
    "Accept": "application/json",
}

# 可选的出口代理。有些网关会按客户端所在网络判断模型可用性，直连不通时
# 可指定一个出口再试。用法：ALL_PROXY=http://host:port python3 ...
# （Windows PowerShell: $env:ALL_PROXY="http://host:port"）
PROXY = os.environ.get("ALL_PROXY") or os.environ.get("HTTPS_PROXY") or os.environ.get("HTTP_PROXY")
# urllib 的 ProxyHandler 会读环境变量，但 all_proxy（小写）不在它的默认名单里，显式建一个
_OPENER = urllib.request.build_opener(
    urllib.request.ProxyHandler({"http": PROXY, "https": PROXY}) if PROXY else urllib.request.ProxyHandler()
)


def post(path, body):
    request = urllib.request.Request(BASE + path, data=json.dumps(body).encode(), headers=HEADERS)
    try:
        with _OPENER.open(request) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        # 成功响应自己也可能带 "error": null（Responses 就是这样），所以失败用一个专属键标出来
        return {"__http_error": error.read().decode("utf-8", "ignore")[:600], "status": error.code}


def get(path):
    request = urllib.request.Request(BASE + path, headers=HEADERS)
    try:
        with _OPENER.open(request) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        return {"__http_error": error.read().decode("utf-8", "ignore")[:600], "status": error.code}


def failed(result):
    if "__http_error" not in result:
        return False
    print("  请求失败:", result.get("status"), result["__http_error"].strip()[:400])
    return True


def snippet(value, limit=120):
    text = value if isinstance(value, str) else json.dumps(value, ensure_ascii=False)
    return text if len(text) <= limit else text[:limit] + f"…({len(text)} 字符)"


def step_endpoints():
    print(f"== 1. {MODEL} 声明的端点 ==")
    listing = get("/v1/models")
    if failed(listing):
        return True
    items = listing.get("data") if isinstance(listing, dict) else listing
    if not isinstance(items, list):
        print("  目录返回的不是列表，跳过这项检查")
        return True
    for item in items:
        if MODEL in (item.get("id") or ""):
            print("  endpoints:", item.get("supported_endpoints") or "(未声明)")
            return False
    print("  目录里没找到这个 id：检查拼写，或这个账户/区域没有这个模型")
    return True


def step_chat():
    print("\n== 2. /chat/completions 的响应里有什么 ==")
    result = post("/v1/chat/completions",
                  {"model": MODEL, "messages": [{"role": "user", "content": PROMPT}], "stream": False})
    if failed(result):
        body = json.dumps(result)
        if "region" in body:
            print("  网关按客户端所在网络判断了模型可用性。")
            print("  换个出口再试：ALL_PROXY=http://host:port python3 " + sys.argv[0])
        return None, True

    message = ((result.get("choices") or [{}])[0].get("message")) or {}
    extra = [k for k in message if k not in ("role", "content")]
    print("  message 字段:", extra or "(只有 role/content)")
    for field in ("reasoning_content", "reasoning", "reasoning_text", "reasoning_details"):
        if field not in message:
            continue
        value = message[field]
        print(f"  {field}: {len(value)} {'条' if isinstance(value, list) else '字符'}")
        head = value[0] if isinstance(value, list) and value else value
        if isinstance(head, dict):
            print("    首条键:", list(head), "| type:", head.get("type"))
            for key, item in head.items():
                if key == "type":
                    continue
                print(f"    {key}: {snippet(item)}")
        else:
            print("    ", snippet(head))
    usage = result.get("usage") or {}
    print("  reasoning_tokens:", (usage.get("completion_tokens_details") or {}).get("reasoning_tokens"))
    print("  cached_tokens:", (usage.get("prompt_tokens_details") or {}).get("cached_tokens"))
    print("  content:", snippet(message.get("content") or "", 60))
    return {"ticket_fields": {k: message[k] for k in extra if k != "content"}}, False


def step_chat_replay(chat):
    print("\n== 3. 把 chat 的票据原样塞回下一轮（网关收不收） ==")
    if not chat or not chat["ticket_fields"]:
        # 单独跑 --only=3 时第 2 步的产物没有——补跑一次，不然这一步永远是「跳过」
        print("  (第 2 步没跑，先补一次)")
        chat, _ = step_chat()
    if not chat or not chat["ticket_fields"]:
        print("  这个模型没有票据，跳过")
        return True
    assistant = {"role": "assistant", "content": "391"}
    assistant.update(chat["ticket_fields"])
    result = post("/v1/chat/completions", {
        "model": MODEL,
        "messages": [
            {"role": "user", "content": PROMPT},
            assistant,
            {"role": "user", "content": FOLLOW_UP},
        ],
        "stream": False,
    })
    if failed(result):
        print("  → 网关拒收原样票据：chat 侧没有回填这条路")
        return True

    message = ((result.get("choices") or [{}])[0].get("message")) or {}
    usage = result.get("usage") or {}
    print("  → 收下了。回复:", snippet(message.get("content") or "", 60))
    print("  reasoning_tokens:", (usage.get("completion_tokens_details") or {}).get("reasoning_tokens"),
          " cached_tokens:", (usage.get("prompt_tokens_details") or {}).get("cached_tokens"))
    return False


def step_chat_stream():
    print("\n== 2b. 流式 delta 里有没有（应用走的是这条） ==")
    request = urllib.request.Request(
        BASE + "/v1/chat/completions",
        data=json.dumps({"model": MODEL, "messages": [{"role": "user", "content": PROMPT}],
                         "stream": True, "stream_options": {"include_usage": True}}).encode(),
        headers=HEADERS)
    try:
        with _OPENER.open(request) as response:
            frames = 0
            with_details = 0
            shape = None
            for raw in response:
                line = raw.decode("utf-8", "ignore").strip()
                if not line.startswith("data:"):
                    continue
                payload = line[5:].strip()
                if payload == "[DONE]":
                    continue
                frames += 1
                delta = ((json.loads(payload).get("choices") or [{}])[0].get("delta")) or {}
                if "reasoning_details" in delta:
                    with_details += 1
                    shape = shape or delta["reasoning_details"]
    except urllib.error.HTTPError as error:
        print("  请求失败:", error.code, error.read().decode("utf-8", "ignore")[:300])
        return True

    print(f"  data 帧 {frames} 个，其中带 reasoning_details 的 {with_details} 个")
    if shape:
        head = shape[0] if isinstance(shape, list) and shape else shape
        print("  首帧形状:", list(head) if isinstance(head, dict) else type(head).__name__,
              "| type:", head.get("type") if isinstance(head, dict) else None,
              "| data 长度:", len(head.get("data") or "") if isinstance(head, dict) else None)
    else:
        print("  → 流式里没有：要么网关只在非流式下发，要么它走别的事件名")
    return with_details == 0


def step_foreign_replay(chat):
    print("\n== 3b. 把票据发给另一个模型（网关校验不校验） ==")
    if not chat or not chat["ticket_fields"]:
        print("  第 2 步没有票据，跳过")
        return True
    # 换一个模型发同一张票：网关若按模型校验会拒，不校验就会安静地收下
    other = os.environ.get("CMD_OTHER_MODEL", "")
    if not other:
        print("  没给 CMD_OTHER_MODEL（要对比的那个模型），跳过")
        return True
    assistant = {"role": "assistant", "content": "391"}
    assistant.update(chat["ticket_fields"])
    result = post("/v1/chat/completions", {
        "model": other,
        "messages": [{"role": "user", "content": PROMPT}, assistant, {"role": "user", "content": FOLLOW_UP}],
        "stream": False,
    })
    if failed(result):
        print(f"  → 换到 {other} 被拒：网关按模型校验票据，客户端必须做同模型门禁")
        return False
    message = ((result.get("choices") or [{}])[0].get("message")) or {}
    print(f"  → {other} 也收下了（是不是真用上不知道）。回复:", snippet(message.get("content") or "", 40))
    print("     说明网关不校验模型归属，客户端自己不加门禁就会把 A 的票据发给 B")
    return False


def step_replay_cost(chat):
    print("\n== 3c. 回填的代价：同一段对话带/不带票据各发一次 ==")
    if not chat or not chat["ticket_fields"]:
        print("  第 2 步没有票据，跳过")
        return
    plain_assistant = {"role": "assistant", "content": "391"}
    with_ticket = dict(plain_assistant)
    with_ticket.update(chat["ticket_fields"])

    def tokens(assistant):
        result = post("/v1/chat/completions", {
            "model": MODEL,
            "messages": [{"role": "user", "content": PROMPT}, assistant, {"role": "user", "content": FOLLOW_UP}],
            "stream": False,
        })
        if failed(result):
            return None
        return (result.get("usage") or {}).get("prompt_tokens")

    without = tokens(plain_assistant)
    with_it = tokens(with_ticket)
    print("  不带票据 prompt_tokens:", without)
    print("  带票据   prompt_tokens:", with_it)
    if without and with_it:
        print(f"  → 一张票据折合 {with_it - without} 个输入 token（{len(json.dumps(chat['ticket_fields']))} 字符的 JSON）")


def step_responses():
    print("\n== 4. /responses + include:[\"reasoning.encrypted_content\"] ==")
    body = {
        "model": MODEL,
        "input": [{"role": "user", "content": [{"type": "input_text", "text": PROMPT}]}],
        "store": False,
        "include": ["reasoning.encrypted_content"],
    }
    result = post("/v1/responses", body)
    if failed(result):
        return None, True

    items = result.get("output") or []
    print("  output 条目:", [item.get("type") for item in items] or "(空)")
    encrypted = [item for item in items if item.get("encrypted_content")]
    if encrypted:
        print("  带 encrypted_content 的条目:", len(encrypted),
              "，首条长度", len(encrypted[0]["encrypted_content"]))
    else:
        print("  带 encrypted_content 的条目: 0")
    reasoning = [item for item in items if item.get("type") == "reasoning"]
    print("  reasoning 条目:", len(reasoning))
    for item in reasoning:
        print("    键:", list(item), "| summary:", snippet(item.get("summary"), 80))
    usage = result.get("usage") or {}
    print("  output_tokens_details:", usage.get("output_tokens_details"))
    print("  input_tokens_details:", usage.get("input_tokens_details"))
    return result, False


def step_responses_replay(first):
    print("\n== 5. 第一轮的 output 原样塞回第二轮（responses 侧回填） ==")
    items = list(first.get("output") or [])
    if not items:
        print("  第一轮没有 output，跳过")
        return True
    items.append({"role": "user", "content": [{"type": "input_text", "text": FOLLOW_UP}]})
    result = post("/v1/responses", {"model": MODEL, "input": items, "store": False,
                                    "include": ["reasoning.encrypted_content"]})
    if failed(result):
        print("  → 第二轮被拒：票据无法原样回放")
        return True
    print("  → 第二轮通过，output 条目:", [item.get("type") for item in result.get("output") or []])
    usage = result.get("usage") or {}
    print("  input_tokens_details:", usage.get("input_tokens_details"))
    return False


def step_responses_summary():
    print("\n== 6. /responses + summary:\"auto\" —— 显式要可读摘要 ==")
    body = {
        "model": MODEL,
        "input": [{"role": "user", "content": [{"type": "input_text", "text": PROMPT}]}],
        "store": False,
        "include": ["reasoning.encrypted_content"],
        "summary": "auto",
    }
    result = post("/v1/responses", body)
    if failed(result):
        print("  → 被拒。摘要要么不支持，要么参数名不对")
        return None

    reasoning = [item for item in (result.get("output") or []) if item.get("type") == "reasoning"]
    print("  reasoning 条目:", len(reasoning))
    for item in reasoning:
        summary = item.get("summary")
        print("    键:", list(item))
        print("    summary 类型:", type(summary).__name__, "| 内容:", snippet(summary, 120))
        print("    summary 长度:", len(summary) if hasattr(summary, "__len__") else "-")
    usage = result.get("usage") or {}
    details = usage.get("output_tokens_details") or {}
    print("  reasoning_tokens:", details.get("reasoning_tokens"))
    return result


def step_chat_summary():
    print("\n== 7. /chat/completions 显式要摘要（各种参数名试一轮）==")
    # chat 端点没有文档化的 summary 参数，但网关可能透传。各试一种，看哪个不报错且给东西。
    candidates = [
        ("reasoning_summary", "auto"),
        ("reasoning", {"summary": "auto"}),
        ("reasoning_effort", "auto"),
    ]
    for key, value in candidates:
        body = {"model": MODEL, "messages": [{"role": "user", "content": PROMPT}], "stream": False}
        body[key] = value
        result = post("/v1/chat/completions", body)
        if failed(result):
            print(f"  {key}: 被拒")
            continue
        message = ((result.get("choices") or [{}])[0].get("message")) or {}
        details = message.get("reasoning_details")
        summary_text = message.get("reasoning_content") or message.get("reasoning")
        types = [d.get("type") for d in details] if isinstance(details, list) else None
        print(f"  {key}: 通过 | reasoning_details 类型={types} | "
              f"reasoning_content 长度={len(summary_text) if isinstance(summary_text, str) else '-'}")
        if types and any(t and "summary" in t for t in types):
            print("    → 拿到摘要条目！")
            for d in details:
                if d.get("type") and "summary" in d["type"]:
                    print("    摘要内容:", snippet(d.get("summary"), 150))
    return None


def main():
    if not KEY:
        print("需要 CMD_API_KEY 环境变量（Studio → API keys 里那个）", file=sys.stderr)
        return 2
    if not MODEL:
        print("需要指定模型：位置参数或 CMD_MODEL 环境变量", file=sys.stderr)
        return 2

    if _ONLY:
        print(f"只跑步骤: {', '.join(sorted(_ONLY))}（模型 {MODEL}）\n")

    # 不短路：端点查不到也照样量一次 chat，答案在响应里而不在目录里
    failed_endpoints = want("1") and step_endpoints()
    chat, failed_chat = (None, True)
    if want("2"):
        chat, failed_chat = step_chat()
    failed_stream = want("2b") and step_chat_stream()
    failed_replay = want("3") and step_chat_replay(chat)
    want("3b") and step_foreign_replay(chat)
    want("3c") and step_replay_cost(chat)
    first, failed_responses = (None, True)
    if want("4"):
        first, failed_responses = step_responses()
    failed_responses_replay = (step_responses_replay(first)
                                if (want("5") and first is not None) else True)
    want("6") and step_responses_summary()
    want("7") and step_chat_summary()

    print("\n结论怎么看：")
    print("  第 2 步有 reasoning_details / reasoning_content 且第 3 步收下 → chat 侧本来就能回填，")
    print("    缺的是我们适配层把它扔了（MEAI 只认 reasoning_content）。")
    print("  第 4 步有 encrypted_content 且第 5 步通过 → 加密票据在两条路上都有，/responses 不是唯一解。")
    print("  第 2、4 步都为空 → 这个模型在网关的两条路上都不给货，别在客户端继续找办法。")
    print("  第 6、7 步拿到摘要 → 界面上能显示真思考，不用开空卡；拿不到就是模型确实只给密文。")
    return 0 if not any([failed_endpoints, failed_chat, failed_stream, failed_replay,
                         failed_responses, failed_responses_replay]) else 1


if __name__ == "__main__":
    sys.exit(main())
