// Author: liaom
// SolutionName: Kitopia
// ProjectName: Pinyin.NET
// FileName:PinyinToken.cs
// Date: 2025/11/28 21:11
// FileEffect:

namespace Pinyin.NET;

public class PinyinToken
{
    /// <summary>
    /// 全拼数组 (例如: ["xing", "hang"])
    /// </summary>
    public string[] Full { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 首字母数组 (例如: ['x', 'h'])
    /// </summary>
    public char[] First { get; set; } = Array.Empty<char>();

    /// <summary>
    /// 原始字符 (例如: "行")
    /// </summary>
    public string Original { get; set; } = string.Empty;
}