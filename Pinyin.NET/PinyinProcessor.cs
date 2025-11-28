using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Pinyin.NET;

public class PinyinProcessor
{
    /// <summary>
    /// 初始化并加载拼音库
    /// </summary>
    public PinyinProcessor(PinyinFormat format = PinyinFormat.WithoutTone)
    {
        // 使用 HashSet 提高 Contains 查找速度
        _upperCharSet = new HashSet<char>
        {
            'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U',
            'V', 'W', 'X', 'Y', 'Z'
        };

        _splitCharSet = new HashSet<char>
        {
            ' ', '_', '-', '.', ',', '!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '+', '=', '[', ']', '{', '}',
            '\\', '|', ';', ':', '"', '\'', '<', '>', '?', '/', '~'
        };

        LoadResource("Pinyin.NET.char_base_a.json", _pinyinDict, format);
        LoadResource("Pinyin.NET.char_common_base_a.json", _pinyinFullDict, format);
    }

    private void LoadResource(string resourceName, Dictionary<string, string[]> dict, PinyinFormat format)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return;

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var data = JsonSerializer.Deserialize<CharModel[]>(json);

        if (data == null) return;

        foreach (var item in data)
        {
            var pinyins = item.pinyin;
            if (format == PinyinFormat.WithoutTone)
            {
                // 去重并去声调
                pinyins = pinyins.Select(RemoveDiacritics).Distinct().ToArray();
            }

            // 避免重复键报错
            dict.TryAdd(item.Char, pinyins);
        }
    }

    /// <summary>
    /// 获取适用于 PinyinSearcher 的 Token 数组结构
    /// </summary>
    /// <param name="text">原始文本</param>
    /// <returns>拼音Token数组</returns>
    public PinyinToken[] GetTokens(string text)
    {
        // 复用你现有的 GetPinyin 逻辑进行分词和查字典
        var pinyinItem = GetPinyin(text);

        var tokens = new PinyinToken[pinyinItem.Keys.Count];

        for (int i = 0; i < pinyinItem.Keys.Count; i++)
        {
            var pinyinList = pinyinItem.Keys[i];
            var originalStr = pinyinItem.SplitWords[i];

            // 转换 List<string> 为 string[]
            string[] fullPinyins = pinyinList.ToArray();

            // 提取首字母 (去重)
            // 如果是英文单词 "Code"，首字母为 'c'
            // 如果是中文 "行"，首字母为 ['x', 'h']
            char[] firstChars = fullPinyins
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p[0])
                .Distinct()
                .ToArray();

            tokens[i] = new PinyinToken
            {
                Full = fullPinyins,
                First = firstChars,
                Original = originalStr
            };
        }

        return tokens;
    }

    public PinyinItem GetPinyin(string text, bool withZhongWen = false)
    {
        var result = new List<List<string>>();
        var split = new List<string>();
        var sb = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            var cChar = text[i];
            var input = cChar.ToString();

            // 1. 中文处理
            if (_chineseRegex.IsMatch(input))
            {
                if (sb.Length > 0)
                {
                    result.Add([sb.ToString().ToLower()]);
                    split.Add(sb.ToString());
                    sb.Clear();
                }

                string[]? pinyins = null;
                if (_pinyinDict.TryGetValue(input, out var basePinyin))
                {
                    pinyins = basePinyin;
                }
                else if (_pinyinFullDict.TryGetValue(input, out var fullPinyin))
                {
                    pinyins = fullPinyin;
                }

                if (pinyins != null)
                {
                    var list = new List<string>(pinyins);
                    if (withZhongWen) list.Add(input);
                    result.Add(list);
                    split.Add(input);
                }
                else
                {
                    result.Add([input]);
                    split.Add(input);
                }
            }
            // 2. 非中文处理
            else
            {
                // 遇到大写字母，切断之前的 buffer (驼峰分词)
                if (_upperCharSet.Contains(cChar))
                {
                    if (sb.Length > 0)
                    {
                        result.Add([sb.ToString().ToLower()]);
                        split.Add(sb.ToString());
                        sb.Clear();
                    }

                    sb.Append(input);
                }
                // 遇到分隔符，切断并丢弃分隔符
                else if (_splitCharSet.Contains(cChar))
                {
                    if (sb.Length > 0)
                    {
                        result.Add([sb.ToString().ToLower()]);
                        split.Add(sb.ToString());
                        sb.Clear();
                    }
                    result.Add([cChar.ToString().ToLower()]);
                    split.Add(cChar.ToString());
                    
                    // continue; // 分隔符本身被丢弃
                }
                // 普通小写字母或数字
                else
                {
                    sb.Append(input);
                }
            }
        }

        // 处理剩余的 buffer
        if (sb.Length > 0)
        {
            result.Add([sb.ToString().ToLower()]);
            split.Add(sb.ToString());
        }

        return new PinyinItem()
        {
            SplitWords = split.ToArray(),
            Keys = result,
        };
    }

    private static string RemoveDiacritics(string text)
    {
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();
        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString();
    }

    // 定义 JSON 数据模型
    private class CharModel
    {
        [JsonPropertyName("char")] public string Char { get; set; }

        public string[] pinyin { get; set; }
    }

    #region 字符与正则定义

    // 大写字母数组 (用于驼峰分词)
    private readonly HashSet<char> _upperCharSet;

    // 分隔符数组
    private readonly HashSet<char> _splitCharSet;

    // 中文匹配正则
    private readonly Regex _chineseRegex = new("[\u4e00-\u9fa5]", RegexOptions.Compiled);

    // 拼音字典缓存
    private readonly Dictionary<string, string[]> _pinyinDict = new();
    private readonly Dictionary<string, string[]> _pinyinFullDict = new();

    #endregion
}