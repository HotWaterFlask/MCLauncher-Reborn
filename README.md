# MCLauncher Reborn

一个从零实现的 **Minecraft: Java Edition** 第三方启动器，Windows 桌面端，基于 **.NET 10 + WPF**。

> 项目处于开发阶段。核心链路「下载 → 校验 → 安装 → 启动 → 登录 → 加载器」已打通，核心逻辑有自动化测试覆盖。

![平台](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4)
![框架](https://img.shields.io/badge/.NET-10.0-512BD4)

---

## 功能

### 已接入界面

| 页面 | 能力 |
|---|---|
| **主页** | 已安装版本下拉选择、一键补全文件、启动游戏、打开游戏目录；保留原版启动器的紧凑外观（窗口拉宽后自动进入多页面布局） |
| **版本管理** | 拉取官方版本清单（正式版 / 快照 / 远古版本）、在线搜索过滤、下载安装、补全缺失文件、版本隔离开关、Fabric / Quilt / Forge / NeoForge 加载器安装 |
| **账号** | 离线账号（按协议派生 UUID）、微软正版登录（浏览器回调 + 设备码两条路径）、账号切换与删除 |
| **启动日志** | 实时查看启动输出与退出码，定位游戏崩溃 |
| **设置** | Java 自动探测与手动指定、内存与额外 JVM/游戏参数、下载源偏好、微软客户端 ID、CurseForge API Key |

### 核心层已实现（界面接入中）

- **实例管理**：扫描 `versions\*`、每实例隔离目录、重命名/删除、游戏目录多开
- **资源管理**：Mod / 资源包 / 光影包 / 数据包 / 世界存档 / 原理图 的扫描、元数据解析、启用禁用（重命名 `.disabled`）、导入与删除
- **整合包导入**：MCBBS、MultiMC/Prism(`mmc-pack.json`)、Modrinth、CurseForge、普通压缩包 的格式识别与安装；`.rar` 会明确提示转 zip

### 尚未实现

- 在线资源检索（Modrinth 检索 UI；CurseForge 需 API Key）
- 资源管理 / 整合包导入的独立页面（Core 已完成）

---

## 环境要求

- **运行**：Windows 10 1809+ / Windows 11，x64 或 arm64
- **构建**：.NET 10 SDK
- **游戏**：对应版本所需的 JDK（启动器会自动探测已安装的 Java；1.17+ 需要 Java 17，1.20.5+ 需要 Java 21）

---

## 构建与运行

```powershell
git clone https://github.com/HotWaterFlask/MCLauncher-Reborn.git
cd MCLauncher-Reborn

# 构建
dotnet build MCLauncher-reborn.slnx -c Debug

# 运行
dotnet run --project src/MCLauncher.App
```

可执行文件：`src/MCLauncher.App/bin/Debug/net10.0-windows/MCLauncher-Reborn.exe`

### 测试

```powershell
dotnet test tests/MCLauncher.Core.Tests
```

默认排除会访问真实网络的 Live 测试；需要连网验证下载链路时：

```powershell
dotnet test tests/MCLauncher.Core.Tests --filter "FullyQualifiedName~Live"
```

---

## 首次使用

1. 打开「设置」，确认已探测到合适的 Java（也可手动填入 `javaw.exe` 路径）。
2. 进入「版本管理」，刷新清单并选择版本安装（首次会下载 client jar、依赖库、资源文件与 natives）。
3. 回到「主页」，选择版本后点「启动游戏」。
4. 需要正版账号时，到「账号」页登录；只想本地游玩时用离线账号即可。

游戏目录默认是启动器同级的 `.minecraft`，可在设置中追加其他目录。

---

## 关于微软正版登录

启动器使用 **OAuth 2.0 授权码 + PKCE** 的公开客户端流程，不包含任何内置凭据，需要你自己注册一个 Azure 应用：

1. **Azure 门户 → 应用注册 → 新注册**
   - 「支持的账户类型」必须包含 **个人 Microsoft 账户**
2. **身份验证**
   - 添加平台「**移动和桌面应用程序**」
   - 重定向 URI 填 `http://localhost`（**不带端口、不带路径**）
   - 高级设置 →「允许公共客户端流」设为 **是**
3. 把「应用程序(客户端) ID」填到「设置 → 微软正版登录」，或写入本地凭据文件（见下）

> 登录页提供两种方式：
> - **浏览器登录**：打开系统浏览器授权，由本地回环服务器接收回调（需要上面第 2 步）
> - **设备码登录**：显示一个短码，到 `microsoft.com/devicelogin` 输入即可，**完全不需要配置回调地址**

### 重要：Mojang AppID 审核

Mojang 对访问 Java 版游戏服务 API 的**新应用**实行允许清单制度。未通过审核的应用调用 `api.minecraftservices.com/authentication/login_with_xbox` 会被直接拒绝（HTTP 403，`Invalid app registration`）。

- 提交入口：<https://aka.ms/mce-reviewappid>
- 审核每周进行一次，重复提交不会加快
- 表单中的**应用名称不得包含** Mojang、Minecraft、Microsoft、Live、Xbox、Discord、Hypixel 等关键词
- 已有的成熟启动器属于历史保留豁免，不能作为「新应用无需申请」的依据

审核通过前，离线账号可正常使用。

---

## 配置与凭据

| 内容 | 位置 |
|---|---|
| 设置、账号、实例信息 | `%LOCALAPPDATA%\MCLauncher-Reborn\settings.json` |
| 运行日志 | `%LOCALAPPDATA%\MCLauncher-Reborn\logs\launcher-<日期>.log` |
| 游戏目录 | 默认 `<启动器目录>\.minecraft` |

凭据解析优先级（高 → 低）：

```
界面设置（settings.json） > 环境变量 > 代码内 LocalSecrets
```

- `MCLR_MS_CLIENT_ID` / `MCLR_CURSEFORGE_API_KEY` 可用于环境变量
- `src/MCLauncher.Core/Secrets/LocalSecrets.cs` 用于本地开发时写死默认值，**已被 `.gitignore` 排除，严禁提交**
- 模板见同目录 `LocalSecrets.example.cs`；缺少该文件时构建会自动复制一份，但值为空

---

## 项目结构

```
MCLauncher-reborn/
├─ MCLauncher-reborn.slnx
├─ src/
│  ├─ MCLauncher.Core/          # 纯业务逻辑（net10.0，不引用任何 UI 框架）
│  │  ├─ Abstractions/          # 日志、配置存储、服务容器
│  │  ├─ Models/                # 版本 JSON、库、规则、账号、实例等模型
│  │  ├─ Network/               # 多源下载、断点续传、校验、镜像映射
│  │  ├─ Services/              # 安装、启动、Java、实例、加载器、资源、整合包
│  │  │  └─ Auth/               # 微软登录（OAuth + PKCE / 设备码）
│  │  ├─ Utils/                 # 路径、哈希、归档、NBT、版本号
│  │  └─ Logging/
│  └─ MCLauncher.App/           # WPF 界面（net10.0-windows）
│     ├─ Shell/                 # 无边框窗口、导航、页面工厂
│     ├─ Views/                 # 页面
│     ├─ ViewModels/            # MVVM（手写，无第三方 MVVM 框架）
│     └─ Themes/
└─ tests/
   └─ MCLauncher.Core.Tests/    # xUnit；Live 测试默认跳过
```

`Core` 与 `App` 拆成两个工程，用编译期约束保证业务层不会依赖 WPF——便于将来复用到 CLI 或测试工程。

---

## 实现要点

- **多源下载**：官方源与镜像源按 URL 类型（资源 / 依赖库 / 元数据 / Forge 系库）分别映射；失败自动切换候选源
- **断点续传**：`.part` 文件严格校验 `206 + Content-Length + ETag`，服务端返回 200 则丢弃重下；镜像返回 `X-BMCLAPI-Hash` 命中时直接复用
- **版本合并**：`inheritsFrom` 时 `libraries` 子在前父在后、`arguments` 父在前子在后，并检测循环继承
- **natives**：按架构优先匹配分类器（`natives-windows-arm64` → `-64` → 通用），解压后排除 `META-INF`
- **远古资源**：`virtual` / `map_to_resources` 的索引会展开到 `assets/virtual/legacy`，避免 1.7 及更早版本贴图丢失
- **进程启动**：使用 `ProcessStartInfo.ArgumentList` 逐项传参，避免 Windows 引号拼接问题；启动日志实时接管
- **离线 UUID**：`MD5("OfflinePlayer:" + name)` 按协议置版本位与变体位

---

## 免责声明

- 本项目为**非官方**第三方启动器，与 Mojang Studios、Microsoft 没有任何隶属或背书关系。
- 本项目**不包含** Minecraft 游戏本体、资源文件或任何 Mojang 资产，下载内容均来自官方或公开镜像；使用前请自行购买正版。
- 「Minecraft」是 Mojang Studios 的商标。本项目名称仅用于说明用途。
- 使用本启动器造成的任何账号、数据或游戏问题，由使用者自行承担。

## 致谢

实现过程中参考了以下开源/公开项目的**行为与格式说明**（协议字段、磁盘布局、执行顺序等事实性信息）：

- [HMCL](https://github.com/HMCL-dev/HMCL)（GPLv3）
- [PCL2](https://github.com/Hex-Dragon/PCL2)（PCL 分发有限许可）

本项目为独立重写，**未复制**上述项目的任何源代码、资源文件或内嵌产物，也未使用其内置的客户端 ID / API Key。感谢这些项目为社区提供的参考。
