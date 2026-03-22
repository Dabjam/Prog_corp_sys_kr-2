using System.Collections.ObjectModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows;

namespace NetworkAnalyzerApp;

public partial class MainWindow : Window
{
    public ObservableCollection<NetworkInterfaceInfo> NetworkInterfaces { get; } = new();
    public ObservableCollection<string> UrlHistory { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        LoadNetworkInterfaces();
        UrlAnalysisResultTextBlock.Text = "Введите URL и нажмите кнопку \"Анализировать\".";
    }

    private void LoadNetworkInterfaces()
    {
        NetworkInterfaces.Clear();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces()
                     .OrderBy(ni => ni.Name))
        {
            var ipProps = networkInterface.GetIPProperties();
            var unicastV4 = ipProps.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

            var ipAddress = unicastV4?.Address.ToString() ?? "Нет";
            var subnetMask = unicastV4?.IPv4Mask?.ToString() ?? "Нет";
            var macRaw = networkInterface.GetPhysicalAddress().GetAddressBytes();
            var macAddress = macRaw.Length > 0
                ? string.Join("-", macRaw.Select(b => b.ToString("X2")))
                : "Нет";

            var speedInMbps = networkInterface.Speed > 0
                ? $"{networkInterface.Speed / 1_000_000.0:F2} Мбит/с"
                : "Неизвестно";

            NetworkInterfaces.Add(new NetworkInterfaceInfo(
                networkInterface.Name,
                networkInterface.Description,
                ipAddress,
                subnetMask,
                macAddress,
                networkInterface.OperationalStatus.ToString(),
                speedInMbps,
                networkInterface.NetworkInterfaceType.ToString()));
        }
    }

    private async void AnalyzeUrlButton_Click(object sender, RoutedEventArgs e)
    {
        var input = UrlInputTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            MessageBox.Show("Введите URL/URI для анализа.", "Пустой ввод", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            MessageBox.Show(
                "Неверный URL/URI. Добавьте схему, например https://example.com",
                "Ошибка формата",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        UrlInputTextBox.IsEnabled = false;
        try
        {
            var host = uri.Host;
            var port = uri.IsDefaultPort ? "(по умолчанию)" : uri.Port.ToString();
            var path = string.IsNullOrWhiteSpace(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
            var query = string.IsNullOrWhiteSpace(uri.Query) ? "(нет)" : uri.Query;
            var fragment = string.IsNullOrWhiteSpace(uri.Fragment) ? "(нет)" : uri.Fragment;

            var dnsInfo = await ResolveDnsInfoAsync(host);
            var pingInfo = await CheckPingAsync(host);
            var addressType = DetermineAddressType(host, dnsInfo.ResolvedAddresses);

            UrlAnalysisResultTextBlock.Text =
                $"URL: {uri}\n" +
                $"Схема: {uri.Scheme}\n" +
                $"Хост: {host}\n" +
                $"Порт: {port}\n" +
                $"Путь: {path}\n" +
                $"Параметры запроса: {query}\n" +
                $"Фрагмент: {fragment}\n\n" +
                $"Ping: {pingInfo}\n" +
                $"DNS: {dnsInfo.DisplayText}\n" +
                $"Тип адреса: {addressType}";

            UrlHistory.Add($"{DateTime.Now:dd.MM.yyyy HH:mm:ss} | {uri}");
        }
        finally
        {
            UrlInputTextBox.IsEnabled = true;
        }
    }

    private static async Task<string> CheckPingAsync(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 1500);
            if (reply.Status == IPStatus.Success)
            {
                return $"Доступен, время отклика: {reply.RoundtripTime} мс";
            }

            return $"Недоступен ({reply.Status})";
        }
        catch (Exception ex)
        {
            return $"Ошибка проверки: {ex.Message}";
        }
    }

    private static async Task<DnsLookupResult> ResolveDnsInfoAsync(string host)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            if (addresses.Length == 0)
            {
                return new DnsLookupResult("Адреса не найдены", Array.Empty<IPAddress>());
            }

            var display = string.Join(", ", addresses.Select(a => a.ToString()));
            return new DnsLookupResult(display, addresses);
        }
        catch (Exception ex)
        {
            return new DnsLookupResult($"Ошибка DNS: {ex.Message}", Array.Empty<IPAddress>());
        }
    }

    private static string DetermineAddressType(string host, IReadOnlyList<IPAddress> resolvedAddresses)
    {
        if (IPAddress.TryParse(host, out var hostAddress) && IPAddress.IsLoopback(hostAddress))
        {
            return "Loopback";
        }

        var ip = IPAddress.TryParse(host, out hostAddress)
            ? hostAddress
            : resolvedAddresses.FirstOrDefault();

        if (ip is null)
        {
            return "Не удалось определить";
        }

        if (IPAddress.IsLoopback(ip))
        {
            return "Loopback";
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
            {
                return "Локальный (IPv6)";
            }

            return "Публичный (IPv6)";
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            var isPrivate =
                bytes[0] == 10 ||
                (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168) ||
                (bytes[0] == 169 && bytes[1] == 254);

            return isPrivate ? "Локальный (IPv4)" : "Публичный (IPv4)";
        }

        return "Не удалось определить";
    }

    private void RefreshInterfacesButton_Click(object sender, RoutedEventArgs e)
    {
        LoadNetworkInterfaces();
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        UrlHistory.Clear();
    }
}

public sealed record NetworkInterfaceInfo(
    string Name,
    string Description,
    string IpAddress,
    string SubnetMask,
    string MacAddress,
    string Status,
    string Speed,
    string InterfaceType)
{
    public string DisplayName => $"{Name} ({IpAddress})";
}

public sealed record DnsLookupResult(
    string DisplayText,
    IReadOnlyList<IPAddress> ResolvedAddresses);