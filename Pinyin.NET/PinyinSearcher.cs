using System.Collections.Concurrent;

namespace Pinyin.NET;

// 假设的拼音单元结构，用于缓存
/// <summary>
/// 拼音搜索单元，支持多音字
/// </summary>
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

public class PinyinSearcher<T>
{
    private readonly List<CachedItem> _cachedItems;
    private readonly PinyinProcessor _pinyinProcessor = new PinyinProcessor();

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="source">数据源</param>
    /// <param name="selector">如何从对象中获取要搜索的字符串 (替代原本的 propertyName)</param>
    /// <param name="pinyinConverter">一个将字符串转为拼音Token的方法</param>
    public PinyinSearcher(IEnumerable<T> source, Func<T, string> selector)
    {
        _cachedItems = new List<CachedItem>();

        // 预处理阶段：这是 NEC 高效的关键，只做一次
        foreach (var item in source)
        {
            var text = selector(item);
            if (string.IsNullOrEmpty(text)) continue;

            _cachedItems.Add(new CachedItem
            {
                Source = item,
                OriginalString = text,
                PinyinTokens = _pinyinProcessor.GetTokens(text) // 这里调用外部的转换库
            });
        }
    }

    public void AppendLoad(IEnumerable<T> source, Func<T, string> selector)
    {
        foreach (var item in source)
        {
            if (_cachedItems.Any(e => e.Source.Equals(item)))
            {
                return;
            }

            var text = selector(item);
            if (string.IsNullOrEmpty(text)) continue;

            _cachedItems.Add(new CachedItem
            {
                Source = item,
                OriginalString = text,
                PinyinTokens = _pinyinProcessor.GetTokens(text) // 这里调用外部的转换库
            });
        }
    }

    public void AddItem(T item, Func<T, string> selector)
    {
        var text = selector(item);
        if (string.IsNullOrEmpty(text)) return;

        _cachedItems.Add(new CachedItem
        {
            Source = item,
            OriginalString = text,
            PinyinTokens = _pinyinProcessor.GetTokens(text) // 这里调用外部的转换库
        });
    }

    /// <summary>
    /// 执行搜索
    /// </summary>
    public IEnumerable<SearchResults<T>> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Enumerable.Empty<SearchResults<T>>();

        var queryLower = query.ToLower();
        var results = new ConcurrentBag<SearchResults<T>>();

        // 并行搜索缓存
        Parallel.ForEach(_cachedItems, item =>
        {
            // 1. 优先尝试直接包含匹配 (性能最快)
            // if (item.OriginalString.Contains(query, StringComparison.OrdinalIgnoreCase)) { ... } 

            // 2. 拼音匹配算法 (NEC Style)
            if (TryMatch(queryLower, item.PinyinTokens, out var matchIndices, out var weight))
            {
                results.Add(new SearchResults<T>
                {
                    Source = item.Source,
                    Weight = weight,
                    CharMatchResults = CreateMatchArray(item.OriginalString.Length, matchIndices)
                });
            }
        });

        return results;
    }

    /// <summary>
    /// NEC 风格的核心匹配算法：贪婪前向匹配
    /// </summary>
    private bool TryMatch(string query, PinyinToken[] tokens, out List<int> matchIndices, out double weight)
    {
        matchIndices = new List<int>();
        weight = 0;
        int qIdx = 0;
        int tIdx = 0;
        int firstMatchIndex = -1;

        while (tIdx < tokens.Length && qIdx < query.Length)
        {
            var token = tokens[tIdx];
            // 假设 query 和 token 数据都已经预处理为统一大小写（通常是小写），如果没处理建议在此处 char.ToLower
            var charQuery = query[qIdx];

            bool matched = false;
            int maxMatchLen = 0; // 记录当前 Token 能匹配的最长长度

            // --- 修改开始 ---

            // 1. 检查简拼 (首字母)
            // 即使匹配了，我们也不要 break，因为全拼可能会匹配更长
            if (token.First.Contains(charQuery))
            {
                maxMatchLen = 1;
                matched = true;
            }

            // 2. 检查全拼 (不再放在 else 里，而是并行检查)
            foreach (var fullPinyin in token.Full)
            {
                // 只有当全拼开头和当前字符一致时才有必要继续
                if (fullPinyin.Length > 0 && fullPinyin[0] == charQuery)
                {
                    int currentMatchLen = 0;
                    // 计算当前这个全拼能匹配多少个字符
                    for (int i = 0; i < fullPinyin.Length && (qIdx + i) < query.Length; i++)
                    {
                        if (fullPinyin[i] == query[qIdx + i])
                        {
                            currentMatchLen++;
                        }
                        else
                        {
                            break; // 遇到不匹配字符停止
                        }
                    }

                    // 如果全拼匹配的长度比简拼（1）更长，则采纳全拼
                    if (currentMatchLen > maxMatchLen)
                    {
                        maxMatchLen = currentMatchLen;
                        matched = true;
                    }
                }
            }
            // --- 修改结束 ---

            if (matched)
            {
                if (firstMatchIndex == -1) firstMatchIndex = tIdx;
                matchIndices.Add(tIdx);

                // 关键点：消耗掉最长匹配的长度
                // 如果是 "wx" 匹配 "Wei"，maxMatchLen=1
                // 如果是 "weixin" 匹配 "Wei"，maxMatchLen=3 ("wei")
                qIdx += maxMatchLen;

                if (qIdx >= query.Length)
                {
                    // 简单的权重计算示例
                    weight = 1000 - (firstMatchIndex * 10) + (matchIndices.Count * 5);
                    return true;
                }

                tIdx++;
            }
            else
            {
                // 匹配中断，重置 Query 索引，Token 继续往后找
                if (qIdx > 0)
                {
                    // 回溯逻辑：如果之前匹配了一部分但断了，重置 query 从头开始
                    // 注意：这里简单的重置可能无法处理复杂的连续匹配回溯，但对于简单拼音搜索够用了
                    qIdx = 0;
                    matchIndices.Clear();
                    firstMatchIndex = -1;
                    // 此时 tIdx 不要加，因为当前的 token 还没作为 query[0] 尝试过
                }
                else
                {
                    tIdx++;
                }
            }
        }

        return false;
    }

    private bool[] CreateMatchArray(int length, List<int> indices)
    {
        var result = new bool[length];
        foreach (var index in indices)
        {
            if (index < length) result[index] = true;
        }

        return result;
    }

    private double CalculateWeight(int totalLength, int startIndex, int matchLength)
    {
        // 简单的权重计算：
        // 越靠前分越高
        // 匹配长度越长分越高
        double score = 1000;
        score -= startIndex * 10; // 越靠后扣分
        score += matchLength * 5;
        return score;
    }

    // 缓存项，避免每次搜索都反射和转换拼音
    private class CachedItem
    {
        public T Source { get; set; }
        public string OriginalString { get; set; }
        public PinyinToken[] PinyinTokens { get; set; }
    }
}