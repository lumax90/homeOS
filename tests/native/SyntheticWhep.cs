using System.Security.Cryptography;

namespace Home.NativeTests;

/// <summary>Isolated SDP negotiation fixture only; no browser, decoder or media content is fabricated.</summary>
public static class SyntheticWhep
{
    public static string Offer()
    {
        var fingerprint = string.Join(':', RandomNumberGenerator.GetBytes(32).Select(value => value.ToString("X2")));
        var common = $"c=IN IP4 0.0.0.0\r\na=ice-ufrag:NativeFixture\r\na=ice-pwd:NativeFixturePassword000000000000\r\na=fingerprint:sha-256 {fingerprint}\r\na=setup:actpass\r\na=recvonly\r\na=rtcp-mux\r\n";
        return "v=0\r\no=- 1 1 IN IP4 127.0.0.1\r\ns=Isolated native fixture\r\nt=0 0\r\na=group:BUNDLE 0 1\r\n" +
            "m=video 9 UDP/TLS/RTP/SAVPF 96\r\n" + common + "a=mid:0\r\na=rtpmap:96 H264/90000\r\na=fmtp:96 packetization-mode=1;profile-level-id=42e01f;level-asymmetry-allowed=1\r\n" +
            "m=audio 9 UDP/TLS/RTP/SAVPF 111\r\n" + common + "a=mid:1\r\na=rtpmap:111 opus/48000/2\r\n";
    }
}
