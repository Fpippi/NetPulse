using NetPulse.Core.Connection;

namespace NetPulse.Tests.Connection;

[TestFixture]
public class WirelessParsersTests
{
    [Test]
    public void Reads_the_signal_level_from_proc_net_wireless()
    {
        const string text = """
            Inter-| sta-|   Quality        |   Discarded packets               | Missed | WE
             face | tus | link level noise |  nwid  crypt   frag  retry   misc | beacon | 22
             wlan0: 0000   54.  -56.  -256        0      0      0      0      0        0
            wlp3s0: 0000   40.  -71.  -256        0      0      0     12      0        0
            """;

        var levels = WirelessParsers.ParseProcNetWireless(text);

        Assert.That(levels, Is.EqualTo(new Dictionary<string, int> { ["wlan0"] = -56, ["wlp3s0"] = -71 }));
    }

    [Test]
    public void Converts_unsigned_levels_of_old_drivers() =>
        Assert.That(WirelessParsers.ParseProcNetWireless(" wlan0: 0000   54.  200.  0  0 0 0 0 0 0")["wlan0"], Is.EqualTo(-56));

    [Test]
    public void An_empty_proc_file_has_no_interfaces() =>
        Assert.That(WirelessParsers.ParseProcNetWireless("Inter-| sta-|\n face | tus |\n"), Is.Empty);

    [Test]
    public void Reads_iw_link()
    {
        const string text = """
            Connected to aa:bb:cc:dd:ee:ff (on wlan0)
            	SSID: Casa Rossi 5G
            	freq: 5180
            	RX: 123456 bytes (789 packets)
            	TX: 65432 bytes (321 packets)
            	signal: -63 dBm
            	rx bitrate: 650.0 MBit/s VHT-MCS 7 80MHz VHT-NSS 2
            	tx bitrate: 866.7 MBit/s VHT-MCS 9 80MHz short GI VHT-NSS 2
            """;

        Assert.That(WirelessParsers.ParseIwLink(text), Is.EqualTo(new WifiReading("wlan0", "Casa Rossi 5G", -63, 866.7)));
    }

    [Test]
    public void Iw_without_a_connection_is_null() =>
        Assert.That(WirelessParsers.ParseIwLink("Not connected.\n"), Is.Null);

    [Test]
    public void Reads_netsh_in_english()
    {
        const string text = """

            There is 1 interface on the system:

                Name                   : Wi-Fi
                Description            : Intel(R) Wi-Fi 6 AX201 160MHz
                GUID                   : 0b6e1f0a-0000-0000-0000-000000000000
                Physical address       : aa:bb:cc:dd:ee:ff
                State                  : connected
                SSID                   : HomeNet
                BSSID                  : 11:22:33:44:55:66
                Network type           : Infrastructure
                Radio type             : 802.11ax
                Authentication         : WPA2-Personal
                Channel                : 36
                Receive rate (Mbps)    : 1201
                Transmit rate (Mbps)   : 960.5
                Signal                 : 84%
                Profile                : HomeNet

                Hosted network status  : Not available
            """;

        Assert.That(WirelessParsers.ParseNetshWlan(text), Is.EqualTo(new WifiReading("Wi-Fi", "HomeNet", -58, 1201)));
    }

    [Test]
    public void Reads_netsh_in_italian()
    {
        const string text = """
            Nel sistema è presente 1 interfaccia:

                Nome                   : Wi-Fi
                Descrizione            : Realtek RTL8822CE
                Stato                  : connessa
                SSID                   : FASTWEB-ABC123
                BSSID                  : 11:22:33:44:55:66
                Velocità ricezione (Mbps)  : 433.3
                Velocità trasmissione (Mbps) : 433.3
                Segnale                : 46%
            """;

        Assert.That(WirelessParsers.ParseNetshWlan(text), Is.EqualTo(new WifiReading("Wi-Fi", "FASTWEB-ABC123", -77, 433.3)));
    }

    [Test]
    public void Netsh_without_a_connected_interface_is_null()
    {
        const string text = """
            There is 1 interface on the system:

                Name                   : Wi-Fi
                State                  : disconnected
                Radio status           : Hardware On
            """;

        Assert.That(WirelessParsers.ParseNetshWlan(text), Is.Null);
    }
}
