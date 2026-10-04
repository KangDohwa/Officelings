using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Vibedotcom.Core;
using Vibedotcom.Core.Sharing;

namespace Vibedotcom.SharingChecks;

internal static partial class Program
{
    private static readonly Assembly AppAssembly = Assembly.Load("Vibedotcom.App");
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static object Settings(string json)
    {
        var type = AppAssembly.GetType("Vibedotcom.App.Settings", throwOnError: true)!;
        var parsed = JsonSerializer.Deserialize(json, type, StateJson.Options)!;
        return type.GetMethod("Normalize", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(parsed, null)!;
    }
    private static Window Dialog(SharingConfiguration configuration, Func<SharingPreview, string?, bool, Task> start, Func<bool, Task> stop) =>
        (Window)Activator.CreateInstance(AppAssembly.GetType("Vibedotcom.App.SharingDialog", true)!, BindingFlags.Instance | BindingFlags.NonPublic,
            null, [configuration, false, start, stop], null)!;

    private static void UiChecks()
    {
        foreach (var input in new[] { "{}", "{\"sharing\":null,\"sharingAliases\":null}", "{\"sharing\":\"invalid\"}" })
            Check("app settings sharing defaults off", () =>
            {
                var settings = Settings(input); Require((string)settings.GetType().GetProperty("Sharing")!.GetValue(settings)! == "off");
            });
        Check("sharing settings roundtrip excludes token and local IDs", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "vibedotcom-sharing-settings-" + Guid.NewGuid().ToString("N"));
            try
            {
                var settings = Settings("{\"sharing\":\"on\",\"sharingServer\":\"https://presence.example\",\"sharingOfficeId\":\"" + Office + "\",\"sharingAliases\":{\"codex:1\":\"Nova\"}}");
                var path = Path.Combine(directory, "settings.json");
                settings.GetType().GetMethod("Write", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, [path]);
                var configuration = SharingConfiguration.Read(path);
                Require(configuration.Sharing == "on" && configuration.SharingAliases["codex:1"] == "Nova");
                Require(!File.ReadAllText(path).Contains("token", StringComparison.OrdinalIgnoreCase) && !File.ReadAllText(path).Contains("private-session"));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
        Check("dialog requires preview and checked consent, sends exactly displayed JSON", () => Sta(() =>
        {
            SharingPreview? sent = null; string? supplied = null; var starts = 0;
            var dialog = Dialog(new() { SharingServer = "https://presence.example", SharingOfficeId = Office, PrivacyMode = "off", SharingAliases = new() { ["codex:1"] = "Nova" } },
                (preview, token, remember) => { starts++; sent = preview; supplied = token; Require(remember); return Task.CompletedTask; }, _ => Task.CompletedTask);
            var children = ((StackPanel)((ScrollViewer)dialog.Content).Content).Children.Cast<UIElement>().ToArray();
            var enable = children.OfType<Button>().Single(b => (string)b.Content == "Enable sharing");
            var review = children.OfType<Button>().Single(b => (string)b.Content == "Preview exact JSON");
            var consent = children.OfType<CheckBox>().Single(b => ((string)b.Content).StartsWith("I reviewed"));
            Require(!enable.IsEnabled);
            enable.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(starts == 0);
            review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var json = children.OfType<TextBox>().Single(b => b.IsReadOnly).Text;
            Require(json.Contains("Nova") && !json.Contains(Secret) && !enable.IsEnabled);
            children.OfType<PasswordBox>().Single().Password = Secret;
            consent.IsChecked = true; Require(enable.IsEnabled);
            enable.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(starts == 1 && supplied == Secret && sent!.Payload.Json == json);
        }));
        Check("editing aliases invalidates preview and consent", () => Sta(() =>
        {
            var dialog = Dialog(new() { SharingServer = "https://presence.example", SharingOfficeId = Office }, (_, _, _) => Task.CompletedTask, _ => Task.CompletedTask);
            var children = ((StackPanel)((ScrollViewer)dialog.Content).Content).Children.Cast<UIElement>().ToArray();
            children.OfType<Button>().Single(b => (string)b.Content == "Preview exact JSON").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var consent = children.OfType<CheckBox>().Single(b => ((string)b.Content).StartsWith("I reviewed")); consent.IsChecked = true;
            children.OfType<TextBox>().Single(b => b.AcceptsReturn && !b.IsReadOnly).Text = "codex:1=Changed";
            Require(consent.IsChecked != true && !children.OfType<Button>().Single(b => (string)b.Content == "Enable sharing").IsEnabled);
            Require(children.OfType<TextBox>().Single(b => b.IsReadOnly).Text == ""); dialog.Close();
        }));
        Check("dialog stop passes optional token deletion", () => Sta(() =>
        {
            var deleted = false;
            var dialog = Dialog(new(), (_, _, _) => Task.CompletedTask, delete => { deleted = delete; return Task.CompletedTask; });
            var children = ((StackPanel)((ScrollViewer)dialog.Content).Content).Children.Cast<UIElement>().ToArray();
            children.OfType<CheckBox>().Single(b => (string)b.Content == "Also delete the stored token").IsChecked = true;
            children.OfType<Button>().Single(b => (string)b.Content == "Stop sharing").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(deleted);
        }));
        Check("full privacy is visible and generic in dialog preview", () => Sta(() =>
        {
            var dialog = Dialog(new() { SharingServer = "https://presence.example", SharingOfficeId = Office, PrivacyMode = "full", SharingAliases = new() { ["codex:1"] = "Chosen" } },
                (_, _, _) => Task.CompletedTask, _ => Task.CompletedTask);
            var children = ((StackPanel)((ScrollViewer)dialog.Content).Content).Children.Cast<UIElement>().ToArray();
            children.OfType<Button>().Single(b => (string)b.Content == "Preview exact JSON").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var json = children.OfType<TextBox>().Single(b => b.IsReadOnly).Text;
            Require(json.Contains("Codex") && !json.Contains("Chosen")); dialog.Close();
        }));
        Check("alias editor rejects duplicate or identity keys", () =>
        {
            var method = AppAssembly.GetType("Vibedotcom.App.SharingDialog", true)!.GetMethod("ParseAliases", BindingFlags.Static | BindingFlags.NonPublic)!;
            foreach (var text in new[] { "codex:1=A\ncodex:1=B", "session-id=A", "codex:65=A", "codex:01=A" })
            {
                try { method.Invoke(null, [text]); throw new InvalidOperationException(); }
                catch (TargetInvocationException e) when (e.InnerException is ArgumentException) { }
            }
        });
        Check("demo office has Sharing context/tray entries and visible off indicator", () => Sta(() =>
        {
            var type = AppAssembly.GetType("Vibedotcom.App.OfficeWindow", true)!;
            var arguments = type.GetConstructors().Single().GetParameters()
                .Select(p => p.Name is "demo" or "qa" ? (object)true : p.DefaultValue).ToArray();
            var window = (Window)Activator.CreateInstance(type, arguments)!;
            Require(window.ContextMenu.Items.OfType<MenuItem>().Any(i => (string)i.Header == "Sharing"));
            var tray = type.GetMethod("BuildTrayMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
            var items = (System.Collections.IEnumerable)tray.GetType().GetProperty("Items")!.GetValue(tray)!;
            Require(items.Cast<object>().Any(i => (string?)i.GetType().GetProperty("Text")!.GetValue(i) == "Sharing"));
            ((IDisposable)tray).Dispose();
            Require(((DockPanel)window.Content).Children.OfType<TextBlock>().Any(b => b.Text == "Sharing: off (demo / QA)"));
            window.Close();
        }));
    }
}
