using Emgu.CV;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using WPFImageTransfer.Helpers;
using WPFImageTransfer.Models;
using WPFImageTransfer.Services.Optimization;

namespace WPFImageTransfer.Controllers
{
    public sealed class MainWindowController
    {
        private readonly ImageWorkspaceModel _workspace;

        public MainWindowController(ImageWorkspaceModel workspace)
        {
            _workspace = workspace;
        }

        public BitmapSource LoadImage(string path)
        {
            return _workspace.Cache.GetOrLoadBitmap(path);
        }

        public void AddImages(IEnumerable<string> paths)
        {
            foreach (string path in paths)
                _workspace.Cache.AddImage(path);
        }

        public void ClearImages()
        {
            _workspace.Cache.Clear();
            _workspace.History.Clear();
        }

        public void SaveUndoState(BitmapSource? source)
        {
            _workspace.History.SaveState(source);
        }

        public BitmapSource? Undo(BitmapSource currentImage)
        {
            return _workspace.History.Undo(currentImage);
        }

        public BitmapSource? Redo(BitmapSource currentImage)
        {
            return _workspace.History.Redo(currentImage);
        }

        public bool CanUndo => _workspace.History.CanUndo;
        public bool CanRedo => _workspace.History.CanRedo;

        public async Task<IReadOnlyList<ProcessedImageResult>> ApplyFilterAsync(
            Func<Mat, Mat> filter,
            bool applyToAll,
            string currentPath,
            Action<BitmapSource, string, Mat?> applyResult,
            Action resetSliders)
        {
            if (applyToAll)
            {
                IReadOnlyList<ProcessedImageResult> results = await ParallelBatchPipelineService.ProcessImagesParallelAsync(
                    _workspace.ImageList, filter, _workspace.Cache);
                foreach (ProcessedImageResult result in results)
                {
                    if (result.Source != null)
                        applyResult(result.Source, result.FilePath, result.Mat);
                    if (result.FilePath == currentPath && result.Source != null)
                        resetSliders();
                }
                return results;
            }

            if (string.IsNullOrEmpty(currentPath))
                return Array.Empty<ProcessedImageResult>();

            ProcessedImageResult singleResult = await Task.Run(() =>
            {
                using Mat mat = _workspace.Cache.GetOrLoadMat(currentPath);
                Mat processed = filter(mat);
                BitmapSource? source = ImageConversionHelper.ToBitmapSource(processed);
                source?.Freeze();
                return new ProcessedImageResult
                {
                    FilePath = currentPath,
                    Source = source,
                    Mat = processed
                };
            });

            if (singleResult.Source != null)
            {
                applyResult(singleResult.Source, currentPath, singleResult.Mat);
                resetSliders();
            }
            return new[] { singleResult };
        }
    }
}