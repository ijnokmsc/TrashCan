using System.Reflection;

// 显式声明程序集版本。
// 本 csproj 关闭了 GenerateAssemblyInfo（<GenerateAssemblyInfo>false</GenerateAssemblyInfo>），
// 以避免与 Dalamud SDK 注入的程序集特性重复（CS0579）。但这也意味着
// <AssemblyVersion>1.0.1.0</AssemblyVersion> 属性不会被自动写进程序集，
// 导致 DLL 版本回退为 0.0.0.0 —— 表现为窗口标题显示 v0.0.0、清单 AssemblyVersion 为 0.0.0.0。
// 因此必须在此手动声明，Dalamud SDK 不会注入 AssemblyVersion（否则会与本文件冲突、报 CS0579），
// 故此处是版本的唯一来源。
[assembly: AssemblyVersion("1.0.2.0")]
[assembly: AssemblyFileVersion("1.0.2.0")]
[assembly: AssemblyInformationalVersion("1.0.2.0")]
