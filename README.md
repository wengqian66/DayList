# DayList

Windows 上的每日待办。清单不另存数据库，只读写 Obsidian 库里的每日笔记。

今天的任务写在对应笔记的一个小节里，默认是 `## DayList` 下面的 GitHub 任务行：

```markdown
## DayList

- [ ] 买牛奶
- [x] 回邮件
```

小节以外的正文会原样保留。小节里不是任务的段落也会保留。在 Obsidian 里改同一份文件时，DayList 会重新读入；正在输入时不会抢走光标，信息条上可以载入外部版本，关掉提示则会在离开输入框时写回你的修改。

## 下载安装（推荐）

从 [GitHub Releases](https://github.com/wengqian66/DayList/releases/latest) 下载最新的 **DayList-Setup** 安装包，双击安装即可。源码版本是 1.2.0。

- 自带 .NET 8 与 Windows App SDK，**无需再装 SDK 或运行时**
- 默认装到 `Program Files\DayList`，可选桌面快捷方式
- 首次打开后在设置里指定你的 **Obsidian 库文件夹**
- 卸载后本机设置仍保留在 `%AppData%\DayList`（含 API Key，不会进安装包或仓库）

当前版本：1.2.0。已发布的安装包见 [Releases](https://github.com/wengqian66/DayList/releases/latest)。Git 标签 v1.1.5 仍是已撤回的无边框窗口改动，主线没有把它带回来。

## 环境

- Windows 10 版本 1809 或更新，x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

普通用户请直接用上面的安装包。从源码编译时：发布结果可自带 Windows App SDK 运行时；用 `dotnet run` 时 .NET 运行时用本机 SDK 即可。若只双击未自包含的 exe，还需 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) x64。

这个界面是 WinUI 3，只能在 Windows 上编译和运行。读写笔记的逻辑在 `DayList.Core`，可以在别的系统上跑测试。

## 运行

在仓库根目录：

```powershell
dotnet run --project src/DayList.App -c Debug
```

或者先生成再启动：

```powershell
dotnet build src/DayList.App -c Release
src\DayList.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\DayList.exe
```

第一次生成会下载 Windows App SDK，时间会久一些。

## 指向库和每日笔记

1. 打开 DayList，选择 Obsidian 库文件夹。
2. 如果库里启用了核心插件「每日笔记」，点「从库设置读取」。程序会读 `.obsidian/daily-notes.json` 里的 `folder` 和 `format`。
3. 没有这份配置时，默认是库根目录下的 `YYYY-MM-DD.md`，也就是 Obsidian 每日笔记的出厂格式。
4. 笔记放在子目录时，把子文件夹填成 `Daily`，文件名格式填 `yyyy-MM-dd` 或 `YYYY-MM-DD`。格式里也可以直接写路径，例如 `Daily/YYYY/MM/YYYY-MM-DD`。
5. 小节标题默认 `## DayList`。只写 `DayList` 时，按二级标题处理。

`YYYY-MM-DD` 和 `yyyy-MM-dd` 都可以。也支持 `YYYY/MM/DD`、`DD-MM-YYYY` 这种常见写法。新文件用 UTF-8（不带 BOM）；已有文件会保留原来的 BOM 和换行。

标题栏上的文件夹按钮会在资源管理器里打开库根目录。左右箭头和日期按钮用来换天，日期按钮里是日历。

设置存在 `%AppData%\DayList\settings.json`。

## 本月月报

设置里填写一个 OpenAI 兼容接口：

- Base URL，例如 `https://api.openai.com/v1`，或本机的 `http://127.0.0.1:11434/v1`
- API Key
- 模型名，例如 `gpt-4o-mini`

点「本月月报」后，DayList 汇总**当前看到的那个月**里，每篇每日笔记的 DayList 小节，请求 `POST {Base URL}/chat/completions`，把返回的 Markdown 写到库里的 `Reports/yyyy-MM.md`。文件夹和文件名可以在设置里改。

没填接口时，按钮会说明还缺什么，不会发请求。这个月没有 DayList 内容时，也不会发请求。已经有同名月报时，会先问要不要覆盖。

API Key 以明文保存在本机的设置文件里，不会写进笔记。

月报只读每日笔记里的 DayList 小节。旁边的「灵感」小节是随手记，**不会**送进月报，也不会参与「用自然语言生成计划」。

## 灵感

打开「今日」时，任务卡片上方按时间列出当天已经记下的灵感（早上在上，没写时间的排在后面）。没有时显示「还没有灵感」，点「添加」写一句。点某一条，或侧栏的「灵感」，打开同一个窗口来改、删或再加。新的一句仍写进当天笔记的单独小节，默认 `## 灵感`，不是 `- [ ]` 任务。逐步操作见 [使用说明 · 灵感怎么用](使用说明.md#灵感怎么用)。

点卡片上的「时间线」，或侧栏的「灵感流」，可以按时间看这一天和前面 6 天。每一天仍读那篇笔记里的灵感小节，没有第二份记录。

灵感**不进入月报**，也不参与「用自然语言生成计划」，也不会出现在「已完成」。在 Obsidian 里改这一节，DayList 会重新读入。

## 专注

主界面顶部有一块轻量番茄钟：开始、暂停、重置。预设是 25/5、15/3 和 50/10。正在计时的时候换不了预设。到点在屏幕左下角弹出一张没有声音的小卡片，可以开始休息或再来一轮；任务提醒仍在右下角，互不遮挡。

选择的时长记在 `%AppData%\DayList\settings.json`（`focusMinutes`、`breakMinutes`）。进行中的倒计时不写入磁盘，退出就停。它用自己的计时器，不占用提醒轮询，也不参与完成庆祝。

## 提醒

任务行右侧铃铛滚动选择这一天的时间（如 `14:30`）。到点在屏幕右下角弹出小卡片：打开、推迟 10 分钟、改时间或关闭。主窗口留在后台或托盘，只有点「打开」才会把它叫出来。逐步操作见 [使用说明 · 提醒怎么用](使用说明.md#提醒怎么用)。

时间存在 `%AppData%\DayList\reminders.json`，不改 Markdown 任务行。**提醒需要 DayList 正在运行**，最小化到托盘也算。退出或重启之后，没有系统计划任务会代为弹出。再次打开时，12 小时内错过的会补一次。

## 打包安装程序

本机需安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 与 [Inno Setup 6](https://jrsoftware.org/isinfo.php)。在仓库根目录：

```powershell
  powershell -ExecutionPolicy Bypass -File scripts\publish-installer.ps1 -Version 1.2.0
```

完成后得到 `dist\DayList-Setup-1.2.0.exe`（自包含 win-x64）。

## 测试

```powershell
dotnet test tests/DayList.Core.Tests
```

## 不做的事

不是 Obsidian 插件，不走日历插件协议，也不做双向同步。只认文件系统上的 Markdown。
