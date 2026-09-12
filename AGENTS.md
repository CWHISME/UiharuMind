# AGENTS.md

本文件是「在这个仓库里怎么干活」。术语表在 [docs/CONTEXT.md](docs/CONTEXT.md)。

实现要点：

1. 优先复用已有代码或组件，避免重复造轮子。
2. 如果同类用法可以提成公共组件，则可以将其封装，尽量减少重复代码。
3. 优先考虑性能、可维护性。

高优先级：

1. 在修改代码时，尤其避免一个文件堆砌大量代码，可适当拆分或应用合适的设计模式(如模板类、方法)。
2. 如果发现代码不符合设计原则(包括但不限于九大原则)的问题，应当优先向用户主动提出更符合设计模式(包括但不限于23种经典设计模式)的重构方案，而非直接在屎山代码上做迭代。
3. 在实现需求时应当反思：应该使用继承还是组合？使用接口还是抽象类？在引入设计模式提高扩展性的同时，如何避免带来可读性降低问题？

## 仓库概览

Avalonia 12 桌面应用，.NET 10。本地跑 GGUF 模型（llama.cpp）+ 远程模型，含角色扮演对话、
工作区 agent、截图 OCR、剪贴板历史、知识库检索等。产品功能见 [README.md](README.md)。

解决方案 `UiharuMind/UiharuMind.sln` 下六个项目：

| 项目 | 是什么 |
|---|---|
| `UiharuMind.Core` | 领域与基础设施。无 UI 依赖，是全仓的重心 |
| `UiharuMind` | Avalonia UI 层（下称 **App 项目**） |
| `UiharuMind.Desktop` | 桌面入口（实际运行的就是它） |
| `UiharuMind.CLI` | 命令行入口 |
| `UiharuMind.Core.Tests` | Core 的测试 |
| `UiharuMind.App.Tests` | App 项目的测试（只测不碰 UI 线程/渲染的纯逻辑） |

可复用：

各种样式：UiharuMind/Assets/Themes

## 构建与测试

在解决方案目录 `UiharuMind/` 下执行：

```bash
dotnet build UiharuMind.sln
dotnet test  UiharuMind.Core.Tests/UiharuMind.Core.Tests.csproj
dotnet test  UiharuMind.App.Tests/UiharuMind.App.Tests.csproj
```

axaml 的命名空间与 `x:Class` 错误在编译期就会炸（`AVLN2000`），所以对结构性改动，
「解决方案编译通过」是很强的信号。

## 打包与发版

版本号的**唯一来源**是 `UiharuMind/Directory.Build.props` 的 `<Version>`。`AppInfo.Version`
从程序集读、`Info.plist` 由脚本填、CI 拿它校验 tag——都别再写第二份。

发布参数（自包含、ReadyToRun、单文件）收在 `UiharuMind.Desktop.csproj` 里，按 RID 生效，
脚本与 CI 只需给 `-r <RID>`。**不开 `PublishTrimmed`，也不上 NativeAOT**：全仓默认反射绑定
（`AvaloniaUseCompiledBindingsByDefault=false`，90 个 axaml 只有 30 个有 `x:DataType`）加上
反射 JSON，裁剪与 AOT 都会造成静默的运行时失败。前置条件是先把编译绑定与 JSON source-gen
做完，那之前别碰。

`UiharuMind/Build/` 下：

| 脚本 | 产物 |
|---|---|
| `buildMac.sh` | `Output/UiharuMind-<版本>-osx-arm64.zip`（.app bundle，ad-hoc 签名） |
| `buildMacFull.sh` | 同上加 `-full` 后缀，额外带入本机的 Runtime/模型（缺则跳过） |
| `buildLinux.sh` | `Output/UiharuMind-<版本>-linux-x64.tar.gz` |
| `buildWin.bat` | `Output/UiharuMind-<版本>-win-x64.zip` |

共享部分在 `common.sh`（取版本、发布、签名、打包）。macOS 的 bundle 签名必须是**最后一步**，
签完再往里拷东西密封就失效了。

### macOS 签名必须用自签名证书，不能用 ad-hoc

TCC（辅助功能 / 屏幕录制授权）把权限钉在 app 的**指定要求**上。ad-hoc 没有颁发者，
系统退化成用 cdhash 当身份，而那个值每次编译都变——表现是「权限列表里条目还在，
但权限已失效」。证书签名的指定要求是 `identifier "com.cwhisme.uiharumind" and
certificate leaf = H"..."`，cdhash 照变但 DR 不变，授权跨版本保留。

证书是自签名的，本地 `openssl` 生成、永久复用，不需要 Apple 账号（Gatekeeper 的
「无法验证开发者」不受影响，那需要付费 Developer ID）。生成与启用见
[Build/signing/README.md](UiharuMind/Build/signing/README.md)。**别重新生成**——换证书
等于换身份，所有用户要再授权一次。

证书不在 keychain 时：本机回退 ad-hoc 并打警告（fork 与新机器仍能构建），
CI 上（`CI=true`）硬失败，避免发出身份不对的 Release。

CI（`.github/workflows/`）：`ci.yml` 在 push/PR 上构建加测试；`release.yml` 由 `v*` tag 触发，
先断言 tag 与 `<Version>` 一致，再在三平台上**调用上面这些脚本**（打包逻辑只有一份），
产物加 `SHA256SUMS` 发成草稿 Release。

Mac 包只有 ad-hoc 签名、未经公证，用户首次打开需手动放行——放行说明写在 Release notes 里。

## 代码规范

正确使用注释：注释精简、无冗余注释，简单代码可忽略，必要代码才进行合理注释。

对于项目现有不符合规范的代码，要求可以忽略，但是新增请按照本规范进行。

### 命名规范

> [规则1-3] ~ [规则1-7]（下划线命名法、大驼峰/小驼峰、`I` 前缀、`E` 前缀）已由
> [.editorconfig](.editorconfig) 机械强制，严重性为 warning。编号保留空洞是为了不让你
> 以往引用过的编号失效。

[规则1-1] 英文单词命名。禁止使用拼音或无意义的字母命名。

[规则1-2] 直观易懂。使用能够描述其功能或有意义的英文单词或词组。

### 编码规范

[规则2-1] 声明变量时，一行只声明一个变量。

[规则2-2] 类的字段声明统一放置于类的最前端。

```csharp
public class Student
{
    private string _firstName;
    private string _lastName;

    public string GetFirstName()
    {
        return _firstName;
    }
}
```

[规则2-3] `Bitmap` 归属分两档，按图的大小与频率定，**不许一见 Bitmap 就 Dispose**。

- **大或高频**（截图、剪贴板历史、对话附件、缩放中间产物）：确定性释放。单一所有者；跨边界传递
  显式移交，方法注释写明「接管」；UI 绑定替换时**先换新值再释放旧值**（反了就撞渲染）；
  Dispose 后置 null；同一实例被多个字段引用时按引用去重，双重释放按 bug 修。
- **小而长寿**（头像、图标等进程级缓存）：明确豁免，注释标「进程级缓存，不 Dispose」，
  禁止顺手加释放——共用的默认图一旦被释放，全进程一起变空白。

### 注释规范

[规则3-1] 公共方法注释，采用 `///` 形式自动产生 XML 标签格式的注释，包括方法介绍、参数含义、
返回内容。私有方法可以不用注释。

```csharp
/// <summary>
/// 设置场景名称
/// </summary>
/// <param name="sceneName">场景名</param>
/// <returns>如果设置成功返回True</returns>
public bool SetSceneName(string sceneName)
{
}
```

[规则3-2] 公共字段注释，采用 `///` 形式。私有字段可以不用注释。

[规则3-3] 私有字段注释，注释位于代码后面，中间 Space 键隔开。

```csharp
private string _firstName; //姓氏
```

[规则3-4] 方法内的代码块注释。

```csharp
public void UpdateHost()
{
    // 和服务器通信
    ...

    // 检测通信结果
    ...
}
```

## 协作口径

- 请严格按照仓库规则工作，如果出现冲突，一切以仓库口径为准
- **提交信息只写一句话。** 不要正文、不要任何附加尾注。
- 提交或推送只在用户要求时做。
