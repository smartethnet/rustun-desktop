namespace Rustun.ViewModels.Pages
{
    public class PeerCardViewModel
    {
        public string Identity { get; init; } = string.Empty;
        public string PrivateIp { get; init; } = string.Empty;
        public string Ipv6 { get; init; } = "-";
        public string CidersDisplay { get; init; } = "-";
        public string LastActiveDisplay { get; init; } = "-";
    }
}
