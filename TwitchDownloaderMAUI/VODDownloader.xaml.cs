using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using TwitchDownloaderCore;
using TwitchDownloaderCore.Extensions;
using TwitchDownloaderCore.Options;
using TwitchDownloaderCore.Tools;
using TwitchDownloaderCore.TwitchObjects.Gql;
using TwitchDownloaderMAUI.Model;

namespace TwitchDownloaderMAUI;

[SupportedOSPlatform("MacCatalyst14.0")]
public partial class VODDownloader : ContentPage
{
	public readonly Dictionary<string, (string url, int bandwidth)> videoQualities = [];
	public int currentVideoId;
	public DateTime currentVideoTime;
	public TimeSpan vodLength;
	public int viewCount;
	public string game;
	private CancellationTokenSource _cancellationTokenSource;
	private readonly VODDownloaderViewModel _viewModel;

	public VODDownloader()
	{
		InitializeComponent();
		_viewModel = (BindingContext as VODDownloaderViewModel) ?? new VODDownloaderViewModel();
		SetEnabled(false);
		WebRequest.DefaultWebProxy = null;
	}

	private void SetEnabled(bool isEnabled)
	{
		comboQuality.IsEnabled = isEnabled;
		checkStart.IsEnabled = isEnabled;
		checkEnd.IsEnabled = isEnabled;
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

	private async void BtnGetInfo_Click(object sender, EventArgs e)
	{
		await GetVideoInfo();
	}

	private async Task GetVideoInfo()
	{
		int videoId = ValidateUrl(textUrl.Text?.Trim() ?? "");
		if (videoId <= 0)
		{
			await DisplayAlert(Translations.Strings.InvalidVideoLinkId, Translations.Strings.InvalidVideoLinkIdMessage, "Close");
			return;
		}

		currentVideoId = videoId;
		try
		{
			Task<GqlVideoResponse> taskVideoInfo = TwitchHelper.GetVideoInfo(videoId);
			Task<GqlVideoTokenResponse> taskAccessToken = TwitchHelper.GetVideoToken(videoId, textOauth.Text);
			await Task.WhenAll(taskVideoInfo, taskAccessToken);

			if (taskAccessToken.Result.data.videoPlaybackAccessToken is null)
			{
				throw new NullReferenceException("Invalid VOD, deleted/expired VOD possibly?");
			}

			var thumbUrl = taskVideoInfo.Result.data.video.thumbnailURLs.FirstOrDefault();
			imgThumbnail.Source = thumbUrl;

			comboQuality.Items.Clear();
			videoQualities.Clear();

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
				if (!videoQualities.ContainsKey(userFriendlyName))
				{
					videoQualities.Add(userFriendlyName, (stream.Path, stream.StreamInfo.Bandwidth));
					comboQuality.Items.Add(userFriendlyName);
				}
			}

			comboQuality.SelectedIndex = 0;

			vodLength = TimeSpan.FromSeconds(taskVideoInfo.Result.data.video.lengthSeconds);
			textStreamer.Text = taskVideoInfo.Result.data.video.owner.displayName;
			textTitle.Text = taskVideoInfo.Result.data.video.title;
			var videoCreatedAt = taskVideoInfo.Result.data.video.createdAt;
			textCreatedAt.Text = TDPreferences.UTCVideoTime ? videoCreatedAt.ToString(CultureInfo.CurrentCulture) : videoCreatedAt.ToLocalTime().ToString(CultureInfo.CurrentCulture);
			currentVideoTime = TDPreferences.UTCVideoTime ? videoCreatedAt : videoCreatedAt.ToLocalTime();
			var urlTimeCodeMatch = TwitchRegex.UrlTimeCode.Match(_viewModel.VODLink);
			if (urlTimeCodeMatch.Success)
			{
				var time = UrlTimeCode.Parse(urlTimeCodeMatch.ValueSpan);
				checkStart.IsChecked = true;
				_viewModel.NumStartHour = time.Hours;
				_viewModel.NumStartMinute = time.Minutes;
				_viewModel.NumStartSecond = time.Seconds;
			}
			else
			{
				_viewModel.NumStartHour = 0;
				_viewModel.NumStartMinute = 0;
				_viewModel.NumStartSecond = 0;
			}
			// TODO: figure out maximum numbers
			// fldStartHour.Maximum = (int)vodLength.TotalHours;

			_viewModel.NumEndHour = (int)vodLength.TotalHours;
			//numEndHour.Maximum = (int)vodLength.TotalHours;
			_viewModel.NumEndMinute = vodLength.Minutes;
			_viewModel.NumEndSecond = vodLength.Seconds;
			labelLength.Text = vodLength.ToString("c");
			viewCount = taskVideoInfo.Result.data.video.viewCount;
			game = taskVideoInfo.Result.data.video.game?.displayName ?? "Unknown";

			UpdateVideoSizeEstimates();

			SetEnabled(true);
		}
		catch (Exception ex)
		{
			btnGetInfo.IsEnabled = true;
			AppendLog(Translations.Strings.ErrorLog + ex.Message);
			await DisplayAlert(Translations.Strings.UnableToGetInfo, Translations.Strings.UnableToGetVideoInfo, "Close");
			if (TDPreferences.VerboseErrors)
			{
				await DisplayAlert(Translations.Strings.VerboseErrorOutput, ex.ToString(), "Close");
			}
		}
	}

	private void UpdateActionButtons(bool isDownloading)
	{
			btnDownload.IsVisible = !isDownloading;
			btnEnqueue.IsVisible = !isDownloading;
			btnCancel.IsVisible = isDownloading;
	}

	public VideoDownloadOptions GetOptions(string? filename, string? folder)
	{
		VideoDownloadOptions options = new VideoDownloadOptions
		{
			DownloadThreads = _viewModel.NumDownloadThreads,
			ThrottleKib = TDPreferences.ThrottleDownloads
				? TDPreferences.MaxDownloadSpeedKiB
				: -1,
			Filename = filename ?? Path.Combine(folder, FilenameService.GetFilename(TDPreferences.VODFilenameTemplate, textTitle.Text, currentVideoId.ToString(), currentVideoTime, textStreamer.Text,
				checkStart.IsChecked == true ? new TimeSpan(_viewModel.NumStartHour, _viewModel.NumStartMinute, _viewModel.NumStartSecond) : TimeSpan.Zero,
				checkEnd.IsChecked == true ? new TimeSpan(_viewModel.NumEndHour, _viewModel.NumEndMinute, _viewModel.NumEndSecond) : vodLength,
				viewCount.ToString(), game) + (((string)comboQuality.SelectedItem).Contains("Audio", StringComparison.OrdinalIgnoreCase) ? ".m4a" : ".mp4")),
			Oauth = textOauth.Text,
			Quality = GetQualityWithoutSize((string)comboQuality.SelectedItem).ToString(),
			Id = currentVideoId,
			CropBeginning = checkStart.IsChecked,
			CropBeginningTime = (int)new TimeSpan(_viewModel.NumStartHour, _viewModel.NumStartMinute, _viewModel.NumStartSecond).TotalSeconds,
			CropEnding = checkEnd.IsChecked,
			CropEndingTime = (int)new TimeSpan(_viewModel.NumEndHour, _viewModel.NumEndMinute, _viewModel.NumEndSecond).TotalSeconds,
			FfmpegPath = "ffmpeg",
			TempFolder = TDPreferences.TempDataDirectory
		};
		return options;
	}

	private void UpdateVideoSizeEstimates()
	{
		int selectedIndex = comboQuality.SelectedIndex;

		var cropStart = checkStart.IsChecked == true
			? new TimeSpan(_viewModel.NumStartHour, _viewModel.NumStartMinute, _viewModel.NumStartSecond)
			: TimeSpan.Zero;
		var cropEnd = checkEnd.IsChecked == true
			? new TimeSpan(_viewModel.NumEndHour, _viewModel.NumEndMinute, _viewModel.NumEndSecond)
			: vodLength;

		for (var i = 0; i < comboQuality.Items.Count; i++)
		{
			var qualityWithSize = (string)comboQuality.Items[i];
			var quality = GetQualityWithoutSize(qualityWithSize);
			var bandwidth = videoQualities[quality].bandwidth;

			var sizeInBytes = VideoSizeEstimator.EstimateVideoSize(bandwidth, cropStart, cropEnd);
			if (sizeInBytes == 0)
			{
				comboQuality.Items[i] = quality;
			}
			else
			{
				var newVideoSize = VideoSizeEstimator.StringifyByteCount(sizeInBytes);
				comboQuality.Items[i] = $"{quality} - {newVideoSize}";
			}
		}

		comboQuality.SelectedIndex = selectedIndex;
	}

	private static string GetQualityWithoutSize(string qualityWithSize)
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

	private static int ValidateUrl(string text)
	{
		var vodIdMatch = TwitchRegex.MatchVideoId(text);
		if (vodIdMatch is { Success: true } && int.TryParse(vodIdMatch.ValueSpan, out var vodId))
		{
			return vodId;
		}

		return -1;
	}

	public bool ValidateInputs()
	{
		if (checkStart.IsChecked)
		{
			var beginTime = new TimeSpan(_viewModel.NumStartHour, _viewModel.NumStartMinute, _viewModel.NumStartSecond);
			if (beginTime.TotalSeconds >= vodLength.TotalSeconds)
			{
				return false;
			}

			if (checkEnd.IsChecked)
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

	private void AppendLog(string message)
	{
		_viewModel.Log += message + "\n";
	}

	/*private void numDownloadThreads_ValueChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e)
	{
		if (this.IsInitialized && numDownloadThreads.IsEnabled)
		{
			Settings.Default.VodDownloadThreads = (int)numDownloadThreads.Value;
			Settings.Default.Save();
		}
	}

	private void TextOauth_TextChanged(object sender, RoutedEventArgs e)
	{
		if (this.IsInitialized)
		{
			Settings.Default.OAuth = TextOauth.Text;
			Settings.Default.Save();
		}
	}*/

	private void btnDonate_Click(object sender, EventArgs e)
	{
		Process.Start(new ProcessStartInfo("https://www.buymeacoffee.com/lay295") { UseShellExecute = true });
	}

	/*private void btnSettings_Click(object sender, EventArgs e)
	{
		var settings = new WindowSettings
		{
			Owner = Application.Current.MainWindow,
			WindowStartupLocation = WindowStartupLocation.CenterOwner
		};
		settings.ShowDialog();
		btnDonate.IsVisible = !Settings.Default.HideDonation;
	}*/

	private void Page_Loaded(object sender, EventArgs e)
	{
		btnDonate.IsVisible = !TDPreferences.HideDonationButton;
	}

	private void checkStart_OnCheckStateChanged(object sender, EventArgs e)
	{
		UpdateVideoSizeEstimates();
	}

	private void checkEnd_OnCheckStateChanged(object sender, EventArgs e)
	{
		UpdateVideoSizeEstimates();
	}

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
		btnGetInfo.IsEnabled = false;

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
		btnGetInfo.IsEnabled = true;
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
			_cancellationTokenSource.Cancel();
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

	private void numEndHour_ValueChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e)
	{
		UpdateVideoSizeEstimates();
	}

	private void numEndMinute_ValueChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e)
	{
		UpdateVideoSizeEstimates();
	}

	private void numEndSecond_ValueChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e)
	{
		UpdateVideoSizeEstimates();
	}

	private void numStartHour_ValueChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e)
	{
		UpdateVideoSizeEstimates();
	}

	private void numStartMinute_ValueChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e)
	{
		UpdateVideoSizeEstimates();
	}

	private void numStartSecond_ValueChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e)
	{
		UpdateVideoSizeEstimates();
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

public class VODDownloaderViewModel : INotifyPropertyChanged
{
	#region Event Management
	public event PropertyChangedEventHandler? PropertyChanged;
	public void OnPropertyChanged([CallerMemberName] string propName = "") =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
	#endregion Event Management

	#region Persistent Properties
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
	#endregion Persistent Properties

	#region Session Properties
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
	#endregion Session Properties
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