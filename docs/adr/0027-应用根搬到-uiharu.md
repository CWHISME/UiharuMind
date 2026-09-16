# 应用根搬到 ~/.uiharu

应用数据根从「各平台应用数据目录下的 `UiharuMind/`」改成**三平台统一的 `~/.uiharu`**，
`UIHARU_HOME` 可覆盖。旧址不迁移、不删除，就地变成孤儿（沿 ADR 0013 先例）。

## 背景

旧根三平台各异：Windows `%AppData%\UiharuMind`、macOS `~/Library/Application Support/UiharuMind`、
Linux `~/.config/UiharuMind`。两处疼：mac 路径含空格，模型写 shell 忘加引号就断一条；
前缀长，人看累、模型每轮在房间路径里重写一遍也贵。另有错配：Windows 上 GB 级模型
进了会同步的 Roaming。

## 决策

1. **新根 `~/.uiharu`**（`AppPaths.Root` 唯一定义处）。目录名取简称，与 `~/.claude` 同构。
2. **`UIHARU_HOME` 环境变量可覆盖**，供测试与排障；为空回落 `~/.uiharu`。
3. **整个 Root 跟走**（`Config/Data/Cache/Logs/External` 相对结构不变）。已自定义过
   模型/引擎路径的用户保留覆盖，不硬搬。
4. **`PythonEnv` 到新址重建**：venv 内的 shebang 与软链是绝对路径，手动拷过去无效，
   设置页重建一次。
5. **旧址不迁不删**，`buildMacFull.sh` 的硬编码旧根同步到新根（含 `UIHARU_HOME` 覆盖）。
6. **Windows 下默认根补 Hidden 属性**（`AppPaths.EnsureRoot`，启动时一次）：点号前缀在
   资源管理器里不隐藏；`UIHARU_HOME` 自选位置只建目录、不动属性。

## 为什么

- 短且无空格，人和模型双省；三平台统一后文档与排障只用一句话。
- 隐藏目录挡住随手删——`Data` 删了就没了，不该摆在显眼处。
- 沿 ADR 0013「不迁移不删除」：自动搬要处理数 GB 跨卷移动与中断续传，
  风险高于让用户拖一次文件夹；历史 `file://` 坏链是已知代价（ADR 0026 同款）。

## 取舍

- **`uiharu` 是第二个名字**：程序名与 bundle id 仍是 `UiharuMind`，目录用简称是别名，
  文档里多注一笔。
- **Finder 默认看不见点文件**：排障入口写 `ls -a`，设置页「打开数据目录」按钮不变。
- **再留一代孤儿**：`SaveData/` 之后又是 `Application Support/UiharuMind`，用户手动清理。
