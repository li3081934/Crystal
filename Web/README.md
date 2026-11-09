# Mir2 Web 客户端 · Phase 0 + Phase 1

Crystal（Legend of Mir 2）C# 服务端的**浏览器客户端验证工程**。
目标不是"能玩"。Phase 0 的目标是：验证把客户端搬到网页端/手机端这条路上，最容易翻车的几个环节能不能走通。

已经跑通的链路（对着真实 `Server.exe` 实测）：

```
浏览器登录 → 握手/版本校验 → 建号 → 选角 → 进图 → 地图渲染 → 人物/怪物渲染 → 点地走路 → 聊天
Phase 1 追加：物品表(UserItem/ItemInfo) → 装备/卸下 → 外观随装备变化 → HP/MP/经验条 + 背包 + 装备栏 + 技能栏
Phase 1 追加：点怪 → 自动追到身边 → 挥砍（本地动作 + 服务端判定）→ 伤害/闪避飘字 → 怪物血条 → 击杀
Phase 1 追加：地图动画瓦片（水面/火把，100ms 节拍只刷动画槽位）+ 门（敲门 → 开 → 5s 自动关）
```

截图在 `shots/`（`npm run shot` / `npm run shot:mobile` 生成，不入库）：`01-login.png`、`02-map.png`、`03-walk.png`、`mobile-map.png`、`mobile-walk.png`。

---

## 一、为什么需要这些东西

| 现状（C# 客户端） | 网页端的问题 | 本工程的做法 |
|---|---|---|
| 裸 TCP（`2B 长度 + 2B 包ID + payload`，无加密） | 浏览器不允许直连 TCP | `bridge/` 做 WebSocket ↔ TCP 字节转发 |
| SlimDX / Direct3D9 | 浏览器没有 D3D9 | PixiJS（WebGL）重画 |
| WinForms 自绘 UI（29k 行对话框） | 不存在 | Phase 0 用 HTML/CSS 顶替，Phase 1 再逐个补 |
| `.Lib` v2/v3 图片库，`Tiles.Lib` 单文件 430MB | 不能整体下载 | HTTP `Range` + 64KB 块缓存，只取用到的图 |
| `.map` 8 种格式 | 无解析器 | TypeScript 重写（type1/100/0/2/3/5/6/7） |
| 客户端资源 1.2GB | 首屏不可能全下 | 按需 + 镜头预取（`warm()`）+ 外圈预取 |
| 客户端缺资源时会去补丁站下（`MirDownload`） | 网页端也得有同样的兜底 | bridge 读 `Mir2Config.ini` 的 `[Download] Host/Proxy`，缺文件就代理下载并缓存到 `Web/.cache` |
| `UserItem` 装备/背包是嵌套结构（含 `Stats`/`Awake`/`Slots`） | 得完整移植才能画对装备、显示物品 | `formats/item.ts` 全量解析 + `game/stats.ts` 算属性 |
| 战斗是"服务端判定 + 客户端演" | 动作、飘字、血条都要客户端自己演 | `Actor.setAction()` + 飘字层 + 血条 + 追击逻辑 |
| C# 每 100ms 重画整块地板（`AnimationCount++`） | 网页端重画 1000+ 瓦片太贵 | 只维护"动画槽位"列表，每拍重算图号换贴图 |

服务端**一行代码都没改**。

---

## 二、跑起来（3 个终端）

```bash
# 0) 先启动游戏服务端（原样，不用改配置）
cd Build/Server/Release && ./Server.exe

# 1) 桥接 + 静态资源（监听 8787）
cd Web && npm install && npm run bridge

# 2) 前端 dev server（5188，手机同局域网可直接访问）
cd Web && npm run dev
```

打开 `http://127.0.0.1:5188/`，账号密码随便填（服务端 `AllowNewAccount=True`，会自动建号并自动建一个角色）。
想跳过手填：`http://127.0.0.1:5188/?account=demo&password=demo123`。

手机测试：Vite 会打印 `Network: http://<你的内网IP>:5188/`，手机浏览器直接打开即可（`/ws`、`/assets` 都被 Vite 代理到 bridge）。
注意 `Bridge` 的 `/assets` 默认指向 `../Build/Client/Release`，可用 `MIR2_ASSETS=...` 覆盖。

端口一览（都可以用环境变量覆盖）：

| 服务 | 默认端口 | 覆盖变量 |
|---|---|---|
| 游戏服务端 | 7000 | 服务端自己的 `Configs/Setup.ini`，不用动 |
| bridge（WS + 静态资源） | 8787 | `PORT`（bridge 监听）/ `MIR2_BRIDGE`（前端往哪连） |
| 前端 dev server | **5188** | `MIR2_WEB_PORT` |

`vite.config.ts` 里开了 `strictPort: true`：5188 被占会直接报错退出，不会偷偷换成 5189，
免得 `tools/*.mjs` 里的固定地址连错端口。

### 自检脚本

```bash
cd Web
npm run smoke            # 解码自检：Lib / map / 协议帧
npm run smoke -- --net   # 额外连真实服务端跑一遍 登录→进图
npm run trace            # 抓包统计（排查服务端发了什么）
npm run shot             # Playwright 自动登录并截图到 shots/
npm run shot:mobile      # iPhone 视口截图
npm run shot:hud         # 打开背包/装备 + 悬停物品出 tooltip
npm run shot:equip       # 双击装备（剑/衣服），看外观变化
npm run shot:two         # 两个客户端互看（注意同 IP 要间隔 5s）
npm run shot:combat      # GM 登录 → 传野外 → 刷鸡 → 追击 → 挥砍 → 击杀
npm run shot:doors       # 动画瓦片换帧 + 敲门开/自动关
npm run diag:slow        # 慢网络下走路，检查黑块（黑像素占比）
npm run diag:camera      # 检查"相机跟随"：走路时角色应钉在屏幕中央、背景平滑滚动
npm run diag:flicker     # 检查"贴图闪烁"：走动时中间区域的瓦片有没有凭空消失
npm run gen:stats        # 重新生成职业基础属性表
```

`node --experimental-strip-types` 让 Node 直接跑 `.ts`，所以 `net/`、`formats/` 这些模块在浏览器和 Node 里是同一份代码。

---

## 三、目录结构

```
Web/
├── bridge/bridge.mjs          # WS↔TCP 隧道 + /assets 静态资源（Range 支持）
├── client/
│   ├── index.html             # 登录面板 + HUD
│   └── src/
│       ├── main.ts            # 流程编排：握手/登录/建号/选角/进图/输入
│       ├── net/
│       │   ├── enums.ts       # 由 tools/gen-enums.mjs 从 Shared/Enums.cs 生成
│       │   ├── bytes.ts       # 对齐 C# BinaryReader/Writer（含 7bit 变长字符串）
│       │   ├── packets.ts     # 分帧 + 服务端包解码 + 客户端包编码
│       │   └── socket.ts      # WebSocket 封装（自动 KeepAlive）
│       ├── formats/
│       │   ├── lib.ts         # .Lib v2/v3（gzip + BGRA→RGBA + Range 块缓存 + FrameSet）
│       │   └── map.ts         # .map 8 种格式 + MapLibs 索引→路径映射
│       │   └── item.ts        # UserItem / ItemInfo / ClientMagic（DB v117 分支）
│       ├── game/
│       │   ├── world.ts       # Actor / 动作插值 / 帧表（对齐 FrameSet.Player）
│       │   ├── basestats.ts   # 由 tools/gen-basestats.mjs 从 Shared/BaseStats.cs 生成
│       │   └── stats.ts       # 等级公式 + 装备属性 + 装备→外观
│       ├── ui/hud.ts          # HP/MP/EXP 条 + 背包 + 装备栏 + 技能栏 + 物品 tooltip
│       └── render/renderer.ts # PixiJS：瓦片三层 + 角色三层 + 按行 zIndex 交织
└── tools/                     # 枚举生成 / 解码自检 / 抓包 / 截图 / 像素探针
```

### 协议层

- 包 ID 不手抄：`npm run gen:ids` 直接解析 `Shared/Enums.cs` 生成 `net/enums.ts`（153 个客户端包 + 279 个服务端包）。
- 解析结果统一放 `packet.data`，**不会**和包名 `name` 冲突（曾经因为 `ObjectMonster.name` 覆盖包名排查了半天）。
- 没实现的包只标记 `unknown: true` 并保留原始字节，不会断线；`UserInformation` 先只解到 `HeroBehaviour`（后面是装备/背包/技能嵌套结构，Phase 1 补）。

### `.Lib` 解码

```
int32 version | int32 count | [v3] int32 frameSeek | int32 index[count]
单张图：int16 w,h,x,y,shadowX,shadowY | uint8 shadow | int32 length | gzip(BGRA)
```

- 图片是 **gzip**（不是 zlib），浏览器用 `DecompressionStream('gzip')` 直接解。
- 像素是 D3D `A8R8G8B8`，内存里是 `B,G,R,A`，需要换成 RGBA 再 `ImageBitmap`。
- 大库走 `Range`：先取头部+索引（`Tiles.Lib` 索引约 620KB），再 64KB 块缓存取图片记录；进图时 `warm()` 把可见索引合并成少量区间请求。
- v3 库自带 FrameSet（怪物/NPC 的帧表就从这里读），v2 用 `Frames.cs` 里的 `FrameSet.Player` / `DefaultMonster`。

### 地图渲染规则（照抄 `MapControl.DrawFloor` / `DrawObjects`）

- 网格 48×32；back 层**只有 x、y 都是偶数**的格子有，左上角对齐。
- middle 层：48×32 / 96×64 铺在格子里（左上角）；更高的长条用 `DrawUp` 语义（底边压在**格子底边** `cellTop+32`）。
- front 层：C# 会**跳过** 48×32 / 96×64 且无动画的前景图，其余按 `(drawX, drawY - 图片高度)` 底部对齐绘制。
  （一开始把这条写反了，结果整个城镇是一片黑块 —— 见 `shots/` 前后对比。）
- **高图的底边是格子底边，不是格子左上角**：`DrawObjects` 里的 `drawY` 比 `DrawFloor` 多一个 `+1 行`，
  然后 `DrawUp()` 再 `y -= 高`。少加这 32px 会让所有建筑/树/路灯整体上浮一格，
  和地面/平铺墙脚对不上 —— 表现就是"墙里被挖掉一条 32px 的沙色横带"（见六之五）。
- 瓦片和角色共用一层，用 `zIndex = 行号*10 + 层偏移` 交织，实现"站在城门后面会被挡住"。

### 移动

- 点击/触摸 → `canWalk()`（`(backImage & 0x20000000)==0 && (frontImage & 0x8000)==0`）+ BFS → 每 600ms 发一个 `C.Walk{Direction}`。
- 自己走路是本地预测（服务端不回发自己的 `ObjectWalk`），`S.UserLocation` 作为权威纠偏。
- 方向键可以单步走，方便验证。

---

## 四、Phase 0 实测结论

| 项 | 结果 |
|---|---|
| 握手/登录/建号/选角/进图 | 通（`npm run smoke -- --net` 可复现） |
| 地图渲染（`0.map` BichonProvince 700×700 type100） | 通，含建筑遮挡 |
| 人物/怪物渲染 | 通（Hen/Deer/Scarecrow/Guard 正常出帧） |
| 走路 + 服务端位置同步 | 通 |
| 聊天 | 通 |
| 手机视口（iPhone 13, DPR 3） | 通，触摸点地可走 |
| 控制台报错 | 0 |

---

## 五、Phase 1 做了什么

| 能力 | 状态 | 说明 |
|---|---|---|
| `UserItem` / `ItemInfo` / `ClientMagic` 解析 | ✅ | 对齐 DB v117 分支（<=84 的老格式不支持）；物品表来自 `S.NewItemInfo`（登录时会来 100+ 条） |
| 自己的装备外观 | ✅ | 装备的 `ItemInfo.Shape/Effect` → 身体/武器/翅膀偏移，双人实测外观互不相同 |
| 双击装备 / 装备栏双击卸下 | ✅ | 装备走 `C.EquipItem`（`To` 必须算好槽位，`-1` 会被服务端直接拒）；卸下走 `C.RemoveItem`（Grid 是**目标背包**） |
| HP/MP/经验/金币 | ✅ | 当前值以 `S.HealthChanged` 为准；上限用 `BaseStats` 公式 + 装备属性算（技能/buff 还没算，见缺口） |
| 背包 / 装备面板 + 物品提示 | ✅ | 图标从 `Items.Lib` 按需解码；tooltip 显示属性/持久/重量/需求等级 |
| 技能栏 | ⚠️ 只显示 | 图标来自 `MagIcon.Lib`（`icon*2`），点击还没接施法 |
| 属性面板 | ✅ | 装备栏下方列出当前属性（中文名走 `Stat` 枚举） |
| 点怪战斗 | ✅ | 点怪物 → 不在身边就 BFS 追到贴身 → 转身朝目标发 `C.Attack` |
| 攻击/受击/死亡动画 | ✅ | `ObjectAttack` / `Struck` / `ObjectStruck` / `ObjectDied` / `Death` 全部驱动同一套动作机 |
| 伤害飘字 | ✅ | `DamageIndicator`：命中白色、暴击金黄、闪避灰 "MISS"（负数是服务端对玩家受伤的约定，照抄原版） |
| 怪物血条 | ✅ | `ObjectHealth{percent, expire}` → 头顶血条，按剩余时间自动消失 |
| 攻击节奏 | ✅ | 本地冷却 `1400 - (攻速*60 + min(370, 等级*14))`，下限 550ms，与服务端 `AttackSpeed` 一致 |
| 地图动画瓦片 | ✅ | 前景/中层动画（`FrontAnimationFrame`/`MidAnimationFrame`，含 `&0x0f` 混合位）按 `(cnt % (frames+frames*tick))/(1+tick)` 取帧；Shanda 的独立动画层（`TileAnimationImage`→MapLibs[190]）也接了 |
| 门 | ✅ | `S.Opendoor` → 50ms 步进的状态机（关→开中→开→关中→关），瓦片图号 `+ (imageIndex+1) * doorOffset`；开 5s 自动关；门没开时 `canWalk` 拒绝进入（对齐 `CheckDoorOpen`） |
| 撞门 | ✅ | BFS 走不通时朝目标方向撞一格：下一格是门就发 `C.Opendoor` 等它开，而不是原地卡住 |

多客户端实测：两个账号互相可见，A 穿着衣服拿着剑、B 裸装，双方看到的外观各自正确（`shots/two-players.png`）。

战斗实测（`npm run shot:combat`，用 GM 命令把野外变成测试场）：

```
@LOGIN → GM 密码 → @MOVE 420 420 → @LEVEL 22 → @MOB Hen
点鸡 → 贴身 → 挥砍 → 飘字 [MISS, -4] → 鸡血条 20% → 击杀（目标消失）
```

> 没有装备时攻击力是 0，Lv1 命中率（`Random(机动性+1) > 准确`）也打不动怪，所以脚本里先 `@LEVEL 22`；
> 想复现原版手感就按正常流程打装备。
> 另外 `ObjectHealth`（头顶血条）只在"受伤但没死"时下发，高等级一刀秒杀时看不到血条，所以脚本用 `ObjectDied` 判定击杀。

截图：`shots/combat-hit.png`（血条 + 飘字 + 挥砍帧）、`shots/combat-after.png`。

动画/门实测（`npm run shot:doors`）：

```
动画瓦片: {"total":11,"withFrames":4,"sample":[{"label":".../Objects.Lib#2723","frames":10,...}]}
图号变化: true [2730,2730,2730,2730] → [2728,2728,2728,2728]      ← 水面在动
门的瓦片: {"doorIndex":61,"base":8088,"doorOffset":20}
  初始        state=0 tiles=[8088]
  敲门 550ms  state=2 tiles=[8108]        ← 开：图号 +20（= base + (0+1)*doorOffset）
  6.8s 后     state=0 tiles=[8088]        ← 自动关
```

> ⚠️ **同一个 IP 不能同时开两个客户端**：Crystal 服务端在每个 `MirConnection` 构造时就给该 IP 上 `IPBlockSeconds`（默认 5s）的封禁做防洪水，所以第二个连接会被立刻 `RST`。
> 两个客户端测试之间要等 5 秒以上（`tools/shot-two.mjs` 里已经这么做了）。这跟 B/S 无关，原版 C# 客户端同样如此。

## 六、"黑块 / 撕裂" 是怎么修掉的

第一版截图里出现过很多**硬边黑矩形**，根因有两个，都已修：

1. **兜底地面被回收成了瓦片**（最要命的那个）
   `rebuildTiles()` 会把 `worldLayer` 的所有子节点丢进对象池复用。兜底背景块也在这个层里，
   于是它被当成瓦片挪到了某一行上 —— 屏幕就再也没有底色了，任何"还没画/没下好"的格子直接露出纯黑画布。
   修复：兜底块单独放 `backLayer`（永不被回收），并且 `resize` 时同步尺寸。
2. **瓦片还在下载/解码时就露黑**
   修复：
   - 镜头移动时把**视野外 3 格**的瓦片一起预取+解码，走近时已经就绪；
   - 兜底色用**第一张解出来的 back 瓦片的平均色**，就算真有空隙也是地面色而不是黑；
   - 兜底块做成整屏 sprite，垫在最底下。

另外补了两个防止长会话变慢/花屏的点：

- 贴图缓存 LRU 上限（6000 张贴图，且 **8 秒内用过的绝不销毁**）+ 位图缓存上限（4000 张），
  避免玩久了 GPU/内存爆掉后出现随机黑块；屏幕上正在显示的贴图被回收掉就是"一块块闪没"；
- `ResizeObserver` 跟随容器尺寸，窗口拉伸/手机旋屏后画布永远贴合。

3. **地图本身就没画地面**（原版那里也是纯黑）
   `0.map` 里确实存在 `backImage == 0` 的格子（比如 (282,612)、(300,632) 一带），原版客户端直接留黑。
   修复：这种格子拿同一张相邻地砖补上；万一整块区域都没图，就用"平铺地砖"兜底。
   兜底层不再是**一块纯色**，而是 `backLayer` 里的两层：底色 sprite + 一张 `TilingSprite`（第一张 back 瓦片的贴图），
   缺图的地方看起来就是"延续的地面"，不会再是一块突兀的补丁色。

自检：

```bash
npm run diag:slow     # 给每个资源请求加 450ms 延迟，再走路采样黑像素占比（修前 4~19%，修后 ≈1.2%）
```

实测（1522×761 @DPR2，走路途中）：

```
修前: {"blackPct":19.33,"pending":48} → 12.0（pending 归零后仍有空洞）
修后: {"blackPct":1.44,"pending":0}（450ms 延迟下依然干净）
```

## 六之一、"名字没和人物对齐"是怎么回事

现象：人物/NPC/怪物头上的名字、血条、聊天气泡和身体对不上（有的穿进身体，有的飘太高）。

原因：原来名字挂在**格子左上角**的固定偏移上（写死 `-42`）。而原版是按
`DisplayRectangle`（= `DrawLocation` + `BodyLibrary.GetTrueSize(当前帧)`）来算的：

```csharp
NameLabel.Location = new Point(
    DisplayRectangle.X + (50 - NameLabel.Size.Width) / 2,
    DisplayRectangle.Y - (32 - NameLabel.Size.Height / 2) + (Dead ? 35 : 8));
ChatLabel.Location = new Point(DisplayRectangle.X + (48 - w) / 2, DisplayRectangle.Y - (60 + h) - (Dead ? 35 : 0));
Libraries.Prguse2.Draw(0, DisplayRectangle.X + 8, DisplayRectangle.Y - 64);   // 血条底图
```

也就是说：**高度取决于角色当前帧的贴图尺寸**，高矮不同的角色/怪自动跟着变。

修法（`updateActors`）：先跑 `updatePlayer/updateMonster` 把身体摆好，再取身体的**实际头顶**：

```ts
const bodyTopOffset = sprites.body.visible ? sprites.body.y - screen.y : -48; // 贴图顶部相对格子左上角（负值）
const labelY = screenUi.y + bodyTopOffset - 2;   // 名字底边 = 头顶上方 2px
bubble → labelY - 18      hpBar → labelY - 16      死亡时名字再下移 35px
```

## 六之二、"角色动了背景不动"是怎么回事

现象：走路时角色先滑出去一格，背景纹丝不动，走完一格背景才"啪"地跳一下。

根因是**两套相机**：角色用浮点相机（`camFloatX/Y`，一格内插值），瓦片用整格相机（`camCellX/Y`）。
瓦片只在整格处重建，所以一格之内背景是死的，只有角色在动。

修法（对齐原版 C# 的做法：把整层挪一个小数偏移）：

| 谁 | 用哪个坐标 |
|---|---|
| 瓦片 + 角色身体/武器（`worldLayer`） | 整格相机落位，再由 `worldLayer.position` 补 `(camCell - camFloat) * 格子尺寸` 的亚格位移 |
| 名字/血条/气泡/飘字（`uiLayer`） | 直接用浮点相机 `toScreen()`，不会被层偏移二次平移 |
| 鼠标拾取 | `toScreen()/toCell()` 用浮点相机；`hitTestActor` 先减掉 `worldLayer.position` 换算回世界层坐标 |

于是每帧只做一次容器位移，瓦片照旧只在整格处重建，视觉上背景和角色一起平滑滚动。
自检：

```bash
npm run diag:camera   # 走路时采样：角色屏幕坐标 ≈ 常量、worldLayer.x/y 连续变化
```

实测（走一步，每 110ms 采一次）：

```
静止   cam 300.00  layer [0,0]      selfScreen [615,352]
+110ms cam 300.50  layer [24,16]    (相机先跳到新格子，再靠层偏移把世界挪回去)
+220ms cam 300.75  layer [12,-8]
+330ms cam 300.97  layer [1,-1]     ← 一步结束，偏移归零
```

## 六之三、"走动时贴图一会消失一会又出现"是怎么回事

这条是三个 bug 叠在一起，都不在资源下载上：

1. **角色的精灵被当成瓦片回收了**（最严重的那个）
   `worldLayer` 里为了和场景按行交错遮挡，同时放着**瓦片**和**角色的身体/武器**。
   而 `rebuildTiles()` 原本是把这一层**所有**子节点塞进 `tilePool` 复用 —— 于是每走一格，
   角色的精灵就被当成瓦片重新分配：地图上某块贴图变成了"人"，下一帧又被打回原样，
   而角色自己也会串位。修复：给瓦片打 `__isTile` 标记，回收时只回收瓦片精灵。
2. **动画瓦片每次重建都闪回第 0 帧**
   火把/水花/门 这些会动的瓦片在 `rebuildTiles()` 里被重新创建，原本先摆基础帧（`base`），
   等 100ms 后的下一次 tick 才切到"当前帧"。所以每换一格，屏幕里所有动画物件都会闪一下；
   有些动画组第 0 帧是空的，看起来就是"贴图消失又出现"。门更明显：会瞬间变回关闭状态。
   修复：新建动画瓦片时立刻按 `animIndexFor(（动画计数、门状态）)` 落图。
3. **贴图缓存查得太晚**
   `peekTexture()` 原本先问 Lib 要 meta，再查贴图缓存。meta 是懒加载的，
   没解析好就判成"没贴图"→ 整块隐藏一帧再出现。修复：缓存里连 meta 一起存，
   先查缓存再问 Lib；顺便在贴图缺失时区分"还没好"和"Lib 里根本没这张图"
   （`Objects6.Lib#1039~1055` 就是空槽，原版也不画）。

另外顺手补了一个一致性保护：`render()` 发现"当前整格相机 ≠ 建这批瓦片时用的整格相机"就立刻重建，
不再只信 `tilesDirty` —— 否则 `setCamera()` 和 `render()` 分别跑在两个 rAF 回调里时，
会出现整整一帧的"新相机 + 旧瓦片"。

自检：

```bash
npm run diag:flicker   # 走动时统计"中间区域上一帧有、这一帧没了"的格子数，以及纹理损坏数
```

实测（走动 10 步、每 90ms 采样）：纹理损坏 `0`；pending 只在刚进入新区域时出现（几十个贴图在 2~3 帧内补齐）。

## 六之四、"建筑贴图缺一块、露出地面"是怎么回事

原版把地图画成**两遍**：

| 遍 | 函数 | 画什么 |
|---|---|---|
| 1 | `DrawFloor`（本地图缓存） | back 只画 2x2 地砖；mid / front **只画平铺尺寸**（48x32、96x64） |
| 2 | `DrawObjects`（每行交错，和角色一起排） | mid / front 里**非平铺尺寸**的（高的建筑、树…），按底边对齐画 `drawY - s.Height`，而 `drawY = cellTop + CellHeight` |

我图省事合成了一遍，还照抄了"平铺尺寸的前景/中景要跳过"这条 —— 但那是**因为第 1 遍已经画过了**。
单遍渲染里跳过，就等于那批图谁都不画：商铺的墙、门框、招牌（`Objects.Lib#8412/8679~8688` …）
整块消失，只剩地面，看起来就是"建筑缺一块"。

修法（`renderer.ts` 的 `placeTile()`）：

- 每张图**只画一次**，按尺寸决定对齐：
  - 平铺尺寸（`48x32` / `96x64`）→ 左上角对齐（原版第 1 遍的位置）；
  - 其它（高的）→ 底边对齐到 **`sy + CELL_HEIGHT`**（原版第 2 遍的位置）；
- z 序：平铺的 `row*10`，高的 `row*10+5`（后者压在本行地面上）；
- 删掉 `tallOnly` / `flatOnly` 那套"跳过"逻辑（`pushTile` 里漏删的那处就是"整列墙面消失"的元凶）。

顺带修掉的另一个隐患：平铺尺寸的 **96x64 前景**原来被当成高的（因为只判断了高度），
会按底部对齐往上挪 32 像素、画到错误位置 —— 也就是"建筑边缘出现错位的碎片"。现在按宽高一起判定。

### 影子层要做加法混合（`DrawBlend`）

原版里 `frontAnimFrame` 的 **`0x80` 位** 表示"这层是影子/半透明"：

```csharp
if ((animation & 0x80) > 0) { blend = true; animation &= 0x7F; }
...
if (blend) DrawBlend(index, new Point(drawX, drawY - s.Height), Color.White, (index >= 2723 && index <= 2732));
```

`DrawBlend` 里是 `SourceAlpha + One`（**加法混合**）。我们原来把影子图当普通不透明图直接贴上，
于是屋顶/屋檐的影子变成一块块**硬边沙色方块**（影子图的 RGB 直接怼上去就是这个效果）。

修法：给瓦片带 `blend` 标记，`placeTile()` 里 `sprite.blendMode = 'add'`；
另外 `2723~2732` 这几张图原版还会叠加图片自带偏移（`DrawBlend(..., offSet: true)` → `point.Offset(mi.X, mi.Y)`）。

**注意这条对动画瓦片也要生效**：路灯就是 `frontAnimFrame = 0x8a`（0x80 混合 + 10 帧动画）的
`Objects.Lib#2723~2732`（100x100）。如果只在普通瓦片上加偏移、动画瓦片那条路漏了，
灯柱和光晕就会整体错开一个 `(mi.X, mi.Y)` —— 表现就是"光照没落在灯上"。
中景那边同理：`(MidAnimationFrame & 0x0f)` 非 0 时原版置 blend，只有帧数 8/10 的组才真的用加法混合。

### z 序要按"原版的两遍"来，不能只按行号

```ts
ROW_Z = 100;            // 每行占 100 个名额，行内：地面 0 / 中景 10 / 前景 20 / 角色 50
GROUND_Z_BASE = -1e6;   // 平铺瓦片（地面）再加这个基数
```

- 地面（平铺尺寸）加 `GROUND_Z_BASE`：**永远压在所有建筑/角色下面**。
  只按行号排序会出事：下面几行的地面 z 比上面那栋楼的 z 大，会把楼"啃"出一块块台阶状的洞。
- 行内再按 地面 < 中景 < 前景 < 角色 排，角色用 `row*100 + 50`，
  和原版 `for (y…) { 画瓦片; M2CellInfo[x,y].DrawObjects(); }` 的顺序一致。

### 六之五、"建筑贴图对不齐、墙里被挖掉一条"是怎么回事

现象：商铺/城墙的墙面每隔一格少一条 **32px 高**的横带（露出沙地），招牌、门框、木桶看着像往上飘了一格，
而地面、路灯底座、墙脚那些平铺尺寸的图又都在原位。

根因是上面那条对齐规则少加了一格高：

| 遍 | 绘制 y 坐标 | 高图顶边 |
|---|---|---|
| `DrawFloor`（back / 平铺 mid·front） | `(y - userY + OffSetY) * 32` = 格子**左上角** | 平铺图 = 左上角 |
| `DrawObjects`（高的 mid·front） | `(y - userY + OffSetY + 1) * 32` = 格子**底边** | `cellTop + 32 - 高` |

我们把两遍的坐标系当成了同一个，高图写成 `sy - 高`，于是**所有高图（墙、树、招牌、路灯…）整体上浮 32px**。
它们彼此之间是同步平移的，所以看不出“整栋楼歪了”；但一旦跟平铺墙脚、地面、角色叠在一起，
就会在交界处留下一条 32px 的沙色横带 —— 就是截图里那几个"洞"。

修法（`placeTile()`）：

```ts
const flat = isFlatTile(hit.meta.width, hit.meta.height);
sprite.position.set(item.sx, flat ? item.sy : item.sy + CELL_HEIGHT - hit.meta.height);
```

离线自检（不需要起服务端，直接读本地 `.map` + `.Lib` 合成 PNG）：

```bash
# 0.map 的当铺一带（302,625 那排 48x474~527 的墙柱），两种规则各出一张图
node --experimental-strip-types tools/tiles-render.ts ../Build/Client/Release/Map/0.map 292,318,610,640 shots/csharp.png csharp 2
node --experimental-strip-types tools/tiles-render.ts ../Build/Client/Release/Map/0.map 292,318,610,640 shots/web.png    web    2

# 列出区域里每格的图 + 尺寸 + 两种规则下的偏移
node --experimental-strip-types tools/tiles-static.ts ../Build/Client/Release/Map/0.map 300,306,619,626
```

`web` 那张会精确复现截图里的每个洞，`csharp` 那张是连续的。

排查工具（这次写的，留着有用）：

```bash
node tools/probe-cells.mjs 301,305,616,623   # 打印这些格子的 map 数据 + 实际渲染的精灵（含 HID）
node tools/probe-point.mjs 650,470;700,520    # 问屏幕上某个点被哪些精灵覆盖、反推格子
node tools/audit-cells.mjs                    # 抽查视野内每格引用的图是不是真的摆在正确位置
node tools/crop2.mjs shots/02-map.png out.png 640 380 420 360 2   # 截图裁剪/放大
```

## 七、补丁站代理（缺资源自动补齐）

本地 `Build/Client/Release/Data/Map/WemadeMir2/` 少了 `Objects17/22/24~27` 等文件（原版是按需下载的），
网页端遇到这些格子就会空着。bridge 现在会：

1. 本地有文件 → 直接给（支持 Range）；
2. 本地没有 → `Web/.cache/assets` 里找（之前下过）；
3. 都没有 → 按 `Mir2Config.ini` 的 `[Download] Host/Proxy` 去补丁站取，**代理失败自动回退直连**，
   同时后台整份下载到 `.cache`，下次就是本地文件。

```
日志示例：
  patch   https://mirfiles.co.uk/.../patch/  代理 http://***@124.222.220.29:7890
  代理失败（ECONNRESET），回退直连
  patch 直连 Data/CHair/06.Lib
  cache  Data/CHair/06.Lib (0.2MB)
```

也可以用环境变量覆盖：`MIR2_PATCH_URL` / `MIR2_PATCH_PROXY` / `MIR2_CACHE`。

## 八、已知缺口（Phase 1 剩余 / Phase 2，按优先级）

1. **法术/远程/特效**：近战已经通了，但 `ObjectSpell`/`ObjectRangeAttack`/`Effect`/`Magic.Lib` 的法术演出还没接
   （技能栏点了仍只写日志）。宠物/英雄（`ObjectHero` 只做了外观）也没动。
2. **法术特效层**：`Effect`/`SpecialEffect`（技能光效、buff 环绕、升级光柱）都还没接，这部分是 `Magic.Lib` 的帧时序。
3. **属性上限更精确**：现在只算 等级基础 + 装备（缺技能加成、buff、套装、`ItemSets`、变身/坐骑）。
4. **UI 剩下的部分**：NPC 对话、商店/仓库、行会、任务、邮件……37 个对话框 29k 行；背包拖拽移动格子（`C.MoveItem`）也还没接。
5. **资源体积**：现在是原样 `.Lib` + Range，够用但请求多。上线前建议离线转成图集（webp）+ CDN，并做首屏包。
6. **音频**：`NAudio` → WebAudio，尚未接。
7. **移动手感**：走路期间没有 `OffSetMove` 的 ±1 像素修正，脚步音效、`Run`（双击跑）未接。
8. **安全/部署**：`wss` + 反向代理、连接鉴权、限流；`bridge` 目前是本地开发用（无鉴权）。

---

## 九、给"要不要重写"的判断

- 协议层、地图、图库、角色帧这些"脏活"在 Phase 0 已经证明**可以完整搬过来**，而且代码量很小（`net/` + `formats/` 约 1.5k 行）。
- 真正的工作量在 **UI（29k 行对话框）** 和 **手机交互再设计**（虚拟摇杆/技能轮盘/自动寻路），以及**美术资源体积**。
- 渲染层是最省心的部分：PixiJS 顶掉 D3D9 只花了不到 500 行，帧率在手机上也没压力。
- Phase 1 的经验：**协议里的嵌套结构（UserItem/ItemInfo）是纯体力活**，一天能搬完；难的是"行为"——
  战斗判定、施法、buff、特效时序，这些在 `MirObjects` 里分散在十几个文件、几千行里。
