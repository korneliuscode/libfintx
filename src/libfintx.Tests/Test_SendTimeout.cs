using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using libfintx.FinTS;
using libfintx.FinTS.Data;
using libfintx.FinTS.Message;
using Xunit;

namespace libfintx.Tests;

/// <summary>
/// A bank that takes the message but never answers: the send ends with a timeout instead of
/// waiting forever.
/// </summary>
[Collection("UPD")]
public class Test_SendTimeout
{
    [Fact]
    public async Task ABankThatNeverAnswers_EndsTheSendWithATimeout()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint) listener.LocalEndpoint).Port;
        // Accepts the connection and keeps it open without a response
        var accepted = listener.AcceptTcpClientAsync();

        var client = TestHelper.CreateTestClient(new ConnectionDetails { Url = $"http://127.0.0.1:{port}/fints" });
        var previous = FinTSMessage.ResponseTimeout;
        FinTSMessage.ResponseTimeout = TimeSpan.FromSeconds(1);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => FinTSMessage.Send(client, "HNHBK:1:3+000000000100+300+0+1'"));
            Assert.IsType<TimeoutException>(error.InnerException);
        }
        finally
        {
            FinTSMessage.ResponseTimeout = previous;
            (await accepted).Dispose();
            listener.Stop();
        }
    }
}
