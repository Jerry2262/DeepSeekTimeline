# DeepSeekTimeline

一个轻量的 Windows 桌面浮窗，用 24 小时时间线显示 DeepSeek API 的峰谷时段、距离下次切换的时间，以及 Flash / Pro 当前价格。

![DeepSeekTimeline 实际运行界面](<docs/screenshot.png>)

## 功能

- 常驻置顶、半透明显示，不占用任务栏和 Alt-Tab 列表。
- 拖到屏幕顶部后自动吸附，鼠标靠近展开、离开收起；前台全屏时不触发展开。
- 自动计算峰谷切换，支持周末与节假日，并保存窗口位置和吸附状态。
- 离线运行，无需 API Key，不调用 DeepSeek API；单文件 C# / WinForms 实现，无第三方依赖。

## 编译与使用

需要 Windows 10 / 11 和 .NET Framework 4.x 的 C# 编译器。脚本使用系统目录内的 `csc.exe`，无需额外安装 .NET SDK 或 Visual Studio。

1. 下载或克隆本仓库：

   ```sh
   git clone https://github.com/Jerry2262/DeepSeekTimeline.git
   ```

2. 双击 [build.bat](<build.bat>) 编译。
3. 运行生成的 `DeepSeekTimeline.exe`。

当前仓库尚未提供预编译 Release。编译后的程序可以单独复制使用，无需安装。

**操作：**按住窗口拖动；拖到屏幕顶部吸附，展开后向下拖动可脱离吸附；点击右上角 × 退出。窗口位置保存在 `%APPDATA%\DeepSeekTimeline.ini`。

**时间：**程序使用 Windows 本地时间计算，系统时区需设为北京时间（UTC+8）。

## 计费规则

截至 **2026-10-22**，按 [DeepSeek 官方定价页](https://api-docs.deepseek.com/zh-cn/quick_start/pricing/)：

- **峰时：**北京时间周一至周五（不含中国法定节假日）09:00–12:00、14:00–18:00。
- **谷时：**其余时间，包括周末和中国法定节假日全天。调休上班的周末也按谷时计算。
- 谷时价格为峰时的一半，时段起点包含、终点不包含。

价格单位为 **人民币元 / 百万 tokens**，其中“输入”指缓存未命中输入，“缓存”指缓存命中输入：

| 模型 | 时段 | 输入 | 缓存 | 输出 |
| --- | --- | ---: | ---: | ---: |
| Flash | 谷时 | 1 | 0.02 | 4 |
| Flash | 峰时 | 2 | 0.04 | 8 |
| Pro | 谷时 | 4.5 | 0.15 | 13.5 |
| Pro | 峰时 | 9 | 0.30 | 27 |

这是个人开发的辅助工具，实际计费以 DeepSeek 官方信息为准。窗口只展示模型 token 价格，不包含 Search / Fetch 等独立服务费用。

## 维护与测试

所有业务配置集中在 [Main.cs](<Main.cs>) 顶部的配置区：

- `PeakRanges`：峰时时段。
- `PriceFlash*` / `PricePro*`：Flash、Pro 的峰谷价格。
- `Holidays`：节假日中落在周一至周五的日期；周末无需重复填写。

**当前节假日表仅覆盖 2026 年**，依据[国务院办公厅关于 2026 年部分节假日安排的通知](https://www.gov.cn/zhengce/zhengceku/202511/content_7047091.htm)。后续年度需按官方放假安排补充日期，否则当年的节假日工作日会按普通工作日计算。价格和日历不会联网自动更新，修改后需重新编译。

在 Windows 桌面会话中运行置顶回归测试：

```bat
tests\run.bat
```

测试覆盖原生取消置顶后的恢复、焦点保持、窗口句柄重建、吸附收起与展开等行为。运行时会短暂显示测试窗口，不改写用户的位置配置；失败时返回非零退出码。测试源码见 [TopMostRegression.cs](<tests/TopMostRegression.cs>)。

在 WSL 中可通过 Windows 互操作运行同一套编译和测试脚本，例如在仓库目录执行：

```sh
cmd.exe /c "$(wslpath -w "$PWD/build.bat")"
cmd.exe /c "$(wslpath -w "$PWD/tests/run.bat")"
```

## 许可证

采用 [MIT License](<LICENSE>)，允许自由使用、修改及商业分发，分发时保留版权和许可声明。
