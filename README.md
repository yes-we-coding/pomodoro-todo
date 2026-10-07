# 番茄待办 · Pomodoro Todo

一个轻量、离线优先的 Windows 番茄钟 + 待办桌面应用。单文件 HTML 前端 + C# 托盘宿主，打包为免安装单 exe。

[![Release](https://img.shields.io/github/v/release/yes-we-coding/pomodoro-todo)](https://github.com/yes-we-coding/pomodoro-todo/releases/latest)
[![License](https://img.shields.io/github/license/yes-we-coding/pomodoro-todo)](LICENSE)

> 🌐 **想用网页版？** 不用安装，手机/电脑浏览器直接打开：
> **https://yes-we-coding.github.io/pomodoro-todo-web/** （支持安装到主屏幕、离线可用）
> 源码：[pomodoro-todo-web](https://github.com/yes-we-coding/pomodoro-todo-web)

## ⬇️ 下载

直接下载免安装单文件 exe，双击即用（需 Windows 10/11 + Microsoft Edge）：

**[下载最新版 PomodoroTodo.exe](https://github.com/yes-we-coding/pomodoro-todo/releases/latest)**

## ✨ 功能

### 计时
- 专注 / 短休息 / 长休息，可自定义时长
- 自动开始休息 / 专注，长休间隔自定义
- 强提醒（常驻通知 + 响铃），多档提示音（经典三音 / 铃铛 / 木鱼 / 编钟）
- 长休结束使用专属上扬音效
- 白噪音（雨声 / 咖啡馆 / 森林）与音量调节
- 空闲自动暂停：离开电脑自动暂停计时，避免时间虚报
- 全局快捷键：空格 开始/暂停

### 待办
- 分组、优先级、截止日期、子任务、备注
- 标签系统（增删 + 按标签筛选）
- 时间预算：为任务设置计划总时长，实时显示已耗 / 剩余
- 预估番茄数：设定预估，完成后对比实际投入
- 重复任务：每天 / 每周 / 每月自动重置
- 拖拽排序、拖拽到分组
- 撤销删除（`Ctrl+Z`）

### 统计与报告
- 近 7 / 30 天番茄柱状图、连续打卡
- 近一年专注热力图
- 高效时段分布（24 小时 + 星期）
- 今日专注时间轴
- 中断统计（放弃率、按任务明细）
- 预估准确度（计划 vs 实际）
- 日报 / 周报 / 月报
- 🧠 智能总结：自动生成的文字总结，每 10 分钟刷新

### 数据安全
- `data.json` 为权威数据源，清浏览器缓存不丢
- 每日自动备份
- 滚动快照（保留最近 12 份，可一键恢复）
- 导出 / 导入 JSON 备份

### 其它
- Windows Fluent Design 风格、深 / 浅色 / 跟随系统主题
- 迷你模式（小窗置顶）
- 窗口尺寸 / 位置记忆
- 久坐提醒
- 任务到期提醒
- 关闭到托盘，计时不中断

## 🏗️ 技术栈

- **前端**：单文件 `index.html`（原生 JS / CSS，无框架）
- **宿主**：C#（`.NET Framework`，`System.Windows.Forms` 托盘 + 内嵌 `HttpListener`）
- **渲染**：Microsoft Edge 的 `--app` 模式
- **打包**：`csc.exe` 将 HTML/图标作为内嵌资源编译为单 exe

数据流：Edge 窗口 ↔ 本地 HTTP（带 token 鉴权）↔ C# 宿主 ↔ `%LocalAppData%\PomodoroTodo\`

## 🔨 构建

需要 Windows 自带的 .NET Framework 编译器（无需额外安装 SDK）：

```bat
cd build
csc.exe /nologo /target:winexe /win32icon:app.ico ^
  /out:PomodoroTodo.exe /platform:anycpu Program.cs ^
  /resource:../index.html,index.html ^
  /resource:../favicon.ico,favicon.ico ^
  /resource:app.ico,app.ico ^
  /resource:icon256.png,icon256.png
```

`csc.exe` 通常位于 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`。

> 修改 `index.html` 后必须重新编译，才会打入 exe。

## 📁 结构

```
.
├── index.html          # 前端（全部 UI 逻辑）
├── build/
│   ├── Program.cs      # C# 托盘宿主 + 本地 HTTP 服务
│   ├── app.ico         # 应用图标
│   ├── icon*.png       # 多尺寸图标
│   └── make_icon.ps1   # 图标生成脚本
├── favicon.ico
├── tray.ps1 / tray.vbs # 旧版 PowerShell 托盘（历史保留）
└── 启动番茄待办.bat
```

## 💾 数据位置

- 数据：`%LocalAppData%\PomodoroTodo\data.json`
- 会话：`%LocalAppData%\PomodoroTodo\session.json`
- 窗口状态：`%LocalAppData%\PomodoroTodo\window.json`
- 备份：`%UserProfile%\Documents\番茄待办备份\`

## 📄 License

MIT
