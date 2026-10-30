#!/usr/bin/env python3
"""验证票据挂法对前缀缓存的影响：用真实日志的请求体对比两种拼法。

背景（2026-10-30）
------------------
上一版票据回填把票存在单个槽位里、只挂**最后一条**助手消息、且**用一次即消费**。
于是每轮票都往新位置跑：上一轮挂过票的那条消息在下一轮变成无票形态，
那条的字节变了 → **前缀分叉**，从那条起缓存全冷。
真实档案实测：纯追加请求平均命中 88.2%，真实分叉的只有 35.3%。

ADR 0100 的新做法：每条助手消息**永久挂自己那张票**，逐轮原样重发。
票的位置不漂移 → 历史部分逐字节相同 → 前缀纯追加。

这个脚本拿真实日志里带票的连续几发，分别按两种拼法重放，比对相邻两发的前缀：
  旧做法 → 期望看到分叉（首差落在上一轮挂票的位置）
  新做法 → 期望纯追加（无首差）

用法:
    python3 src/scripts/perf/verify-ticket-prefix.py
    python3 src/scripts/perf/verify-ticket-prefix.py --model <模型id片段>
"""
import argparse
import importlib.util
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def load_audit():
    spec = importlib.util.spec_from_file_location("ca", os.path.join(HERE, "cache-audit.py"))
    ca = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(ca)
    return ca


def canon(messages):
    return json.dumps(messages, ensure_ascii=False, sort_keys=False, separators=(",", ":"))


def first_difference(a, b):
    """相邻两发的首差下标；没有差异返回 None"""
    n = min(len(a), len(b))
    for k in range(n):
        if canon(a[k]) != canon(b[k]):
            return k
    return None


def replay_legacy(segments):
    """旧做法：只挂最后一条、用一次即消费。历史里之前的票都没了。"""
    rounds = []
    for segment in segments:
        current = [dict(m) for m in segment["messages"]]
        if rounds:
            previous = rounds[-1]
            for j, message in enumerate(current):
                if j < len(previous):
                    message.pop("reasoning_details", None) # 票被消费掉了
        rounds.append(current)
    return rounds


def replay_pinned(segments):
    """新做法：每条挂自己那张，位置不漂移。"""
    rounds = []
    previous = None
    for segment in segments:
        current = [dict(m) for m in segment["messages"]]
        if previous is not None:
            for j, message in enumerate(current):
                if j >= len(previous):
                    continue
                old = previous[j]
                if old.get("reasoning_details") and not message.get("reasoning_details"):
                    message["reasoning_details"] = old["reasoning_details"] # 按位钉住
        rounds.append(current)
        previous = current
    return rounds


def report(name, rounds):
    print(f"=== {name} ===")
    broke = 0
    for i in range(len(rounds) - 1):
        first = first_difference(rounds[i], rounds[i + 1])
        if first is None:
            print(f"  第{i + 1}轮 -> 第{i + 2}轮：纯追加（无首差）")
        else:
            broke += 1
            print(f"  第{i + 1}轮 -> 第{i + 2}轮：首差下标 {first}  <== 分叉")
    print(f"  分叉数：{broke}")
    return broke


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--logs", default=os.path.expanduser("~/.uiharu/Logs"))
    parser.add_argument("--model", default="", help="只取含这个串的模型")
    parser.add_argument("--rounds", type=int, default=3, help="取几发连续请求")
    args = parser.parse_args()

    ca = load_audit()
    bodies, _ = ca.parse_bodies(ca.log_files(args.logs, "Bodies"), None)
    if args.model:
        bodies = [b for b in bodies if args.model in b["model"]]
    bodies = [b for b in bodies if any(m.get("reasoning_details") for m in b["messages"])]
    if len(bodies) < args.rounds:
        print(f"带票的请求体只有 {len(bodies)} 份，不够 {args.rounds} 发", file=sys.stderr)
        return 2

    segments = bodies[-args.rounds:]
    print(f"真实日志带票的连续 {len(segments)} 发（model {segments[0]['model']}）：")
    for i, segment in enumerate(segments):
        for j, message in enumerate(segment["messages"]):
            details = message.get("reasoning_details")
            if details:
                print(f"  #{i} 消息{len(segment['messages'])} 票在下标{j} id={details[0].get('id')}")
    print()

    legacy_broke = report("旧做法：只挂最后一条、用一次即消费（已还原的那版）", replay_legacy(segments))
    print()
    pinned_broke = report("新做法：每条挂自己那张、逐轮原样重发（ADR 0100）", replay_pinned(segments))
    print()

    ok = legacy_broke > 0 and pinned_broke == 0
    print(f"结论：{'符合预期' if ok else '不符合预期'}"
          f"（旧做法分叉 {legacy_broke} 处，新做法 {pinned_broke} 处）")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
