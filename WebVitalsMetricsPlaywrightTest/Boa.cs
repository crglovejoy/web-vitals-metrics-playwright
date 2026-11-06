using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace WebVitalsMetricsPlaywrightTest
{
    public class BoaPaint : AvoidBotDetection
    {
        [Test]
        public async Task BoaWebVitalsMetricsTest()
        {
            //ThrottleOptions? throttle = null;  // new ThrottleOptions();
            ThrottleOptions? throttle = new();

            try
            {
                var summary = await WebVitalsRunner.RunAsync(
                    Browser,
                    "https://www.bankofamerica.com/",
                    interact: null, // async page => await page.ClickAsync("button#menu"),
                    iterations: 5,
                    warmup: 1,
                    throttle: throttle
                );

                if (throttle != null)
                {
                    TestContext.Out.WriteLine("Throttling in use: Latency {0} ms, Download {1} Kbps, Upload {2} Kbps, CPU rate {3}",
                        throttle.LatencyMs, throttle.DownloadKbps, throttle.UploadKbps, throttle.CpuRate);
                }
                
                TestContext.Out.WriteLine(summary);

                if (throttle != null)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(summary.Median("LCP"), Is.Not.Null.And.LessThanOrEqualTo(4500d));
                        Assert.That(summary.Median("CLS"), Is.Not.Null.And.LessThanOrEqualTo(0.15d));
                        Assert.That(summary.Median("FCP"), Is.Not.Null.And.LessThanOrEqualTo(1800d));
                        Assert.That(summary.Median("TTFB"), Is.Not.Null.And.LessThanOrEqualTo(720d));
                    });
                }
                else
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(summary.Median("LCP"), Is.Not.Null.And.LessThanOrEqualTo(250d));
                        Assert.That(summary.Median("CLS"), Is.Not.Null.And.LessThanOrEqualTo(0.1d));
                        Assert.That(summary.Median("FCP"), Is.Not.Null.And.LessThanOrEqualTo(250d));
                        Assert.That(summary.Median("TTFB"), Is.Not.Null.And.LessThanOrEqualTo(80d));
                    });     
                }
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine($"Error: {ex}");
                throw;
            }
        }
    }
}