using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TwitchDownloaderMAUI.Model
{
    abstract class TDPreferences
    {
        public static bool UTCVideoTime
        {
            get => Preferences.Default.Get(nameof(UTCVideoTime), false);
            set => Preferences.Default.Set(nameof(UTCVideoTime), value);
        }

        public static bool VerboseErrors
        {
            get => Preferences.Default.Get(nameof(VerboseErrors), false);
            set => Preferences.Default.Set(nameof(VerboseErrors), value);
        }


        public static int VodDownloadThreads
        {
            get => Preferences.Default.Get(nameof(VodDownloadThreads), 4);
            set => Preferences.Default.Set(nameof(VodDownloadThreads), value);
        }


        public static string Oauth
        {
            get => Preferences.Default.Get(nameof(Oauth), "");
            set => Preferences.Default.Set(nameof(Oauth), value);
        }

        public static bool ThrottleDownloads
        {
            get => Preferences.Default.Get(nameof(ThrottleDownloads), true);
            set => Preferences.Default.Set(nameof(ThrottleDownloads), value);
        }

        public static int MaxDownloadSpeedKiB
        {
            get => Preferences.Default.Get(nameof(MaxDownloadSpeedKiB), 4096);
            set => Preferences.Default.Set(nameof(MaxDownloadSpeedKiB), value);
        }

        public static string VODFilenameTemplate
        {
            get => Preferences.Default.Get(nameof(VODFilenameTemplate), "[{date_custom=\"M-d-yy\"}] {channel} - {title}");
            set => Preferences.Default.Set(nameof(VODFilenameTemplate), value);
        }

        public static string TempDataDirectory
        {
            get => Preferences.Default.Get(nameof(TempDataDirectory), "");
            set => Preferences.Default.Set(nameof(TempDataDirectory), value);
        }

        public static bool HideDonationButton
        {
            get => Preferences.Default.Get(nameof(HideDonationButton), false);
            set => Preferences.Default.Set(nameof(HideDonationButton), value);
        }
    }
}
