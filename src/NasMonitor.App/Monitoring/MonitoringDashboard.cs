using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NasMonitor.Core;

namespace NasMonitor.App.Monitoring;

internal sealed class MonitoringDashboard : UserControl
{
    private readonly MonitoringWorkspace _workspace;
    private readonly ComboBox _profiles = new() { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _name = new() { Watermark = "NAS name, e.g. Home DS1821+" };
    private readonly TextBox _endpoint = new() { Watermark = "https://nas.local:5001/" };
    private readonly TextBox _username = new() { Watermark = "DSM username" };
    private readonly TextBox _password = new() { Watermark = "Password", PasswordChar = '●' };
    private readonly TextBox _otp = new() { Watermark = "One-time code (if required)", PasswordChar = '●' };
    private readonly CheckBox _remember = new() { Content = "Save credentials in the OS credential store" };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _cards = new() { Spacing = 16 };
    private readonly StackPanel _form = new() { Spacing = 8 };
    private readonly Dictionary<Guid, (TextBlock State, TextBlock Metrics)> _rows = [];
    private bool _loaded, _closed;
    public event Action<NasProfile>? WidgetRequested;
    internal Task CurrentOperation { get; private set; } = Task.CompletedTask;
    public MonitoringDashboard(MonitoringWorkspace workspace)
    {
        _workspace = workspace;
        _remember.IsEnabled = workspace.CanRemember;
        _profiles.SelectionChanged += (_, _) => FillForm();
        _form.Children.Add(new TextBlock { Text = "Connect a Synology NAS", FontSize = 22, FontWeight = FontWeight.SemiBold });
        _form.Children.Add(_profiles);
        _form.Children.Add(_name);
        _form.Children.Add(_endpoint);
        _form.Children.Add(new TextBlock { Text = "HTTPS only. The NAS certificate must be trusted by this computer.", TextWrapping = TextWrapping.Wrap });
        _form.Children.Add(_username);
        _form.Children.Add(_password);
        _form.Children.Add(_otp);
        _form.Children.Add(_remember);
        var connect = new Button { Content = "Connect / save NAS" };
        connect.Click += (_, _) => CurrentOperation = ExecuteAsync(ConnectAsync);
        _form.Children.Add(connect);
        _form.Children.Add(_message);
        var root = new StackPanel { Margin = new Thickness(24), Spacing = 24 };
        root.Children.Add(new TextBlock { Text = "NAS Monitor", FontSize = 30, FontWeight = FontWeight.Bold });
        root.Children.Add(_form);
        root.Children.Add(_cards);
        Content = new ScrollViewer { Content = root };
        _workspace.Monitor.Updated += OnUpdated;
        RefreshProfiles();
        AttachedToVisualTree += (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            CurrentOperation = ExecuteAsync(async () => { await _workspace.InitializeAsync(); RefreshProfiles(); });
        };
    }
    public void StopObserving()
    {
        _closed = true;
        _workspace.Monitor.Updated -= OnUpdated;
        _password.Text = _otp.Text = "";
    }
    private void OnUpdated(MonitorUpdate update) => Dispatcher.UIThread.Post(() =>
    {
        if (!_closed && _rows.TryGetValue(update.Profile.Id, out var row))
        {
            row.State.Text = MetricsText.State(update);
            row.Metrics.Text = MetricsText.Format(update);
        }
    });
    private async Task ExecuteAsync(Func<Task> operation)
    {
        _form.IsEnabled = false;
        _cards.IsEnabled = false;
        try { await operation(); }
        catch (OperationCanceledException) { if (!_closed) _message.Text = "Operation cancelled."; }
        catch (ArgumentException e) { _message.Text = e.Message; }
        catch (Exception) { _message.Text = "Could not update NAS profiles or the credential store. Check storage access and unlock the OS keychain, then retry."; }
        finally { if (!_closed) { _form.IsEnabled = true; _cards.IsEnabled = true; } }
    }
    private async Task ConnectAsync()
    {
        var selected = (_profiles.SelectedItem as Choice)?.Profile;
        var profile = NasProfile.Create(_name.Text ?? "", _endpoint.Text ?? "", _username.Text ?? "",
            _remember.IsChecked == true, selected?.Id);
        if (string.IsNullOrEmpty(_password.Text)) throw new ArgumentException("Enter the DSM password. Saved passwords are never displayed in this form.");
        var credential = new NasCredentials(profile.Username, _password.Text);
        var otp = _otp.Text;
        _password.Text = _otp.Text = "";
        await _workspace.ConnectAsync(profile, credential, otp);
        RefreshProfiles(profile.Id);
        _message.Text = "Profile saved. Connecting… Progress appears on its NAS card.";
    }
    private void FillForm()
    {
        var profile = (_profiles.SelectedItem as Choice)?.Profile;
        _name.Text = profile?.Name ?? "";
        _endpoint.Text = profile?.Endpoint.AbsoluteUri ?? "";
        _username.Text = profile?.Username ?? "";
        _password.Text = _otp.Text = "";
        _remember.IsChecked = _workspace.CanRemember && profile?.RememberCredentials == true;
    }
    private void RefreshProfiles(Guid? selected = null)
    {
        var choices = new[] { new Choice(null) }.Concat(_workspace.Profiles.Select(p => new Choice(p))).ToArray();
        _profiles.ItemsSource = choices;
        _profiles.SelectedItem = choices.FirstOrDefault(x => x.Profile?.Id == selected) ?? choices[0];
        _cards.Children.Clear();
        _rows.Clear();
        foreach (var profile in _workspace.Profiles)
        {
            var update = _workspace.Monitor.Get(profile.Id);
            var state = new TextBlock { Text = MetricsText.State(update), TextWrapping = TextWrapping.Wrap };
            var metrics = new TextBlock { Text = MetricsText.Format(update), TextWrapping = TextWrapping.Wrap };
            var card = new StackPanel { Spacing = 10, Margin = new Thickness(16) };
            card.Children.Add(new TextBlock { Text = profile.Name, FontSize = 22, FontWeight = FontWeight.SemiBold });
            card.Children.Add(new TextBlock { Text = profile.Endpoint.GetLeftPart(UriPartial.Path), Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap });
            card.Children.Add(state);
            card.Children.Add(metrics);
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
            var widget = new Button { Content = "Show widget", Margin = new Thickness(0, 0, 8, 8) };
            widget.Click += (_, _) => WidgetRequested?.Invoke(profile);
            var disconnect = new Button { Content = "Disconnect", Margin = new Thickness(0, 0, 8, 8) };
            disconnect.Click += (_, _) => CurrentOperation = ExecuteAsync(() => _workspace.DisconnectAsync(profile.Id));
            var edit = new Button { Content = "Edit / reconnect", Margin = new Thickness(0, 0, 8, 8) };
            edit.Click += (_, _) => _profiles.SelectedItem = choices.First(x => x.Profile?.Id == profile.Id);
            var remove = new Button { Content = "Remove NAS", Margin = new Thickness(0, 0, 8, 8) };
            remove.Click += (_, _) => CurrentOperation = ExecuteAsync(async () => { await _workspace.RemoveAsync(profile.Id); RefreshProfiles(); });
            buttons.Children.Add(widget); buttons.Children.Add(edit); buttons.Children.Add(disconnect); buttons.Children.Add(remove);
            card.Children.Add(buttons);
            _rows[profile.Id] = (state, metrics);
            _cards.Children.Add(new Border { Child = card, BorderThickness = new Thickness(1), BorderBrush = Brushes.DimGray, CornerRadius = new CornerRadius(8) });
        }
    }
    private sealed record Choice(NasProfile? Profile)
    {
        public override string ToString() => Profile?.Name ?? "+ New NAS";
    }
}
