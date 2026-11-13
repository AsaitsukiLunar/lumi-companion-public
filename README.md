---
版本: 0.3.16-beta.1
生成/整理时间: 2026-11-11 (Asia/Shanghai)
最后更新时间: 2026-11-14 (Asia/Shanghai)
内容覆盖截至: 2026-11-14
来源类型: RAW｜发布需求与角色素材；LOCAL-WORK｜Lumi 源码及本地验证
来源平台 / Project / Agent: Codex Desktop / Lumi Companion / Codex
本次整理节点: Windows / Codex Desktop / Codex
内容原始来源: Lumi 发布源码、ComfyUI 前端估算逻辑与用户试用反馈
---

# Lumi Companion

Lumi 是 Windows x64 桌宠。它可以跟随鼠标视线、显示本机 Agent 的任务气泡和 Codex 用量，并用外框进度显示本地 ComfyUI 的当前节点步数。

## 安装与运行

从 GitHub Releases 下载适合自己的包。首次试用可直接打开单文件 EXE。文件夹版解压后打开其中的 `LumiCompanion.exe`，设置和统计会保存到文件夹里的 `data`。单文件 EXE 使用 `%LOCALAPPDATA%\LumiCompanion`。切换版本前可从托盘退出当前运行的 Lumi。

## 本地集成

- **Codex** 根据当前账号在本机的活动记录显示任务，兼容新旧桌面端的账号存储。用量卡显示额度快照和采样时间。
- **WorkBuddy** 读取状态桥的 `wb-state.json` 和 `wb-events.jsonl`。当且仅当发现一个已配置状态桥的 WorkBuddy 工作区时自动接入。可以将环境变量 `LUMI_WORKBUDDY_BRIDGE_DIR` 指向指定工作区的 `.workbuddy\lumi` 文件夹。WorkBuddy 侧需要有提供状态桥的发布程序。
- **ComfyUI** 默认连接本机 `127.0.0.1:8188`。可以查看队列、历史和输出目录。单个任务运行时，「节点步数」模式会根据近期成功任务的耗时估算剩余时间。若要读取其他客户端提交的节点步数，可将 [可选只读监测节点](tools/comfyui_lumi_monitor) 放进本机 ComfyUI 的 `custom_nodes` 并重启 ComfyUI。

点击或拖动 Lumi 可以确认已结束的回合。仍在执行的任务会继续显示。任务感知和历史保存可在设置里调整。

## 从源码构建

使用 Windows 和 .NET 8 SDK，在仓库根目录执行：

```powershell
dotnet publish .\LumiCompanion.csproj -c Release -r win-x64 --self-contained true -o .\dist\win-x64
```

本测试版公开源码。源码允许非商用使用、修改和再分发，第三方商用需权利人另行许可。[程序许可](LICENSE)

Lumi 角色允许非商用二创，商用权由 AsaitsukiLunar 保留。[素材说明](ASSET-NOTICE.md) · [.NET 许可与第三方声明](licenses)
