## DayList 1.2.17

### 新增：活跃度视图

- 侧栏新增「活跃度」，GitHub 风格完成热图，每天按完成的任务数上色（五档暖色）
- 顶部可切换 **一年 / 半年 / 三月**，区间越短格子越大
- 格子随窗口宽度自适应（窗口拉宽或最大化，格子跟着变大）
- 悬停显示「10月22日 周四 · 完成 3 / 共 5 项」，点击跳到那天并回到今日
- 底部四个统计块：累计完成 / 活跃天数 / 当前连续 / 最长连续
- 统计结果缓存，只有写笔记或外部改动后才重新扫描

### 修复

- 逾期条：同一条任务带入今日后，不再按源日期重复成多行（只保留最近一天的副本）
- 快速添加：打开时可靠聚焦输入框，可直接打字
- 活跃度：左侧「一/三/五」标签曾被行高裁掉笔画并错位一行
- 活跃度：月份标签在放不下时不再右移挤压，改为让位给后一个月份，间距均匀
- 高 DPI：窗口尺寸改为按系统缩放换算（原先在 125%/150% 缩放下窗口内的可用空间会少三分之一，快速添加那行按钮可能放不下）

### 说明

**Since 1.2.15:** new activity heatmap view (year / half / quarter, width-adaptive cells, hover tooltip, click to open that day, four stat blocks); overdue banner no longer repeats one task per source day; quick-add reliably focuses its input; weekday/month label clipping and spacing fixes; window sizing is now DPI-aware so 125%/150% displays look the same as 100%.

**Installer:** `DayList-Setup-1.2.17.exe` (self-contained win-x64)
