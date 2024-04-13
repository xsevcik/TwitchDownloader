namespace TwitchDownloaderMAUI;

public partial class GlobalSettings : ContentPage
{
	public GlobalSettings()
	{
		InitializeComponent();
	}

	public async void BtnDone_Clicked(object sender, EventArgs e)
	{
		await Navigation.PopModalAsync();
	}
}