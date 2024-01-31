using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using Foundation;
using UIKit;

namespace TwitchDownloaderMAUI.Model
{
        [SupportedOSPlatform("MacCatalyst14.0")]
        public partial class FileDialog
        {
            UIDocumentPickerViewController? documentPickerViewController;
            TaskCompletionSource<string>? taskCompletionSource;

            public partial async Task<string> PromptForFile()
            {
                var fileManager = NSFileManager.DefaultManager;
                var tempDirectoryPath = fileManager.GetTemporaryDirectory().Append(Guid.NewGuid().ToString(), true);
                var isDirectoryCreated = fileManager.CreateDirectory(tempDirectoryPath, true, null, out var error);
                if (!isDirectoryCreated)
                {
                    throw new Exception("Could not create temporary directory");
                }
                var tcs = taskCompletionSource = new();
                NSUrl fileUrl = tempDirectoryPath.Append("test", false);
                if (fileUrl.Path is null) { throw new Exception("Null file url"); }
                using (FileStream fs = File.Open(fileUrl.Path, FileMode.CreateNew))
                {
                    fs.Write(Encoding.UTF8.GetBytes("test string"));
                }
                documentPickerViewController = new(new[] {fileUrl})
                {
                    DirectoryUrl = NSUrl.FromString("/")
                };

                documentPickerViewController.DidPickDocumentAtUrls += DocumentPickerViewControllerOnDidPickDocumentAtUrls;
                documentPickerViewController.WasCancelled += DocumentPickerViewControllerOnWasCancelled;

                var currentViewController = Platform.GetCurrentUIViewController();
                if (currentViewController is not null)
                {
                    currentViewController.PresentViewController(documentPickerViewController, true, null);
                }
                else
                {
                    throw new Exception("Coult not open save dialog");
                }

                return await tcs.Task;
            }

            void DocumentPickerViewControllerOnWasCancelled(object? sender, EventArgs e)
            {
                taskCompletionSource?.TrySetException(new Exception("Operation canceled."));
                InternalDispose();
            }

            void DocumentPickerViewControllerOnDidPickDocumentAtUrls(object? sender, UIDocumentPickedAtUrlsEventArgs e)
            {
                try
                {
                    taskCompletionSource?.TrySetResult(e.Urls[0].Path ?? throw new Exception("Unable to retrieve the path of the saved file."));
                }
                finally
                {
                    InternalDispose();
                }
            }

            void InternalDispose()
            {
                if (documentPickerViewController is not null)
                {
                    documentPickerViewController.DidPickDocumentAtUrls -= DocumentPickerViewControllerOnDidPickDocumentAtUrls;
                    documentPickerViewController.WasCancelled -= DocumentPickerViewControllerOnWasCancelled;
                    documentPickerViewController.Dispose();
                }
            }
        }
}
