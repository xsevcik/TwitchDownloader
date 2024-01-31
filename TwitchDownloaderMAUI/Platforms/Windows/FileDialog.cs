using System.Diagnostics;
using Windows.Storage.Pickers;

namespace TwitchDownloaderMAUI.Model
{
    public partial class FileDialog
    {
        public static async partial Task<string> PromptForFile()
        {
            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary
            };
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, Process.GetCurrentProcess().MainWindowHandle);
            savePicker.FileTypeChoices.Add("MP4 Files", new List<string> { ".mp4" });
            savePicker.SuggestedFileName = "test";

            var savePickerOperation = savePicker.PickSaveFileAsync();
            var file = await savePickerOperation;

            return file?.Path ?? string.Empty;
        }
    }
}
