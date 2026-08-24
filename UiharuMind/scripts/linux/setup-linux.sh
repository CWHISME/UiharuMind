#!/usr/bin/env bash
#
# UiharuMind Linux 一次性环境配置。
#
# Wayland 不允许应用监听全局按键或注入输入，唯一不依赖显示服务器的途径是内核的
# evdev/uinput。它们分别要求：能读 /dev/input/event*（input 组），能写 /dev/uinput
# （uinput 组 + udev 规则）。本脚本把这两件事一次配好。
#
# 注意：加入 input 组等于获得读取全部键盘输入的能力（keylogger 级权限）。
# 这是 Wayland 下实现全局快捷键的既定代价，请确认后再执行。
#
# 执行后必须重新登录（注销再登录，或重启）才会生效。

set -euo pipefail

RULES_NAME="99-uiharumind-uinput.rules"
RULES_SOURCE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/${RULES_NAME}"
RULES_TARGET="/etc/udev/rules.d/${RULES_NAME}"
TARGET_USER="${SUDO_USER:-$USER}"

if [[ "$(uname -s)" != "Linux" ]]; then
    echo "本脚本仅适用于 Linux。" >&2
    exit 1
fi

echo "==> 目标用户：${TARGET_USER}"

echo "==> 将 ${TARGET_USER} 加入 input 组（全局快捷键、鼠标事件监听）"
sudo usermod -aG input "${TARGET_USER}"

echo "==> 创建 uinput 组并加入（自动点击回放所需的输入模拟）"
sudo groupadd -f uinput
sudo usermod -aG uinput "${TARGET_USER}"

echo "==> 安装 udev 规则：${RULES_TARGET}"
sudo install -m 0644 "${RULES_SOURCE}" "${RULES_TARGET}"

echo "==> 重载 udev 规则"
sudo udevadm control --reload-rules
sudo udevadm trigger

# uinput 模块未加载时 /dev/uinput 不存在，规则也就无从生效
echo "==> 确保 uinput 内核模块开机加载"
sudo modprobe uinput || true
echo uinput | sudo tee /etc/modules-load.d/uiharumind-uinput.conf > /dev/null

echo
echo "==> 检查 xdg-desktop-portal（Wayland 下截图的唯一通道）"
if command -v gdbus > /dev/null && \
   gdbus call --session --dest org.freedesktop.DBus \
        --object-path /org/freedesktop/DBus \
        --method org.freedesktop.DBus.NameHasOwner \
        org.freedesktop.portal.Desktop 2>/dev/null | grep -q true; then
    echo "    已就绪。"
else
    echo "    未检测到 portal 后端，请安装："
    echo "    sudo apt install xdg-desktop-portal xdg-desktop-portal-gnome"
fi

echo
echo "完成。请注销并重新登录后再启动 UiharuMind，"
echo "然后在「权限引导」界面确认三项均为已授权。"
