# Crystal (Legend of Mir 2) —— Windows 编译部署包

编译时间：本机 `dotnet build -c Release`，上游提交 `0e315fe`（DB v117 / 最低兼容 v60）

## 目录结构

```
D:\my-code\mir2\
├── Legend of Mir.sln          源码解决方案（可用 VS2022 打开）
├── Client\ Server\ Shared\ …  源码
├── Build\Server\Release\      ★ 服务端（可直接运行，数据已就位）
├── Build\Client\Release\      ★ 客户端（已编译，等美术资源）
└── Build\Server Tools\        LibraryEditor / LibraryViewer（.Lib 资源工具）
```

## 一、服务端（已就绪，可直接跑）

```
Build\Server\Release\
├── Server.exe                  ← 双击 或 运行 启动服务端.bat
├── Configs\                    Setup.ini + 30 个系统配置
├── Envir\                      NPC/掉落/任务/商店/事件…（225 MB）
├── Maps\                       498 个 .map
├── Server.MirDB                主数据库（已自动升级到 v117）
├── Server.MirADB               账号库（首次运行自动生成）
├── Localization\               English.json / Chinese.json
└── Logs\ Back Up\ Archive\ Guilds\ Conquests\ Exports\   自动生成
```

### 关键配置 `Configs\Setup.ini`

| 段 | 键 | 当前值 | 说明 |
|---|---|---|---|
| `[General]` | `Language` | `Chinese` | **我改过的**，想用英文改成 `English` |
| `[Network]` | `IPAddress` | `127.0.0.1` | 局域网/外网服改成 `0.0.0.0` 或实际 IP |
| `[Network]` | `Port` | `7000` | 客户端要一致 |
| `[General]` | `GMPassword` | `C#Mir 4.0` | 游戏内 `@` GM 命令密码 |
| `[General]` | `CheckVersion` | `False` | 校验客户端 `Mir2.Exe` 哈希，本地测试保持 False |
| `[General]` | `ThreadLimit` | `2` | 怪物 AI 线程数 |
| `[Game]` | `ExpRate` / `DropRate` | `1` | 经验/爆率倍率 |

### 启动验证结果（已实测）

```
进程      Server.exe 存活
端口      127.0.0.1:7000  LISTENING
怪物生成  SMain 标题显示 "Total: 114639" → 全地图怪物已刷出
数据库    540,512 B → 548,567 B（自动迁移 v107 → v117）
备份      Back Up\Database\Database 2026-10-27 08-23-37.bak
账号库    Server.MirADB 自动创建
```

> 服务端是 WinForms 程序，`SMain_Load` 里会自动 `LoadDB()` + `Envir.Start()`，
> 开窗即开服。菜单可开数据库编辑器（物品/怪物/NPC/任务…）。

### 注意事项

- **`Server.MirDB` 已被升级到 v117**，旧版服务端（≤v107）将无法再读。原始文件在
  `Back Up\Database\` 里。
- 服务端 GUI 里 `Database` 菜单改的数据要**点保存**才写入 `Server.MirDB`。
- 关窗口会自动 `SaveDB()`（`SMain_FormClosing` → `Envir.Stop()`）。

## 二、客户端（已编译，缺美术资源）

```
Build\Client\Release\
├── Client.exe                 ← 双击 或 运行 启动客户端.bat
├── Mir2Config.ini             已配好：127.0.0.1:7000 / Chinese / 微软雅黑
├── Data\                      ★ 骨架目录已建好，等你放 .Lib（当前为空）
├── Map\                       493 个 .map（已从服务端复制）
├── Sound\                     空（可选）
├── Localization\              Chinese.json / English.json
├── 资产清单.md                ★ 需要哪些 .Lib 的完整清单
└── _check-assets.ps1          ★ 资源自检脚本
```

### 你要做的：补美术资源

1. 打开 `Build\Client\Release\资产清单.md`，按目录把 `.Lib` 丢进去
2. 跑自检：
   ```powershell
   cd D:\my-code\mir2\Build\Client\Release
   powershell -ExecutionPolicy Bypass -File _check-assets.ps1
   ```
3. 全绿后 `Client.exe` 即可进游戏

**最容易踩的坑**：编号套装库（`Data\CArmour\00.Lib` 等）代码是「扫描目录取最大编号 + 1」来建数组的，
所以 **`00` 到最大值必须连续**，不能跳号。详见 `资产清单.md` 第 0 节。

### 客户端为何只能 Windows

`WinForms` + `SlimDX`（DirectX 9）+ `user32.dll` P/Invoke + `WebView2`，无跨平台可能。

## 三、工具

```
Build\Server Tools\LibraryEditor\Release\LibraryEditor.exe   编辑/导出 .Lib
Build\Server Tools\LibraryViewer\Release\CMirLibraryViewer.exe  浏览 .Lib
```

找资源时用这两个先确认 `.Lib` 是 **v3** 格式（版本不符会弹 “Wrong version, expecting lib version: 3”）。

## 四、重新编译

```bat
cd /d D:\my-code\mir2
dotnet build Client\Client.csproj        -c Release
dotnet build Server.MirForms\Server.csproj -c Release
dotnet build LibraryEditor\LibraryEditor.csproj -c Release
```

或用 VS2022 打开 `Legend of Mir.sln`。

> ⚠️ 不要用 `dotnet build "Legend of Mir.sln"` —— 解决方案里包含 `PatcherWebSite`
> （ASP.NET WebForms 网站），只支持 .NET Framework 版 MSBuild，会报 `MSB4249`。
> 逐个工程编译即可。

## 五、已知需要留意的点

| 项 | 说明 |
|---|---|
| 字体 | 客户端默认 `FontName=Microsoft YaHei`（我改的，原来 Arial 无汉字字形）。系统里没这字体可在 `Mir2Config.ini` 改成 `SimSun` |
| 服务端中文 | `Configs\Setup.ini` → `[General] Language=Chinese`，服务端 766 个 key 已全量翻译 |
| 管理端 GUI | `Server.exe` 的菜单/按钮**只有英文**（上游未本地化） |
| 缺 20+ 个语言 key | `StoragePassword*` 系列会显示成英文枚举名，不影响玩 |
| `BinaryFormatter` | `Shared/Functions/Functions.cs` 还有 2 处，将来升 .NET 9 会抛异常 |
| 数据库无回滚 | 只能靠 `Back Up\Database\` 里的时间戳备份 |

---

# 六、客户端内挂：自动喝药（自定义功能，非上游代码）

> 纯客户端实现，**服务端一行没改**。走的是正常 `C.UseItem` 包，所以任何 Crystal 服务端都能用，
> 也不会被服务端当成非法包踢掉。

## 打开方式

| 方式 | 说明 |
|---|---|
| **W** | 默认快捷键（`W` 在原版里没有任何绑定，零冲突） |
| 按 **U** 打开按键设置 | 列表里 `Dialogs` 组 → `自动喝药`，可改成任意键 |

> 早期版本用过 `Ctrl + B`，但 `B` 本身是"打开/关闭大地图"（该绑定忽略修饰键，所以 `Ctrl+B` 会同时命中大地图）。现已改为 `W`。

> 新增的 keybind 名是 `AutoPot`，存在 `KeyBinds.ini` 的 `[AutoPot]` 段里。

## 界面

```
┌────────────────────────────────────────────┐
│ 自动喝药                              [X]  │
├────────────────────────────────────────────┤
│ 已关闭（点击开启）                          │  ← 点击切换开关
│                                            │
│ HP <  [ 50 ] %     金创药(小)(23)          │  ← 百分比是内嵌输入框
│ MP <  [ 30 ] %     魔法药(小)(12)          │    点药水名弹出选择列表
│                                            │
│ 检测间隔 [1000] 毫秒                        │
│                                            │
│ 左键点击药水切换 / 点击百分比修改            │
└────────────────────────────────────────────┘
   364 × 172，背景自绘
```

- **背景完全自绘**（`MirControl` + `BackColour` + `Border`），不依赖任何窗口底图 ——
  因为现成的窗口图里烤死了别的对话框的文字和按钮，叠字会乱
- 百分比 / 检测间隔都是**内嵌输入框**（`MirTextBox`），回车或失焦即生效，不再弹二级窗口
- 输入框只接受数字，按 min/max 自动夹取（百分比 1~99，间隔 300~5000）

## 逻辑（`Client/AutoPotion.cs`）

```
每帧 GameScene.Process() → AutoPotion.Process()
  ├─ 未开启 → 直接 return（零开销）
  ├─ 死亡 / 未登录 → 跳过
  ├─ 未到检测间隔（默认 1000ms，可调 300~5000）→ 跳过
  ├─ HP: 当前% < 阈值 且 背包里有该药 → C.UseItem
  └─ MP: 同上
```

| 细节 | 实现 |
|---|---|
| 阈值范围 | 1 ~ 99 %（学原版英雄，不给设 100） |
| 找药范围 | `User.Inventory` 全数组，**含快捷栏 0~5 号格** |
| 药水筛选 | `ItemType.Potion` 且 `Shape <= 1`（普通药 / 太阳水），与原版英雄自动喝药一致 |
| 防重复发送 | 同一属性最短间隔 **700ms**，避免网络延迟内连发把药喝光 |
| 百分比计算 | 直接算 `HP / Stats[Stat.HP]`。⚠️ 上游 `UserObject.PercentMana` **从来没被赋值过**（只有英雄的赋了），所以不能直接用那个字段 |

## 配置（`Mir2Config.ini`）

```ini
[AutoPot]
Enabled=False      ; 总开关
HPPercent=50       ; HP 低于百分之多少时喝
MPPercent=30
HPItem=0           ; 药水的 ItemInfo.Index，0 = 未设置
MPItem=0
Delay=1000         ; 检测间隔(毫秒)，限制 300~5000
```

`HPItem` / `MPItem` 存的是**物品模板 Index**（不是格子号），所以背包里药水换位置、合并堆叠都不影响。
界面上点药名会自动在"背包里现有的药水"之间循环切换，并回写这两项。

## 改了哪些文件

| 文件 | 改动 |
|---|---|
| `Client/AutoPotion.cs` | 🆕 自动喝药引擎 |
| `Client/MirScenes/Dialogs/AutoPotDialog.cs` | 🆕 配置界面 |
| `Client/Settings.cs` | 新增 `[AutoPot]` 段读写 + `SaveAutoPot()` |
| `Client/KeyBindSettings.cs` | 新增 `KeybindOptions.AutoPot`，默认 `Ctrl + B` |
| `Client/MirScenes/GameScene.cs` | 新增 `AutoPotDialog` 字段/实例化、快捷键分支、`Process()` 里调用引擎 |
| `Shared/Language.cs` | 新增 9 个 `ClientTextKeys` + 英文默认值 |
| `Client/Localization/{Chinese,English}.json` | 补齐这 9 个 key（中文已翻译） |

## 想继续加什么

| 需求 | 改动量 |
|---|---|
| 喝药音效 / 屏幕提示 | 极小，`AutoPotion.TryAutoUse` 里加一行 |
| 只在某地图/某状态喝 | 小，读 `GameScene.User.CurrentMap` / `Buffs` |
| 自动吃太阳水（`Shape==1`）优先于普通药 | 小，`FindPotion` 里加优先级 |
| 自动喝 buff 药 | 中，`Shape==3` 是 buff 药，需要额外判断不重复覆盖 |
| **自动战斗** | 大，建议复用服务端英雄 AI 那套，别在客户端模拟点击 |

---

# 七、客户端资源按需下载（网络加载 + 代理支持）

> 客户端**不再要求本地预先放齐 8.4 GB 美术资源**。缺哪个库，就在后台从补丁站拉哪个，
> 下完立即热加载生效。`.Lib` 格式、协议、服务端**全都没动**。

## 配置 `Mir2Config.ini`

```ini
[Download]
Enabled=True
Host=https://mirfiles.co.uk/resources/mir2/crystal/patch/
Proxy=http://user:pass@127.0.0.1:7890     ; 支持 http:// 和 socks5://，留空 = 直连
ProxyUser=                                ; Proxy 里没写账号时用这两个
ProxyPass=
MaxConcurrent=8                           ; 并发下载数 1~16
RetryCount=5                              ; 单文件重试次数 1~20
```

**代理支持三种写法**（都支持带账号密码）：

| 写法 | 说明 |
|---|---|
| `http://127.0.0.1:7890` | HTTP 代理，无认证 |
| `http://user:pass@127.0.0.1:7890` | HTTP 代理 + Basic 认证（账号密码写在 URL 里） |
| `socks5://127.0.0.1:1080` | SOCKS5 代理（.NET 6+ 原生支持） |

也可以分开写：`Proxy=http://127.0.0.1:7890` + `ProxyUser=xxx` + `ProxyPass=yyy`。

## 工作流程

```
客户端启动
  └─ Libraries 静态构造 → MLibrary.Initialize()
       ├─ 本地有文件  → 正常加载
       └─ 本地没文件  → MirDownload.Request(".\Data\XXX.Lib")   ← 非阻塞，立刻返回
                          │
                          ├─ 后台线程拉 PList.gz 清单（167 KB，只拉一次）
                          ├─ 按优先级入队 + 8 并发 worker 下载
                          │    ├─ .part 断点续传（Range 请求）
                          │    ├─ 下载后按清单里的原始大小校验
                          │    └─ 失败退避重试
                          └─ 完成后投递到 Finished 队列
                                │
             渲染线程 GameScene.Process() → Libraries.ProcessDownloads()
                                └─ MLibrary.Reload()  ← 立即生效，不用重启
```

## 下载优先级

| 优先级 | 内容 | 体积 |
|---|---|---|
| 0 | `Data/` 根目录 26 个单体库（Prguse / Items / Title / ChrSel / Effect / Magic…）| 240 MB |
| 5 | `Data/Cursors/` | 29 KB |
| 10 | `Data/Map/` 地图图块库 | 3.6 GB |
| 15 | `DirectX/` | 11 MB |
| 40 | 装备/怪物/NPC 等编号套装（`CArmour` `CWeapon` `Monster` …） | ~3 GB |
| 90 | `Data/Monster/` | 2.1 GB |
| 95 | `Sound/` | 436 MB |

> 注意：**套装库是整目录排队**的（比如 `CArmour` 59 个文件一起排 40 优先级），
> 因为它们不是一个个单独的 `MLibrary`，而是一个数组按索引访问。

## 三个关键改动

### 1. `MLibrary` 编号套装库固定槽位（必须）

原来这样算数组长度：

```csharp
var count = int.Parse(Regex.Match(lastFile, @"\d+").Value) + 1;   // 目录空时 = 1
library = new MLibrary[count];
```

本地目录为空 → 数组长度 1 → 上层 `CArmours[shape]` 直接 `IndexOutOfRangeException`。

现已改为：`[Download] Enabled=True` 时统一给 **600 个槽位**，缺文件只是画不出来，不会越界：

```csharp
if (Settings.DownloadEnabled && count < MinLibrarySlots) count = MinLibrarySlots;
```

对应地，清单里不存在的编号会被自动跳过（不产生无效请求）—— 实测跳过 ~15,000 个空槽位。

### 2. `MLibrary.Reload()` 热加载

上游 `Initialize()` 有个坑：文件不存在时也会把 `_initialized = true`，之后**永不重试**。

```csharp
public void Initialize()
{
    _initialized = true;                                              // ← 先置 true
    if (!File.Exists(_fileName)) { MirDownload.Request(_fileName); return; }   // ← 现在会请求下载
    ...
}

public void Reload()   // 新加：文件到位后由渲染线程调用
{
    _fStream?.Dispose(); _reader = null; _images = null; _indexList = null;
    _frames = null; _count = 0; _initialized = false;
    Initialize();
}
```

### 3. 下载在后台线程，重载在渲染线程

- DirectX9 的 `Device` **不是线程安全的**，所以下载线程只碰文件和网络，
  真正 `MLibrary.Reload()` 由 `GameScene.Process()` 在渲染线程执行。
- 进度日志写在 `Logs\Download.txt`（每 10 秒一行），失败写 `Error.txt`。

## 实测验证

用你给的代理实测（`MaxConcurrent=2`）：

```
下载文件    Data/Items.Lib   4,035,433 字节  ✓ 与清单一致
           Data/Deco.Lib    2,069,338 字节  ✓ 与清单一致

完整性      Items.Lib  ver=3, 5380 张图, 首图 32x23 → gzip 解压 2944 字节 = 32*23*4  ✓
           Deco.Lib   ver=2,  317 张图, 首图 40x32 → gzip 解压 5120 字节 = 40*32*4  ✓

断点续传    ChrSel.Lib.part 中断后重启客户端，从断点继续（15 MB → 继续增长）✓
调度       队列 1428 个有效文件，跳过 15018 个空槽位  ✓
```

## ⚠️ 已知限制

| 项 | 说明 |
|---|---|
| **速度** | 实测单连接 ~166 KB/s，4 并发合计 ~414 KB/s。**8.4 GB 全量约需 6 小时**。可把 `MaxConcurrent` 调到 16 |
| **流量** | 全量 8.4 GB，注意玩家流量成本 |
| **无 hash 校验** | `PList.gz` 里只有 `Length` / `Creation`，**没有 hash**。目前只校验大小。要更强完整性得自己加哈希表 |
| **代理写在 ini 里** | `Proxy=` 里如果带账号密码就是**明文**，发布包记得清空 |
| **缺图期观感** | 未下完的库"画不出来"（不崩），所以第一次进新地图会短暂缺图块/特效 |
| **`Sound/` 未接** | 音效不走 `MLibrary`，是 `SoundManager` 直接读 wav，目前不会自动按需下载 |
| **尚未做** | HTTP Range 单图流式读取（"看哪取哪"）、异步纹理解码预取、下载进度 UI |

## 后续可做（按收益排序）

| 项 | 收益 | 工作量 |
|---|---|---|
| 异步纹理解码 + 视野优先级预取 | 消除首次进图掉帧 | 3~5 天 |
| 下载进度 UI（加载画面/小地图旁） | 体验 | 1 天 |
| 按地图/装备精确调度（只下当前需要的套装编号） | 省流量 60%+ | 1~2 天 |
| SHA256 校验表 | 安全 | 1 天 |
| HTTP Range 单图流式 | 少下无用资源 | 5~8 天 |

---

# 八、渐进可用：边下边读（.part 流式加载）

> 解决的核心场景：**进登录页 → 背景图没有 → 立刻开始下背景 → 下到一部分就能看到图**，
> 而不是「整库 50 MB 下完才出现」。

## 为什么原来做不到

`MLibrary.Initialize()` 只认最终文件：

```csharp
if (!File.Exists(_fileName)) return;        // 文件没有 → 整个库永久不可用
_fStream = new FileStream(_fileName, ...);  // 而且必须整库完整才能读
```

网络下载中途只有一个 `Xxx.Lib.part`，所以登录界面全程黑屏，直到整库下完。

## 现在的做法

### 1. `MLibrary` 直接读 `.part`

```csharp
if (File.Exists(_fileName))              path = _fileName;          // 正式文件优先
else if (File.Exists(_fileName + ".part")) path = _fileName + ".part";  // 半成品也能读
else { MirDownload.Request(_fileName); return false; }

_fStream = new FileStream(path, FileMode.Open, FileAccess.Read,
                          FileShare.ReadWrite | FileShare.Delete);
```

- `FileShare.ReadWrite` → 下载线程可以同时往同一个 `.part` 里追加
- `FileShare.Delete` → 下载完成后 `File.Move` 能把它改名成正式文件，不会因为句柄占用失败

### 2. 每张图按需检查「数据够不够」

`.Lib` 的布局是「文件头 → 索引表 → 一张张图（17 字节头 + gzip 数据）」，**索引表在文件最前面**，
所以只要开头几 KB 到位，就能算出所有图的偏移量：

```csharp
private bool EnsureImage(int index)
{
    long off = _indexList[index];
    if (!HasBytes(off, 17)) return false;          // 这张图的数据还没下到
    ...解析 17 字节头，拿到 Length / HasMask...
    if (!HasBytes(off, need)) return false;        // 只下了半张，先不缓存
    _images[index] = probe;
    return true;
}
```

没下到的图 → `Draw()` 返回 `false` → 那一帧不画，**不会崩、不会越界**。
下到了 → 下一帧自动就出来了（`MirImageControl` 是立即模式，每帧都调 `Library.Draw`，不需要刷新缓存）。

### 3. 解压失败安全兜底

`.part` 还在追加时有可能读到「半张图」，gzip 会抛异常：

```csharp
try { mi.CreateTexture(_reader); }
catch { mi.DisposeTexture(); return false; }   // 释放半成品纹理，下帧重试
```

### 4. 重启后自动续传

关键修复：**`.part` 存在时也要通知下载器**，否则客户端重启后旧的 `.part` 没人接着下，永远完不成：

```csharp
// 即使本地已有 .part 也要通知下载器续传（Request 内部去重，重复调用无开销）
MirDownload.Request(_fileName);
```

### 5. 热加载覆盖所有场景

`Libraries.ProcessDownloads()` 原先只在 `GameScene.Process()` 里调用 —— 登录页/选人页
下载完的库**永远不会生效**。现已移到 `CMain.UpdateEnviroment()`，三个场景都覆盖。

### 6. 清单拉取失败不雪崩

`PList.gz` 偶尔会被 Cloudflare 挡（实测遇到过 HTTP 523）。原来的行为是「没清单也照排队」，
会把上万个不存在的编号全塞进队列。现在：

- 清单没拿到 → **完全不排队**，每 15 秒重试一次
- 清单拿到后才按「文件是否存在 + 原始大小」过滤（实测过滤掉 ~15,020 个空槽位）

## 实测验证

以 **登录界面背景**（`ChrSel.Lib`，50.8 MB，19 帧动画）为例：

| 下载进度 | 可渲染帧数 |
|---|---|
| 0 MB | 0 / 19（黑屏，但立刻开始下载） |
| 22 MB | **7 / 19** ← 登录背景已经出图 |
| 44.8 MB | **19 / 19** ← 完整动画可播放，文件还没下完 |

```
$ python 校验脚本
登录背景 19 帧动画：可渲染 19 帧 [0..18]
                        缺 0 帧 []
文件进度: 44.8 / 50.8 MB
=> 登录界面现在可以完整显示背景动画
```

同批次完整的库：`Prguse.Lib`(11.8MB) `Prguse2.Lib`(3.4MB) `Prguse3.Lib`(123KB)
`Title.Lib`(5.4MB) `UI_32bit.Lib`(6.5MB) 等 18 个文件，`Error.txt` 全程为空。

## 注意

| 项 | 说明 |
|---|---|
| 读 `.part` 会有轻微抖动 | 极少数情况下读到半张图，会浪费一次解压（已兜底，无副作用） |
| 顺序不一定按索引 | 下载是线性的，所以「先下到的是文件里靠前的图」，与索引号无关 |
| 单文件不能并行分段 | 一个大库仍然是一个连接线性下，想要更快可以后续加多 Range 分段 |

---

# 九、三项优化（已完成）

## 优化 1：单文件多 Range 分段并行

**问题**：一个 50 MB 的库原来只用一个连接线性下，实测单连接只有 ~166 KB/s，首图要等 5 分钟。

**做法**：`size >= 8 MB` 的文件按 **4 MB 一段**切分，每段一个 `Range` 请求，`SegmentsPerFile` 个并行。

```csharp
req.Headers.Range = new RangeHeaderValue(start, end);   // 每段独立请求
fs.Seek(start, SeekOrigin.Begin);                        // 直接写到 .part 对应偏移
```

配套解决两个难点：

| 难点 | 解决 |
|---|---|
| 预写高位段会让 `.part` 长度瞬间等于完整大小，`FileStream.Length` 不再是"可用长度" | 下载器维护 `ConcurrentDictionary<string,long> SafePrefix`（**连续可用前缀**），`MLibrary.HasBytes()` 优先查它 |
| 断点续传无法知道哪些段已完成 | 侧车文件 `Xxx.Lib.part.meta`（`大小 / 段长 / 已完成段号`），每段完成即落盘；没有 meta 的老 `.part` 按"顺序写"推断前缀段 |

小文件（< 8 MB）仍走单连接，避免为几 MB 文件开 4 个连接。

配置：
```ini
MaxConcurrent=4        ; 同时下几个文件
SegmentsPerFile=4      ; 每个大文件几段并行（1 = 关闭分段）
```

## 优化 2：下载进度提示

`MirScene` 是所有场景（`LoginScene` / `SelectScene` / `GameScene`）的基类，所以在基类构造里放一个
`MirLabel`，三个场景**自动都有**，不需要逐场景加：

```
┌────────────────────┐
│ 资源加载 18/24  1.7 MB/s │   ← 左上角，有下载任务时才出现
└────────────────────┘
```

- 每 250 ms 刷新一次，文本没变就不重建纹理（`MirLabel` 的纹理是按文本生成的，频繁设置会浪费 CPU）
- 任务清空后自动隐藏
- `NotControl = true`，不抢鼠标事件

## 优化 3：按需调度（不再预拉全部资源）

**这是三个里收益最大的一个。**

原来 `LoadGameLibraries()` 会对**每一个槽位**调 `Initialize()`：

```csharp
for (int i = 0; i < CArmours.Length; i++) { CArmours[i].Initialize(); Progress++; }
// × 28 组 × 600 槽位 ≈ 18,000 次
```

配合下载器就变成"把 1412 个文件全排进队列"，包括玩家根本用不到的套装编号和 510 个怪物库。

**改法**：给预扫描加一个线程级标记，预扫描阶段**只打开本地已有文件，不触发下载**：

```csharp
[ThreadStatic] internal static bool EagerLoading;

private static void LoadGameLibraries()
{
    EagerLoading = true;
    try { LoadGameLibrariesCore(); }
    finally { EagerLoading = false; }
}

// MLibrary.EnsureOpen()
if (!Libraries.EagerLoading) MirDownload.Request(_fileName);
```

然后下载变成**纯绘制驱动**：哪个库被真正画到，才去下哪个库。

- 地图图块库 → 地图渲染时自动请求该地图用到的 `MapLibs[i]`
- 衣服/武器/头发 → 角色被画出来时按 `shape` 索引请求对应编号
- 怪物库 → 怪物出现在视野里才请求
- 用不到的编号永远不会下

## 实测

```
=== 优化前 ===
[11:18:15] 队列 1414 | 完成 9  | 跳过 15017 | 已下载 15.4 MB     ← 一次排 1414 个
[11:30:18] 队列 1402 | 完成 18 | 跳过 15020 | 已下载 21.1 MB

=== 优化后 ===
[11:37:39] 队列 0 | 完成 1 | 失败 0 | 跳过 5 | 0.0 MB/s | 累计 6.8 MB
                                                       ↑ 只下了实际需要的那 1 个
```

| 指标 | 优化前 | 优化后 |
|---|---|---|
| 启动排队文件数 | **1414** | **6**（再减到 1 个真正缺的） |
| 流量 | 全量 8.4 GB 无差别下载 | 只下画到的 |
| 进度可见性 | 只能看 `Logs\Download.txt` | 屏幕左上角实时显示 |
| ChrSel.Lib（50.8 MB） | 单连接 ~166 KB/s | 4 段并行 + 断点续传 |

**完整性校验**（分段并行最容易出错的地方）：

```
Data/ChrSel.Lib  50.82 MB  ✓ 与清单字节数完全一致
                 ver=2  1146 张图 → 1145 张解压成功，1 张失败

  ← 失败的是 index 1145，偏移 50332672 是个"回头"的异常值

对比所有已下载库的「相邻紧凑性」（每张图结尾 == 下一张开头）：
  ChrSel.Lib      ver=2  图数=1146  紧凑=1144  不紧凑=1  ← 最后 1 个索引
  Background.Lib  ver=2  图数= 100  紧凑=  98  不紧凑=1  ← 最后 1 个索引
  Deco.Lib        ver=2  图数= 317  紧凑= 315  不紧凑=1  ← 最后 1 个索引
  Prguse.Lib      ver=3  图数=2447  紧凑=2446  不紧凑=0  单调 ✓
  Title.Lib       ver=3  图数= 899  紧凑= 898  不紧凑=0  单调 ✓
  UI_32bit.Lib    ver=3  图数= 474  紧凑= 473  不紧凑=0  单调 ✓
  Items.Lib       ver=3  图数=5380  紧凑=5379  不紧凑=0  单调 ✓
  Prguse2.Lib     ver=3  图数=1602  紧凑=1601  不紧凑=0  单调 ✓
  Prguse3.Lib     ver=3  图数=  32  紧凑=  31  不紧凑=0  单调 ✓
```

结论：**ver=3 的库 100% 结构完美**；ver=2 的库全部是"最后一个索引表项异常"，
这是 `.Lib v2` 格式本身的特性（三个文件分别经过顺序下载 / 分段下载 / 再下载，
都呈现同一现象），不是下载损坏。客户端对 `w/h == 0` 的图本来就直接跳过，无影响。

## 配置总览

```ini
[Download]
Enabled=True
Host=https://mirfiles.co.uk/resources/mir2/crystal/patch/
Proxy=http://user:pass@host:port     ; http:// / socks5://，留空=直连
ProxyUser=
ProxyPass=
MaxConcurrent=4                      ; 同时下几个文件（1~16）
SegmentsPerFile=4                    ; 每个大文件几段并行（1=关闭，上限 16）
RetryCount=5                         ; 单请求重试次数（1~20）
```

---

# 十、修复：进入游戏后建筑/地图不加载、左上角没有下载提示

## 根因 1：`.part` 半成品**永远不会续传**（建筑/地板缺失的直接原因）

`EnsureOpen()` 的第一行是：

```csharp
if (_fStream != null)
{
    if (_headerLoaded) return true;   // ← 直接返回，再也不调 Request
    ...
}
```

启动时的"全库预扫描"会把**已有的 `.part` 打开**（这是边下边读需要的），
`_fStream != null` + `_headerLoaded == true` 之后，`EnsureOpen` 就永久短路了。

结果：

```
Data/Map/WemadeMir2/Tiles.Lib.part      7.9 MB  (11:30 停住)
Data/Map/WemadeMir2/Objects.Lib.part    6.2 MB  (11:30 停住)
Data/Map/WemadeMir2/Objects2..5.Lib.part         (11:30 停住)
                ↑ 地板和建筑就在这几个库里，它们永远补不齐
```

而且队列是空的 → `MirDownload.IsActive == false` → 进度提示也不显示。

### 修法

```csharp
private bool EnsureOpen()
{
    // 真·完整文件已加载 → 什么都不用做
    if (_fStream != null && _headerLoaded && !_isPart)
        return true;

    // 没文件，或者正在读没下完的 .part → 周期性催下载器
    if (now < _nextOpenAttempt) return _fStream != null && _headerLoaded;
    _nextOpenAttempt = now + ReopenInterval;
    if (!Libraries.EagerLoading) MirDownload.Request(_fileName);
    ...
}
```

配套把下载器的去重状态从「一次性 `Requested` 集合」改成
「`Queued` / `Done` / `NextRetry`」三态：

| 状态 | 含义 |
|---|---|
| `Queued` | 排队中或正在下载，重复 `Request` 直接返回 |
| `Done` | 磁盘上已是完整文件，永不重复请求 |
| `NextRetry` | 失败/中断，冷却 **30 秒**后才允许重新入队 |

这样每帧的 `Request` 调用是安全廉价的，同时重启后遗留的 `.part` 会被自动续传。

## 根因 2：进度提示被地图盖住

`_downloadLabel` 原来是**场景的子控件**。而 `MirScene.CreateTexture()` 是把所有子控件
画进场景纹理里的，`MapControl` 是覆盖全屏的子控件、且在 Controls 列表里排在后面 →
后画的把先画的（我的提示）盖掉了。

### 修法

改成和 `CMain.DebugBaseLabel` / `HintBaseLabel` 一样的思路：
**不设 Parent，在场景纹理画完之后单独 `Draw()` 到最上层**。

```csharp
public override void Draw()
{
    if (IsDisposed || !Visible) return;
    OnBeforeShown();
    DrawControl();                       // 场景（含全部子控件）

    UpdateDownloadLabel();               // ← 之后单独画，保证在最上层
    if (_downloadLabel != null && !_downloadLabel.IsDisposed && _downloadLabel.Visible)
        _downloadLabel.Draw();
    ...
}
```

## 根因 3：库刚下完不会立即刷新画面

`Libraries.ProcessDownloads()` 原来只 `Reload()`，不触发重绘，得等下一次地图动画才刷出来。
现在补上：

```csharp
if (any) Client.MirControls.MirScene.ActiveScene?.Redraw();
```

## 验证

用真实场景验证（把 `Prguse2.Lib` 改成"上次中断的半成品"，只留 1,000,000 / 3,557,648 字节）：

```
启动前   Data/Prguse2.Lib.part   1,000,000 字节
运行中   日志: 累计 0.6 MB → 2.4 MB → 2.8 MB   (Range 续传)
结束后   Data/Prguse2.Lib        3,557,648 字节  ✓ 与清单完全一致

完整性   Prguse2.Lib ver=3 图数=1602 解压成功=1602 失败=0   ✓ 逐张校验
Error.txt  空
```

## 现在的行为

1. 进游戏 / 打开大地图 → 缺哪个库就请求哪个库
2. 有 `.part` 的直接 **Range 续传**，不用重下
3. 左上角显示 `资源加载 12/24  1.8 MB/s`，画在地图之上，不会被盖
4. 库下完 → `Reload()` + 强制重绘 → 建筑/地板**当场出现**
5. 失败的文件 30 秒后自动重试

---

# 十一、再修：`.part` 仍不续传的最后一环 + 进图预取

上一轮修完 `EnsureOpen` 之后，实测发现**建筑/地板还是不下载**，日志显示
`队列 0 | 完成 0 | 跳过 8` —— 一件新东西都没请求。

## 真正的原因：`EnsureImage` 根本没调用 `EnsureOpen`

```csharp
private bool EnsureImage(int index)
{
    if (!_initialized || _fStream == null || !_headerLoaded)   // ← 已经打开过就不进
    {
        _initialized = true;
        EnsureOpen();                                          // ← 所以这里从没执行
    }
    ...
}
```

启动的"全库预扫描"已经把 `.part` 打开并把索引表读进来了
（`_fStream != null`、`_headerLoaded == true`），于是这个 `if` 永远为假，
`EnsureOpen()` 一次都不会被调用，我上一轮加在它里面的 `Request` 自然也就没机会跑。

**修法**：`EnsureImage` 无条件走 `EnsureOpen()`

```csharp
private bool EnsureImage(int index)
{
    _initialized = true;

    // 无条件走 EnsureOpen：
    //  - 完整文件 → 第一行就 return true，几乎零开销
    //  - 没文件 / 正在读 .part → 顺手催下载器续传
    if (!EnsureOpen()) return false;
    ...
}
```

## 补充：进游戏时**主动预取**整幅地图的图块库

原来只有"某个格子被画到"才请求那个库，站位不同、视野外的图块永远不请求。
现在 `MapControl.LoadMap()` 里扫一遍全图，把这幅地图用到的所有库一次排进队列：

```csharp
private void PrefetchMapLibraries()
{
    HashSet<int> seen = new HashSet<int>();

    for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            CellInfo cell = M2CellInfo[x, y];
            if (cell.BackIndex   >= 0 && seen.Add(cell.BackIndex))   Libraries.PrefetchMapLibraries(cell.BackIndex);
            if (cell.MiddleIndex >= 0 && seen.Add(cell.MiddleIndex)) Libraries.PrefetchMapLibraries(cell.MiddleIndex);
            if (cell.FrontIndex  >= 0 && seen.Add(cell.FrontIndex))  Libraries.PrefetchMapLibraries(cell.FrontIndex);
        }
}
```

`CellInfo.BackIndex / MiddleIndex / FrontIndex` 就是 `Libraries.MapLibs[]` 的下标
（格式里 `BackIndex = 字节+100`、`FrontIndex = 字节+2`，`-1` 表示没有），
所以进图那一刻就能把 `Tiles / Smtiles / Objects*` 全部排好队，不用等画到。

## 顺便修了进度提示的显示

原来算总数时把"已有的库"也算进去了，所以会看到 `资源加载 0/8` 这种莫名其妙的数字。
现在只统计**真正需要下载的**：

```csharp
int need = DoneCount + FailCount + PendingCount + active;
if (need <= 0) return string.Empty;          // 没活干就不显示
return string.Format("资源下载 {0}/{1}   {2:F1} MB/s", DoneCount, need, SpeedBytes / 1048576.0);
```

## 启动冒烟测试

```
[14:18:59] 队列 6 | 完成 0 | 失败 0 | 跳过 0 | 0.0 MB/s | 累计 0.0 MB
[14:19:04] 队列 0 | 完成 0 | 失败 0 | 跳过 6 | 0.0 MB/s | 累计 0.0 MB
Error.txt 空
```
登录页只请求 6 个主库，都已完整 → 跳过。符合预期（地图库只在进图时才请求）。

## 三轮修复的问题清单

| # | 问题 | 现象 |
|---|---|---|
| 1 | `.part` 存在时没有通知下载器 | 重启后半成品永远不续传 |
| 2 | `ProcessDownloads` 只在 GameScene 调用 | 登录/选人页下完的库不生效 |
| 3 | 进度标签是场景子控件 | 被 MapControl 盖住，看不到 |
| 4 | **`EnsureImage` 跳过 `EnsureOpen`** | **建筑/地板/大地图永远缺失，且队列恒为 0** |
| 5 | 只按"画到"请求 | 视野外的图块不下载（现改为进图预取） |

---

# 十二、远程资源加载：现状盘点 + 本轮补齐

## 已接入（会按需下载）

| 资源 | 触发点 | 说明 |
|---|---|---|
| `Data/*.Lib`（26 个单体库） | 绘制时 / 启动 | UI、物品、技能、特效… |
| `Data/Map/**/*.Lib`（138 个） | **进图预取** + 绘制 | 地板 / 建筑图块 |
| `Data/<套装>/*.Lib`（衣服/武器/怪物/NPC…） | 绘制时（按 shape 索引） | 用不到的编号永不下载 |
| **`Data/Cursors/*.CUR`（7 个）** | 🆕 启动预取 | 下到后**热加载生效**，不用重启 |
| **`Data/Shaders/*.ps`** | 🆕 启动预取 | 可选，下次启动生效 |
| **`Sound/SoundList.lst`（10 KB）** | 🆕 首次播放音效 | 编号→文件名映射，缺它 177 个特殊命名播不了 |
| **`Sound/*.wav`（1607 个 / 436 MB）** | 🆕 播放时 | 缺文件就排队，本次静默跳过；失败**不缓存**以便重试 |

`Data/` 里其余目录（`Extra/` 等）代码里只定义了路径、没有实际读取，不需要处理。

## 本轮新增的配置

```ini
[Download]
Sound=True             ; 音效也按需下载（关掉就完全不下 436 MB 音频）
MaxConnections=16      ; 全局并发连接硬上限
```

`MaxConnections` 的做法不是加信号量，而是**反推每文件可用段数**，从构造上封顶：

```csharp
if (maxFiles > maxConn) maxFiles = maxConn;
_segWorkersPerFile = Math.Max(1, Math.Min(Settings.DownloadSegmentWorkers, maxConn / maxFiles));
```

这样 `MaxConcurrent × SegmentsPerFile` 无论配多大都不会超过 `MaxConnections`（之前配成 16×16 就是 256 条连接）。

## 本轮新增的健壮性

**`.part` 被截断但 `.meta` 还在** 是最隐蔽的坑：meta 声称某些段已完成，实际数据没了，结果拼出个大小正确但内容损坏的库。
现在 meta 第 4 行记录当时的 `.part` 长度：

```
<size>
<segmentSize>
0,1,2,5,...
<当时的文件长度>
```

续传时如果发现当前长度 < 记录长度 → 判定被截断 → 丢弃 meta 从头下。

## 实测

```
启动 35 秒，登录页：
[14:36:44] 队列 17 | 完成 0 | 失败 0 | 跳过 0
[14:36:49] 队列 0  | 完成 9 | 失败 0 | 跳过 8

完成 9 个 = 7 个 .CUR + grayscale.ps + SoundList.lst
跳过 8 个 = 6 个主库 + normal.ps + magic.ps（清单里没有，服务器上就没这俩）

落地文件：
  Data/Cursors/Cursor_*.CUR     7 个 × 4286 字节
  Data/Shaders/grayscale.ps     72 字节
  Sound/SoundList.lst           10,317 字节
Error.txt 空
```

## 还没做的（按优先级）

| 优先级 | 项 | 现状 | 建议 |
|---|---|---|---|
| **P0** | 只有大小校验、无 hash | `PList.gz` 里没有 hash 字段 | 发布端自建 `hash.txt`（每库一个 SHA256），客户端下完比对 |
| **P0** | 损坏 `.Lib` 不会自愈 | 解压失败只当帧跳过，永不重下 | 同一库连续 N 次解压失败（且非 `.part`）→ 删除重下，带一次防抖 |
| P1 | 没有"一键下全量"入口 | `MirDownload.RequestEverything()` 已实现但无人调用 | 加命令行参数 `-downloadall` 或配置项 |
| P1 | 进度没有总量/ETA | 只有"已完成/需求数 + MB/s" | 记录本会话需求总量，显示百分比 + 剩余分钟 |
| P1 | 无磁盘空间检查 | 写满后错误信息不明确 | 下载前查 `DriveInfo.AvailableFreeSpace` |
| P2 | 代理不能热切换 | `HttpClient` 启动时建一次 | 监听配置变化后重建 client |
| P2 | 玩家看不到失败 | 只写 `Error.txt` | 界面上加一条"有 N 个资源下载失败" |
| P3 | 单源、无 CDN | 只有 mirfiles 一个源 | 支持多 Host 轮询 / 本地镜像 |
| P3 | 无 P2P / 局域网共享 | — | 网吧场景可考虑，工作量大 |

### 一个需要注意的副作用

进图预取会把**这幅地图用到的所有图块库**一次性排队。`Data/Map/WemadeMir2` 里单个 `Objects*.Lib` 就有 30~70 MB，
一张图可能用 5~10 个库 → **进图瞬间会排队 100~300 MB**。

优点是地图完整可见（分段下载 + 边下边读，画面逐步补全），缺点是首次进大图流量较大。
如果需要更保守，可以加一个开关只预取前 N 个库、其余等画到再下。

---

# 十三、P0 #2：损坏 `.Lib` 自愈（已完成）

## 判定与处理

| 触发条件 | 处理 |
|---|---|
| 完整文件（非 `.part`）里累计出现 **5 张不同**的图解压失败 | 判定文件损坏 → 删除 `.Lib` / `.part` / `.meta` → 清掉下载器的"已完成"标记 → 立即重新下载 |
| 同一张图连续失败 **3 次** | 本会话放弃这张图（避免每帧重建纹理白白烧 CPU） |
| `.Lib` 头里的版本号不对（`< 2`） | 开了按需下载时同样按损坏处理，**不再直接弹框退出程序** |
| 每个库最多重下 **2 次** | 防止"源文件本身有问题"导致无限循环烧流量 |

关键点：**只有完整文件才参与判定**。`.part` 还在下载时出现半张图是正常的，不计入。

## 顺带修掉一个会直接崩客户端的 bug

故障注入测试时发现：`GetTrueSize()` 里直接调 `mi.CreateTexture(_reader)` 而**没有 try/catch**。

```csharp
// 修复前
if (!mi.TextureValid)
{
    _fStream.Seek(_indexList[index] + 17, SeekOrigin.Begin);
    mi.CreateTexture(_reader);        // ← 损坏的图会把异常抛出去
}
```

而 `LoginScene` 是在 `MirScene` 的**静态构造**里创建的：

```
MirScene..cctor → LoginScene..ctor → LoginDialog..ctor → MirImageControl.get_Size
    → MLibrary.GetTrueSize → MImage.CreateTexture → InvalidDataException
    → TypeInitializationException → 客户端启动即崩
```

实测就是这一串把客户端炸掉的。现在 `GetTrueSize` 统一走 `CheckImage()`（内部已捕获并记账），
异常不会再逃出 `MLibrary`。

## 故障注入测试（真实环境）

**做法**：把 `Data/Title.Lib`（899 张图，5.6 MB）里每张图的 gzip 头 32 字节清零，
**文件大小保持不变**——让下载器误以为文件是完整的（只有大小校验，没有 hash）。

```
注入后    md5 = b53e4c20...   893/899 张图可解，6 张损坏
           （下载器认为"已完成"，不会自动重下）

运行客户端 90 秒：

Error.txt:
  [6:51:43] MLibrary: .\Data\Title.Lib 判定文件损坏 → 删除并重新下载（第 1 次）

Download.txt:
  [14:51:45] 队列 18 | 完成 0 | 跳过 0
  [14:52:10] 队列 0  | 完成 1 | 跳过 16    ← Title 重下完成
  [14:52:36] 队列 0  | 完成 2 | 跳过 16

结果      Data/Title.Lib  md5 = 81a289c6...   与良好副本完全一致 ✓
          客户端全程无崩溃、无 Error 之外异常
```

## 另一个修掉的问题：分段失败不再整份重下

原来任何一段失败都会退回单连接**整份重下**（下好的几十 MB 全扔掉，实测 5.6 MB 的文件下了 16 MB）。
现在 `DownloadSegment` 返回三态：

| 返回值 | 含义 | 处理 |
|---|---|---|
| `1` | 成功 | 记完成 |
| `-1` | 服务器不支持 Range | 退回单连接重下 |
| `0` | 这次重试耗尽了 | **保留已完成的分段**，30 秒冷却后继续续传 |

## 顺带确认：进图预取 + 续传在游戏里是生效的

日志/文件时间戳显示，进入游戏后：

```
Data/Map/WemadeMir2/Objects12.Lib      10,535,629  完成（与清单一致，5381 张图 5380 张可解）
Data/Map/WemadeMir2/Objects6.Lib       15,727,931  完成（与清单一致，7751 张图 7750 张可解）
Data/Map/WemadeMir2/Objects.Lib.part   6.2 MB → 44.7 MB   ← 续传中
Data/Map/WemadeMir2/Objects8.Lib.part  15.3 MB
Data/Map/WemadeMir2/Objects9.Lib.part  12.7 MB
```

> 这两个库各只有 **最后一张**图解压失败，是 `.Lib v2` 格式的固定特性（最后一个索引表项是垃圾值），
> 不是损坏 —— 所以不会误触发自愈。

而登录页实测**不再请求任何地图库**（临时加了调用栈诊断验证后已移除）。

---

# 十四、客户端内挂：自动战斗（寻敌 / 战斗 / 恢复 / 拾取）

> 纯客户端实现，**服务端一行没改，也没新增任何协议**。
> 所有动作都走客户端原有的操作通道，服务端看到的和真人操作完全一样。

## 打开方式

默认快捷键 **W**（原「自动喝药」面板已整合进这个新面板）。
按 **U** 打开按键设置 → `Dialogs` 组 → `内挂（自动战斗）`，可改。

## 界面

```
┌────────────────────────────────────────────────────┐
│ 内挂（自动战斗）                              [X]  │
├────────────────────────────────────────────────────┤
│ 已关闭（点击开启）                                  │
│                                                    │
│ ── 寻敌 ──                                          │
│ 范围 [ 8] 格   优先级 [最近]    不打玩家 [是]        │
│                                                    │
│ ── 战斗 ──                                          │
│ 追击 [是]   攻击距离 [ 1] 格   攻击间隔 [ 300] 毫秒  │
│                                                    │
│ ── 恢复 ──                                          │
│ 自动喝药 [是]   检测间隔 [1000] 毫秒                 │
│ HP < [50] %   药水 [金创药(小)(23)]                 │
│ MP < [30] %   药水 [魔法药(小)(12)]                 │
│                                                    │
│ ── 拾取 ──                                          │
│ 自动拾取 [是]  范围 [ 3] 格  过滤 [全部]  先捡后打 [否]│
│                                                    │
│ 状态：攻击 牛魔战将                                 │
└────────────────────────────────────────────────────┘
   440 × 340（背景自绘，数字都是内嵌输入框）
```

可点击项（点击即切换，立刻存盘）：

| 项 | 取值 |
|---|---|
| 总开关 | 开 / 关 |
| 优先级 | 最近 / 血最少 |
| 不打玩家 | 是 / 否 |
| 追击 | 是 / 否 |
| 自动喝药 | 是 / 否 |
| 自动拾取 | 是 / 否 |
| 过滤 | 全部 / 仅药品 / 仅金币 |
| 先捡后打 | 是 / 否 |
| 药水 | 点开背包药水列表选择 |

## 实现方式（关键：全部复用原生通道）

| 功能 | 用的通道 | 对应的人工操作 |
|---|---|---|
| **走位/追击** | `MapControl.CurrentPath` + `AutoPath = true`（内部走 `PathFinder`） | 右键点地面自动寻路（`NewMove`） |
| **攻击** | `User.QueuedAction = Attack1 / AttackRange1` | 鼠标点击 / Shift+点击攻击 |
| **拾取** | 站上物品格后 `Network.Enqueue(new C.PickUp())` | 按 Tab 拾取 |

- 近战用 `MirAction.Attack1`，会自动带上 Slaying / Thrusting / HalfMoon / CrossHalfMoon /
  DoubleSlash / TwinDrakeBlade / FlamingSword 这些技能的原有逻辑（`PlayerObject.Process` 里那段）。
- 弓手有职业武器时自动改用 `MirAction.AttackRange1`，并把目标 ID + 坐标塞进 `Params`。
- 会把 `MapObject.TargetObjectID` 指向当前目标，所以游戏自带的**目标高亮**会跟着走。

## 规则细节

**寻敌**
- 只在 `寻敌范围`（默认 8 格）内找，同时受"目标仍在地图上、未死亡、未隐身"约束
- 优先级 `最近`：距离升序；`血最少`：`PercentHealth*1000 + 距离`（同血比距离）
- 排除：自己、`AI == 64`（守卫/NPC 类，客户端会强制取消锁定）、`AI == 70`、
  `MasterObjectId != 0`（别人的宝宝/宠物）、以及开启"不打玩家"时的所有玩家

**战斗**
- 距离 ≤ `攻击距离` 才出手；不够就按 `追击` 开关决定追还是停
- 双保险节流：`GameScene.AttackTime`（游戏自己的攻速）+ 自己的 `攻击间隔`
- 攻击时自动关掉 `AutoPath`，避免边走边打

**恢复**
- 完全就是原来的自动喝药（`AutoPotion`），只是搬进了这个面板，并加了独立开关
- 百分比 1~99、检测间隔 300~5000ms

**拾取**
- 在 `拾取范围`（默认 3 格）内找地面物品（`ItemObject`）
- `先捡后打 = 否`：只有当目标不存在、或物品就在脚边 1 格时才去捡；否则先打完
- `先捡后打 = 是`：看到就先去捡
- 过滤：全部 / 仅药品（名字含"药"或"水"）/ 仅金币（名字含"金币"）
  —— 因为 `ObjectItem` 包只带名字，没有物品类型，只能按名字判断

**暂停条件**（避免和手动作业打架）
- 未开启 / 死亡 / `Observing` / NPC 对话、交易、拍卖窗口打开时自动停

## 配置（`Mir2Config.ini`）

```ini
[AutoFight]
Enabled=False
TargetRange=8        ; 寻敌范围(格) 1~30
Priority=0           ; 0=最近 1=血最少
IgnorePlayers=True
Chase=True
AttackRange=1        ; 攻击距离(格) 1~12
AttackDelay=300      ; 额外攻击间隔(ms) 0~3000
Loot=True
LootRange=3          ; 拾取范围(格) 1~20
LootFilter=0         ; 0=全部 1=仅药品 2=仅金币
LootFirst=False      ; 先捡后打
```

## 改动文件

| 文件 | 说明 |
|---|---|
| `Client/AutoFight.cs` | 🆕 自动战斗引擎（寻敌/追击/攻击/拾取状态机） |
| `Client/MirScenes/Dialogs/AutoFightDialog.cs` | 🆕 配置界面（原 `AutoPotDialog.cs` 已删除并整合进来） |
| `Client/AutoPotion.cs` | 保持不动，被新面板复用 |
| `Client/Settings.cs` | 新增 `[AutoFight]` 段 + `SaveAutoFight()` |
| `Client/MirScenes/GameScene.cs` | 字段改名 `AutoFightDialog`，`Process()` 里调用 `AutoFight.Process()` |
| `Client/KeyBindSettings.cs` | 快捷键描述改为"内挂（自动战斗）" |
| `Shared/Language.cs` + 2 个 JSON | 新增 1 个 key `AutoFight` |

## 尚未实现（后续可加）

| 项 | 说明 |
|---|---|
| 自动放技能 | 需要在 `ClientMagic` 里挑可用攻击技能 + 走 `C.Magic`，还要管蓝耗和冷却 |
| 怪物黑白名单 | 需要怪物名字/索引筛选列表 UI |
| 血量低自动随机/回城 | 需要配置回城卷轴等 |
| 卡点巡逻 | 固定坐标循环巡逻 |
| 内挂状态浮层 | 现在状态只显示在面板里 |

## 验证情况

```
Shared / Client / Server(Debug)  编译通过
客户端启动冒烟测试               RUNNING，Error.txt 空
Mir2Config.ini                  自动生成 [AutoFight] 段，11 个键齐全
```

> 说明：三个动作通道都是客户端已有功能（右键寻路 / 点击攻击 / Tab 拾取）在用的同一条代码路径，
> 但**游戏内实际打怪效果需要你进游戏确认**——我这边没有账号和地图环境。

---

# 十五、修：开自动战斗后帧数暴降

## 根因：客户端自带的自动寻路「每帧跑一次全图 A*」

`MapControl.CheckInput()` 里这段：

```csharp
if (AutoPath)
{
    ...
    var path = PathFinder.FindPath(User.CurrentLocation, CurrentPath.Last().Location);  // ← 每帧！
    ...
}
```

- `FindPath` 的第三个参数 `MaxNodes` 不传 = **0 = 不限节点**
- 目标不可达时 A* 会把**整张可达区域搜完**
- `GetNeighbours()` 每次扩展节点都 `new List<Node>()` —— 全图搜索时是几万次分配，GC 直接爆

手动右键点地面走一次，这个开销还能接受（走完就 `AutoPath = false`）；
但自动战斗会**长时间保持 `AutoPath = true`** 追击，于是每帧一次全图 A* + 海量分配 → 帧数暴降。

## 修法（三层）

### 1. 重算路径加节流（300ms）

```csharp
private const int AutoPathRepathInterval = 300;
private long _nextAutoPathRepath;

// 开启 AutoPath 时重置计时
if (_autoPath) _nextAutoPathRepath = CMain.Time + AutoPathRepathInterval;

// CheckInput 里
if (CMain.Time >= _nextAutoPathRepath)
{
    _nextAutoPathRepath = CMain.Time + AutoPathRepathInterval;
    var path = PathFinder.FindPath(..., 0, 4000);
    ...
}
```

路径节点本来就是逐步消费的（每走一步 trim 掉一个），根本不需要每帧重算。

### 2. A* 加搜索节点预算

```csharp
public List<Node> FindPath(Point start, Point target, int MaxNodes = 0, int MaxExpansions = 0)
{
    int expansions = 0;
    while (openSet.Count > 0)
    {
        if (MaxExpansions > 0 && ++expansions > MaxExpansions)
            return null;          // 目标不可达也不会把整张图搜完
        ...
    }
}
```

自动寻路和自动战斗都传 `4000`，单次开销有了确定上界（约 3 万次邻居检查，远低于 1ms）。

### 3. `GetNeighbours` 不再每次分配

```csharp
[ThreadStatic] private static List<Node> _neighbourBuffer;

private List<Node> GetNeighbours(Node node)
{
    List<Node> neighbours = _neighbourBuffer ??= new List<Node>(8);
    neighbours.Clear();
    ...
}
```

`FindPath` 不是递归的，复用安全。这一步单独就能去掉全图搜索时几万次 `List` 分配。

### 4. 自动战斗自己的重算也收敛

```csharp
// 冷却期内且目标只在 1 格内挪动 → 直接沿用
if (Dist(dest, _pathDest) <= 1 && now < _nextPathTime) return;
```
原来失败时因为 `map.AutoPath` 已经是 false，冷却条件不成立，会变成**每 100ms 一次 A\***。
现在成功/失败都统一走 700ms 冷却。

## 效果对比（估算）

| 场景 | 修前 | 修后 |
|---|---|---|
| 追击中每秒 A* 次数 | ~60 次（每帧） | ~1.4 次（700ms）+ 3.3 次（内置 300ms） |
| 目标不可达时单次搜索 | 整张可达地图 | 最多 4000 节点 |
| 每次搜索的 `List` 分配 | 每节点 1 次（可达数万） | 0（复用缓冲） |

## 怎么确认

`Mir2Config.ini` 里加一行就能看帧数：

```ini
[Graphics]
DebugMode=True
```

游戏左上角会显示 `FPS: xx`。开着自动战斗追怪时对比一下修前修后。

> 顺带说明：这个「每帧 A*」是**上游原有代码**的问题，手动右键长距离走路也会卡；
> 这次一并修好了。

---

# 十六、修：自动战斗卡在障碍物附近不动

## 根因 1（主因）：寻路终点设成了**怪物所在的格子**

`MapControl.EmptyCell()` 会把「有阻挡物的格子」判为不可走：

```csharp
public bool EmptyCell(Point p)
{
    if ((M2CellInfo[p.X, p.Y].BackImage & 0x20000000) != 0 || ...) return false;

    foreach (var ob in Objects.Values)
        if (ob.CurrentLocation == p && ob.Blocking) return false;   // ← 怪物自己就站在这格
    return true;
}
```

而 `PathFinder` 的 `Node.Walkable => Map.EmptyCell(Location)` —— 用的是**同一个谓词**。

所以把寻路终点设成怪物格时：**目标格永远不可走 → A\* 到不了终点 → 必然返回 null**。
表现就是"卡在怪旁边、状态显示走不过去、再也不动"。

同理，`MapControl.CheckInput()` 里内置的 `AutoPath` 重算也用的是我们设的终点，
所以它也一样失败并立刻把 `AutoPath` 关掉。

### 修法

寻路到目标**旁边**最近的一个可走格子：

```csharp
private static Point ResolveChaseDest(MapControl map, MapObject target, Point from)
{
    Point cell = target.CurrentLocation;

    if (InBounds(map, cell) && map.EmptyCell(cell))
        return cell;

    return FindWalkableNeighbour(map, cell, from);   // 8 邻格里挑离自己最近的空位
}
```

这样**终点和判定谓词完全一致**，A* 一定能到达。

## 根因 2：`MaxNodes = 20` 把绕路的长路径全丢了

`FindPath` 的第三个参数是**后置过滤**，不是搜索上限：

```csharp
if (currentNode == targetNode)
{
    var path = RetracePath(startNode, targetNode);
    return MaxNodes == 0 || path.Count < MaxNodes ? path : null;   // ← 超长直接判失败
}
```

绕个墙角，8 格距离的路径很容易就超过 19 个节点 → 直接返回 null → "走不过去"。
现在改成 `MaxNodes = 0`（不限长度），开销上界交给 `MaxExpansions`（4000 节点预算）。

## 根因 3：走不到的目标会无限重试

新增两个保护：

| 机制 | 说明 |
|---|---|
| **卡住检测** | 追击中 3 秒位置没变化 → 判定走不到 |
| **目标拉黑** | 判定的目标 5 秒内不再选它，自动换下一个 |

```csharp
_avoidId = _target.ObjectID;
_avoidUntil = now + 5000;
```

另外 `MoveTo` 的失败也会进入 700ms 冷却（原来失败后 `map.AutoPath` 已是 false，
冷却条件不成立 → 变成每 tick 一次 A*）。

## 现在的状态提示

面板底部会显示具体在干什么，方便判断卡在哪：

```
状态：追击 牛魔战将
状态：被挡住，换目标
状态：走不到，换目标
状态：走不过去
状态：攻击 牛魔战将
状态：拾取 金创药(小)
状态：附近没有目标
```

## 逻辑自检

修复后的不变量：**寻路终点一定满足 `map.EmptyCell(dest) == true`**，
而 `PathFinder` 的 `Node.Walkable` 就是 `map.EmptyCell` —— 终点和谓词一致，
所以只要目标旁边有立即可走的格子，A* 就必然能找到路。

---

# 十七、修：自动战斗把守卫当目标

## 服务端给出的权威依据

```csharp
// Server/MirObjects/Monsters/Guard.cs
public override bool IsAttackTarget(HumanObject attacker) { return false; }
public override bool IsAttackTarget(MonsterObject attacker) { return false; }
public override int Attacked(HumanObject attacker, int damage, ...) { throw new NotSupportedException(); }
```

守卫**根本不可攻击**——服务端连 `Attacked` 都直接抛异常，所以客户端打它既没伤害也没反应，
表现为"一直对着守卫挥刀"。

服务端 AI → 怪物的映射（`MonsterObject.GetMonster`）：

| AI | 服务端类 | 说明 |
|---|---|---|
| **6** | `Guard` | 城镇守卫（不可攻击） |
| **58** | `Guard` | 道观守卫等（不可攻击） |
| **113** | `ArcherGuard : Guard` | 弓箭守卫（不可攻击） |
| 64 | `IntelligentCreatureObject` | 智能宝宝（不可攻击） |
| 70 | `Hugger` | 客户端自己的点击攻击就跳过它 |

注意 **102/103/104/105 是 `IceGuard`/`ElementGuard`/`DemonGuard`/`KingGuard`**，
那些是正常 BOSS 小怪，**可以打，不能误排除**。

## 修法 1：AI 黑名单

```csharp
private static bool IsNonCombatAI(byte ai)
{
    switch (ai)
    {
        case 6:   // Guard
        case 58:  // Guard
        case 64:  // IntelligentCreatureObject
        case 70:  // Hugger（客户端原生就不可点击攻击）
        case 113: // ArcherGuard : Guard
            return true;
        default:
            return false;
    }
}
```

在 `IsAttackable()` 里优先判断，直接把守卫排除在寻敌之外。

## 修法 2：通用的"打不动"检测

守卫只是"不可攻击"的一种。还有一类怪物是**条件免疫**（`HellKeeper`、`IcePillar`、
`TucsonEgg`、`Tree` 等，服务端在特定条件下才允许掉血），打上去同样毫无反应。

所以加了一个通用兜底：

```csharp
// 连续 15 次攻击，对方血量百分比一次都没变化 → 判定打不动
if (hp == _streakHp)
{
    if (++_streak >= 15) GiveUp(scene, now, "状态：打不动，换目标", 30000);
}
else { _streakHp = hp; _streak = 0; }
```

- 用 `PercentHealth`（整数百分比）判断，**任何变化都重置计数**，包括首次从 0 变成非 0
  （怪物没被打过时客户端没有血量数据，第一次命中后服务端才下发 `ObjectHealth`）
- 命中但免疫、完全打不中、攻击被无视，这几种情况都会自然收敛
- 拉黑 30 秒（比"走不到"的 5 秒长，因为豁免类怪物不会自己变可打）

## 现在的寻敌排除清单

| 排除项 | 依据 |
|---|---|
| 自己 | — |
| 已死亡 / 隐身（`Hidden`） | — |
| `AI ∈ {6, 58, 64, 70, 113}` | 服务端 `Guard` / `IntelligentCreatureObject` / 客户端 AI 70 不可点击 |
| `MasterObjectId != 0` | 别人的宝宝/宠物 |
| 所有玩家（开启"不打玩家"时） | 配置 |
| 刚判定"走不到"的目标（5 秒内） | 防卡死 |
| 刚判定"打不动"的目标（30 秒内） | 防无效输出 |

## 状态提示新增

```
状态：打不动，换目标
```

---

# 十八、修：还在打弓箭守卫（改成按 Monster 枚举排除）

## 上一版为什么没拦住

上一版只按 **AI 编号**排除（6 / 58 / 64 / 70 / 113），这是我根据服务端 `Guard.cs` 的注释推的。
但 AI 编号是**数据库里配置的**，不同 DB 完全不一样。

直接解析 `Server.MirDB`（版本 117）确认本服的配置：

```
DB 里 "守卫" 类型的怪物条目：
   Guard          image=0    AI=6       ← 上一版已排除
   ArcherGuard    image=139  AI=57      ← AI 57 不在名单里，所以漏了
```

`AI 57` 在服务端映射到 `TownArcher`：

```csharp
case 57:
    return new TownArcher(info);      // 城镇弓箭守卫
case 58:
    return new Guard(info);
case 113:
    return new ArcherGuard(info);     // ArcherGuard : Guard
```

## 改成按 **Monster 枚举**排除（DB 无关）

客户端 `MonsterObject.BaseImage` 就是服务端下发的 `ObjectMonster.Image`：

```csharp
// Server/MirObjects/MonsterObject.cs:2865
Image = Info.Image,        // = MonsterInfo.Image，数据库里配的怪物外观枚举
```

```csharp
// Client/MirObjects/MonsterObject.cs:46
public Monster BaseImage;
```

这个枚举是**怪物类型**的稳定标识（与 AI 编号解耦）：

```csharp
switch (monster.BaseImage)
{
    case Monster.Guard:         // 0
    case Monster.TaoistGuard:   // 1
    case Monster.Guard2:        // 2
    case Monster.ArcherGuard:   // 139  ← 本次的元凶
    case Monster.ArcherGuard2:  // 141
    case Monster.Guard3:        // 377
    case Monster.ArcherGuard3:  // 378
        return true;
}
```

AI 名单保留作为第二重保险（**并补上 57**）：

```csharp
case 6:   // Guard
case 57:  // TownArcher（弓箭守卫）
case 58:  // Guard
case 64:  // IntelligentCreatureObject
case 70:  // Hugger
case 113: // ArcherGuard : Guard
```

## 为什么不误伤 BOSS 小怪

`IceGuard(249)` / `ElementGuard(250)` / `DemonGuard(251)` / `KingGuard(252)`
是正常可击杀的 BOSS 小怪，**不在排除名单里**，名字里带 Guard 但 image 值不同。

## 验证方法（下次可以直接查 DB）

以后遇到"某个怪被误打"，可以按下面的方法查它的真实 AI/外观：

```
1) 在 Server.MirDB 里搜怪物名字，读名字后面的 ushort Image + byte AI
2) 对照 Shared/Enums.cs 的 Monster 枚举拿到 BaseImage
3) 把 BaseImage 加进 IsNonCombat 的 switch 即可（与 AI 无关，跨 DB 通用）
```

---

# 十九、修：小地图/大地图空、部分资源下不下来（URL 大小写）

## 现象

右上角小地图一直是空的，`Data/MMap.Lib` 也从来没出现在本地磁盘上。

## 根因：**HTTP 服务器大小写敏感**，而客户端按自己代码里的拼写拼 URL

`PList.gz` 清单里的名字和客户端代码里的路径拼写不一致：

| 客户端代码 | 清单里的名字 | 结果 |
|---|---|---|
| `Data/MMap.Lib` | `Data/mmap.Lib` | **404** |
| `Data/StateItem.Lib` | `Data/Stateitem.Lib` | **404** |
| `Data/DNItems.Lib` | `Data/dnitems.Lib` | **404** |
| `Data/Map/WemadeMir3/wood/...` | `Data/Map/WemadeMir3/Wood/...` | **404** |
| `.../Smtiles.Lib`、`.../smObjectsc.Lib`、`Objects27.Lib`、`tiles3.Lib`、`innersc.Lib` | 大小写各不相同 | **404** |

实测验证：

```
Data/mmap.Lib                          -> 206 ✓
Data/MMap.Lib                          -> 404 ✗   ← 客户端请求的是这个
Data/Stateitem.Lib                     -> 206 ✓
Data/StateItem.Lib                     -> 404 ✗
Data/dnitems.Lib                       -> 206 ✓
Data/DNItems.Lib                       -> 404 ✗
Data/Map/WemadeMir3/Wood/Tilesc.Lib    -> 206 ✓
Data/Map/WemadeMir3/wood/Tilesc.Lib    -> 404 ✗
```

Windows 本地文件系统不区分大小写，所以**代码能读到 `mmap.Lib`**；
但 HTTP 服务器（nginx/Linux）区分大小写，**用错大小写就是 404**。

## 修法：用清单里的规范名拼 URL

```csharp
// 清单里的"规范大小写"
private static readonly Dictionary<string, string> CanonicalRel =
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

// LoadManifest 里
CanonicalRel[name] = name;

// 入队时统一换成规范名
add.Add(new QueueItem { Priority = PriorityOf(rel), Seq = ++_seq, Rel = Canonicalize(rel) });
```

这样：
- URL 一定是 `.../Data/mmap.Lib`（清单里的写法）→ 206 ✓
- 本地文件也按规范名落盘（`mmap.Lib`），客户端用 `MMap.Lib` 打开照样能读到（Windows 不区分大小写）
- `DownloadRegistry` / `SafePrefix` 都是 `OrdinalIgnoreCase` 字典，热加载匹配不受影响

顺带把 404 也记进日志，方便以后诊断：

```
[17:14:12] !! HTTP 404  Data/MMap.Lib
```

## 另外：把这三个「进游戏必用」的库加入启动预取

```csharp
MirDownload.Request(@".\Data\MMap.Lib");       // 右上角小地图 / 大地图
MirDownload.Request(@".\Data\StateItem.Lib");  // 装备外观
MirDownload.Request(@".\Data\DNItems.Lib");    // 地面物品
```

## 实测（修复后跑客户端 3 分钟）

```
Data/mmap.Lib        23,507,526 / 23,507,526  ✓ ver=3  450 张图全部可解
Data/Stateitem.Lib    3,076,805 /  3,076,805  ✓ ver=3  5192 张图全部可解
Data/dnitems.Lib      1,715,976 /  1,715,976  ✓ ver=3  5280 张图全部可解

Download.txt 里 "!! HTTP 404" 计数 = 0
Error.txt 空
Data 顶层 .Lib 从 19 个变成 23 个
```

现在进游戏右上角小地图应该正常显示了。

---

# 二十、优化寻敌：扩大搜索 + 漫游寻敌

## 之前的问题

`FindTarget()` 只在 `寻敌范围`（默认 8 格）内找，找不到就 `Stop("状态：附近没有目标")`，
于是**原地发呆**，必须手动走过去才会继续。

## 现在的三级寻敌

```
1) 常规范围      TargetRange（默认 8 格）        → 找到就打
2) 扩大搜索      SeekRange  （默认 16 格）       → 附近没怪时，视野内更远处有怪就走过去
3) 漫游寻敌      Roam + RoamRange（默认 6 格）   → 完全没有目标时随机走动找怪
```

### 1) 扩大搜索

```csharp
_target = FindTarget(now, Settings.AutoFightTargetRange);

// 小范围没怪 → 扩大到 SeekRange 再找一遍
if (_target == null && Settings.AutoFightSeekRange > Settings.AutoFightTargetRange)
    _target = FindTarget(now, Settings.AutoFightSeekRange);
```

配合**牵引距离**（ leash）：追出 `max(TargetRange, SeekRange) + 3` 格就放弃，
避免被一只怪拖着满地图跑。

### 2) 漫游寻敌

围绕**锚点**（最后一次战斗的位置）在 `漫游半径` 内随机游走：

```csharp
// 正在走向漫游点
if (map.AutoPath && map.CurrentPath.Count > 0) { Status = "状态：漫游寻敌"; return; }

// 到了就歇 400ms 再挑下一个点
if (now < _nextRoamTime) { Status = "状态：原地等待"; return; }

// 跑太远先走回锚点
if (Dist(user.CurrentLocation, _anchor) > radius) { MoveTo(_anchor); ... }

Point dest = FindRoamCell(map, user.CurrentLocation, _anchor, radius);   // 随机可走格子
MoveTo(dest);
```

`FindRoamCell` 的约束：

| 约束 | 原因 |
|---|---|
| 随机最多试 24 次 | 避免死循环 |
| `InBounds` + `map.EmptyCell` | 地图可走 + 没有阻挡物 |
| 距离自己 ≥ 2 格 | 否则会原地左右抖 |

锚点会在**每次获得目标时**更新为当前位置，所以打完一片区域后会在附近继续巡逻，
不会越走越远。

## 面板新增一行

```
── 寻敌 ──
范围 [ 8] 格   优先级 [最近]   不打玩家 [是]
扩大搜索 [16] 格               ← 新增
漫游 [是]   半径 [ 6] 格        ← 新增
```
面板高度 340 → 358，其余各行 y 值整体下移 22px。

## 新增配置

```ini
[AutoFight]
SeekRange=16     ; 扩大搜索范围(格) 1~40，且不小于 TargetRange
Roam=True        ; 完全没有目标时随机漫游
RoamRange=6      ; 漫游半径(格) 1~20，围绕锚点
```

## 状态提示新增

```
状态：漫游寻敌
状态：原地等待
状态：返回巡逻点
状态：周围走不动
```

## 行宽校验（微软雅黑实测）

```
y=86   范围/优先级/不打玩家       最右 296 < 434  ok
y=110  扩大搜索/漫游/半径          最右 306 < 434  ok
y=158  追击/攻击距离/攻击间隔      最右 376 < 434  ok
y=208  自动喝药/检测间隔           最右 286 < 434  ok
y=232  HP/药水                    最右 254 < 434  ok
y=306  自动拾取/范围/过滤/先捡后打  最右 400 < 434  ok
最后一行 y=334 + 17 = 351 < 面板高 358  ok
```

---

# 二十一、修：拾取被挡住时人物卡住不动

## 之前的问题

`DoLoot()` 的寻路逻辑只有一行：

```csharp
// 物品不阻挡（ItemObject.Blocking = false），所以直接寻路到物品格就行
MoveTo(loot.CurrentLocation);
```

但 **卡住检测只在追击分支里做**，拾取分支完全没有。于是：

| 情况 | 旧行为 |
|---|---|
| 路径被怪/别的玩家挡住 | `CanWalk` 失败、原地不动，每 700ms 重算一次，**永远卡住** |
| 物品格被怪站着 | `EmptyCell(物品格)` = false → `FindPath` 到不了 → 返回 null → 一直重试 |
| 物品在走不到的角落 | 同上，无限重试同一件 |
| 背包满，站在物品上也捡不动 | 每 200ms 发一次 `C.PickUp`，服务端默默拒绝，**人物一直杵着** |

## 修法（4 处）

### 1. 卡住检测抽成公共方法，追击/拾取共用

```csharp
private static bool IsStuck(UserObject user, long now)
{
    if (user.CurrentLocation != _lastProgressPos) { TouchProgress(user, now); return false; }
    if (now - _lastProgressTime <= StuckTimeout) return false;   // 3 秒
    _lastProgressTime = now;    // 重置，避免每 tick 都报
    return true;
}
```

### 2. 拾取分支加卡住处理

```csharp
// 换了一件物品 → 重置计时
if (loot.ObjectID != _lootId) { _lootId = loot.ObjectID; TouchProgress(user, now); }

// 走不到 / 站上去也捡不动 → 这件先跳过
if (IsStuck(user, now))
{
    _lootAvoidId = loot.ObjectID;
    _lootAvoidUntil = now + 8000;
    if (scene.MapControl.AutoPath) scene.MapControl.AutoPath = false;
    Status = "状态：捡不到，跳过";
    return;
}
```

### 3. 寻物时就排除「站不上去」的物品

```csharp
if (item.ObjectID == _lootAvoidId && CMain.Time < _lootAvoidUntil) continue;

// 物品格不可走（被怪物/别的玩家占着，或本身是障碍格）→ 永远捡不到，别去追
// 注意：自己脚下那一格要排除 —— EmptyCell 会把玩家自己也当成阻挡物
if (item.CurrentLocation != from && !map.EmptyCell(item.CurrentLocation)) continue;
```

⚠️ 这个「排除自己脚下」很关键：`PlayerObject.Blocking => !Dead`，
如果不排除，玩家走到物品上之后 `FindLoot` 会把这件物品过滤掉，导致**永远不触发拾取**。

### 4. 攻击时不误判卡住

站着打怪本来就不移动，所以在攻击分支里 `TouchProgress()` 一下，避免下一轮追击立刻被判 "卡住"。

## 现在的拾取容错矩阵

| 情况 | 行为 |
|---|---|
| 物品格被怪/人占着 | 寻物阶段就跳过，不会去追 |
| 路径被挡 | 3 秒没挪窝 → 跳过这件 8 秒 → 去干别的（打怪/漫游） |
| 物品走不到 | 同上 |
| 背包满捡不动 | 同上（站在上面也判定卡住） |
| 物品消失/被捡走 | `IsValidTarget` 式检查 + 对象被移除，自动换下一件 |

## 状态提示新增

```
状态：捡不到，跳过
```

---

# 二十二、拾取漏捡问题：避让改成字典 + 回收范围

## 之前的重试行为盘点

| 情况 | 会不会重试 |
|---|---|
| 被拉黑的物品（3 秒走不到） | ✅ 8 秒后 `_lootAvoidUntil` 到期，自动重新纳入候选 |
| 目标格被怪物占着而跳过的 | ✅ `FindLoot` 每个 tick 都重新判断 `EmptyCell`，怪一走开就能捡 |
| **走开之后的远处掉落** | ❌ `FindLoot` 只在 `拾取范围`（默认 3 格）内找，**出了圈就永远不管了** |
| **多件同时捡不到** | ⚠️ 避让只有一个槽位（`_lootAvoidId`），A 被拉黑后 B 再被拉黑会顶掉 A 的冷却 |

所以「一直往前打 → 漏捡」确实存在。

## 修法 1：避让改成字典，每件物品独立冷却

```csharp
private static readonly Dictionary<uint, long> _lootAvoid = new Dictionary<uint, long>();

private static bool IsLootAvoided(uint objectId, long now)
{
    long until;
    if (!_lootAvoid.TryGetValue(objectId, out until)) return false;
    if (now < until) return true;
    _lootAvoid.Remove(objectId);      // 到期自动解禁
    return false;
}
```

- 多件物品各自 8 秒冷却，互不影响
- 字典超过 64 条时清空，不会无限增长

## 修法 2：周围没怪时扩大拾取半径（回收）

```csharp
int lootRange = Settings.AutoFightLootRange;

// 有怪时只在身边捡；周围清干净了就把范围放大，回去把之前漏掉的回收一遍
if (_target == null && Settings.AutoFightLootSweep > lootRange)
    lootRange = Settings.AutoFightLootSweep;

MapObject loot = FindLoot(lootRange);
```

逻辑顺序上，`_target == null`（没有可打的目标）时才会放宽到 `回收范围`（默认 10 格），
所以不会因为贪捡而放着怪不打。

配合漫游（锚点），打完一片区域后会在这个范围内来回捡干净再走。

## 面板新增一行

```
── 拾取 ──
自动拾取 [是]  范围 [ 3] 格  过滤 [全部]  先捡后打 [否]
回收范围 [10] 格   周围没怪时回收远处掉落（0=关闭）   ← 新增
```
面板高度 358 → 380，状态行下移到 356。

## 新增配置

```ini
[AutoFight]
LootSweep=10     ; 周围没怪时的回收半径(格) 0~30，0=关闭
```

## 现在完整的拾取容错矩阵

| 情况 | 行为 | 会不会补捡 |
|---|---|---|
| 物品格被怪/人占着 | 寻物阶段跳过 | ✅ 怪一走开立刻重新纳入 |
| 路径被挡，3 秒没挪窝 | 跳过这件 8 秒 | ✅ 8 秒后重试 |
| 物品走不到 | 同上 | ✅ 8 秒后重试 |
| 背包满捡不动 | 同上 | ✅ 8 秒后重试（腾出空间就能捡） |
| 打怪时没顾上捡 | 打完没目标后半径放大到 `回收范围` | ✅ 会走回去捡 |
| 物品消失/被别人捡走 | 对象被移除 | — |

## 行宽校验

```
y=330 回收范围 [ ] 格 + 提示文字   最右 316 < 434  ok
y=356 状态行 + 17 = 373 < 面板高 380          ok
所有输入框右边界 ≤ 348 < 434                  ok
```

---

# 二十三、修：第一次按 W 开面板会卡几秒

## 根因：10 个 `MirTextBox` 的首次绘制

`MirTextBox` 的所有者绘制不是直接画字，而是把 WinForms 的 `TextBox` 截屏成纹理：

```csharp
// Client/MirControls/MirTextBox.cs
protected unsafe override void CreateTexture()
{
    ...
    TextBox.DrawToBitmap(bm, new Rectangle(0, 0, Size.Width, Size.Height));
```

而 `MirControl.Draw()` 的执行顺序是：

```
OnBeforeShown → BeforeDrawControl → DrawControl() → DrawChildControls() → ... → OnShown()
                                     ↑ 这里 CreateTexture → DrawToBitmap
                                                                        ↑ OnShown 才设父窗口
void MirTextBox_Shown(...) { TextBox.Parent = Program.Form; ApplyNativeTextBoxState(); SetFocus(); }
```

也就是说**首次绘制时，TextBox 还没有父窗口、没有句柄**，`DrawToBitmap` 会：
1. 强制创建 HWND（`CreateControl`，内部会泵消息 —— 嵌在 D3D 渲染循环里）
2. 截屏
3. 紧接着 `OnShown` 又把它挂到 `Program.Form` 上 → **改父窗口会销毁并重建 HWND**
4. `SetFocus()` 又来一次焦点切换

原来自动战斗面板里有 **10 个** `MirTextBox`（寻敌范围 / 扩大搜索 / 半径 / 攻击距离 / 攻击间隔 / 检测间隔 / HP% / MP% / 拾取范围 / 回收范围），
首次打开就是 10 次这套流程 → 卡几秒。之后句柄已建好、纹理已缓存，就正常了。

## 修法 1：全局只用一个共享输入框

- 每个数字字段改成**廉价的 `MirLabel`** 显示当前值（绿色，可点）
- 点哪个字段，就把**唯一**的那个 `MirTextBox` 移过去并聚焦
- 回车 / 失焦 → 提交、夹取范围、写盘、把标签还原

```csharp
private void BeginEdit(NumField field)
{
    CommitEdit();
    _editing = field;
    field.Label.Visible = false;                 // 标签让位给输入框
    _editBox.MaxLength = field.Digits;
    _editBox.Size = new Size(field.Digits * 9 + 10, 16);
    _editBox.Location = field.Pos;
    _editBox.Text = field.Get().ToString();
    _editBox.Visible = true;
    _editBox.SetFocus();
}
```

`_editBox` 创建时是 `Visible = false`，而 `MirControl.Draw()` 对不可见控件直接 return，
所以**首次打开面板时它完全不参与绘制** —— 一次 `DrawToBitmap` 都不会发生。

首次点击某个数字时才建一次句柄（约 100~300ms，一次性）。收起面板时会自动提交。

## 修法 2：标签绘制成本降到 1/5

`MirLabel` 默认 `OutLine = true`，每张标签纹理要画 **5 次** `TextRenderer.DrawText`
（上下左右 4 次描边 + 1 次正文），而且默认 `DrawFormat = WordBreak`
（CJK 逐字都是断行点，断行算法很贵）。

面板底色是深色，根本不需要描边，所以统一改成：

```csharp
OutLine = false,
DrawFormat = TextFormatFlags.NoPrefix,      // 单行、不处理 & 前缀、不做断行
```

40 个标签 → 40 次 DrawText 而不是 200 次。

## 修法 3：加了两个一次性计时日志

如果还慢，直接看 `Error.txt` 里的两行就知道卡在哪：

```
[AutoFight] 首次 Show 耗时 X ms        ← 可见性变化 + 排序
[AutoFight] 首次绘制耗时 Y ms          ← 创建自身纹理 + 所有子控件纹理
```

## 效果对比

| 项 | 改前 | 改后 |
|---|---|---|
| 首次打开时的 WinForms 句柄创建 | **10 次**（每次含建句柄+截屏+换父窗口） | **0 次** |
| 标签 DrawText 次数 | ~200 次（40 标签 × 5） | ~40 次 |
| 首次点击数字输入框 | 已在打开时付过 | 一次性 ~100~300ms |

## 提醒

`Error.txt` 里那两行是临时诊断，看完可以直接删掉文件；后续版本会移除。

---

# 二十四、修：自动喝药没生效（开关是关的）

## 现象

面板里配好了 `HP < 40 %` + 药水 `(HP)DrugSmall`，血球显示 `HP 9/39`（23%），但不喝药。

## 根因：`自动喝药` 这个开关本身是「否」

截图里这一行是：

```
自动喝药   否        ← 关着
HP < 40 %  药水 (HP)DrugSmall
```

`AutoPotion.Process()` 第一行就是：

```csharp
if (!Settings.AutoPotEnabled) return;
```

所以后面的逻辑（百分比判断、找药、发包）一次都没走。

这是我上次把「自动喝药」整合进内挂面板时，给它单独加了一个开关，而：
- 代码里字段默认是 `false`
- 生成的 `Mir2Config.ini` 里被写成了 `[AutoPot] Enabled=False`
- 面板上只显示一个不起眼的「否」

内挂总开关开着、阈值和药水都配好了，唯独这个开关关着 —— 非常容易漏。

## 修了三处

### 1. 默认改成开启

```csharp
public static bool AutoPotEnabled = true;      // 默认开启：只要配了药水+阈值就该生效
```

### 2. 面板文案明确动作方向

```
自动喝药   是（点击关闭）      /      自动喝药   否（点击开启）
```

### 3. 状态栏主动提示

内挂总开关开着、但自动喝药关着时，状态行会追加提示：

```
状态：攻击 Scarecrow    （自动喝药未开启）
```

这样不用盯着开关那一行也能发现。

### 4. 直接把现有 ini 改成 True

```ini
[AutoPot]
Enabled=True
HPPercent=40
MPPercent=30
```

## 顺带确认：喝药逻辑本身没问题

| 检查项 | 结果 |
|---|---|
| HP 百分比数据源 | `AutoPotion` 用 `User.HP / User.Stats[Stat.HP]`，和血球显示 `HP 9/39` 的**同一份数据** ✓ |
| 药水查找 | 按 `ItemInfo.Index` 扫 `User.Inventory`（含快捷栏 0~5 格）✓ |
| 发包 | `C.UseItem { UniqueID, Grid = Inventory }`，和双击背包吃药完全一样 ✓ |
| 节流 | 检测间隔 1000ms + 同属性最短 700ms ✓ |

## 关于药水名显示 `(HP)DrugSmall`

那是**数据库里的物品名**（`Server.MirDB` 里配的），不是 UI 文本，
`Client/Localization/*.json` 只覆盖界面文案，不覆盖物品/怪物名。
要中文物品名得改数据库里的 `ItemInfo.Name`（服务端 DB 编辑器）。

---

# 二十五、修：先捡后打 = 否 没生效（被围攻时仍去捡东西）

## 根因：判断顺序错了 —— 先捡后寻敌

`Process()` 原来的顺序是：

```
1) 目标有效性检查（怪物死了 → _target 被清成 null）
2) 拾取决策   ← 用 _target == null 判断"周围有没有怪"
3) 寻敌
```

于是最典型的一刻：**刚打死身边一只怪** → `_target` 被清空 →
第 2 步看到 `_target == null`，误判成"周围没怪" → 立刻跑去捡远处的物品（甚至扩大到回收范围 10 格），
旁边其余的怪就白打了。而且第 2 步 `return` 掉了，根本走不到第 3 步的寻敌，
所以只要地上有东西，就会一直捡下去。

## 修法 1：把寻敌挪到拾取之前

```
1) 目标有效性 + 牵引距离
2) 寻敌（先 TargetRange，再 SeekRange）
3) 拾取决策   ← 这时 _target 是"刚刚搜出来的真实结果"
4) 没目标 → 漫游
5) 追击 / 攻击
```

这样"刚打死一只"之后，下一 tick 会**先把附近的其它怪搜出来**，
`_target != null`，就不会去捡了。

## 修法 2：`先捡后打 = 否` 改成严格模式

原来还留了一个"1 格内顺手捡"的口子：

```csharp
return Dist(user.CurrentLocation, loot.CurrentLocation) <= 1;
```

结果被围攻时会为了脚边一件物品反复挪动，观感上还是"先捡后打没生效"。现在改成字面意思：

```csharp
private static bool ShouldLootNow(UserObject user, MapObject loot)
{
    if (Settings.AutoFightLootFirst) return true;   // 先捡后打 = 是 → 看到就捡
    return _target == null;                          // 先捡后打 = 否 → 只要还有怪，一件都不捡
}
```

## 现在的完整行为

| 状态 | 先捡后打 = 是 | 先捡后打 = 否 |
|---|---|---|
| 视野内有怪 | 优先捡 | **只打，不捡** |
| 有怪但离得远（SeekRange 内） | 优先捡 | **只打，不捡** |
| 附近没怪 | 捡（拾取范围 3 格） | 捡（扩大到回收范围 10 格） |
| 捡到一半来了怪 | 继续捡 | **立刻中断，回去打** |

> 最后一行是关键差别：因为寻敌在拾取之前，捡东西途中一旦搜到怪，
> `ShouldLootNow` 立刻返回 false，`AutoPath` 会切到追击目标。

## 地上的掉落不会丢

`先捡后打 = 否` 期间没捡的东西不会漏：打完（`_target == null`）后
拾取半径自动放大到 `回收范围`（默认 10 格），会把之前漏掉的统一回收一遍。

---

# 二十六、新增：优先反击（被围攻时先打打我的怪）

## 场景

当前目标是个「不还手」的怪（稻草人、中立怪之类），旁边另有怪在打我 ——
旧逻辑会一直对着那个不还手的怪输出，完全无视正在咬自己的怪。

## 怎么知道「谁在打我」

服务端三类攻击包，字段各不相同：

| 包 | 有没有 TargetID | 处理方式 |
|---|---|---|
| `S.ObjectAttack`（近战） | ❌ 没有 | **推断**：近战命中"朝向前方一格"，`PointMove(p.Location, p.Direction, 1) == 玩家脚下` 就说明在打我 |
| `S.ObjectRangeAttack`（远程） | ✅ 有 | `p.TargetID == User.ObjectID` |
| `S.ObjectMagic`（魔法） | ✅ 有 | `p.TargetID == User.ObjectID` |

给每个 `MapObject` 加了一个字段：

```csharp
// Client/MirObjects/MapObject.cs
/// <summary>最近一次"打到我/对我施法"的时间（CMain.Time）</summary>
public long AttackedUserTime;
```

三个包的处理器里各打一次标记（`GameScene.cs`）。

## 反击逻辑（两处）

### 1. 换目标：当前目标不还手 + 附近有怪在打我

```csharp
// 条件：当前目标没在打我，且附近确实有怪在打我
if (_target != null && !IsAttackingUser(_target, now))
{
    MapObject attacker = FindRecentAttacker(user, now);
    if (attacker != null && attacker != _target)
    {
        _target = attacker;              // 立刻换去打我的那只
        TouchProgress(user, now);
        _anchor = user.CurrentLocation;
    }
}
```

`IsAttackingUser` 的时间窗是 **4 秒**（`CounterWindow`）——怪连续攻击会不断刷新，
所以只要它还在打我，就一直保持最高优先级。

### 2. 寻敌排序：正在打我的怪绝对优先

```csharp
int score = Settings.AutoFightPriority == 1 ? ob.PercentHealth * 1000 + d : d;

// 正在打我的怪 → 绝对优先
if (IsAttackingUser(ob, now))
    score -= 1000000;
```

所以即使没有当前目标、重新寻敌时，也会先扑向正在咬自己的怪。

## 不会误判

| 情况 | 会不会被标记成"在打我" |
|---|---|
| 怪在打另一个玩家/怪 | ❌ 命中格不是我的格子 |
| 怪在打我的英雄 | ❌ 命中格是英雄的格子 |
| 怪对着我挥空（我不在那格） | ❌ |
| 只是站在我旁边没攻击 | ❌ |
| 远程/魔法点名打我 | ✅（直接看 TargetID） |

## 性能

- `FindRecentAttacker` 只在「**有目标且目标不还手**」时才扫一遍对象列表，没目标时不扫
- 判断只比对 `AttackedUserTime` 时间戳，不做寻路、不调 `EmptyCell`

---

# 二十七、新增：不攻击怪物名单（用户自定义）

## 用法

内挂面板「寻敌」那一行右侧多了一个入口：

```
范围 [ 8] 格   优先级 [最近]   不打玩家 [是]   不攻击名单(3)
                                                  ↑ 点这里
```

点开是一个名单编辑器：

```
┌────────────────────────────────────┐
│ 不攻击的怪物（点击切换）             │
│ ── 已加入（点击移除）──              │
│   Scarecrow                        │  ← 红色，点一下移出名单
│   Hen                              │
│ ── 附近可见（点击加入）──            │
│   Deer                             │  ← 青色，点一下加入名单
│   Oma                              │
└────────────────────────────────────┘
```

- **已加入**：红色条目，点一下就从名单里移除
- **附近可见**：青色条目，点一下加进名单
- 再点一次「不攻击名单(N)」或关闭内挂面板 → 收起
- 每次切换立即写盘（`Mir2Config.ini`）

附近列表取的是**当前 20 格内**、名字不重复的怪物（最多显示 10 种，
多了会提示「走近一点再打开」）。

## 存储

```ini
[AutoFight]
IgnoreMonsters=Scarecrow,Hen,Deer
```

- 逗号分隔，**按名字精确匹配（不区分大小写）**
- 上限 64 条
- 名单里多余 10 条的，面板只显示前 10 条并提示直接改 ini

## 实现要点

**1. 带缓存的匹配**（避免每 tick 重新 split 字符串）

```csharp
public static bool IsMonsterIgnored(string name)
{
    if (string.IsNullOrEmpty(name)) return false;
    return IgnoreSet().Contains(name.Trim());
}

private static HashSet<string> IgnoreSet()      // 缓存，字符串变了才重建
{
    if (_autoFightIgnoreSet != null && _autoFightIgnoreCached == AutoFightIgnoreMonsters)
        return _autoFightIgnoreSet;
    ...
}
```

**2. 判定接在固定黑名单之后**

```csharp
if (ob is MonsterObject monster)
{
    if (IsNonCombat(monster)) return false;                  // 守卫/宝宝等固定排除
    if (Settings.IsMonsterIgnored(monster.Name)) return false; // 用户自定义
    if (monster.MasterObjectId != 0) return false;
    return true;
}
```

因为 `IsAttackable` 也用在 `IsValidTarget` 里，所以
**把当前正在打的目标加进名单，会立刻生效并换目标**（不用等它死）。

## 行宽校验（微软雅黑实测）

```
不攻击名单(0)     宽  83  右边界 413 < 434  ok
不攻击名单(64)    宽  90  右边界 420 < 434  ok
名单面板最高 480px（10+10 行）           ok
面板内最长行「…还有 12 个（可直接改 ini…）」右边界 289 < 294  ok
```

---

# 二十八、修：点面板开关要等一会才生效

## 根因：一次点击把整个 ini 重写了十几遍

`InIReader.Write()` 每次调用末尾都会 `Save()`：

```csharp
public void Write(string section, string key, bool value)
{
    _contents[FindIndex(section, key)] = key + "=" + value;
    Save();                      // ← 每次写一个键就整文件重写一遍
}

public void Save()
{
    try { File.WriteAllLines(_fileName, _contents); } catch { }
}
```

而面板上点一下「是/否」会调 `Settings.SaveAutoFight()`，它要写 **16 个键**：

```
Enabled / TargetRange / SeekRange / Roam / RoamRange / Priority / IgnorePlayers /
IgnoreMonsters / Chase / AttackRange / AttackDelay / Loot / LootRange / LootSweep /
LootFilter / LootFirst
```

也就是说 **一次点击 = 16 次 `File.WriteAllLines`**（整文件重写 16 遍）。
带实时扫描的杀毒软件环境下，每次 open/truncate/write/close 是 5~50ms，
16 次就是 **80~800ms** —— 表现就是"点了，过一会才生效"。
`SaveAutoPot()` 同理（6 次），客户端退出时的 `Settings.Save()` 更是接近 **130 次**。

## 修法 1：给 `InIReader` 加"挂起保存"

```csharp
public bool SuspendSave;

public void Save()
{
    if (SuspendSave) return;
    ...
}
```

批量写键时先挂起，最后统一落盘一次：

```csharp
public static void SaveAutoFight()
{
    Reader.SuspendSave = true;
    try
    {
        Reader.Write("AutoFight", "Enabled", AutoFightEnabled);
        ... 共 16 个键 ...
    }
    finally
    {
        Reader.SuspendSave = false;
        Reader.Save();          // 只写一次
    }
}
```

`SaveAutoFight()` / `SaveAutoPot()` / `Save()` 三个方法都包上了。

## 修法 2：内容没变就不写盘

```csharp
private string _savedSnapshot;      // 上次落盘时的内容快照

public void Save()
{
    if (SuspendSave) return;

    string snapshot = Snapshot();
    if (snapshot == _savedSnapshot) return;      // 内容没变，直接返回

    File.WriteAllLines(_fileName, _contents);
    _savedSnapshot = snapshot;
}
```

这样即使别处重复调 `Settings.Save()`，内容没变化也不会真的去碰磁盘。

## 效果

| 操作 | 改前 | 改后 |
|---|---|---|
| 点一下 是/否 开关 | **16 次整文件重写** | **1 次** |
| 点一下数字输入提交 | 16 + 6 = 22 次 | 2 次 |
| 改一次药水 | 6 次 | 1 次 |
| 客户端退出 | ~130 次 | 1 次 |

## 为什么之前只有这个面板明显

游戏里其他设置（音量、特效开关等）散在各个对话框里，一次点击大多只改 1~2 个键，
所以只重写 1~2 次，感觉不出来。内挂面板的开关都绑在同一个 `SaveAutoFight()` 上，
16 个键一起写，问题被放大了十几倍。

---

# 二十九、修：完全不捡东西了（先捡后打=否 + 扩大搜索 冲突）

## 根因：`_target == null` 几乎永远不成立

上一轮把「先捡后打 = 否」改成严格模式：

```csharp
return _target == null;      // 只要还有目标就一件都不捡
```

但同一轮里，寻敌是这么写的（近处没怪就**立刻**扩大到 SeekRange）：

```csharp
_target = FindTarget(now, TargetRange);
if (_target == null) _target = FindTarget(now, SeekRange);   // ← 16 格内几乎总能搜到怪
```

结果：`_target` 在整个战斗循环里**几乎永远非空** →
`lootAllowed` 永远为 false → **一次都不捡**。打死身边的怪之后，
16 格里随便有只远处的怪就继续追，掉落全被无视。

## 修法：把「远处寻敌」挪到拾取之后

现在 `Process()` 的分层是：

```
1) 目标有效性 + 牵引距离
1b) 优先反击（有怪在打我）
2)  近处寻敌     ← 只用 TargetRange（默认 8）
3)  拾取         ← 身边没怪就在这里捡
4)  远处寻敌     ← 捡完了才去 SeekRange（默认 16）找远怪
4b) 漫游
5)  追击 / 攻击
```

关键改动：

```csharp
// 2) 近处寻敌：只找身边的
if (_target == null && FindNearTarget(user, now, out MapObject near))
    AcquireTarget(near, user, now);

// 3) 拾取
bool lootAllowed = Settings.AutoFightLoot
                   && (Settings.AutoFightLootFirst || _target == null);

if (lootAllowed)
{
    int lootRange = Settings.AutoFightLootRange;
    if (_target == null && Settings.AutoFightLootSweep > lootRange)
        lootRange = Settings.AutoFightLootSweep;      // 身边清干净 → 放大到回收范围

    MapObject loot = FindLoot(lootRange);
    if (loot != null) { DoLoot(scene, user, loot, now); return; }
}

// 4) 远处寻敌：掉落都处理完了才看远处的怪
if (_target == null && Settings.AutoFightSeekRange > Settings.AutoFightTargetRange
    && FindNearTarget(user, now, out MapObject far, Settings.AutoFightSeekRange))
    AcquireTarget(far, user, now);
```

## 现在的行为矩阵

| 情况 | 先捡后打 = 是 | 先捡后打 = 否 |
|---|---|---|
| 身边有怪（TargetRange 内） | 有掉落就先捡 | **只打，不捡** |
| 身边没怪，地上有掉落 | 捡（拾取范围 3 格） | **捡（放大到回收范围 10 格）** |
| 身边没怪，地上没掉落，远处有怪 | 去追 | **去追** |
| 身边没怪，地上没掉落，远处也没怪 | 漫游 | 漫游 |

「先捡后打 = 否」的完整语义现在是：**打完身边的怪 → 把附近掉落捡干净 → 再去找远处的怪**。

## 顺带清理

`ShouldLootNow()` 这个函数在新结构下已经多余（"能不能捡"由 `lootAllowed` 直接表达），
已删除，避免两处逻辑打架。

---

# 三十、不攻击名单支持「带前缀的强化版」

## 需求

名单里加了 `deer`，`非凡_deer` 这种带前缀的强化版也一并不要打。

## 匹配规则

```csharp
private static readonly char[] MonsterNameSeparators = { '_', (char)92, '/' };   // 92 = 反斜杠

public static bool IsMonsterIgnored(string name)
{
    string n = name.Trim();
    HashSet<string> set = IgnoreSet();
    if (set.Count == 0) return false;

    if (set.Contains(n)) return true;                    // ① 完全一致

    int cut = n.LastIndexOfAny(MonsterNameSeparators);    // ② 取最后一个分隔符后的那一段
    if (cut >= 0 && cut + 1 < n.Length && set.Contains(n.Substring(cut + 1)))
        return true;

    return false;
}
```

也就是**取名字里最后一个分隔符（`_` / `\` / `/`）之后的那一段**去比对。

## 用真实 DB 验证

`Server.MirDB` 里含 deer 的怪物名：

| 名字 | 名单 = `deer` 时 |
|---|---|
| `Deer` | 不打 ✓（完全一致） |
| `Provinces\Deer` | 不打 ✓（最后一个 `\` 之后是 Deer） |
| `非凡_deer`（示例） | 不打 ✓（最后一个 `_` 之后是 deer） |
| `Frozen_deer`（示例） | 不打 ✓ |
| `Deer1` | **打**（数字后缀不算前缀变体） |
| `DeerBlood` / `DeerHorn` | 打（那是物品名，不是怪物） |

## 为什么用"最后一个分隔符"而不是简单的 `Contains`

如果用 `名称.Contains(条目)`：

- 名单里加 `oma` → `OmaKing`、`OmaGuard`、`OmaValley` 全被误伤
- 名单里加 `deer` → `DeerBlood`（物品）也会被误判

用分隔符切段就精确得多：**只认"前缀 + 分隔符 + 条目"这种结构**。

## 与名单编辑器联动

名单编辑器里「附近可见」的候选列表用的也是 `Settings.IsMonsterIgnored()`，
所以加了 `deer` 之后，`非凡_deer` 这类变体会**自动从候选列表里消失**，不会重复添加。

## 注意：数字后缀不匹配

`Deer1`、`Deer2` 这种**数字后缀**的变体不在规则内（可能和不同的怪撞名）。
如果你们服的强化版是数字后缀命名的，需要把它们单独加进名单（名单编辑器里点一下就加）；
要支持数字后缀我再加一条规则即可。

---

# 三十一、拾取前检查背包空格

## 问题

背包满了以后，`C.PickUp` 会被服务端**默默拒绝**（没有回包、没有提示），
客户端会一直站在物品上每 200ms 发一次，直到 3 秒后被卡住检测拉黑 ——
表现就是"人物杵在那儿不动也不捡走"。

## 修法

### 1. `UserObject` 暴露背包空格数

```csharp
/// <summary>背包空格子数（含快捷栏 0~5 格）。0 = 满了，捡东西会被服务端拒绝。</summary>
public int FreeInventorySlots => FreeSpace(Inventory);
```

`Inventory` 是长度 46 的数组，前 `BeltIdx`（6）格就是快捷栏 —— 所以这个数字天然包含快捷栏。

### 2. 寻物阶段直接跳过

```csharp
bool bagFull = user.FreeInventorySlots <= 0;

if (lootAllowed && !bagFull)
{
    ... FindLoot / DoLoot ...
}
```

背包满时**连寻物都不做**，直接去打怪 / 漫游，不会为了捡不了的东西白跑。

### 3. 走过去途中再兜一次

背包可能在"走向物品"的途中被塞满（比如另一个掉落自动进了包），
所以 `DoLoot` 开头再判一次：

```csharp
if (user.FreeInventorySlots <= 0)
{
    if (scene.MapControl.AutoPath) scene.MapControl.AutoPath = false;
    Status = "状态：背包已满";
    return;
}
```

### 4. 状态提示

| 情况 | 面板底部显示 |
|---|---|
| 有怪在打、背包满 | `状态：攻击 xxx`（战斗状态优先） |
| 没怪、背包满、开了漫游 | `状态：漫游寻敌` |
| 没怪、背包满、关了漫游 | **`状态：背包已满，停止拾取`** |
| 走向物品途中背包满了 | **`状态：背包已满`** |

## 为什么不用"先看看能不能放下"这种复杂判断

`C.PickUp` 本身没有目标参数（服务端捡的是**玩家脚下这一格**的掉落），
客户端无法预知具体捡哪件、需要几个格子。
但**"有没有空格"这个判断已经足够**：

- 有空格 → 发出去大概率成功
- 没空格 → 100% 失败，不如直接不打这个包

真遇到"物品需要的格子数 > 空格数"（比如可叠加的药水其实不需要空格），
服务端会正常处理，不需要客户端干预。

---

# 三十二、新增：道士自动治疗术

## 配置

面板「恢复」段新增一行：

```
── 恢复 ──
自动喝药 是（点击关闭）      检测间隔 [1000] 毫秒
HP < [40] %   药水 (HP)DrugSmall
MP < [30] %   药水 未设置
治疗术：是   [60] %    道士专用（血量百分比，0=关闭）   ← 新增
```

```ini
[AutoPot]
HealEnabled=True
HealPercent=60      ; 血量低于 60% 就放治疗术，0 = 关闭
```

## 实现：走客户端原有的施法通道

```csharp
ClientMagic magic = user.GetMagic(Spell.Healing);
if (magic == null) return false;                                  // 没学治疗术

int cost = magic.Level * magic.LevelCost + magic.BaseCost;
if (user.Stats[Stat.ManaPenaltyPercent] > 0)
    cost += (cost * user.Stats[Stat.ManaPenaltyPercent]) / 100;
if (cost > user.MP) return false;                                 // 蓝不够
if (CMain.Time <= magic.CastTime + magic.Delay) return false;      // 冷却中

user.NextMagic = magic;
user.NextMagicObject = user;                                      // 治疗自己
user.NextMagicLocation = user.CurrentLocation;
user.NextMagicDirection = user.Direction;
```

只需要设置 `NextMagic*` 这几个字段，剩下的全由原有流程处理：

```
GameScene.CheckInput()
  └─ if (User.NextMagic != null && !User.RidingMount) { UseMagic(User.NextMagic, User); return; }
       └─ UseMagic()   ← 冷却校验、扣蓝、射程校验、组包
            └─ User.QueuedAction = MirAction.Spell { Spell, TargetID, Location, Level }
                 └─ PlayerObject.Process() 在施法帧发 C.Magic
```

所以**和手动按技能键完全等价**，不存在任何自定义协议。

`Spell.Healing` 在 `UseMagic` 的 switch 里有专门分支：

```csharp
case Spell.Purification:
case Spell.Healing:
case Spell.UltimateEnhancer:
...
    if (actor.NextMagicObject != null)
    {
        if (!actor.NextMagicObject.Dead && ... && !(monster && monster.MasterObjectId == 0))
            target = actor.NextMagicObject;
    }
```

传自己（`User`）能正常通过判定 → 服务端收到 `TargetID = 自己` → 治疗生效。

## 前置检查（避免刷屏）

`UseMagic` 自己做校验时会 `OutputMessage` 提示（"魔法值不足" / "还需 N 秒"），
所以 `TryHeal` 先把这些条件自己判一遍，不满足就直接不设置 `NextMagic`：

| 检查 | 原因 |
|---|---|
| `Class == MirClass.Taoist` | 只给道士用 |
| `GetMagic(Spell.Healing) != null` | 没学就别试 |
| `NextMagic == null` | 已经在施法了 |
| `!RidingMount` | 骑马不能施法 |
| `CMain.Time >= GameScene.SpellTime` | 全局施法锁 |
| `CMain.Time > CastTime + Delay` | 冷却中 |
| `cost <= MP`（含 ManaPenaltyPercent） | 蓝不够 |

## 施法优先级：治疗术 > 喝红药

```csharp
bool healed = TryHeal(user, now);

if (!Settings.AutoPotEnabled) return;
if (!healed) TryAutoUse(user, Stat.HP, now);   // 已经放了治疗术就不用再喝红
TryAutoUse(user, Stat.MP, now);
```

治疗术不消耗道具，能省药。蓝药照喝（治疗本身要蓝）。

> 注意：治疗术现在**只治自己**。要顺带给英雄/队友治疗，
> 把 `NextMagicObject` 换成对应对象即可（`MapObject.Hero` 或队伍成员），需要的话我可以加。

## 面板高度

恢复段多了一行，面板 380 → **404**，拾取段各行整体下移 24px：

```
y=280  治疗术：是  [60] %   道士专用（血量百分比，0=关闭）
y=306  ── 拾取 ──
y=330  自动拾取 / 范围 / 过滤 / 先捡后打
y=354  回收范围
y=380  状态
```

行宽实测：`治疗术：是`(10-78) / 输入框(80-108) / `%`(112-131) / 提示(140-315)，
状态行底部 397 < 面板高 404，全部无溢出。

---

# 三十三、UI 重构：内挂面板改成 Tab 布局

## 新布局

```
┌────────────────────────────────────────────────────┐
│ 内挂（自动战斗）                              [X]  │
├────────────────────────────────────────────────────┤
│  战斗    恢复    拾取    职业                       │  ← 点击切换，当前页金色 + 下划线
│  ────                                              │
│                                                    │
│ （当前 Tab 的内容区）                               │
└────────────────────────────────────────────────────┘
   440 × 240（原来是 440 × 404 的单页长面板）
```

## 四个 Tab 的内容

### 战斗（原「寻敌」+「战斗」+「状态」合并）

```
已开启（点击关闭）                      ← 内挂总开关
── 寻敌 ──
范围 [ 8] 格   优先级 [最近]   不打玩家 [是]   不攻击名单(3)
扩大搜索 [16] 格   漫游 [是]   半径 [ 6] 格
── 战斗 ──
追击 [是]   攻击距离 [ 1] 格   攻击间隔 [ 300] 毫秒
状态：攻击 Scarecrow
```

### 恢复

```
自动喝药 是（点击关闭）   检测间隔 [1000] 毫秒
HP < [40] %   药水 (HP)DrugSmall
MP < [30] %   药水 未设置
血量/魔法低于阈值时自动喝药
```

### 拾取

```
自动拾取 是   范围 [ 3] 格   过滤 [全部]   先捡后打 [否]
回收范围 [10] 格   周围没怪时回收远处掉落（0=关闭）
```

### 职业（新增）

```
── 道士 ──
治疗术：是   [60] %   血量低于该百分比自动放治疗术（0=关闭）
```

> 治疗术从「恢复」页挪到了新的「职业」页，以后别的职业的专属功能也往这里加。

## 实现方式

### 1. 所有控件只创建一次，用 `Visible` 切页

```csharp
private readonly List<MirControl>[] _pages = { new(), new(), new(), new() };
private int _buildPage;      // 构建时指定当前控件属于哪一页

private T Reg<T>(T control) where T : MirControl
{
    if (control != null) _pages[_buildPage].Add(control);
    return control;
}
```

所有工厂方法（`Lbl` / `Section` / `Hint` / `Click` / `AddNum`）都经过 `Reg()` 自动登记到当前页，
构建完 `SelectTab(0)` 一次性把非当前页设为不可见。

这样**不会因为切 Tab 反复创建/销毁控件**，切换是零成本的。

### 2. 切页前的收尾

```csharp
private void SelectTab(int index)
{
    if (_tab == index) return;

    CommitEdit();          // 正在编辑的数字先提交
    ClosePicker();         // 药水选择框收起
    CloseIgnorePicker();   // 名单编辑器收起

    _tab = index;
    // 1) Tab 文字变色（当前页金色，其它灰色）
    // 2) 下划线移到当前 Tab 下面
    // 3) 各页「当前页可见 / 其它页隐藏」
}
```

### 3. 编辑数字时的页归属

`CommitEdit()` 结束时要把值标签恢复显示，但**不能把别的页的标签也显示出来**：

```csharp
_editBox.Visible = false;
field.Label.Visible = _tab == IndexOfPage(field);   // 只在自己所属页显示
```

## 尺寸与位置

| 项 | 值 |
|---|---|
| 面板 | 440 × **240**（原 404） |
| Tab 文字 | y=30，x = 24 / 96 / 168 / 240 |
| Tab 下划线 | y=47，宽 32（与 2 字标签等宽） |
| 内容首行 | y=56 |
| 内容行距 | 24px |
| 状态行 | y=204（只有「战斗」页有） |

## 行宽实测（微软雅黑）

```
Tab:  战斗(24-56) 恢复(96-128) 拾取(168-200) 职业(240-272)
战斗 y=104  末项「不攻击名单(12)」右边界 400 < 434  ok
战斗 y=128  末项「格」右边界 310 < 434              ok
战斗 y=176  末项「毫秒」右边界 384 < 434            ok
状态行 y=204 底 221 < 面板高 240                    ok
提示文字最长 219px（职业页，起点 140 → 359）        ok
```

---

# 三十四、新增：道士自动施毒（绿毒 / 红毒）

## 界面（职业 Tab）

```
── 道士 ──
治疗术：是   [60] %    血量低于该百分比自动放治疗术（0=关闭）

── 施毒 ──
[✓] 绿毒   [ ] 红毒    当前毒药：绿毒
```

勾选框用的是游戏自带的 `MirCheckBox`（`Libraries.Prguse` 2086/2087，和游戏商城同一个勾选框图形）。

## 关键机制：毒的类型由**装备的毒药**决定

服务端：

```csharp
// HumanObject.GetPoison() —— 注意是扫 Equipment，不是 Inventory
for (int i = 0; i < Info.Equipment.Length; i++)
{
    UserItem item = Info.Equipment[i];
    if (item != null && item.Info.Type == ItemType.Amulet && item.Count >= count)
    {
        if (shape == 0) { if (item.Info.Shape == 1 || item.Info.Shape == 2) return item; }
        ...
    }
}

// Spell.Poisoning 生效时按道具 Shape 决定毒的类型
switch (item.Info.Shape)
{
    case 1: → PType = PoisonType.Green   // 绿毒
    case 2: → PType = PoisonType.Red     // 红毒
}
```

所以在 Crystal 里：

| 项 | 说明 |
|---|---|
| 毒药放哪 | **`EquipmentSlot.Amulet`（护身符槽）**，`ItemType.Amulet` |
| `Shape == 1` | 绿毒 |
| `Shape == 2` | 红毒 |
| `Shape == 0` | 护身符（放毒用不了） |

**同时带绿毒和红毒没有意义** —— 装备槽只有一个位置，实际只能用装上的那一种。
客户端的勾选框是「允许自动补哪种毒」，真正能放哪种由装备决定，所以面板上会显示
`当前毒药：绿毒 / 红毒 / 护身符（不能施毒） / 未装备`。

## 施毒逻辑

```csharp
private static bool TryPoison(UserObject user, MapObject target, long now, int attackRange)
{
    if (user.Class != MirClass.Taoist) return false;
    if (!Settings.AutoPoisonGreen && !Settings.AutoPoisonRed) return false;

    UserItem poison = user.Equipment[(int)EquipmentSlot.Amulet];
    bool isGreen = poison.Info.Shape == 1;
    bool isRed   = poison.Info.Shape == 2;
    if (!isGreen && !isRed) return false;                 // 装的是护身符

    if (isGreen && !Settings.AutoPoisonGreen) return false;   // 用户没勾这类
    if (isRed   && !Settings.AutoPoisonRed)   return false;

    PoisonType want = isGreen ? PoisonType.Green : PoisonType.Red;
    if (target.Poison.HasFlag(want)) return false;        // 毒还在，不用补

    ClientMagic magic = user.GetMagic(Spell.Poisoning);
    ...冷却 / 蓝耗 / 射程 检查...
    user.NextMagic = magic;                               // 走客户端原生施法通道
    user.NextMagicObject = target;
    ...
}
```

调用位置在「追击/攻击」之前：

```csharp
// ---------- 3.5) 施毒（道士） ----------
if (TryPoison(user, _target, now, wantRange)) { TouchProgress(user, now); return; }
```

**施毒优先级高于普通攻击** —— 毒会持续掉血/降防，先上毒收益更大。
状态栏会显示 `状态：施毒(绿) 牛魔战将`。

## 防浪费

| 机制 | 说明 |
|---|---|
| 目标已有该毒 | `target.Poison.HasFlag(...)` 直接跳过，毒在就不补 |
| 施毒后冷却 | 2.5 秒内不再尝试（服务端毒有 500ms 延迟 + 走 DotTick，别连着扔毒药） |
| 魔法冷却 | 额外检查 `magic.CastTime + magic.Delay` |
| 蓝不够 | 含 `ManaPenaltyPercent` 的完整算法，不够就不放 |

## 配置

```ini
[AutoFight]
PoisonGreen=False
PoisonRed=False
```

## 行宽实测

```
治疗术：是        宽  68  右边界  78（输入框起点 80）
治疗提示(8pt)     宽 219  右边界 359 < 434   ok
当前毒药(最长)     宽 176  右边界 376 < 434   ok
内容底部 152 < 面板高 240                    ok
```

---

# 三十五、施毒自动换毒药

## 需求

勾了绿毒/红毒之后，如果装备槽里不是对应毒药，就自动从背包里找出来装上再施毒；找不到就不放。

## 完整流程

```
TryPoison()
 ├─ 目标缺哪种毒？  缺绿 / 缺红 / 都缺 / 都不缺（都不缺就直接收工）
 ├─ 看 Amulet 槽里装的是什么（Shape 1=绿 2=红 0=护身符）
 │
 ├─ ① 装的就是目标缺的那种 → 直接施法 Spell.Poisoning
 ├─ ② 装的不是 → 去背包（含快捷栏）找对应 Shape 的毒药
 │      ├─ 找到 → 发 C.EquipItem 换装，本轮不施法（下一 tick 再走一遍①）
 │      └─ 没找到 → 本轮不施法
 └─ 切换 Tab / 目标已有毒 / 蓝不够 / 冷却中 → 不动作
```

「优先用已经装好的」这条很关键：如果当前装的是绿毒、目标缺绿毒，
就不会为了补齐红毒多做一次换装。

## 换装只用发一个包

服务端 `PlayerObject.EquipItem` 里是**自动互换**的：

```csharp
array[index] = toArray[to];    // 原来装备的那件塞回新装备拿出来的背包格子
toArray[to] = temp;            // 新的装上
```

所以不需要"先脱再穿"：

```csharp
Network.Enqueue(new C.EquipItem
{
    Grid = MirGridType.Inventory,
    UniqueID = item.UniqueID,
    To = (int)EquipmentSlot.Amulet,
});
```

客户端收到 `S.EquipItem` 后会把两边物品互换（`CharacterDialog.Grid[To]` ↔ `InventoryDialog/BeltDialog`），
所以 `User.Equipment[(int)EquipmentSlot.Amulet]` 会自动更新成新毒药。

## 防抖

| 机制 | 说明 |
|---|---|
| 换装冷却 1.5 秒 | 等服务器回包，不会连着发装备包 |
| 施毒冷却 2.5 秒 | 服务端的毒有 500ms 延迟，别连着扔毒药 |
| 背包没有对应毒药 | 直接不施法，不会空按技能 |
| 装备槽本来就是对的 | 完全跳过换装逻辑 |

## 界面

```
── 施毒 ──
[✓] 绿毒   [ ] 红毒    当前毒药：绿毒
勾选的毒若没装备，会自动从背包装上（需已学施毒术）
```

`当前毒药` 实时显示装备槽里的东西：`绿毒` / `红毒` / `护身符（不能施毒）` / `未装备`。

状态栏会显示 `状态：换毒药(红)` / `状态：施毒(红) 牛魔战将`，能看出在干什么。

## 行宽实测

```
提示(8pt) 宽 282 右边界 292 < 434   ok
内容底部 173 < 面板高 240            ok
```

---

# 三十六、职业页按职业显示 + 内挂配置按角色隔离

## 一、职业 Tab 按职业显示

目前职业页只有道士的内容，非道士角色打开只会看到一行提示。

```csharp
/// <summary>当前角色是不是道士</summary>
private static bool TaoistVisible
{
    get { return GameScene.User != null && GameScene.User.Class == MirClass.Taoist; }
}

/// <summary>按「当前 Tab + 当前职业」决定每个控件的可见性</summary>
private void ApplyVisibility()
{
    bool taoist = TaoistVisible;

    for (int i = 0; i < _pages.Length; i++)
    {
        bool showPage = i == _tab;

        foreach (MirControl control in _pages[i])
        {
            bool show = showPage;

            // 职业页里道士专属的控件，非道士就隐藏
            if (show && i == ClassPage && !taoist && _taoistControls.Contains(control))
                show = false;

            control.Visible = show;
        }
    }

    // 非本职业时显示"本职业暂无专属功能"
    if (_noClassHint != null)
        _noClassHint.Visible = _tab == ClassPage && !taoist;
}
```

控件登记是自动的 —— 构建职业页（`_buildPage == 3`）时经过 `Reg()` 的控件会同时进 `_taoistControls`，
以后加别的职业只要按同样的方式分组即可。

可见性在两种时机重算：

| 时机 | 触发 |
|---|---|
| 切 Tab | `SelectTab()` 末尾 |
| 职业变化（刚登录 / 换角色） | `Refresh()` 里比对 `_classVisibleCache` |

## 二、内挂配置按角色隔离

### 存储位置

```
<客户端目录>\
├── Mir2Config.ini            ← 主配置（图形/声音/资源下载…，内挂段作为新角色的默认值）
└── AutoFight\
    ├── 沙巴克城主.ini         ← 每个角色一份内挂配置
    ├── 小道士.ini
    └── ...
```

角色名里的非法文件名字符（`\ / : * ? " < > |`）会被替换成 `_`。

### 隔离范围

| 段 | 是否按角色 |
|---|---|
| `[AutoFight]`（寻敌 / 战斗 / 拾取 / 施毒 / 总开关） | ✅ 独立 |
| `[AutoPot]`（自动喝药 / 治疗术 / 检测间隔） | ✅ 独立 |
| `[Graphics]` `[Sound]` `[Network]` `[Download]` `[Launcher]` … | ❌ 全局共用 |

### 实现

```csharp
/// <summary>当前载入的内挂配置所属角色名（空 = 还没进游戏）</summary>
public static string AutoFightProfile = "";

private static InIReader ProfileReader;

/// <summary>内挂配置写到哪里：有角色档案就写档案，否则写主配置</summary>
private static InIReader AutoIni => ProfileReader ?? Reader;
```

`SaveAutoFight()` / `SaveAutoPot()` 都改成写 `AutoIni`，其它保存照旧写主配置。

读取则抽成了一个共用函数，主配置加载和按角色加载都走它，键名和夹取逻辑只有一份：

```csharp
private static void ReadAutoConfig(InIReader r) { ...18 个键 + 夹取... }

// Settings.Load()
ReadAutoConfig(Reader);

// LoadAutoFightProfile()
if (File.Exists(file)) ReadAutoConfig(ProfileReader);
```

### 切换角色的检测

`AutoFight.Process()` 第一件事就是 `EnsureProfile()`：

```csharp
public static void EnsureProfile()
{
    UserObject user = GameScene.User;
    if (user == null) return;

    string name = user.Name;
    if (string.IsNullOrEmpty(name)) return;
    if (Settings.AutoFightProfile == name) return;      // 没换角色

    Stop();                       // 清掉上一个角色的跑图状态
    AutoPotion.Reset();

    Settings.LoadAutoFightProfile(name);
}
```

### 新角色的初始配置

第一次用某个角色时，会把**当前内存里的内挂配置**落一份成它的档案，
但 **`AutoFightEnabled` 强制设为 false** —— 免得一进游戏就自动开打。
喝药/治疗/施毒的其它设置会继承下来。

所以行为是：

| 情况 | 结果 |
|---|---|
| 老角色（档案已存在） | 完全恢复上次的设置（包括自动战斗开关状态） |
| 新角色 | 继承其它设置，但自动战斗默认关闭，需要自己点开一次 |
| 同角色重新登录 | 沿用内存里的值，不重复读盘 |

## 校验

```
Client 编译通过、启动无错误
AutoFight\ 目录在首次进游戏时自动创建（未登录时不创建，符合预期）
```

---

# 三十七、普攻开关 + 法师自动技能

## 一、普通攻击开关（战斗 Tab）

```
── 战斗 ──
追击 [是]   攻击距离 [ 1] 格
攻击间隔 [ 300] 毫秒    普攻 [是]
状态：攻击 Scarecrow
```

关掉「普攻」后，自动战斗**不会做普通攻击**（只会施毒 / 放技能）。

```csharp
// 法师技能放不出来时才会走到这里
Status = "状态：攻击 " + _target.Name;

if (!Settings.AutoFightBasicAttack)
{
    Status = "状态：普攻已关闭";
    return;
}
```

面板高度 240 → **260**，攻击间隔挪到第二行给普攻腾位置。

## 二、法师自动技能（职业 Tab）

```
── 法师 ──
自动技能 [是]   技能 [FireBall]
法师专用：选中技能对目标连续释放
```

点「技能」弹出已学魔法列表（按 Spell 排序，当前选中的显示黄色）：

```
┌──────────────────────────┐
│ 选择技能（点击设置）       │
│ ── 已学魔法 ──            │
│ 不使用（清除）             │
│ FireBall  Lv3            │
│ GreatFireBall  Lv1       │
│ ThunderBolt  Lv2         │
│ …                        │
└──────────────────────────┘
```

列表来自客户端本地的 `User.Magics`，不需要额外请求。最多显示 14 个。

## 施法优先级

```
目标在攻击距离内
  ├─ ① 法师技能（TryCastSkill）  ← 放得出来就放，本轮不再普攻
  ├─ ② 普攻（受开关控制）
  └─ ③ 都没有 → 站着（状态栏会提示"普攻已关闭"）
```

施毒（道士）在更前面（`3.5` 步），所以道士的施毒优先级高于普攻。

## 实现

和施毒 / 治疗术同一套路 —— 只设置 `NextMagic*`，发包/冷却/扣蓝交给原生流程：

```csharp
ClientMagic magic = user.GetMagic((Spell)Settings.AutoSkillSpell);
if (magic == null) return false;                          // 没学 / 换职业了

if (CMain.Time <= magic.CastTime + magic.Delay) return false;    // 冷却中
int range = Math.Max(attackRange, (int)magic.Range);             // 射程取两者较大的
if (!Functions.InRange(...)) return false;
if (cost > user.MP) return false;                                // 蓝不够

user.NextMagic = magic;
user.NextMagicObject = target;
user.NextMagicLocation = target.CurrentLocation;
user.NextMagicDirection = Functions.DirectionFromPoint(...);
```

`Spell.FireBall` / `GreatFireBall` / `ThunderBolt` 等攻击法术在 `UseMagic` 的
「Targeting」分支里，会读取 `NextMagicObject` 作为目标 ✓。

只对 `MirClass.Wizard` 生效（和职业页的显示范围一致）。

## 配置

```ini
[AutoFight]
BasicAttack=True     ; 普通攻击开关
SkillEnabled=False   ; 自动技能
SkillSpell=0         ; 选中的技能 (int)Spell，0 = 未选择
```

## 行宽实测

```
y=200  攻击间隔(10-66) box(74-120) 毫秒(124-156) 普攻(200-232) 是(236-256)   ok
y=206  自动技能(10-66) 是(76-96) 技能(116-148) 名称(152-202)                 ok
y=232  提示(8pt) 宽 183 右边界 193 < 434                                    ok
内容底部 247 < 面板高 260                                                   ok
```

---

# 三十八、修：法师进游戏后职业 Tab 是空的

## 根因：控件归组写成了"整页都算道士"

上一版按职业显示时是这么登记的：

```csharp
private T Reg<T>(T control) where T : MirControl
{
    _pages[_buildPage].Add(control);

    // 职业页目前只做了道士，整页控件都算"道士专属"
    if (_buildPage == ClassPage)
        _taoistControls.Add(control);       // ← 法师的控件也被算进道士组
    ...
}
```

后来在同一个页里加了法师的「自动技能 / 技能」，这些控件同样进了 `_taoistControls`。
于是 `ApplyVisibility()` 里：

```csharp
if (_taoistControls.Contains(control) && cls != MirClass.Taoist)
    show = false;                            // 法师 → 整页全灭
```

法师一切进来，职业页所有控件都被判定成"道士专属"而隐藏 → 空白页。

## 修法：按职业分成两组，构建时打标记

```csharp
private readonly List<MirControl> _taoistControls = new List<MirControl>();
private readonly List<MirControl> _wizardControls = new List<MirControl>();
private MirClass _buildClass;               // 构建职业页时当前分组属于哪个职业

private T Reg<T>(T control) where T : MirControl
{
    _pages[_buildPage].Add(control);

    if (_buildPage == ClassPage)
    {
        if (_buildClass == MirClass.Taoist)      _taoistControls.Add(control);
        else if (_buildClass == MirClass.Wizard) _wizardControls.Add(control);
    }

    return control;
}
```

构建时切换标记：

```csharp
_buildPage  = 3;
_buildClass = MirClass.Taoist;      // ── 道士 ── / ── 施毒 ── 及以下
...
_buildClass = MirClass.Wizard;      // ── 法师 ── 及以下
```

判定也改成两边对称：

```csharp
if (_taoistControls.Contains(control) && cls != (int)MirClass.Taoist) show = false;
else if (_wizardControls.Contains(control) && cls != (int)MirClass.Wizard) show = false;
```

## 顺带修：法师块自动上移

道士块隐藏时，法师块本来会在 y=182 开始，上面留一大片空白。
现在把法师块所有控件的**构建时 y** 记下来，按职业整体平移：

```csharp
private const int WizardTop = 182;
private const int ClassTop  = Row0;      // 56

int shift = (cls == (int)MirClass.Wizard) ? ClassTop - WizardTop : 0;   // -126

foreach (KeyValuePair<MirControl, int> pair in _wizardBaseY)
    pair.Key.Location = new Point(pair.Key.Location.X, pair.Value + shift);
```

所以两种职业看到的都是紧凑布局：

| 职业 | 职业 Tab |
|---|---|
| 道士 | `── 道士 ──` 治疗术 → `── 施毒 ──` 绿/红毒 → **（法师块隐藏）** |
| 法师 | **（道士块隐藏）** → `── 法师 ──` 自动技能 / 技能（上移到 y=56 开始） |
| 其他职业 | `本职业暂无专属功能（目前支持道士 / 法师）` |

## 变化检测

```csharp
private int _classCache = -1;

private static int CurrentClass => GameScene.User == null ? -1 : (int)GameScene.User.Class;

// Refresh() 里
if (_classCache != CurrentClass)
{
    _classCache = CurrentClass;
    ApplyVisibility();
}
```

走 Tab 切换（`SelectTab`）时也会重算一次，所以登录后第一次打开面板就是正确的。

---

# 三十九、修：法师关掉普攻还是会跑过去贴脸

## 根因：追击用的是「近战距离」，不是「技能射程」

```csharp
int wantRange = Math.Max(1, Settings.AutoFightAttackRange);   // 默认 1 格
int dist      = Dist(user.CurrentLocation, _target.CurrentLocation);

// ---------- 4) 追击 ----------
if (dist > wantRange)     // ← 怪在 5 格外、技能明明有 9 格射程，也照样跑过去
{
    MoveTo(ResolveChaseDest(...));
    return;
}
```

`AutoFightAttackRange` 是给近战用的（默认 1），技能射程完全没参与判定。
于是法师无论开不开普攻，都会先追到怪身边 1 格才停 —— 观感就是"一路小跑过去"。

追到身边之后又因为 `dist <= wantRange` 落进攻击分支，
如果那一刻普攻开关正好是开的，就顺手砍一刀 —— 所以看起来"一边近战一边放技能"。

## 修法 1：交战距离 = max(近战距离, 技能射程)

```csharp
private static int SkillRange(UserObject user)
{
    if (!Settings.AutoSkillEnabled || Settings.AutoSkillSpell <= 0) return 0;
    if (user == null || user.Class != MirClass.Wizard) return 0;

    ClientMagic magic = user.GetMagic((Spell)Settings.AutoSkillSpell);
    return magic == null ? 0 : magic.Range;
}

int wantRange    = Math.Max(1, Settings.AutoFightAttackRange);
int skillRange   = SkillRange(user);
int engageRange  = Math.Max(wantRange, skillRange);     // ← 交战距离

// 追击判定改用 engageRange
if (dist > engageRange) { MoveTo(...); return; }
```

法师开了技能后，怪在 9 格内就**原地施法**，不会往前走。

## 修法 2：普攻要自己再判一次距离

```csharp
// 技能射程内但够不着近战 → 等技能冷却，别原地空挥
if (dist > wantRange)
{
    Status = "状态：等待技能 " + _target.Name;
    return;
}

if (!Settings.AutoFightBasicAttack)
{
    Status = "状态：普攻已关闭";
    return;
}
```

以前只要进了攻击分支就直接 `Attack()`（近战是"打前方一格"），
现在多一道 `dist > wantRange` 的闸，站在 5 格外绝不会挥空刀。

## 现在的行为（法师，技能 FireBall 射程 9，普攻=否）

| 怪物距离 | 行为 |
|---|---|
| 10 格以上 | 追击（走到 9 格内） |
| 9 格内、蓝够、冷却好 | **原地放技能**，不移动 |
| 9 格内、冷却中 / 蓝不够 | 站着等（`状态：等待技能 xxx`），不靠近 |
| 贴到 1 格 | 依旧放技能（射程内），不挥拳 |

普攻开关打开时，只有贴到 `攻击距离`（默认 1 格）内、且技能放不出来才会近战。

## 状态提示

```
状态：技能 FireBall → 牛魔战将     ← 正常输出
状态：等待技能 牛魔战将            ← 在技能射程内等冷却/回蓝
状态：普攻已关闭                  ← 贴脸且技能不可用
状态：攻击 牛魔战将                ← 普攻（仅当开关打开）
```

---

# 四十、修：关掉普攻还是会砍（元凶是客户端原生的自动索敌攻击）

## 真正的元凶不在自动战斗代码里

`GameScene.CheckInput()` 每帧都跑这段（**客户端原生逻辑**）：

```csharp
if (MapObject.TargetObject != null && !MapObject.TargetObject.Dead)
{
    if (... TargetObject is MonsterObject ...)
    {
        ...
        // 近战分支
        else if (Functions.InRange(MapObject.TargetObject.CurrentLocation, User.CurrentLocation, 1))
        {
            if (CMain.Time > GameScene.AttackTime && CanRideAttack() && !User.Poison.HasFlag(PoisonType.Dazed))
            {
                User.QueuedAction = new QueuedAction { Action = MirAction.Attack1, ... };
                return;
            }
        }
    }
}
```

**只要 `MapObject.TargetObject` 有值、且目标贴身 1 格，客户端就会自己发起 `Attack1`** ——
跟内挂的普攻开关毫无关系。

而自动战斗为了让技能能拿到目标、并显示目标高亮，每 tick 都会：

```csharp
if (MapObject.TargetObjectID != _target.ObjectID)
    MapObject.TargetObjectID = _target.ObjectID;
```

于是：法师站在技能射程内打技能 → 怪一贴到 1 格 → `CheckInput` 自动砍 → **一边放技能一边近战**。
之前改了半天 `AutoFight` 的攻击分支，全都是无效功 —— 那条路径根本没被走到。

## 修法：对"内挂自己锁定的目标"抑制这段原生自动攻击

```csharp
bool suppressAutoAttack = Settings.AutoFightEnabled
                          && !Settings.AutoFightBasicAttack
                          && MapObject.TargetObject == AutoFight.Target;   // ← 只针对内挂的目标

if (!suppressAutoAttack && ((...原条件...)))
{
    ...
}
```

关键点：

| 项 | 说明 |
|---|---|
| 只比较 `AutoFight.Target` | **手动点选的目标不受影响**，手动操作照常 |
| 同时覆盖弓箭手分支 | 这个 `if` 包住了近战和弓手的自动攻击，一起抑制 |
| 普攻开 = true 时不抑制 | 保持原生手感（两边都发也只是被 `AttackTime` 节流） |
| 技能走的是 `NextMagic` | `CheckInput` 里 `if (User.NextMagic != null) { UseMagic(); return; }` 在更前面，不受影响 |

## 为什么之前没发现

`AutoFight` 里 `Attack()` 确实有 `BasicAttack` 开关保护，逻辑上"不会近战"。
但攻击有两个入口：

1. `AutoFight.Attack()` → 我们控制的（有开关）
2. `GameScene.CheckInput()` 原生自动索敌 → **不受我们控制**

只堵住第一个不够。

## 现在的完整行为

| 普攻开关 | 内挂锁定的目标贴到 1 格 | 结果 |
|---|---|---|
| 是 | 客户端原生会自动砍 | ✅ 正常近战 |
| 否 | **被抑制** | ✅ 只放技能 |
| 关闭内挂 | 原生自动砍 | ✅ 正常手动玩 |

---

# 四十一、修：人物自己走到怪物跟前（鼠标按键残留）

## 日志证据

```
16:34:49  状态：追击 CaveMaggot  d14/r9 w9 s9
16:34:49  状态：追击 CaveMaggot  d13/r9 w9 s9
16:34:50  状态：追击 CaveMaggot  d11/r9 w9 s9
16:34:51  状态：技能 FireBall → CaveMaggot  d9/r9    ← 到 9 格，我的代码已经停手
16:34:53  状态：技能 FireBall → CaveMaggot  d6/r9    ← 状态还是"技能"，不是"追击"
16:34:56  状态：技能 FireBall → CaveMaggot  d1/r9
```

`w9 s9` 证明配置读对了（攻击距离 9、技能射程 9）。
`d9` 之后状态再也没出现过「追击」，说明 **`AutoFight.MoveTo()` 一次都没被调用** —— 移动不是自动战斗代码干的。

## 真凶：客户端自己的「按住鼠标左键 = 朝鼠标方向走」

`MapControl.CheckInput()` 里：

```csharp
switch (MapButtons)
{
    case MouseButtons.Left:
        ... 朝 MapLocation（鼠标位置）走/攻击 ...
```

`MapButtons` 的清除在 `CMain_MouseUp`：

```csharp
public static void CMain_MouseUp(object sender, MouseEventArgs e)
{
    MapControl.MapButtons &= ~e.Button;      // ← 依赖 WinForms 收到 MouseUp
```

**如果鼠标是在窗口外松开的，WinForms 收不到 MouseUp**，`MapButtons` 就永久卡在 `Left`。
之后每一帧 `CheckInput` 都会让角色朝**最后记录的鼠标位置**走 —— 而你正盯着怪看，鼠标就在怪身上，于是「自己走去贴怪」。

这也解释了为什么改了 `Chase` / `AttackRange` / `Loot` 全都没用 —— 根本没走那条代码。

## 修法 1：鼠标按键看门狗（每 tick 检查）

```csharp
private static void WatchStuckMouseButtons(GameScene scene, long now)
{
    if (MapControl.MapButtons == MouseButtons.None) { _buttonsDownSince = 0; return; }

    if (_buttonsDownSince == 0) { _buttonsDownSince = now; return; }

    if (now - _buttonsDownSince < 2500) return;      // 按住超过 2.5 秒 → 判定为残留

    SetStatus($"[鼠标按键残留已清除] btn={MapControl.MapButtons} 持续 {now - _buttonsDownSince}ms");

    MapControl.MapButtons = MouseButtons.None;
    MapControl.AutoRun = false;
    if (scene.MapControl != null && scene.MapControl.AutoPath) scene.MapControl.AutoPath = false;

    _buttonsDownSince = 0;
}
```

`AutoFight.Process()` 每 tick 调用一次。内挂开启时，按键最多卡 2.5 秒就会被清掉。

## 修法 2：输出状态下检测"不该有的移动"

在攻击分支（已经在交战距离内）里比对上一 tick 的位置：

```csharp
if (user.CurrentLocation != _lastPos)
{
    bool external = !scene.MapControl.AutoPath;          // 不是我们自己的寻路

    if (external && (MapControl.AutoRun || MapControl.MapButtons != MouseButtons.None))
    {
        SetStatus($"[外部移动已抑制] btn={MapControl.MapButtons} autorun={MapControl.AutoRun} act={user.CurrentAction} d{dist}/r{engageRange}");
        MapControl.AutoRun = false;
        MapControl.MapButtons = MouseButtons.None;
    }

    _lastPos = user.CurrentLocation;
}
```

这条是**自愈**：只要在射程内发现自己挪了位置，就立刻把外部输入源掐掉，并在日志里留下现场（`btn` / `autorun` / `action`）。

## 验证

进游戏后如果还有异常移动，`Logs\AutoFight.txt` 里会出现：

```
[鼠标按键残留已清除] btn=Left 持续 2513ms
[外部移动已抑制] btn=Left autorun=False act=Running d6/r9
```

看到这两行就说明检测生效了（而且人已经被拉住）。

## 真凶：客户端原生的「自动贴近目标」

日志抓到了铁证：

```
16:48:56  [移动] (19,105) → (18,106)  d8/r9  act=Walking btn=None autorun=False path=False
16:48:56  [移动] (18,106) → (17,107)  d7/r9  act=Walking btn=None autorun=False path=False
16:48:57  [移动] (17,107) → (16,106)  d6/r9  act=Walking btn=None autorun=False path=False
```

`act=Walking`（人在走）、`btn=None`（没按鼠标）、`autorun=False`、`path=False`（不是内挂寻路）。
鼠标、自动跑、拾取、`Chase` 全被排除。

源头在 `GameScene.CheckInput()` 的**最后一段**：

```csharp
if (MapObject.TargetObject == null || MapObject.TargetObject.Dead) return;
...
if (Functions.InRange(MapObject.TargetObject.CurrentLocation, User.CurrentLocation, 1)) return;  // 到 1 格才停
direction = Functions.DirectionFromPoint(User.CurrentLocation, MapObject.TargetObject.CurrentLocation);
if (!CanWalk(direction, out direction)) return;

User.QueuedAction = new QueuedAction { Action = MirAction.Walking, Direction = direction, Location = Functions.PointMove(User.CurrentLocation, direction, 1) };
```

这是「左键点怪 → 自动走过去砍」的实现。条件判断对怪物恒成立：

```csharp
MapObject.TargetObject.Name.EndsWith(")")        // "Skeleton" → false
(!CMain.Shift)                                   // → true
→ ((true && true) || true) && (false || false)   // = false → 不 return → 继续走到 1 格
```

**只要 `TargetObject` 是怪、且不在 1 格内，客户端每帧都会发一次 `Walking`。**

而 `AutoFight` 为了显示选中框设了：

```csharp
MapObject.TargetObjectID = _target.ObjectID;      // ← 顺手激活了原生"贴脸"逻辑
```

所以法师一边放火球一边被这条逻辑一路拖到贴身，看起来就像"内挂自己跑去近战"。

## 修法

同一处加一行守卫 —— **只屏蔽内挂自己的目标**，手动点选的目标照旧会走过去：

```csharp
if (MapObject.TargetObject == null || MapObject.TargetObject.Dead) return;

// 内挂自己锁定的目标不要走这条「自动贴近」逻辑。
// 法师/道士需要站在技能射程外输出，被这条拖到 1 格就会表现为"内挂自己跑去贴脸"。
// 手动点选的目标不受影响（只比较 AutoFight 自己的目标）。
if (Settings.AutoFightEnabled && MapObject.TargetObject == AutoFight.Target) return;
```

被屏蔽后：
- `Chase=false` → 站在 `<攻击距离>` 格原地输出，怪不过来就不打
- `Chase=true` → 由 `AutoFight.MoveTo()`（A* + 300ms 节流）接管走位，走到射程边缘就停

## 顺带保留的三样东西

1. **鼠标按键看门狗** —— 鼠标在窗口外松开时 `MapButtons` 会卡在 `Left`，人物会一直朝最后的鼠标位置走；内挂开启时按住超过 2.5 秒自动清掉
2. **输出状态下自愈** —— 已在射程内却检测到位移且存在外部输入源（`btn` / `autorun`）时，直接掐掉
3. **诊断日志** `Logs\AutoFight.txt` —— 状态变化 + 射程内每次位移（含坐标、`CurrentAction`、输入状态）

---

# 四十二、远程职业：风筝走位 + 障碍物检测

法师/道士站在远处输出才是正确玩法，但之前只有「站在射程边缘不动」，怪贴上来就白挨打；
而且隔着墙放技能是纯空放（弹道照飞、伤害落空）。这一节加两件事：

1. **保持距离（风筝）**：怪进入 `保持距离` 以内就自动后撤，撤到能打到的圈上再继续输出
2. **障碍物检测**：与目标之间隔墙时不放技能，改为寻找能直线命中的格子

## 新增配置

| 键 | 默认 | 说明 |
|---|---|---|
| `KeepDistance` | `True` | 保持距离（风筝）开关 |
| `KeepRange` | `5` | 保持距离(格)：怪近到这个距离以内就后撤 |
| `LineOfSight` | `True` | 障碍物检测开关 |

配置面板「战斗」页变成两行：

```
追击 [是]   保持距离 [是] [5]格   障碍 [是]
攻击距离 [9]格   攻击间隔 [300]毫秒   普攻 [否]
状态：拉开距离 d2/r9 保持5
```

> `KeepRange` 会被 `Math.Min(KeepRange, 交战距离)` 夹住 —— 近战职业 `攻击距离=1` 时 `KeepRange` 也是 1，
> `dist < 1` 永不成立，等于自然关闭，不需要为战士单独判断职业。

## 障碍检测怎么判定

用**地图格子自带的标志**，和 `MapControl.EmptyCell()` 同一套：

```csharp
private static bool IsWall(MapControl map, Point p)
{
    if (!InBounds(map, p)) return true;
    if (map.M2CellInfo == null) return false;

    CellInfo info = map.M2CellInfo[p.X, p.Y];

    // BackImage 0x20000000 = 不可走（Mir2 经典图：原文件 BackImage 的 0x8000 位转过来的）
    // FrontImage 0x8000    = 阻挡（Mir3 格式图自带"是否遮挡视线"标志，flag & 0x02 != 2 时置位）
    return (info.BackImage & 0x20000000) != 0 || (info.FrontImage & 0x8000) != 0;
}
```

直线采样用 Bresenham，**跳过起点和终点**（终点站着怪物，怪物不算墙）：

```csharp
private static bool HasLineOfSight(MapControl map, Point from, Point to)
{
    if (!Settings.AutoFightLineOfSight) return true;
    if (map == null || from == to) return true;

    int dx = Math.Abs(to.X - from.X), dy = Math.Abs(to.Y - from.Y);
    int sx = from.X < to.X ? 1 : -1, sy = from.Y < to.Y ? 1 : -1;
    int err = dx - dy;
    Point p = from;

    for (int guard = 0; guard < 64; guard++)
    {
        int e2 = err * 2;
        if (e2 > -dy) { err -= dy; p.X += sx; }
        if (e2 < dx) { err += dx; p.Y += sy; }

        if (p == to) return true;
        if (IsWall(map, p)) return false;
    }

    return true;
}
```

> 只算墙、**不算怪物**：怪物站在弹道上不会挡伤害，把它算进去会误判成"打不到"，反而站着不动。

## 站位选择（一份代码同时管风筝和绕障碍）

`TryReposition(scene, user, target, now, minDist, maxDist)` 在目标周围 `[minDist, maxDist]` 的**环**上找落点：

```csharp
for (int r = minDist; r <= maxDist; r++)          // maxDist 会被夹到 minDist+2，只看最近几圈
    for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;   // 只取环上的格子

            Point p = new Point(tg.X + dx, tg.Y + dy);

            if (!InBounds(map, p)) continue;
            if (!map.EmptyCell(p)) continue;                 // 能站
            if (!HasLineOfSight(map, p, tg)) continue;       // 能打到

            int step = Dist(me, p);
            if (step > 7) continue;                          // 走太远不划算

            int score = step * 16 + (maxDist - r);           // 近的优先，其次贴着射程上限
            ...
        }
```

三个细节：

1. **落点粘住**：选定后 1200ms 内沿用同一个落点，否则每 tick 换目的地把 A* 路径抖掉
2. **兜底后撤**：环上一个能站能打的格子都没有（被墙围住/角落）时，沿「怪→我」方向往外挪一格
3. **走位窗口**：`_repositionUntil` 期间不要清 `AutoPath`，否则刚下的路径下一 tick 就被自己停掉（走一格就停）

## `Process()` 里的接入顺序

```
2) 近处寻敌 → 3) 拾取 → 4) 远处寻敌
   ↓
计算 los = HasLineOfSight(me, 目标)          （每个 tick 一次，很便宜）
计算 keepRange = Min(KeepRange, 交战距离)
   ↓
施毒（需要 los）
   ↓
4) 追击（dist > 交战距离）—— 走位逻辑不变
   ↓
5) 攻击段
   ├─ los && TryCastSkill()      技能优先，能放就放（冷却 1~2 秒）
   ├─ 5.5 风筝/绕障碍            tooClose || !los → TryReposition()
   ├─ !los 且绕不过去            → 有追击就 MoveTo(绕过墙)，卡住 3 秒换目标
   ├─ !los 且不追                → "被障碍物挡住"，原地停手（不空放）
   ├─ dist > 近战距离            → "等待技能"
   └─ 普攻开关 → Attack()
```

关键顺序：**技能先于风筝**。技能能放就放（输出优先），放不出来（冷却中）才后撤 —— 否则会变成只会跑不打。

---

# 四十三、拾取过滤改成按物品类型（勾选框）+ 极品优先

原来的过滤是 `LootFilter` 一个整数（0=全部 1=仅药品 2=仅金币），靠名字里有没有「药/水/金币」来猜，
既不准（`药` 会命中「毒药」以外的奇怪东西）也不够用（不能单独要武器不要衣服）。改成 8 个勾选框。

## 新配置（`AutoFight\{角色}.ini`）

```ini
LootGold=True        ; 金币
LootPotion=True      ; 药品/食物
LootBook=True        ; 书籍
LootWeapon=True      ; 武器
LootArmour=True      ; 衣服/头盔/靴子/腰带
LootJewel=True       ; 饰品（项链/手镯/戒指）
LootOther=True       ; 其它（矿石/材料/卷轴/宝石…）
LootTreasure=True    ; 极品：带附加属性，不看类型一律捡
```

旧的 `LootFilter` 键不再读取（留着也不影响）。

## 面板「拾取」页

```
自动拾取 [是]   范围 [3]格   回收 [10]格   先捡后打 [否]
── 拾取类型（不勾的不捡） ──
[x]金币   [x]药品   [x]书籍   [x]武器
[x]衣服   [x]饰品   [x]其它   [x]极品
衣服=衣服/头盔/靴子/腰带    饰品=项链/手镯/戒指
极品=带附加属性的物品（地面名字为青色），勾上就不看类型一律捡
回收=周围没怪时放大到该范围回收掉落（0=关闭）
```

勾选框用现成的 `MirCheckBox`（Prguse 2086/2087），和道士页的绿毒/红毒同一套机制：
点击后 `MirCheckBox_Click` 先翻转 `Checked`，再触发我们挂的 `Click`，所以直接读 `box.Checked` 存盘。

## 关键难点：地面物品拿不到 ItemInfo

`ItemObject` 只从服务端收到三样东西：

```csharp
public void Load(S.ObjectItem info)
{
    Name = info.Name;            // 名字
    NameColour = info.NameColour; // 名字颜色
    DrawFrame = info.Image;      // 图
}
```

**没有 `ItemInfo`、没有 `ItemType`**。所以类型只能靠名字反查客户端物品库：

```csharp
private static ItemInfo FindItemInfo(string name)
{
    List<ItemInfo> list = GameScene.ItemInfoList;
    if (_itemIndexCount != list.Count)          // 边下边追加，数量变了就重建
    {
        _itemIndex.Clear();

        for (int i = 0; i < list.Count; i++)
        {
            ItemInfo info = list[i];
            if (info == null || string.IsNullOrEmpty(info.Name)) continue;

            // 地面物品用的是 ItemInfo.FriendlyName（服务端 ItemObject.Name 就是它）：
            //   去掉末尾数字 + 去掉 [] 里的内容
            _itemIndex[info.Name] = info;

            string friendly = info.FriendlyName;
            if (!string.IsNullOrEmpty(friendly)) _itemIndex[friendly] = info;
        }

        _itemIndexCount = list.Count;
    }

    if (_itemIndex.TryGetValue(name, out ItemInfo found)) return found;

    // 数量后缀 " (3)" 剥掉再查
    int p = name.LastIndexOf(" (", StringComparison.Ordinal);
    if (p > 0 && name.EndsWith(")", StringComparison.Ordinal))
        if (_itemIndex.TryGetValue(name.Substring(0, p), out found)) return found;

    return null;
}
```

两个坑：

1. **服务端 `ItemObject.Name` 返回的是 `Item.Info.FriendlyName`**（`ItemData.cs`）：
   ```csharp
   temp = Regex.Replace(temp, @"\d+$", string.Empty);       // 去掉末尾数字
   temp = Regex.Replace(temp, @"\[[^\]]*\]", string.Empty);  // 去掉方括号内容
   ```
   所以必须**同时按 `Name` 和 `FriendlyName` 建索引**，否则「魔杖2」「屠龙[精英]」这类名字查不到类型。
2. **数量后缀**：服务端 `Name = Item.Count > 1 ? string.Format("{0} ({1})", Name, Item.Count) : Name`，
   地面名字可能是「金创药 (3)」，查之前要剥掉。

## 类型映射

| 勾选框 | `ItemType` |
|---|---|
| 金币 | `ItemObject.IsGold`（新增字段，`Load(S.ObjectGold)` 里置位） |
| 药品 | `Potion` / `Meat` / `Food` |
| 书籍 | `Book` |
| 武器 | `Weapon` |
| 衣服 | `Armour` / `Helmet` / `Boots` / `Belt` |
| 饰品 | `Necklace` / `Bracelet` / `Ring` |
| 其它 | 其余全部（`Ore` / `Gem` / `Scroll` / `CraftingMaterial` / `Mount` / `Quest` …） |

金币原来靠名字猜（`name.Contains("金币")`），现在给 `ItemObject` 加了一个 `IsGold` 字段，
`Load(S.ObjectGold)` 里置 `true`、`Load(S.ObjectItem)` 里置 `false`，不再依赖语言文件文案。

## 极品判定：比 ARGB，不要比 Color

服务端 `ItemObject` 生成地面物品时：

```csharp
if (Item.IsAdded) NameColour = Color.Cyan;          // ← 极品（带附加属性）
else { 按 ItemGrade 上色：Common=白 Rare=蓝 Legendary=橙 Mythical=紫 Heroic=红 }
```

所以「极品」= **地面名字是青色**。但这里有个必须注意的坑：

```csharp
// 服务端发：writer.Write(NameColour.ToArgb());
// 客户端收：NameColour = Color.FromArgb(reader.ReadInt32());
```

`Color.FromArgb(...)` 得到的 `Color` 内部 `state = ARGB`，而 `Color.Cyan` 的 `state = KnownColor`，
`Color` 的 `==` 会同时比较 `value` 和 `state` → **直接比永远不相等**。必须比 ARGB：

```csharp
private static readonly int TreasureArgb = Color.Cyan.ToArgb();   // 0xFF00FFFF

if (Settings.AutoFightLootTreasure && item.NameColour.ToArgb() == TreasureArgb) return true;
```

这条规则放在类型判断**之前** —— 极品不管类型一律捡。

## `LootAllowed` 最终形态

```csharp
private static bool LootAllowed(MapObject ob)
{
    if (!(ob is ItemObject item)) return false;

    // 极品不看类型
    if (Settings.AutoFightLootTreasure && item.NameColour.ToArgb() == TreasureArgb) return true;

    if (item.IsGold) return Settings.AutoFightLootGold;

    ItemInfo info = FindItemInfo(item.Name);

    if (info == null) return Settings.AutoFightLootOther;      // 查不到就归到"其它"

    switch (info.Type) { ... }

}
```

查不到 `ItemInfo` 的物品（比如本地物品库还没下完、或者服务端有客户端没有的道具）
归到「其它」处理，不会因为查不到就完全不捡。
