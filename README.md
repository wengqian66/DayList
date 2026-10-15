# DayList

Windows 上的每日待办。清单不另存数据库，只读写 Obsidian 库里的每日笔记。

今天的任务写在对应笔记的一个小节里，默认是 `## DayList` 下面的 GitHub 任务行：

```markdown
## DayList

- [ ] 买牛奶
- [x] 回邮件
```

小节以外的正文会原样保留。小节里不是任务的段落也会保留。在 Obsidian 里改同一份文件时，DayList 会重新读入；正在输入时不会抢走光标，信息条上可以载入外部版本，关掉提示则会在离开输入框时写回你的修改。

## 环境

- Windows 10 版本 1809 或更新，x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

不需要安装包。生成结果会带上 Windows App SDK 运行时，所以不用再单独装一份。用 `dotnet run` 时，.NET 运行时用本机 SDK 里的即可。如果要直接双击 exe，还需要安装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) x64。

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

## 测试

```powershell
dotnet test tests/DayList.Core.Tests
```

## 不做的事

不是 Obsidian 插件，不走日历插件协议，也不做双向同步。只认文件系统上的 Markdown。
