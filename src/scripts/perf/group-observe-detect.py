#!/usr/bin/env python3
"""群聊一轮观察的异常启发式检测：解析群聊 dump，判断是否有复读/空转/暴涨信号。

用法: group-observe-detect.py <dump.md> <delta_posts> <reply_delta> <http429_delta> <prev_texts_file>
输出一行 JSON: {"anomaly": "none|repetition|surge|stall", "detail": "..."}

判定口径（保守，宁漏勿误）：
- surge    本轮发言增量 >= 40（异常量级，正常讨论 30 分钟通常 10~25 条）
- repetition 新增发言（最多取 15 条）中，与更早发言文本相似度 >= 0.80 的占比 > 0.5；
             或新增发言两两高度相似（复读/绕圈）。
- stall    本轮发言增量为 0，但日志区间仍有模型请求（reply_delta>0 或 http429_delta>0）→ 群在空转

只检测与记录，不做介入（要不要发消息/停止，由观察者人工判断）。
从上一会话草稿目录的 detect.py 平移（2026-10-13，收编进 src/scripts/perf）。
"""

import difflib
import json
import re
import sys


def extract_blocks(path):
    """按 '### ' 标题切发言块，返回 [(name, text)]。"""
    blocks = []
    cur_name = None
    cur_lines = []
    with open(path, encoding="utf-8") as f:
        for raw in f:
            line = raw.rstrip("\n")
            if line.startswith("### "):
                if cur_name is not None:
                    blocks.append((cur_name, "\n".join(cur_lines)))
                cur_name = line[4:].strip()
                cur_lines = []
            else:
                cur_lines.append(line)
    if cur_name is not None:
        blocks.append((cur_name, "\n".join(cur_lines)))
    return blocks


def norm(s):
    s = re.sub(r"\s+", "", s)
    s = re.sub(r"[，。！？、；：,.!?;:\"'“”‘’（）()]", "", s)
    return s


def ratio(a, b):
    if not a or not b:
        return 0.0
    return difflib.SequenceMatcher(None, a, b, autojunk=False).ratio()


def main():
    dump_path, delta_s, reply_delta_s, http429_s, prev_file = sys.argv[1:6]
    delta = int(delta_s)
    reply_delta = int(reply_delta_s)
    http429 = int(http429_s)

    blocks = extract_blocks(dump_path)
    total = len(blocks)
    # 发言内容取文本前 400 字
    texts = [norm(t[:400]) for _, t in blocks]

    anomaly = "none"
    detail = ""

    if delta >= 40:
        anomaly = "surge"
        detail = f"本轮新增发言 {delta} 条（>=40），量大异常"
    elif delta == 0 and (reply_delta > 0 or http429 > 0):
        anomaly = "stall"
        detail = f"本轮发言 0 增量，但日志区间有 {reply_delta} 次回复结束、{http429} 次 429 —— 群疑似空转"
    elif delta > 0:
        # 新增发言 = 最后 delta 条（上限 15）
        n = min(delta, 15)
        new_texts = texts[-n:] if n > 0 else []
        if len(new_texts) >= 2:
            # 与更早发言的重复度
            older = texts[:-n]
            dup_vs_older = 0
            for t in new_texts:
                if older and max((ratio(t, o) for o in older[-30:]), default=0.0) >= 0.80:
                    dup_vs_older += 1
            # 新增发言两两重复度
            dup_within = 0
            pairs = 0
            for i in range(len(new_texts)):
                for j in range(i + 1, len(new_texts)):
                    pairs += 1
                    if ratio(new_texts[i], new_texts[j]) >= 0.80:
                        dup_within += 1
            older_ratio = dup_vs_older / len(new_texts)
            within_ratio = dup_within / pairs if pairs else 0.0
            if older_ratio > 0.5 or within_ratio > 0.5:
                anomaly = "repetition"
                detail = (f"本轮新增 {delta} 条：与历史相似(>=0.8)占比 {older_ratio:.0%}，"
                          f"彼此相似占比 {within_ratio:.0%} —— 疑似复读/绕圈")

    print(json.dumps({"anomaly": anomaly, "detail": detail, "total_posts": total}, ensure_ascii=False))


if __name__ == "__main__":
    main()