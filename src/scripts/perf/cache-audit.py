#!/usr/bin/env python3
"""缓存体检：从真实日志里量「前缀稳不稳」和「命中率高不高」，一次跑完。

用法:
  python3 src/scripts/perf/cache-audit.py                    # 读 ~/.uiharu/Logs
  python3 src/scripts/perf/cache-audit.py --logs /tmp/uiharu-scratch/Logs
  python3 src/scripts/perf/cache-audit.py --model <模型id>    # 只看某个模型（子串匹配）
  python3 src/scripts/perf/cache-audit.py --detail            # 逐发列出 prompt/cached
  python3 src/scripts/perf/cache-audit.py --json before.json  # 存基线，供以后比
  python3 src/scripts/perf/cache-audit.py --compare before.json
  UIHARU_HOME=/tmp/uiharu-scratch python3 src/scripts/perf/cache-audit.py

量两件事，分开看：

1. **前缀稳不稳**（读 Bodies*.txt 里的请求体）。相邻两次请求按消息逐条规范化对比，分类：
   append         前面所有消息逐字节相同，只多出新的（完全重复也算）——前缀还能用
   sessionSwitch  第 0 条（system）就不同 —— 换了会话，不是分叉
   edit           只动了最后一条 —— 用户改消息重发，整段冷一次，属正常
   shrink         前面相同但消息变少 —— 回放/删消息/压缩后重发
   break          中间某条更早的消息变了 —— **真正的前缀杀手**，从那条起全部冷启动
   命中率低但这里全是 append，说明锅不在请求体，在路由。

2. **命中率高不高**（读 Log*.txt 里的用量）。每发取 prompt_tokens / cached_tokens，
   再按「前缀纯追加」算出命中率上限（上一发整段 prompt 都该命中），实际与上限的差距就是白付的钱。
   掉到 ≤113 tokens 的那几发是「换了一台副本」的签名。

两个数据源不必成对：请求体没有时间戳，响应没有正文，各数各的。

依赖：只用标准库。日志里的正文是「字符串里的换行还原成真换行」的展示形态（见 LlmBodyLogFormat），
不能直接当 JSON 解析，这里先按字符串状态机转义回去再解析。

日志轮转：Log.txt / Bodies.txt 是最新的一份，.1 是上一份，序号越大越旧。按「旧 → 新」的顺序读。
"""

import argparse
import glob
import json
import os
import re
import sys
from collections import Counter

HEADER_RE = re.compile(r"(?m)^OpenAI-compatible request \(([\d,]+) bytes\): ")
USAGE_RE = re.compile(r"data: (\{.*\})\s*$")
AFFINITY_RE = re.compile(r"affinity: ([^\s,]+)")
BACKSLASH = chr(92)

# 掉到这个数以下就算「整段冷」：多份日志里反复出现的常数，像是只命中了固定的一小段头部
COLD_THRESHOLD = 113

# 「固定小头部」的判据：值本身很小，且在同一模型里占到这么多发
FIXED_COLD_LIMIT = 500
FIXED_COLD_SHARE = 0.3


# ---------- 日志读取 ----------

def log_files(directory, base):
    """按「旧 → 新」返回 <base>.N.txt ... <base>.txt。序号越大越旧（轮转时新文件是裸名）"""
    found = []
    for path in glob.glob(os.path.join(directory, base + "*.txt")):
        m = re.fullmatch(re.escape(base) + r"(?:\.(\d+))?\.txt", os.path.basename(path))
        if not m:
            continue
        found.append((int(m.group(1)) if m.group(1) else 0, path))
    found.sort(key=lambda x: -x[0])
    return [path for _, path in found]


def read_text(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def repair(txt):
    """把「字符串内部被还原成真换行」的展示形态转回合法 JSON：只在引号内转义控制字符。

    反斜杠与转义引号在日志里是原样保留的（LlmBodyLogFormat 解码后重新转义过），
    所以只要跟踪「是否在字符串内」就能区分结构换行与内容换行。
    """
    out = []
    in_str = False
    esc = False
    for ch in txt:
        if in_str:
            if esc:
                out.append(ch)
                esc = False
            elif ch == BACKSLASH:
                out.append(ch)
                esc = True
            elif ch == '"':
                out.append(ch)
                in_str = False
            elif ch == "\n":
                out.append(BACKSLASH + "n")
            elif ch == "\r":
                out.append(BACKSLASH + "r")
            elif ch == "\t":
                out.append(BACKSLASH + "t")
            else:
                out.append(ch)
        else:
            if ch == '"':
                in_str = True
            out.append(ch)
    return "".join(out)


def parse_bodies(paths, model_filter):
    """从 Bodies*.txt 里切出请求体。返回 (bodies, stats)

    切法：先把整份文件转回合法 JSON 形态，再按列首的 header 行切——
    转义之后，正文里若回显了别人的请求体，那些换行已经变成 \\n，不会再出现在列首。
    """
    bodies = []
    stats = {"files": 0, "blocks": 0, "unparsed": 0, "interleaved": 0}
    for path in paths:
        raw = read_text(path)
        if model_filter and model_filter not in raw:
            continue  # 预筛：整份文件里没有目标模型就一个字符都不修
        stats["files"] += 1
        fixed = repair(raw)
        marks = list(HEADER_RE.finditer(fixed))
        for i, m in enumerate(marks):
            end = marks[i + 1].start() if i + 1 < len(marks) else len(fixed)
            block = fixed[m.end():end].strip()
            stats["blocks"] += 1
            try:
                obj, consumed = json.JSONDecoder().raw_decode(block)
            except json.JSONDecodeError:
                stats["unparsed"] += 1
                continue
            if not isinstance(obj, dict) or "messages" not in obj or "model" not in obj:
                stats["unparsed"] += 1
                continue
            if consumed != len(block):
                stats["interleaved"] += 1  # 正文后面插了别的日志行（框架诊断），正文本身是完整的
            if model_filter and model_filter not in obj["model"]:
                continue
            bodies.append({
                "file": os.path.basename(path),
                "model": obj["model"],
                "messages": obj["messages"],
                "tools": obj.get("tools") or [],
                "bytes": int(m.group(1).replace(",", "")),
            })
    return bodies, stats


def parse_usage(paths, model_filter):
    """从 Log*.txt 里取每发的用量。只认 SSE 收尾那一行（带 usage 的那一帧）

    顺带把这一发带出去的亲和值抄下来（响应日志那行末尾的 <c>affinity:</c>）。
    三种取值：<c>(off)</c>=这家供应商没声明亲和头、一个都没发；<c>(none)</c>=声明了但本次没会话；
    其余是真正写到网线上的哈希值。分组时前两者要能一眼认出来——它们的修法完全不同。
    只在串行请求时可靠：并发的两个请求在同一个文件里交错时，后一条响应行会把前一条的
    亲和值顶掉——那时宁可看到「(未记录)」，也别拿错值下结论。
    """
    rows = []
    for path in paths:
        affinity = None
        for lineno, line in enumerate(read_text(path).splitlines(), 1):
            if "OpenAI-compatible response:" in line:
                m = AFFINITY_RE.search(line)
                affinity = m.group(1) if m else None
                continue
            if '"usage"' not in line or "data: " not in line:
                continue
            m = USAGE_RE.search(line)
            if not m:
                continue
            try:
                obj = json.loads(m.group(1))
            except json.JSONDecodeError:
                continue
            usage = obj.get("usage") or {}
            if "prompt_tokens" not in usage:
                continue
            model = obj.get("model") or ""
            if model_filter and model_filter not in model:
                continue
            details = usage.get("prompt_tokens_details") or {}
            rows.append({
                "model": model,
                "created": obj.get("created"),
                "prompt": usage.get("prompt_tokens") or 0,
                "cached": details.get("cached_tokens") or 0,
                "affinity": affinity,
                "where": "%s:%d" % (os.path.basename(path), lineno),
            })
    return rows


# ---------- 分析 ----------

def canon(obj):
    return json.dumps(obj, ensure_ascii=False, sort_keys=False, separators=(",", ":"))


def describe(a, b):
    """一句话说清这一条为什么不同——定位用，不追求穷尽"""
    if a.get("role") != b.get("role"):
        return "role %s → %s" % (a.get("role"), b.get("role"))
    ca, cb = a.get("content"), b.get("content")
    if isinstance(ca, str) and isinstance(cb, str):
        # 同一份内容，一边被 JSON 序列化过一次（外层多一层引号与转义）
        if ca == json.dumps(cb, ensure_ascii=False) or cb == json.dumps(ca, ensure_ascii=False):
            return "同一份内容多/少了一层 JSON 引号"
        return "content %d → %d 字符" % (len(ca), len(cb))
    keys_a = sorted(a.keys())
    keys_b = sorted(b.keys())
    if keys_a != keys_b:
        return "字段变了: %s → %s" % (",".join(keys_a), ",".join(keys_b))
    return "内容变了"


def analyze_prefix(bodies):
    """相邻两份逐条对比并分类，见文件头"""
    pairs = []
    for i in range(len(bodies) - 1):
        a, b = bodies[i], bodies[i + 1]
        ma, mb = a["messages"], b["messages"]
        n = min(len(ma), len(mb))
        first = None
        for k in range(n):
            if canon(ma[k]) != canon(mb[k]):
                first = k
                break
        if first is None and len(ma) != len(mb):
            first = n

        if first is None:
            kind = "append"
        elif first == 0:
            kind = "sessionSwitch"
        elif first == n and len(mb) > len(ma):
            kind = "append"
        elif first == n:
            kind = "shrink"
        elif first == n - 1 and len(ma) == first + 1:
            kind = "edit"
        else:
            kind = "break"

        pair = {
            "from": i, "to": i + 1, "kind": kind, "firstDiff": first,
            "messages": (len(ma), len(mb)), "file": b["file"],
            "toolsSame": canon(a["tools"]) == canon(b["tools"]),
        }
        if kind == "break":
            pair["why"] = describe(ma[first], mb[first])
            pair["role"] = ma[first].get("role")
            pair["lostMessages"] = n - first
        pairs.append(pair)
    return pairs


def summarize_usage(rows):
    """命中率与上限。上限 = 相邻两发里较短的那份 prompt（前缀纯追加时的理论值）。

    prompt 变短（换会话、压缩）的那一对不计入上限，单独记一笔——那种情况本来就该整段冷。
    """
    prompt = sum(r["prompt"] for r in rows)
    cached = sum(r["cached"] for r in rows)
    ceiling = 0
    skipped = 0
    for prev, cur in zip(rows, rows[1:]):
        if cur["prompt"] >= prev["prompt"]:
            ceiling += prev["prompt"]
        else:
            skipped += 1
    return {
        "requests": len(rows),
        "promptTokens": prompt,
        "cachedTokens": cached,
        "hitRate": (cached / prompt) if prompt else 0.0,
        "ceilingTokens": ceiling,
        "ceilingRate": (ceiling / prompt) if prompt else 0.0,
        "cold": sum(1 for r in rows if r["cached"] <= COLD_THRESHOLD),
        "zero": sum(1 for r in rows if r["cached"] == 0),
        "skippedPairs": skipped,
        "fixedCold": fixed_cold(rows),
    }


def fixed_cold(rows):
    """找出「这个模型每发都报同一个极小的 cached」那种情况。

    实测口径：某个闭源模型的 36 发里有 13 发恒为 113，
    而另一批模型冷的时候一律报 0、从不出现 113。同一个网关、同一个客户端、同一套头，
    差别只在模型——所以那种常数不是「命中了一小段前缀」，是上游自己的固定小头部，
    等于<b>完全没命中</b>。看命中率时把它当命中会高估这个模型。
    """
    small = [r["cached"] for r in rows if 0 < r["cached"] < FIXED_COLD_LIMIT]
    if not rows or not small:
        return None
    value, count = Counter(small).most_common(1)[0]
    if count / len(rows) < FIXED_COLD_SHARE:
        return None
    return {"value": value, "count": count, "share": count / len(rows)}


def merge_usage(parts):
    """把各模型的汇总加成总计：比率必须由总量重算，不能把比率相加"""
    merged = {k: 0 for k in ("requests", "promptTokens", "cachedTokens", "ceilingTokens", "cold",
                             "zero", "skippedPairs")}
    for part in parts:
        for k in merged:
            merged[k] += part[k]
    merged["hitRate"] = (merged["cachedTokens"] / merged["promptTokens"]) if merged["promptTokens"] else 0.0
    merged["ceilingRate"] = (merged["ceilingTokens"] / merged["promptTokens"]) if merged["promptTokens"] else 0.0
    return merged


# ---------- 输出 ----------

def pct(x):
    return "%.1f%%" % (100.0 * x)


def report(logs_dir, model_filter, detail):
    body_paths = log_files(logs_dir, "Bodies")
    log_paths = log_files(logs_dir, "Log")
    if not body_paths and not log_paths:
        print("没找到日志：%s（Bodies*.txt / Log*.txt）" % logs_dir, file=sys.stderr)
        return None, 2

    bodies, body_stats = parse_bodies(body_paths, model_filter)
    rows = parse_usage(log_paths, model_filter)

    print("日志目录: %s" % logs_dir)
    print("请求体: %d 份（读了 %d 个文件，切出 %d 块，解析失败 %d 块，被其他日志行打断 %d 块）" % (
        len(bodies), body_stats["files"], body_stats["blocks"], body_stats["unparsed"],
        body_stats["interleaved"]))
    print("响应用量: %d 发" % len(rows))
    print()

    summary = {"logs": logs_dir, "models": {}}
    for name in sorted({b["model"] for b in bodies} | {r["model"] for r in rows}):
        mine = [b for b in bodies if b["model"] == name]
        pairs = analyze_prefix(mine)
        usage_rows = [r for r in rows if r["model"] == name]
        usage = summarize_usage(usage_rows)
        counts = {}
        for p in pairs:
            counts[p["kind"]] = counts.get(p["kind"], 0) + 1

        print("== %s ==" % name)
        if mine:
            msgs = [len(b["messages"]) for b in mine]
            tools = max((len(b["tools"]) for b in mine), default=0)
            print("  请求体 %d 份，消息 %d~%d 条，工具 %d 个" % (
                len(mine), min(msgs), max(msgs), tools))
            print("  前缀: 纯追加 %d / 换会话 %d / 改尾巴 %d / 变短 %d / **真实分叉 %d**" % (
                counts.get("append", 0), counts.get("sessionSwitch", 0), counts.get("edit", 0),
                counts.get("shrink", 0), counts.get("break", 0)))
            for p in pairs:
                if p["kind"] != "break":
                    continue
                print("    #%d→#%d %s：第 %d 条 %s 起不同（%s），该条起 %d 条消息不再复用%s" % (
                    p["from"], p["to"], p["file"], p["firstDiff"], p["role"], p["why"],
                    p["lostMessages"], "" if p["toolsSame"] else "；工具定义也变了"))
            if counts.get("break", 0) == 0:
                print("    → 前缀没有被打断：问题不在请求体，看下面的命中率与「固定小头部」")
        else:
            print("  请求体: 0 份（这个模型只留下了响应）")

        if usage_rows:
            print("  命中: %d 发，prompt %d，cached %d，**%s**；上限 %s（差 %s，%d tokens 白付）" % (
                usage["requests"], usage["promptTokens"], usage["cachedTokens"],
                pct(usage["hitRate"]), pct(usage["ceilingRate"]),
                pct(usage["ceilingRate"] - usage["hitRate"]),
                usage["ceilingTokens"] - usage["cachedTokens"]))
            print("  掉底: ≤%d tokens %d 发，其中 0 命中 %d 发%s" % (
                COLD_THRESHOLD, usage["cold"], usage["zero"],
                "" if not usage["skippedPairs"] else "；%d 对 prompt 变短未计入上限" % usage["skippedPairs"]))
            fixed = usage.get("fixedCold")
            if fixed:
                print("  注意: %d 发（%s）的 cached 恒为 %d —— 那是上游自己的固定小头部，不是你的前缀，"
                      "这几发等于没命中；算进命中率会高估" % (
                          fixed["count"], pct(fixed["share"]), fixed["value"]))
            if detail:
                for i, r in enumerate(usage_rows):
                    print("    %2d %8d %8d  %6s  %-8s %s" % (
                        i, r["prompt"], r["cached"],
                        pct(r["cached"] / r["prompt"] if r["prompt"] else 0),
                        (r.get("affinity") or "-")[:8], r["where"]))
        else:
            print("  命中: 没有带用量的响应（服务端不报 usage 时量不到）")

        # 按亲和值分组：一组说明每发都带着同一个值（那么命中率低就只能是网关不认），
        # 多组则先看是不是混了几个会话——不然会把「样本里混了别的会话」当成缓存坏了
        groups = {}
        for r in usage_rows:
            key = r.get("affinity") or "(未记录)"
            g = groups.setdefault(key, [0, 0, 0])
            g[0] += 1
            g[1] += r["prompt"]
            g[2] += r["cached"]
        if groups:
            print("  亲和值: %d 组" % len(groups))
            for key, (n, prompt, cached) in sorted(groups.items(), key=lambda kv: -kv[1][0]):
                print("    %-40s %3d 发  %s" % (key[:40], n, pct(cached / prompt if prompt else 0)))
        print()
        summary["models"][name] = {
            "bodies": len(mine),
            "prefix": {"pairs": len(pairs), **{k: counts.get(k, 0) for k in
                                               ("append", "sessionSwitch", "edit", "shrink", "break")}},
            "breaks": [{"file": p["file"], "firstDiff": p["firstDiff"], "role": p["role"],
                        "why": p["why"]} for p in pairs if p["kind"] == "break"],
            "usage": usage,
            "affinities": {key: {"requests": n, "promptTokens": p, "cachedTokens": c}
                           for key, (n, p, c) in groups.items()},
        }

    total = merge_usage([m["usage"] for m in summary["models"].values()])
    summary["totals"] = total
    print("合计: %d 发，prompt %d，cached %d，%s（上限 %s）" % (
        total["requests"], total["promptTokens"], total["cachedTokens"],
        pct(total["hitRate"]), pct(total["ceilingRate"])))
    return summary, 0


def compare(baseline_path, summary):
    try:
        with open(baseline_path, encoding="utf-8") as f:
            base = json.load(f)
    except (OSError, json.JSONDecodeError) as e:
        print("基线读不了：%s" % e, file=sys.stderr)
        return
    print()
    print("== 与基线对比（%s）==" % baseline_path)
    print("  %-42s %10s %10s %10s" % ("模型", "基线命中", "本次命中", "变化"))
    for name in sorted(set(base.get("models", {})) | set(summary["models"])):
        old = (base.get("models", {}).get(name) or {}).get("usage") or {}
        new = (summary["models"].get(name) or {}).get("usage") or {}
        if not old or not new:
            continue
        print("  %-42s %10s %10s %+9.1fpp" % (
            name[:42], pct(old["hitRate"]), pct(new["hitRate"]),
            100.0 * (new["hitRate"] - old["hitRate"])))
        print("  %-42s %10d %10d" % ("  掉底发数", old.get("cold", 0), new.get("cold", 0)))
    old_t, new_t = base.get("totals") or {}, summary["totals"]
    if old_t and new_t:
        print("  %-42s %10s %10s %+9.1fpp" % ("合计", pct(old_t["hitRate"]), pct(new_t["hitRate"]),
                                             100.0 * (new_t["hitRate"] - old_t["hitRate"])))


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    parser = argparse.ArgumentParser(description="从日志里量前缀稳定性与缓存命中率")
    parser.add_argument("--logs", help="日志目录；默认 $UIHARU_HOME/Logs 或 ~/.uiharu/Logs")
    parser.add_argument("--model", default="", help="只统计模型名含该子串的请求")
    parser.add_argument("--detail", action="store_true", help="逐发列出 prompt/cached")
    parser.add_argument("--json", metavar="PATH", help="把汇总写成 JSON，供 --compare 用")
    parser.add_argument("--compare", metavar="PATH", help="与之前存下的 JSON 比命中率")
    args = parser.parse_args()

    logs_dir = args.logs
    if not logs_dir:
        home = os.environ.get("UIHARU_HOME") or os.path.join(os.path.expanduser("~"), ".uiharu")
        logs_dir = os.path.join(home, "Logs")

    summary, code = report(logs_dir, args.model, args.detail)
    if summary is None:
        return code
    if args.json:
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump(summary, f, ensure_ascii=False, indent=2)
        print("已写入 %s" % args.json)
    if args.compare:
        compare(args.compare, summary)
    return 0


if __name__ == "__main__":
    sys.exit(main())
