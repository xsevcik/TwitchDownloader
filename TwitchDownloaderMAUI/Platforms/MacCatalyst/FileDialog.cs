using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TwitchDownloaderMAUI.Model
{
        public partial class FileDialog
        {
            public static partial Task<string> PromptForFile()
        {
            return new Task<string>(() => "");
        }
        }
}
