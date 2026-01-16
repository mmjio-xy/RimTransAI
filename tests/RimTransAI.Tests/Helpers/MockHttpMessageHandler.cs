namespace RimTransAI.Tests.Helpers;

/// <summary>
/// Mock HttpMessageHandler 用于测试 HTTP 请求
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_handler(request));
    }

    /// <summary>
    /// 创建返回 JSON 响应的 Handler
    /// </summary>
    public static MockHttpMessageHandler WithJsonResponse(string json, System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
    {
        return new MockHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
