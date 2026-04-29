using Microsoft.UI.Xaml;
using Rustun.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Rustun.ViewModels.Pages
{
    public sealed class PeersPageViewModel : ViewModelBase, IDisposable
    {
        private IReadOnlyList<PeerCardViewModel> _peerCards = [];

        public IReadOnlyList<PeerCardViewModel> PeerCards => _peerCards;
        public bool IsPeerEmpty => _peerCards.Count == 0;

        private string _emptyTitle = "暂无 Peer 信息";
        public string EmptyTitle
        {
            get => _emptyTitle;
            private set
            {
                if (_emptyTitle == value) return;
                _emptyTitle = value;
                OnPropertyChanged();
            }
        }

        private string _emptyDescription = "请先在首页连接 VPN！";
        public string EmptyDescription
        {
            get => _emptyDescription;
            private set
            {
                if (_emptyDescription == value) return;
                _emptyDescription = value;
                OnPropertyChanged();
            }
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected == value) return;
                _isConnected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDisconnected));
                UpdateEmptyText();
            }
        }

        public bool IsDisconnected => !_isConnected;

        public PeersPageViewModel()
        {
            VpnService.Instance.PropertyChanged += handleVpnPropertyChanged;
            VpnService.Instance.ConnectionStateChanged += handleVpnConnectionStateChanged;
            IsConnected = VpnService.Instance.IsConnected;
            UpdatePeerCards();
        }

        private void UpdatePeerCards()
        {
            var peers = VpnService.Instance.PeerDetails;
            var cards = new List<PeerCardViewModel>(peers.Count);
            foreach (var p in peers)
            {
                cards.Add(new PeerCardViewModel
                {
                    Identity = p.Identity,
                    PrivateIp = p.PrivateIp,
                    Ipv6 = string.IsNullOrEmpty(p.Ipv6) ? "-" : p.Ipv6,
                    CidersDisplay = FormatCiders(p.Ciders),
                    LastActiveDisplay = FormatLastActive(p.LastActive),
                });
            }

            _peerCards = cards;
            OnPropertyChanged(nameof(PeerCards));
            OnPropertyChanged(nameof(IsPeerEmpty));
            UpdateEmptyText();
        }

        private void UpdateEmptyText()
        {
            if (!IsConnected)
            {
                EmptyTitle = "暂无 Peer 信息";
                EmptyDescription = "请先在首页连接 VPN！";
                return;
            }

            EmptyTitle = "暂无 Peer 信息";
            EmptyDescription = "已连接，等待同步 Peer 列表…";
        }

        private void handleVpnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VpnService.PeerDetails))
            {
                UpdatePeerCards();
            }
        }

        private void handleVpnConnectionStateChanged(object? sender, bool connected)
        {
            IsConnected = connected;
        }

        private static string FormatCiders(IReadOnlyList<string>? ciders)
        {
            if (ciders is null || ciders.Count == 0)
            {
                return "-";
            }
            return string.Join(", ", ciders);
        }

        private static string FormatLastActive(long lastActive)
        {
            if (lastActive <= 0)
            {
                return "-";
            }

            // 兼容秒/毫秒时间戳
            DateTimeOffset ts = lastActive >= 1_000_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(lastActive)
                : DateTimeOffset.FromUnixTimeSeconds(lastActive);

            DateTimeOffset now = DateTimeOffset.Now;
            TimeSpan delta = now - ts.ToLocalTime();
            if (delta < TimeSpan.FromSeconds(0))
            {
                delta = TimeSpan.Zero;
            }

            if (delta < TimeSpan.FromMinutes(1)) return "刚刚";
            if (delta < TimeSpan.FromHours(1)) return $"{(int)delta.TotalMinutes} 分钟前";
            if (delta < TimeSpan.FromDays(1)) return $"{(int)delta.TotalHours} 小时前";
            if (delta < TimeSpan.FromDays(7)) return $"{(int)delta.TotalDays} 天前";

            return ts.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        public Visibility ToVisibility(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

        public void Dispose()
        {
            VpnService.Instance.PropertyChanged -= handleVpnPropertyChanged;
            VpnService.Instance.ConnectionStateChanged -= handleVpnConnectionStateChanged;
        }
    }
}
