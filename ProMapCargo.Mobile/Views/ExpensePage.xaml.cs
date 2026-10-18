using ProMapCargo.Mobile.Services;
using ProMapCargo.Mobile.ViewModels;

namespace ProMapCargo.Mobile.Views;

public partial class ExpensePage : ContentPage, IQueryAttributable
{
    private bool scanOnOpen;

    // Accepted by the server (FileStorageService): JPG, PNG and PDF.
    private static readonly FilePickerFileType ReceiptImages = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.Android] = ["image/jpeg", "image/png"],
    });

    private static readonly FilePickerFileType ReceiptPdf = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.Android] = ["application/pdf"],
    });

    private readonly ExpenseViewModel viewModel;

    public ExpensePage(ExpenseViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        this.viewModel = viewModel;
        viewModel.Saved += (_, _) => SafeUi.Run(() => Shell.Current.GoToAsync(".."));
    }

    // expense?scan=1 (Troškovi → Skeniraj račun): start with the camera.
    public void ApplyQueryAttributes(IDictionary<string, object> query)
        => scanOnOpen = query.TryGetValue("scan", out var value) && Convert.ToString(value) == "1";

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.Prepare();
        if (scanOnOpen)
        {
            scanOnOpen = false;
            OnCameraClicked(null, EventArgs.Empty);
        }
    }

    private void OnBackClicked(object? sender, EventArgs e) => SafeUi.Run(() => Shell.Current.GoToAsync(".."));

    private void OnCategoryClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is ExpenseCategoryOption option)
        {
            viewModel.SelectCategoryCommand.Execute(option);
        }
    }

    private void OnCameraClicked(object? sender, EventArgs e) => SafeUi.Run(async () =>
    {
        if (!MediaPicker.Default.IsCaptureSupported)
        {
            await DisplayAlertAsync("Kamera", "Ovaj uređaj nema kameru. Izaberite račun iz galerije.", "U redu");
            return;
        }

        if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted)
        {
            await DisplayAlertAsync("Kamera", "Za fotografiju računa dozvolite pristup kameri u podešavanjima telefona.", "U redu");
            return;
        }

        await LoadAsync(await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions { Title = "Račun" }));
    });

    private void OnGalleryClicked(object? sender, EventArgs e) => SafeUi.Run(async () =>
        await LoadAsync(await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Fotografija računa", FileTypes = ReceiptImages })));

    private void OnPdfClicked(object? sender, EventArgs e) => SafeUi.Run(async () =>
        await LoadAsync(await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Račun (PDF)", FileTypes = ReceiptPdf })));

    private async Task LoadAsync(FileResult? file)
    {
        if (file is null)
        {
            return; // cancelled
        }

        await using var stream = await file.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);

        // The camera may name the file without an extension the server accepts.
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "image/jpeg" : file.ContentType;
        var extension = contentType switch
        {
            "application/pdf" => ".pdf",
            "image/png" => ".png",
            _ => ".jpg",
        };
        var name = Path.HasExtension(file.FileName) ? file.FileName : file.FileName + extension;
        viewModel.SetReceipt(memory.ToArray(), name, contentType);
    }
}
