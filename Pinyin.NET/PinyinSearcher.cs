using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Pinyin.NET;

public class PinyinSearcher<T>
{
    private readonly List<CachedItem> _cachedItems;
    private readonly PinyinProcessor _pinyinProcessor = new PinyinProcessor();

    private static readonly HashSet<char> _splitCharSet = new HashSet<char>
    {
        ' ', '_', '-', '.', ',', '!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '+', '=', '[', ']', '{', '}',
        '\\', '|', ';', ':', '"', '\'', '<', '>', '?', '/', '~', '`'
    };

    public PinyinSearcher(IEnumerable<T> source, Func<T, string> selector)
    {
        _cachedItems = new List<CachedItem>();
        AppendLoad(source, selector);
    }

    public void AppendLoad(IEnumerable<T> source, Func<T, string> selector)
    {
        foreach (var item in source)
        {
            if (_cachedItems.Any(e => e.Source.Equals(item))) continue;
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
        var keywords = queryLower.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        var results = new ConcurrentBag<SearchResults<T>>();

        Parallel.ForEach(_cachedItems, item =>
        {
            if (TryMatchStream(item, keywords, out var matchIndices, out var weight))
            {
                results.Add(new SearchResults<T>
                {
                    Source = item.Source,
                    Weight = weight,
                    CharMatchResults = CreateMatchArray(item.OriginalString.Length, matchIndices)
                });
            }
        });

        return results.OrderByDescending(r => r.Weight)
                      .ThenBy(r => r.Source.ToString().Length);
    }

    private bool TryMatchStream(CachedItem item, string[] keywords, out List<int> allMatchIndices, out double weight)
    {
        allMatchIndices = new List<int>();
        weight = 0;
        int globalCharIndex = 0;

        foreach (var keyword in keywords)
        {
            if (ScanForwardForKeyword(item, globalCharIndex, keyword, out var keywordIndices, out int matchEndIndex))
            {
                allMatchIndices.AddRange(keywordIndices);
                weight += (1000 - matchEndIndex); 
                globalCharIndex = matchEndIndex;
            }
            else
            {
                return false;
            }
        }
        return true;
    }

    private bool ScanForwardForKeyword(CachedItem item, int startIdx, string keyword, out List<int> indices, out int endIndex)
    {
        indices = null;
        endIndex = -1;
        var sourceLen = item.OriginalString.Length;

        for (int i = startIdx; i < sourceLen; i++)
        {
            if (IsMatchSequence(item, i, keyword, out var matchIndices, out int scanEnd))
            {
                indices = matchIndices;
                endIndex = scanEnd;
                return true;
            }
        }
        return false;
    }

    private bool IsMatchSequence(CachedItem item, int startPos, string keyword, out List<int> matchIndices, out int scanEndPos)
    {
        matchIndices = new List<int>();
        scanEndPos = startPos;
        
        int kIdx = 0; // Query 游标
        int sIdx = startPos; // Source 游标

        List<int> gapIndices = new List<int>(); 

        while (kIdx < keyword.Length && sIdx < item.OriginalString.Length)
        {
            // 获取当前字符所属 Token 信息
            GetTokenInfoAt(item, sIdx, out var token, out int charOffsetInToken, out int tokenStartIndex);
            
            // --- 策略1：如果是分隔符，尝试跳过 ---
            // (注：这里把分隔符判断放在前面，防止 "." 字符进入拼音匹配逻辑)
            if (_splitCharSet.Contains(item.OriginalString[sIdx]))
            {
                // 如果用户正好输入了这个符号，那就直接匹配，不跳过
                if (keyword[kIdx] == item.OriginalString[sIdx])
                {
                     // Fall through to char match
                }
                else
                {
                    gapIndices.Add(sIdx);
                    sIdx++;
                    continue;
                }
            }

            // --- 策略2：中文拼音贪婪匹配 (1对多) ---
            // 场景：Query="teng...", Token="腾"(teng). 
            // 动作：一次性消耗 Query 中的 "teng" (4个字符)，Source 消耗 "腾" (1个字符)
            bool pinyinMatched = false;
            if (charOffsetInToken == 0 && IsChinese(token.Original))
            {
                // 检查 Query 从 kIdx 开始，是否匹配该中文字符的任意一个全拼
                int bestMatchLen = 0;
                foreach (var fullPinyin in token.Full)
                {
                    // 计算 query[kIdx...] 与 fullPinyin 的公共前缀长度
                    int currentLen = GetCommonPrefixLength(keyword, kIdx, fullPinyin);
                    
                    // 只有匹配了至少1个字符才算数
                    if (currentLen > bestMatchLen)
                    {
                        bestMatchLen = currentLen;
                    }
                }

                // 如果找到了匹配 (例如匹配了 "t", "te", "teng")
                if (bestMatchLen > 0)
                {
                    matchIndices.AddRange(gapIndices);
                    gapIndices.Clear();
                    
                    matchIndices.Add(sIdx);
                    
                    kIdx += bestMatchLen; // Query 前进 N 步
                    sIdx++;               // Source 前进 1 步 (因为一个汉字就是一个字符)
                    pinyinMatched = true;
                    continue; 
                }
            }

            // --- 策略3：常规单字符匹配 (1对1) ---
            // 适用于：英文、数字、或者中文拼音首字母匹配(如果策略2没覆盖到，或者策略2只匹配了1个字母时)
            if (!pinyinMatched)
            {
                char kChar = keyword[kIdx];
                if (IsCharMatch(token, charOffsetInToken, kChar))
                {
                    matchIndices.AddRange(gapIndices);
                    gapIndices.Clear();
                    matchIndices.Add(sIdx);
                    kIdx++;
                    sIdx++;
                    continue;
                }
            }

            // --- 策略4：跳跃 (Skip) ---
            bool canSkip = false;
            if (charOffsetInToken > 0)
            {
                canSkip = true; // Token 内部，允许跳过
            }
            else
            {
                // Token 开头，非中文允许跳过
                if (!IsChinese(token.Original))
                {
                    canSkip = true;
                }
            }

            if (canSkip)
            {
                int currentTokenEndIndex = tokenStartIndex + token.Original.Length;
                if (sIdx < currentTokenEndIndex)
                {
                    sIdx = currentTokenEndIndex;
                    gapIndices.Clear();
                    continue;
                }
            }

            // 彻底失败
            return false;
        }

        if (kIdx >= keyword.Length)
        {
            scanEndPos = sIdx;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 计算 query 在 kIdx 处的子串与 pinyin 的公共前缀长度
    /// </summary>
    private int GetCommonPrefixLength(string query, int kIdx, string pinyin)
    {
        int len = 0;
        int maxLen = Math.Min(query.Length - kIdx, pinyin.Length);
        for (int i = 0; i < maxLen; i++)
        {
            if (query[kIdx + i] == pinyin[i])
            {
                len++;
            }
            else
            {
                break;
            }
        }
        return len;
    }

    private bool IsCharMatch(PinyinToken token, int offset, char kChar)
    {
        // 原文匹配
        if (token.Original.Length > offset && char.ToLower(token.Original[offset]) == kChar)
            return true;

        // 英文 Token 内部不进行拼音首字母匹配 (防止逻辑混乱)
        // 中文 Token 的首字母匹配已经在前面的“贪婪匹配”中覆盖了 (长度为1的情况)，
        // 但为了保险起见保留这里的检测 (主要用于 First 数组)
        if (offset == 0)
        {
            if (token.First.Contains(kChar)) return true;
        }
        return false;
    }

    private bool IsChinese(string text)
    {
        foreach (char c in text)
        {
            if (c >= 0x4E00 && c <= 0x9FA5) return true;
        }
        return false;
    }

    private void GetTokenInfoAt(CachedItem item, int absoluteIndex, out PinyinToken token, out int offset, out int tokenStartIndex)
    {
        int currentLen = 0;
        for (int i = 0; i < item.PinyinTokens.Length; i++)
        {
            var t = item.PinyinTokens[i];
            int tLen = t.Original.Length;
            
            if (absoluteIndex < currentLen + tLen)
            {
                token = t;
                offset = absoluteIndex - currentLen;
                tokenStartIndex = currentLen;
                return;
            }
            currentLen += tLen;
        }
        token = new PinyinToken { Original = " " };
        offset = 0;
        tokenStartIndex = currentLen;
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
}