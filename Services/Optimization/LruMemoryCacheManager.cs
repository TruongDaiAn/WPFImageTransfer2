using Emgu.CV;
using System;
using System.Collections.Generic;
using WPFImageTransfer.Helpers;

namespace WPFImageTransfer.Services.Optimization
{
    /// <summary>
    /// Module tối ưu hóa: Bộ nhớ đệm thông minh LRU (Least Recently Used)
    /// Đặt trần dung lượng RAM (ví dụ: tối đa 600MB) và tự động dọn dẹp các Mat cũ, chống tràn bộ nhớ 100%.
    /// </summary>
    public class LruMemoryCacheManager
    {
        private readonly long _maxMemoryBudget;
        private long _currentMemoryUsage = 0;

        private readonly Dictionary<string, LinkedListNode<LruCacheItem>> _cacheMap = new Dictionary<string, LinkedListNode<LruCacheItem>>();
        private readonly LinkedList<LruCacheItem> _lruList = new LinkedList<LruCacheItem>();
        private readonly object _lock = new object();

        private class LruCacheItem
        {
            public string Key { get; set; } = string.Empty;
            public Mat Mat { get; set; } = new Mat();
            public long ByteSize { get; set; }
        }

        public LruMemoryCacheManager(long maxMemoryBudgetMb = 600)
        {
            _maxMemoryBudget = maxMemoryBudgetMb * 1024 * 1024;
        }

        public bool TryGet(string key, out Mat? resultMat)
        {
            lock (_lock)
            {
                if (_cacheMap.TryGetValue(key, out var node) && node.Value.Mat != null && !node.Value.Mat.IsEmpty)
                {
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                    resultMat = node.Value.Mat.Clone();
                    return true;
                }
                resultMat = null;
                return false;
            }
        }

        public void Put(string key, Mat mat)
        {
            if (mat == null || mat.IsEmpty) return;

            lock (_lock)
            {
                long itemSize = (long)mat.Step * mat.Height;

                if (_cacheMap.TryGetValue(key, out var existingNode))
                {
                    _currentMemoryUsage -= existingNode.Value.ByteSize;
                    existingNode.Value.Mat?.Dispose();
                    _lruList.Remove(existingNode);
                    _cacheMap.Remove(key);
                }

                // Dọn dẹp các Mat ít dùng nhất nếu vượt quá giới hạn RAM
                while (_currentMemoryUsage + itemSize > _maxMemoryBudget && _lruList.Count > 0)
                {
                    var oldest = _lruList.Last;
                    if (oldest != null)
                    {
                        _currentMemoryUsage -= oldest.Value.ByteSize;
                        oldest.Value.Mat?.Dispose();
                        _cacheMap.Remove(oldest.Value.Key);
                        _lruList.RemoveLast();
                    }
                }

                var newItem = new LruCacheItem
                {
                    Key = key,
                    Mat = mat.Clone(),
                    ByteSize = itemSize
                };

                var newNode = _lruList.AddFirst(newItem);
                _cacheMap[key] = newNode;
                _currentMemoryUsage += itemSize;
            }
        }

        public void Remove(string key)
        {
            lock (_lock)
            {
                if (_cacheMap.TryGetValue(key, out var node))
                {
                    _currentMemoryUsage -= node.Value.ByteSize;
                    node.Value.Mat?.Dispose();
                    _lruList.Remove(node);
                    _cacheMap.Remove(key);
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                foreach (var node in _lruList)
                {
                    node.Mat?.Dispose();
                }
                _lruList.Clear();
                _cacheMap.Clear();
                _currentMemoryUsage = 0;
            }
        }
    }
}
