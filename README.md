# NoHavok Split StaticModelGroup

Frosty Editor 插件：把 Battlefield 1 地图里的 `StaticModelGroupEntityData` 拆成已有 `WorldPartData` 上的独立 `ObjectReference`，方便按件编辑（位置 / 拖拽），而不是整组 Havok compound。

当前版本：**1.0.0.0**

## 做什么

`StaticModelGroup` 把大量静态模型编成一组，组变换受 Havok compound 约束，整组很难单独挪。本插件会：

1. 扫描你勾选的地图目录下的 `SubWorldData`
2. 找出其中的 `StaticModelGroupEntityData`
3. 按组成员的 mesh / variation / Havok 实例变换，写入**已经存在**的同目录 `WorldPartData`
4. 组成员全部落盘成功后，从 SubWorld 里删掉该 group，并清掉相关连接

不会新建 WorldPart。某个 SubWorld 底下如果没有任何现成 `WorldPartData`，该处的 group **原样保留**，结果窗口会提示。

某个 group 只要有成员放不进 WorldPart，**整个 group 都不会删**，避免拆一半。

## 环境

- [Frosty Editor](https://github.com/CadeEvs/FrostyToolsuite)（本仓库的 `.csproj` 是对着 **Frosty2000** 源码树写的）
- 已加载 **Battlefield 1** 数据
- 插件工程依赖（均在 Frosty 源码树内，不随本仓库发布）：
  - `FrostyControls`
  - `FrostyPlugin` / FrostyCore
  - `FrostySdk`
  - `FrostyHash`
  - SharpDX（`FrostyEditor/ThirdParty`）

把本目录放到 `Frosty2000/Plugins/NoHavokSplitPlugin`，用解决方案一起编译。PostBuild 会把 DLL 拷到 `FrostyEditor\<OutDir>\Plugins\`。

独立克隆本仓库时，需要旁边有完整的 Frosty 工程，或自行改 `.csproj` 里的 `ProjectReference` / `HintPath`。

## 使用

1. 用 Frosty Editor 打开 BF1
2. 菜单 **Tools → Split StaticModelGroups (NoHavok)**
3. 列表按 `Levels/.../MP|SP/<地图>` 分组；可用过滤、全选 / 全不选
4. 勾选地图后点 **拆解所选地图**，确认
5. 看结果：扫描 / 修改的 SubWorld 数、删掉的 group 数、更新的 WorldPart 数、新增 ObjectReference 数、跳过的成员
6. 在 Frosty 里 **Save** 工程后再导出 / 进游戏验证

未加载游戏数据时会提示 `No game data loaded.`

## 地图范围

只列出路径里带 `MP` / `SP`、且文件夹名像 `MP_*` / `SP_*`（或 `MP`/`SP` + 字母）的关卡。例如 `Levels/MP/MP_ItalianCoast`。过滤框同时匹配显示名和路径 key。

## 注意

- 这是破坏性 EBX 修改。先备份 Frosty 工程。
- 拆完应在编辑器里抽查：原 group 消失、物件出现在对应 WorldPart、变换与拆前一致。
- Havok `.hkx` / compound 读失败时，相关成员可能被跳过，group 会留下。

## 许可

未单独指定许可证。Frosty 本体请遵循其原项目许可。
