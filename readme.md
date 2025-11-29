# PinyinM.NET

[![NuGet](https://img.shields.io/nuget/v/PinyinM.NET?style=for-the-badge&logo=nuget&label=release)](https://www.nuget.org/packages/PinyinM.NET/)
[![NuGet](https://img.shields.io/nuget/dt/PinyinM.NET?label=downloads&style=for-the-badge&logo=nuget)](https://www.nuget.org/packages/PinyinM.NET)
[![License](https://img.shields.io/github/license/yourusername/PinyinM.NET?style=for-the-badge)](LICENSE)

一个高性能、易用的 .NET 拼音处理库，支持汉字转拼音和拼音模糊搜索功能。

## ✨ 特性

- 🚀 **高性能**: 使用 HashSet 优化查找速度，支持并行搜索
- 🎯 **多音字支持**: 完整支持多音字处理
- 🔍 **智能搜索**: 支持拼音全拼、首字母、混合搜索
- 🌐 **混合文本**: 智能处理中英文混合文本
- 🎵 **音调支持**: 支持带/不带音调的拼音格式
- 💪 **泛型设计**: 类型安全的搜索接口
- ⚡ **即开即用**: 无需额外配置，开箱即用

## 📦 安装

```bash
dotnet add package PinyinM.NET
```

或通过 NuGet 包管理器:

```
Install-Package PinyinM.NET
```

## 🚀 快速开始

### 1. 汉字转拼音

#### 基础用法

```csharp
using Pinyin.NET;

// 创建拼音处理器（默认不带音调）
var processor = new PinyinProcessor();

// 获取拼音
var pinyin = processor.GetPinyin("到底");
// 返回: PinyinItem { Keys: [["dao"], ["di", "de"]] }
```

#### 带音调格式

```csharp
// 创建带音调标记的处理器
var processorWithTone = new PinyinProcessor(PinyinFormat.WithToneMark);

var pinyin = processorWithTone.GetPinyin("你好");
// 返回带音调的拼音
```

#### 包含原始汉字

```csharp
var processor = new PinyinProcessor();

// 第二个参数设为 true 可在结果中包含原始汉字
var pinyin = processor.GetPinyin("到底", withZhongWen: true);
// 返回: PinyinItem { 
//   SplitWords: ["到", "底"],
//   Keys: [["dao"], ["di", "de"]] 
// }
```

#### 混合文本处理

```csharp
var processor = new PinyinProcessor();

// 自动处理中英文混合、数字、特殊字符
var pinyin = processor.GetPinyin("Windows相机");
// 返回: PinyinItem { Keys: [["windows"], ["xiang"], ["ji"]] }

var pinyin2 = processor.GetPinyin("JetBrainsToolbox");
// 自动按大写字母分词: [["jet"], ["brains"], ["toolbox"]]
```

### 2. 拼音模糊搜索

#### 基础搜索

```csharp
using Pinyin.NET;

// 准备数据
public class AppInfo
{
    public string Name { get; set; }
}

var apps = new List<AppInfo>
{
    new AppInfo { Name = "微信" },
    new AppInfo { Name = "网易云音乐" },
    new AppInfo { Name = "Windows相机" }
};

// 创建搜索器
var searcher = new PinyinSearcher<AppInfo>(apps, app => app.Name);

// 搜索（支持拼音全拼）
var results = searcher.Search("weixin");
foreach (var result in results)
{
    Console.WriteLine($"匹配度: {result.Weight}, 应用: {result.Source.Name}");
}
// 输出: 匹配度: 1.0, 应用: 微信
```

#### 首字母搜索

```csharp
// 支持首字母缩写搜索
var results = searcher.Search("wx");
// 可以匹配 "微信"、"网易"等

var results2 = searcher.Search("wxyy");
// 匹配 "网易云音乐"
```

#### 混合搜索

```csharp
// 支持拼音全拼和首字母混合
var results = searcher.Search("wxj");
// 可以匹配 "Windows相机"

var results2 = searcher.Search("winxj");
// 同样匹配 "Windows相机"
```

#### 动态添加数据

```csharp
var searcher = new PinyinSearcher<AppInfo>(apps, app => app.Name);

// 后续追加更多数据
var moreApps = new List<AppInfo>
{
    new AppInfo { Name = "QQ音乐" },
    new AppInfo { Name = "钉钉" }
};

searcher.AppendLoad(moreApps, app => app.Name);
```

### 3. 高级功能

#### Token 化处理

```csharp
var processor = new PinyinProcessor();

// 获取 Token 数组（用于更底层的处理）
var tokens = processor.GetTokens("微信");

foreach (var token in tokens)
{
    Console.WriteLine($"原文: {token.Original}");
    Console.WriteLine($"全拼: {string.Join(", ", token.Full)}");
    Console.WriteLine($"首字母: {string.Join(", ", token.First)}");
}
```

#### 自定义音调格式

```csharp
// 不带音调（默认）
var processor1 = new PinyinProcessor(PinyinFormat.WithoutTone);

// 带音调符号（ā, á, ǎ, à）
var processor2 = new PinyinProcessor(PinyinFormat.WithToneMark);

// 带音调数字（a1, a2, a3, a4）
var processor3 = new PinyinProcessor(PinyinFormat.WithToneNumber);
```

#### 匹配结果详情

```csharp
var results = searcher.Search("wx");

foreach (var result in results)
{
    // 匹配权重（0-1之间，1表示完全匹配）
    Console.WriteLine($"权重: {result.Weight}");
    
    // 原始数据对象
    Console.WriteLine($"数据: {result.Source.Name}");
    
    // 字符匹配结果（bool数组，标记哪些字符被匹配）
    Console.WriteLine($"匹配位置: {string.Join(", ", result.CharMatchResults)}");
}
```

## 📚 API 文档

### PinyinProcessor

主要用于汉字到拼音的转换。

#### 构造函数

```csharp
public PinyinProcessor(PinyinFormat format = PinyinFormat.WithoutTone)
```

**参数:**
- `format`: 拼音格式（WithoutTone/WithToneMark/WithToneNumber）

#### 方法

```csharp
// 获取拼音
public PinyinItem GetPinyin(string text, bool withZhongWen = false)

// 获取 Token 数组
public PinyinToken[] GetTokens(string text)
```

### PinyinSearcher&lt;T&gt;

用于拼音模糊搜索。

#### 构造函数

```csharp
public PinyinSearcher(IEnumerable<T> source, Func<T, string> selector)
```

**参数:**
- `source`: 数据源
- `selector`: 用于提取搜索文本的函数

#### 方法

```csharp
// 执行搜索
public IEnumerable<SearchResults<T>> Search(string query)

// 追加数据
public void AppendLoad(IEnumerable<T> source, Func<T, string> selector)
```

### PinyinFormat

拼音格式枚举。

```csharp
public enum PinyinFormat
{
    WithToneNumber,   // 带数字音调：pin1 yin1
    WithToneMark,     // 带音调符号：pīn yīn
    WithoutTone       // 不带音调：pin yin
}
```

## 🎯 使用场景

- ✅ 应用启动器（如 Spotlight、Alfred）
- ✅ 联系人搜索
- ✅ 商品/文章搜索
- ✅ 输入法辅助
- ✅ 文本标注和分析
- ✅ 拼音学习工具

## 🔧 技术特点

1. **智能分词**: 自动识别并处理大写字母、分隔符进行分词
2. **多音字处理**: 完整支持多音字，搜索时考虑所有可能的读音
3. **性能优化**: 
   - 使用 HashSet 优化字符查找
   - 并行处理提升搜索速度
   - 缓存机制减少重复计算
4. **权重算法**: 智能的匹配权重计算，优先展示更相关的结果

## 📝 更新日志

### 2.0.0
- 🚀 全面重构代码结构，提升性能和可维护性
- 🎯 增强搜索功能，支持更多搜索场景
- 🔧 优化拼音转换算法，提升准确度
### 1.1.0
- 🔧 重构搜索算法，提升匹配准确度
- ⚡ 优化汉字转拼音性能
- 🐛 修复已知问题

### 1.0.3
- 🔧 重构搜索逻辑
- 📚 改进 API 设计

### 1.0.2
- ✨ 优化混合字符转拼音
- 🎯 自动识别大写字母和分隔符进行分词

### 1.0.1
- ✨ 优化包含英文字符的拼音转换
- 🎁 新增泛型搜索支持
- 📦 添加对字典和 KeyValuePair 的支持
- 🎯 优化权重计算算法

## 📄 许可证

本项目采用 MIT 许可证。详见 [LICENSE](LICENSE) 文件。

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

## 📮 联系方式

- 提交 Issue: [GitHub Issues](https://github.com/yourusername/PinyinM.NET/issues)
- NuGet 包: [PinyinM.NET](https://www.nuget.org/packages/PinyinM.NET)

---

⭐ 如果这个项目对你有帮助，请给个 Star！
