using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Windows.Input;
using TwitchDownloaderCore;
using TwitchDownloaderCore.Extensions;
using TwitchDownloaderCore.Options;
using TwitchDownloaderCore.Tools;
using TwitchDownloaderCore.TwitchObjects.Gql;
using TwitchDownloaderMAUI.Model;

namespace TwitchDownloaderMAUI;

[SupportedOSPlatform("MacCatalyst14.0")]
[SupportedOSPlatform("Windows")]
public partial class VODDownloader : ContentPage
{
	public readonly Dictionary<string, (string url, int bandwidth)> videoQualities = [];
	public int currentVideoId;
	public DateTime currentVideoTime;
	public TimeSpan vodLength;
	public int viewCount;
	public string game = "";
	private CancellationTokenSource? _cancellationTokenSource;
	private readonly VODDownloaderViewModel _viewModel;

	public VODDownloader()
	{
		InitializeComponent();
		_viewModel = (BindingContext as VODDownloaderViewModel) ?? new VODDownloaderViewModel();
		_viewModel.View = this;
		SetEnabled(false);
		WebRequest.DefaultWebProxy = null;
	}

	private void SetEnabled(bool isEnabled)
	{
		//comboQuality.IsEnabled = isEnabled;
		//checkStart.IsEnabled = isEnabled;
		//checkEnd.IsEnabled = isEnabled;
		btnDownload.IsEnabled = isEnabled;
		btnEnqueue.IsEnabled = isEnabled;
	}
	private void Hyperlink_RequestNavigate(object sender)
	{
		// TODO
		/*
		Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
		e.Handled = true;*/
	}

	private void UpdateActionButtons(bool isDownloading)
	{
			btnDownload.IsVisible = !isDownloading;
			btnEnqueue.IsVisible = !isDownloading;
			btnCancel.IsVisible = isDownloading;
	}

	public VideoDownloadOptions GetOptions(string? filename, string? folder)
	{
		string qualityTitle = _viewModel.Qualities[_viewModel.SelectedQualityIndex];
        VideoDownloadOptions options = new VideoDownloadOptions
		{
			DownloadThreads = _viewModel.NumDownloadThreads,
			ThrottleKib = TDPreferences.ThrottleDownloads
				? TDPreferences.MaxDownloadSpeedKiB
				: -1,
			Filename = filename ?? Path.Combine(folder ?? "", FilenameService.GetFilename(TDPreferences.VODFilenameTemplate, _viewModel.Title, currentVideoId.ToString(), currentVideoTime, _viewModel.Streamer,
				_viewModel.TrimStart == true ? new TimeSpan(_viewModel.NumStartHour, _viewModel.NumStartMinute, _viewModel.NumStartSecond) : TimeSpan.Zero,
				_viewModel.TrimEnd == true ? new TimeSpan(_viewModel.NumEndHour, _viewModel.NumEndMinute, _viewModel.NumEndSecond) : vodLength,
                viewCount.ToString(), game) + (qualityTitle.Contains("Audio", StringComparison.OrdinalIgnoreCase) ? ".m4a" : ".mp4")),
			Oauth = textOauth.Text,
			Quality = GetQualityWithoutSize(qualityTitle).ToString(),
			Id = currentVideoId,
			CropBeginning = _viewModel.TrimStart,
			CropBeginningTime = (int)new TimeSpan(_viewModel.NumStartHour, _viewModel.NumStartMinute, _viewModel.NumStartSecond).TotalSeconds,
			CropEnding = _viewModel.TrimEnd,
			CropEndingTime = (int)new TimeSpan(_viewModel.NumEndHour, _viewModel.NumEndMinute, _viewModel.NumEndSecond).TotalSeconds,
			FfmpegPath = "ffmpeg",
			TempFolder = TDPreferences.TempDataDirectory
		};
		return options;
	}

	public static string GetQualityWithoutSize(string qualityWithSize)
	{
		var qualityIndex = qualityWithSize.LastIndexOf(" - ", StringComparison.Ordinal);
		return qualityIndex == -1
			? qualityWithSize
			: qualityWithSize[..qualityIndex];
	}

	private void OnProgressChanged(ProgressReport progress)
	{
		switch (progress.ReportType)
		{
			case ReportType.Percent:
				_viewModel.Progress = (int)progress.Data;
				break;
			case ReportType.NewLineStatus or ReportType.SameLineStatus:
				statusMessage.Text = (string)progress.Data;
				break;
			case ReportType.Log:
				AppendLog((string)progress.Data);
				break;
		}
	}

	public bool ValidateInputs()
	{
		if (_viewModel.TrimStart)
		{
			var beginTime = new TimeSpan(_viewModel.NumStartHour, _viewModel.NumStartMinute, _viewModel.NumStartSecond);
			if (beginTime.TotalSeconds >= vodLength.TotalSeconds)
			{
				return false;
			}

			if (_viewModel.TrimEnd)
			{
				var endTime = new TimeSpan(_viewModel.NumEndHour, _viewModel.NumEndMinute, _viewModel.NumEndSecond);
				if (endTime.TotalSeconds < beginTime.TotalSeconds)
				{
					return false;
				}
			}
		}

		return true;
	}

	public void AppendLog(string message)
	{
		_viewModel.Log += message + "\n";
	}

	private async void OpenSettings(object sender, EventArgs e)
	{
		await Navigation.PushModalAsync(new GlobalSettings());
	}

	/*private void checkStart_OnCheckStateChanged(object sender, EventArgs e)
	{
		UpdateVideoSizeEstimates();
	}

	private void checkEnd_OnCheckStateChanged(object sender, EventArgs e)
	{
		UpdateVideoSizeEstimates();
	}*/

	private async void BtnDownload_Click(object sender, EventArgs e)
	{

		if (!ValidateInputs())
		{
			AppendLog(Translations.Strings.ErrorLog + Translations.Strings.InvalidCropInputs);
			return;
		}

		/*SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Filter = ((string)comboQuality.SelectedItem).Contains("Audio", StringComparison.OrdinalIgnoreCase) ? "M4A Files | *.m4a" : "MP4 Files | *.mp4",
			FileName = FilenameService.GetFilename(Settings.Default.TemplateVod, textTitle.Text, currentVideoId.ToString(), currentVideoTime, textStreamer.Text,
				checkStart.IsChecked == true ? new TimeSpan(numStartHour, numStartMinute, numStartSecond) : TimeSpan.Zero,
				checkEnd.IsChecked == true ? new TimeSpan(numEndHour, numEndMinute, numEndSecond) : vodLength,
				viewCount.ToString(), game)
		};
		if (saveFileDialog.ShowDialog() == false)
		{
			return;
		}*/
		FileDialog fileDialog = new();
		string fileName = await fileDialog.PromptForFile();

		if (string.IsNullOrEmpty(fileName)) { return; }

		SetEnabled(false);
		//btnGetInfo.IsEnabled = false;

		VideoDownloadOptions options = GetOptions(fileName, null);

		Progress<ProgressReport> downloadProgress = new(OnProgressChanged);
		VideoDownloader? currentDownload = new(options, downloadProgress);
		_cancellationTokenSource = new CancellationTokenSource();

		statusImage.Source = "ppoverheat.gif";
		statusMessage.Text = Translations.Strings.StatusDownloading;
		UpdateActionButtons(true);
		try
		{
			await currentDownload.DownloadAsync(_cancellationTokenSource.Token);
			statusMessage.Text = Translations.Strings.StatusDone;
			statusImage.Source = "pphop.gif";
		}
		catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException && _cancellationTokenSource.IsCancellationRequested)
		{
			statusMessage.Text = Translations.Strings.StatusCanceled;
			statusImage.Source = "pphop.gif";
		}
		catch (Exception ex)
		{
			statusMessage.Text = Translations.Strings.StatusError;
			statusImage.Source = "peeposad.png";
			AppendLog(Translations.Strings.ErrorLog + ex.Message);
			if (Preferences.Default.Get("VerboseErrors", false))
			{
				await DisplayAlert(Translations.Strings.VerboseErrorOutput, ex.ToString(), "Close");
			}
		}
		//btnGetInfo.IsEnabled = true;
		statusProgressBar.Progress = 0;
		_cancellationTokenSource.Dispose();
		UpdateActionButtons(false);

		currentDownload = null;
		GC.Collect();
	}

	private void BtnCancel_Click(object sender, EventArgs e)
	{
		statusMessage.Text = Translations.Strings.StatusCanceling;
		try
		{
			_cancellationTokenSource?.Cancel();
		}
		catch (ObjectDisposedException) { }
	}

   /* private void BtnEnqueue_Click(object sender, EventArgs e)
	{

		if (ValidateInputs())
		{
			var queueOptions = new WindowQueueOptions(this)
			{
				Owner = Application.Current.MainWindow,
				WindowStartupLocation = WindowStartupLocation.CenterOwner
			};
			queueOptions.ShowDialog();
		}
		else
		{
			AppendLog(Translations.Strings.ErrorLog + Translations.Strings.InvalidCropInputs);
		}
	}

	private async void TextUrl_OnKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter)
		{
			await GetVideoInfo();
			e.Handled = true;
		}
	}*/
}

[SupportedOSPlatform("MacCatalyst14.0")]
[SupportedOSPlatform("Windows")]
public class VODDownloaderViewModel : INotifyPropertyChanged
{
	const string _defaultThumb = "placeholder.png";

    #region Constructor
	public VODDownloaderViewModel()
	{
		CmdOpenDonatePage = new Command(
				() => Browser.Default.OpenAsync(new Uri("https://www.buymeacoffee.com/lay295"), BrowserLaunchMode.SystemPreferred)
			);

		CmdOpenSettings = new Command(
				async () => await View!.Navigation.PushModalAsync(new GlobalSettings()),
				() => View is not null);

		CmdGetVideoInfo = new Command(
				async () => await GetVideoInfo()
			);

		PropertyChanged += VideoSizePropertyChanged;
	}
    #endregion Constructor

    #region Event Management
    public event PropertyChangedEventHandler? PropertyChanged;
	public void OnPropertyChanged([CallerMemberName] string propName = "") =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
	#endregion Event Management

	public VODDownloader View { get; set; }

	#region Bindings
	public int NumDownloadThreads
	{
		get => TDPreferences.VodDownloadThreads;
		set
		{
			if (value == TDPreferences.VodDownloadThreads) { return; }
			TDPreferences.VodDownloadThreads = value;
			OnPropertyChanged();
		}
	}

	public string Oauth
	{
		get { return TDPreferences.Oauth; }
		set
		{
			if (value == TDPreferences.Oauth) { return; }
			TDPreferences.Oauth = value;
			OnPropertyChanged();
		}
	}

	public bool HideDonationButton
	{
        get { return TDPreferences.HideDonationButton; }
        set
        {
            if (value == TDPreferences.HideDonationButton) { return; }
            TDPreferences.HideDonationButton = value;
            OnPropertyChanged();
        }
    }

	public int NumStartHour
	{
		get => _numStartHour;
		set
		{
			if (_numStartHour == value) { return; }
			_numStartHour = value;
			OnPropertyChanged();
		}
	}
	private int _numStartHour;
	public int NumStartMinute
	{
		get
		{
			return _numStartMinute;
		}
		set
		{
			if (_numStartMinute == value) { return; }
			_numStartMinute = value;
			OnPropertyChanged();
		}
	}
	private int _numStartMinute;
	public int NumStartSecond
	{
		get
		{
			return _numStartSecond;
		}
		set
		{
			if (_numStartSecond == value) { return; }
			_numStartSecond = value;
			OnPropertyChanged();
		}
	}
	private int _numStartSecond;

	public int NumEndHour
	{
		get
		{
			return _numEndHour;
		}
		set
		{
			if (_numEndHour == value) { return; }
			_numEndHour = value;
			OnPropertyChanged();
		}
	}
	private int _numEndHour;
	public int NumEndMinute
	{
		get
		{
			return _numEndMinute;
		}
		set
		{
			if (_numEndMinute == value) { return; }
			_numEndMinute = value;
			OnPropertyChanged();
		}
	}
	private int _numEndMinute;
	public int NumEndSecond
	{
		get
		{
			return _numEndSecond;
		}
		set
		{
			if (_numEndSecond == value) { return; }
			_numEndSecond = value;
			OnPropertyChanged();
		}
	}
	private int _numEndSecond;


	public bool TrimStart
	{
		get
		{
			return _trimStart;
		}
		set
		{
			if (_trimStart == value) { return; }
			_trimStart = value;
			OnPropertyChanged();
		}
	}
	private bool _trimStart;

	/// <summary>
	/// Whether to use customized end trim values
	/// </summary>
	public bool TrimEnd
	{
		get
		{
			return _trimEnd;
		}
		set
		{
			if (_trimEnd == value) { return; }
			_trimEnd = value;
			OnPropertyChanged();
		}
	}
	private bool _trimEnd;


	public string Log
	{
		get { return _log; }
		set
		{
			if (value == _log) { return; }
			_log = value;
			OnPropertyChanged();
		}
	}
	private string _log = "";

	public int Progress
	{
		get { return _progress; }
		set
		{
			if (value == _progress) { return; }
			_progress = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(ProgressText));
		}
	}
	private int _progress = 0;

	public string ProgressText
	{
		get { return Progress.ToString() + "%"; }
	}

	public string VODLink
	{
		get => _vodLink;
		set
		{
			if (value == _vodLink) { return; }
			_vodLink = value;
			OnPropertyChanged();
		}
	}
	private string _vodLink = "";


	public bool DownloadInProgress
	{
		get => _downloadInProgress;
		set
		{
			if (value == _downloadInProgress) { return; }
			_downloadInProgress = value;
			OnPropertyChanged();
		}
	}
	private bool _downloadInProgress;

	public ObservableCollection<string> Qualities
	{
		get => _qualities;
		set 
		{
            if (value == _qualities) { return; }
            _qualities = value;
			OnPropertyChanged();
		}
	}
    private ObservableCollection<string> _qualities = new ObservableCollection<string>();

	private int _selectedQualityIndex;

	public int SelectedQualityIndex
	{
		get { return _selectedQualityIndex; }
		set 
		{
            if (value == _selectedQualityIndex)
            {
				return;
            }
            _selectedQualityIndex = value;
			OnPropertyChanged();
		}
	}

	// preview streamer
	private string _streamer = "";

	public string Streamer
	{
		get { return _streamer; }
        set
        {
            if (value == _streamer)
            {
                return;
            }
            _streamer = value;
            OnPropertyChanged();
        }
    }

	// preview title
	private string _title = "";

	public string Title
	{
		get { return _title; }
        set
        {
            if (value == _title)
            {
                return;
            }
            _title = value;
            OnPropertyChanged();
        }
    }

	// preview creation date
	private string _createdOn = "";

	public string CreatedOn
	{
		get { return _createdOn; }
        set
        {
            if (value == _createdOn)
            {
                return;
            }
            _createdOn = value;
            OnPropertyChanged();
        }
    }


	// preview thumbnail
	private string _thumbnail = _defaultThumb;

	public string Thumbnail
	{
		get { return _thumbnail; }
        set
        {
            if (value == _thumbnail)
            {
                return;
            }

			_thumbnail = value;
			OnPropertyChanged();
        }
    }


	// preview length
	private string _vodLength = "";

	public string VODLength
	{
		get { return _vodLength; }
        set
        {
            if (value == _vodLength)
            {
                return;
            }
            _vodLength = value;
            OnPropertyChanged();
        }
    }


	#endregion Session Properties

	#region Commands
	public ICommand CmdOpenDonatePage { get; private set; }
	public ICommand CmdOpenSettings { get; private set; }
	public ICommand CmdGetVideoInfo { get; private set; }
    #endregion Commands

    #region Methods
    private void VideoSizePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is null) { return; }

		if (!new List<string> 
		{
			nameof(NumStartHour),
			nameof(NumStartMinute),
			nameof(NumStartSecond),
			nameof(NumEndHour),
			nameof(NumEndMinute),
			nameof(NumEndSecond),
			nameof(TrimStart),
			nameof(TrimEnd),
		}.Contains(e.PropertyName)) { return; }

		UpdateVideoSizeEstimates();
	}

	public void UpdateVideoSizeEstimates()
	{
        int selectedIndex = SelectedQualityIndex;

        var cropStart = TrimStart == true
            ? new TimeSpan(NumStartHour, NumStartMinute, NumStartSecond)
            : TimeSpan.Zero;
        var cropEnd = TrimEnd == true
            ? new TimeSpan(NumEndHour, NumEndMinute, NumEndSecond)
            : View.vodLength;

        for (var i = 0; i < Qualities.Count; i++)
        {
            var qualityWithSize = Qualities[i];
            var quality = VODDownloader.GetQualityWithoutSize(qualityWithSize);
            var bandwidth = View.videoQualities[quality].bandwidth;

            var sizeInBytes = VideoSizeEstimator.EstimateVideoSize(bandwidth, cropStart, cropEnd);
            if (sizeInBytes == 0)
            {
                Qualities[i] = quality;
            }
            else
            {
                var newVideoSize = VideoSizeEstimator.StringifyByteCount(sizeInBytes);
				Qualities[i] = $"{quality} - {newVideoSize}";
            }
        }

        SelectedQualityIndex = selectedIndex;
    }

    private async Task GetVideoInfo()
    {
        int videoId = ValidateUrl(VODLink.Trim() ?? "");
        if (videoId <= 0)
        {
            await View.DisplayAlert(Translations.Strings.InvalidVideoLinkId, Translations.Strings.InvalidVideoLinkIdMessage, "Close");
            return;
        }

        View.currentVideoId = videoId;
        try
        {
            Task<GqlVideoResponse> taskVideoInfo = TwitchHelper.GetVideoInfo(videoId);
            Task<GqlVideoTokenResponse> taskAccessToken = TwitchHelper.GetVideoToken(videoId, Oauth);
            await Task.WhenAll(taskVideoInfo, taskAccessToken);

            if (taskAccessToken.Result.data.videoPlaybackAccessToken is null)
            {
                throw new NullReferenceException("Invalid VOD, deleted/expired VOD possibly?");
            }

            var thumbUrl = taskVideoInfo.Result.data.video.thumbnailURLs.FirstOrDefault();
            Thumbnail = thumbUrl ?? _defaultThumb;

            Qualities.Clear();
            View.videoQualities.Clear();

            var playlistString = await TwitchHelper.GetVideoPlaylist(videoId, taskAccessToken.Result.data.videoPlaybackAccessToken.value, taskAccessToken.Result.data.videoPlaybackAccessToken.signature);
            if (playlistString.Contains("vod_manifest_restricted") || playlistString.Contains("unauthorized_entitlements"))
            {
                throw new NullReferenceException(Translations.Strings.InsufficientAccessMayNeedOauth);
            }

            var videoPlaylist = M3U8.Parse(playlistString);
            videoPlaylist.SortStreamsByQuality();

            //Add video qualities to combo quality
            foreach (var stream in videoPlaylist.Streams)
            {
                var userFriendlyName = stream.GetResolutionFramerateString();
                if (!View.videoQualities.ContainsKey(userFriendlyName))
                {
                    View.videoQualities.Add(userFriendlyName, (stream.Path, stream.StreamInfo.Bandwidth));
                    Qualities.Add(userFriendlyName);
                }
            }

            SelectedQualityIndex = 0;
            OnPropertyChanged(nameof(VODDownloaderViewModel.SelectedQualityIndex));

            View.vodLength = TimeSpan.FromSeconds(taskVideoInfo.Result.data.video.lengthSeconds);
            Streamer = taskVideoInfo.Result.data.video.owner.displayName;
            Title = taskVideoInfo.Result.data.video.title;
            var videoCreatedAt = taskVideoInfo.Result.data.video.createdAt;
            CreatedOn = TDPreferences.UTCVideoTime ? videoCreatedAt.ToString(CultureInfo.CurrentCulture) : videoCreatedAt.ToLocalTime().ToString(CultureInfo.CurrentCulture);
            View.currentVideoTime = TDPreferences.UTCVideoTime ? videoCreatedAt : videoCreatedAt.ToLocalTime();
            var urlTimeCodeMatch = TwitchRegex.UrlTimeCode.Match(VODLink);
            if (urlTimeCodeMatch.Success)
            {
                var time = UrlTimeCode.Parse(urlTimeCodeMatch.ValueSpan);
                TrimStart = true;
                NumStartHour = time.Hours;
                NumStartMinute = time.Minutes;
                NumStartSecond = time.Seconds;
            }
            else
            {
                NumStartHour = 0;
                NumStartMinute = 0;
                NumStartSecond = 0;
            }

            NumEndHour = (int)View.vodLength.TotalHours;
            NumEndMinute = View.vodLength.Minutes;
            NumEndSecond = View.vodLength.Seconds;
            VODLength = View.vodLength.ToString("c");
            View.viewCount = taskVideoInfo.Result.data.video.viewCount;
            View.game = taskVideoInfo.Result.data.video.game?.displayName ?? "Unknown";

            //UpdateVideoSizeEstimates();
        }
        catch (Exception ex)
        {
            //btnGetInfo.IsEnabled = true;
            Log += Translations.Strings.ErrorLog + ex.Message + '\n';
            await View.DisplayAlert(Translations.Strings.UnableToGetInfo, Translations.Strings.UnableToGetVideoInfo, "Close");
            if (TDPreferences.VerboseErrors)
            {
                await View.DisplayAlert(Translations.Strings.VerboseErrorOutput, ex.ToString(), "Close");
            }
        }
    }

    private static int ValidateUrl(string text)
    {
        var vodIdMatch = TwitchRegex.MatchVideoId(text);
        if (vodIdMatch is { Success: true } && int.TryParse(vodIdMatch.ValueSpan, out var vodId))
        {
            return vodId;
        }

        return -1;
    }
    #endregion Methods
}

public class PercentToDouble : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is null) { return 0d; }
		return (int)value / 100d;
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is null) { return 0; }
		return (int)((double)value * 100);
	}
}

public class Invert : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return !(bool)(value ?? false);
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		Convert(value, targetType, parameter, culture);
}