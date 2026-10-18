using ProMapCargo.Mobile.ViewModels;

namespace ProMapCargo.Mobile.Views;

/// <summary>Poruke: chat bubble (mine right, dispatch left) or an alert card.</summary>
public sealed class MessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Mine { get; set; }

    public DataTemplate? Dispatch { get; set; }

    public DataTemplate? Alert { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        => item is MessageItem { IsChat: true } message
            ? (message.FromMe ? Mine : Dispatch)!
            : Alert!;
}
