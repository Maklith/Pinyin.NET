using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Pinyin.NET;

// 包含原本的 PinyinToken 定义，确保文件完整可用
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
    // 假设 PinyinProcessor 存在于上下文中，这里保留引用
    private readonly PinyinProcessor _pinyinProcessor = new PinyinProcessor();

    public PinyinSearcher(IEnumerable<T> source, Func<T, string> selector)
    {
        _cachedItems = new List<CachedItem>();
        foreach (var item in source)
        {
            var text = selector(item);
            if (string.IsNullOrEmpty(text)) continue;

            _cachedItems.Add(new CachedItem
            {
                Source = item,
                OriginalString = text,
                PinyinTokens = _pinyinProcessor.GetTokens(text)
            });
        }
    }

    public IEnumerable<SearchResults<T>> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Enumerable.Empty<SearchResults<T>>();

        var queryLower = query.ToLower();
        var results = new ConcurrentBag<SearchResults<T>>();

        Parallel.ForEach(_cachedItems, item =>
        {
            // 修复后的 TryMatch 调用
            if (TryMatch(queryLower, item.PinyinTokens, out var matchIndices, out var weight))
            {
                results.Add(new SearchResults<T>
                {
                    Source = item.Source,
                    Weight = weight, // 这里可以结合 matchIndices 的位置进一步优化权重算法
                    CharMatchResults = CreateMatchArray(item.OriginalString.Length, matchIndices)
                });
            }
        });

        // 建议按权重排序返回
        return results.OrderByDescending(r => r.Weight);
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
    /// <summary>
    /// 修复后的匹配算法：双层循环避免死循环和索引错乱
    /// </summary>
    private bool TryMatch(string query, PinyinToken[] tokens, out List<int> matchIndices, out double weight)
    {
        matchIndices = new List<int>();
        weight = 0;

        // currentTokenStartCharIndex: 记录当前考察的 Token 在原字符串中的绝对起始位置
        // 比如 "Hello World", token[1] 是 "World", 它的起始位置应该是 6 (如果Hello是5+空格1)
        int currentTokenStartCharIndex = 0;

        // 外层循环：尝试每一个 Token 作为匹配的起点
        for (int startTIdx = 0; startTIdx < tokens.Length; startTIdx++)
        {
            // 如果剩余 Token 数量甚至少于 Query 长度（极端情况假设每个Token只配1个字符），可以提前剪枝优化，但非必须
            
            // 尝试从 startTIdx 开始匹配整个 Query
            if (IsMatchStartingAt(tokens, startTIdx, currentTokenStartCharIndex, query, out var currentIndices, out var currentWeight))
            {
                matchIndices = currentIndices;
                weight = currentWeight;
                return true; 
                // 找到第一个匹配即可返回。
                // 如果需要找“最佳”匹配（例如越靠前越好），这里已经是了，因为是从头开始遍历的。
            }

            // 更新下一个 Token 的绝对字符位置
            currentTokenStartCharIndex += tokens[startTIdx].Original.Length;
        }

        return false;
    }

    /// <summary>
    /// 内部核心：尝试从指定的 Token 索引开始匹配完整的 Query
    /// </summary>
    private bool IsMatchStartingAt(PinyinToken[] tokens, int startTokenIdx, int startCharPos, string query, out List<int> matchIndices, out double weight)
    {
        matchIndices = new List<int>();
        weight = 0;
        
        int qIdx = 0; // Query 内部游标
        int tIdx = startTokenIdx; // Token 内部游标
        int currentCharPos = startCharPos; // 字符绝对位置游标

        // 循环直到 Query 被完全消耗，或者 Token 用完
        while (qIdx < query.Length && tIdx < tokens.Length)
        {
            var token = tokens[tIdx];
            char charQuery = query[qIdx];
            
            bool matched = false;
            int maxMatchLen = 0;

            // --- 核心匹配逻辑 (保留了你原本的逻辑) ---

            // 1. 检查简拼
            if (token.First.Contains(charQuery))
            {
                maxMatchLen = 1;
                matched = true;
            }

            // 2. 检查全拼 (贪婪匹配)
            foreach (var fullPinyin in token.Full)
            {
                if (fullPinyin.Length > 0 && fullPinyin[0] == charQuery)
                {
                    int currentMatchLen = 0;
                    for (int i = 0; i < fullPinyin.Length && (qIdx + i) < query.Length; i++)
                    {
                        if (fullPinyin[i] == query[qIdx + i])
                            currentMatchLen++;
                        else
                            break;
                    }

                    if (currentMatchLen > maxMatchLen)
                    {
                        maxMatchLen = currentMatchLen;
                        matched = true;
                    }
                }
            }

            if (matched)
            {
                // 记录高亮位置：
                // 修复：只高亮实际匹配的长度，而不是整个 Token 的原始文本
                // 1. 中文：Query="z" (1), Original="张" (1) -> Min(1,1)=1 (高亮"张")
                // 2. 中文全拼：Query="zhang" (5), Original="张" (1) -> Min(5,1)=1 (高亮"张")
                // 3. 英文前缀：Query="hel" (3), Original="hello" (5) -> Min(3,5)=3 (高亮"hel")
                int highlightLen = Math.Min(maxMatchLen, token.Original.Length);

                for (int i = 0; i < highlightLen; i++)
                {
                    matchIndices.Add(currentCharPos + i);
                }

                // 推进 Query 游标
                qIdx += maxMatchLen;
                
                // 推进 Token
                currentCharPos += token.Original.Length;
                tIdx++;
            }
            else
            {
                // 一旦中间断开，说明从 startTokenIdx 开始的这条路径走不通
                return false;
            }
        }

        // 只有当 Query 被完全匹配完才算成功
        if (qIdx >= query.Length)
        {
            // 简单的权重计算：匹配越靠前 (startTokenIdx 小)，权重越高
            weight = 1000 - (startTokenIdx * 10) + (matchIndices.Count * 2);
            return true;
        }

        return false;
    }

    private bool[] CreateMatchArray(int length, List<int> indices)
    {
        var result = new bool[length];
        foreach (var index in indices)
        {
            if (index >= 0 && index < length) result[index] = true;
        }
        return result;
    }

    private class CachedItem
    {
        public T Source { get; set; }
        public string OriginalString { get; set; }
        public PinyinToken[] PinyinTokens { get; set; }
    }

    // 伪代码占位符，为了让代码编译通过
}