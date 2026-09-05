using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace WPFImageTransfer.Services
{
    /// <summary>
    /// Quản lý lịch sử thao tác ảnh cho phép Undo (Ctrl+Z) và Redo (Ctrl+Y).
    /// </summary>
    public class ImageHistoryManager
    {
        private const int MAX_UNDO_STEPS = 5;
        private readonly LinkedList<BitmapSource> _undoList = new LinkedList<BitmapSource>();
        private readonly LinkedList<BitmapSource> _redoList = new LinkedList<BitmapSource>();

        public bool CanUndo => _undoList.Count > 0;
        public bool CanRedo => _redoList.Count > 0;

        /// <summary>
        /// Lưu trạng thái hiện tại của ảnh vào stack Undo trước khi áp dụng bộ lọc mới.
        /// </summary>
        public void SaveState(BitmapSource? currentSource)
        {
            if (currentSource == null) return;

            if (!currentSource.IsFrozen && currentSource.CanFreeze)
            {
                currentSource.Freeze();
            }

            if (_undoList.Count >= MAX_UNDO_STEPS)
            {
                _undoList.RemoveLast();
            }

            _undoList.AddFirst(currentSource);
            _redoList.Clear();
        }

        /// <summary>
        /// Hoàn tác thao tác gần nhất (Undo).
        /// </summary>
        public BitmapSource? Undo(BitmapSource? currentSource)
        {
            if (_undoList.Count == 0) return null;

            if (currentSource != null)
            {
                _redoList.AddFirst(currentSource);
            }

            var firstNode = _undoList.First;
            if (firstNode == null) return null;

            var previousState = firstNode.Value;
            _undoList.RemoveFirst();
            return previousState;
        }

        /// <summary>
        /// Làm lại thao tác vừa Undo (Redo).
        /// </summary>
        public BitmapSource? Redo(BitmapSource? currentSource)
        {
            if (_redoList.Count == 0) return null;

            if (currentSource != null)
            {
                _undoList.AddFirst(currentSource);
            }

            var firstNode = _redoList.First;
            if (firstNode == null) return null;

            var nextState = firstNode.Value;
            _redoList.RemoveFirst();
            return nextState;
        }

        /// <summary>
        /// Xóa bước Undo gần nhất khi có lỗi xảy ra.
        /// </summary>
        public void RemoveFirstUndo()
        {
            if (_undoList.Count > 0)
            {
                _undoList.RemoveFirst();
            }
        }

        /// <summary>
        /// Làm rỗng toàn bộ lịch sử khi chuyển sang ảnh khác hoặc xóa danh sách.
        /// </summary>
        public void Clear()
        {
            _undoList.Clear();
            _redoList.Clear();
        }
    }
}
