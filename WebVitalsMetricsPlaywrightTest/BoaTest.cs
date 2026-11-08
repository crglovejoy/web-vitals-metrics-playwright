using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace WebVitalsMetricsPlaywrightTest
{
    public class BoaTest : AvoidBotDetection
    {
        [Test]
        public async Task BoaWebVitalsMetricsTest()
        {
            //ThrottleOptions? throttle = null;  // new ThrottleOptions();
            ThrottleOptions? throttle = new();

            // Assign thresholds to be used by assertions later based on whether throttling is in use or not
            double lcpMedianThresh = (throttle != null) ? 4500d : 250d;
            double clsMedianThresh = (throttle != null) ? 0.15d : 0.1d;
            double fcpMedianThresh = (throttle != null) ? 1800d : 250d;
            double ttfbMedianThresh = (throttle != null) ? 720d : 80d;

            try
            {
                var summary = await WebVitalsRunner.RunAsync(
                    Browser,
                    "https://www.bankofamerica.com/",
                    interact: null, // Example: async page => await page.ClickAsync("button#menu"),
                    iterations: 5,  // execute 5 iterations (after the number of warmup iterations are completed) and capture the metrics for each
                    warmup: 1,      // exclude warmup executions from the metrics that will be analyzed later via assertions
                    throttle: throttle
                );

                if (throttle != null)
                {
                    TestContext.Out.WriteLine($"Throttling in use: Latency {throttle.LatencyMs} ms, Download {throttle.DownloadKbps} Kbps, Upload {throttle.UploadKbps} Kbps, CPU rate {throttle.CpuRate}");
                }

                TestContext.Out.WriteLine($"Median Thresholds: LCP={lcpMedianThresh} ms, CLS={clsMedianThresh}, FCP={fcpMedianThresh} ms, TTFB={ttfbMedianThresh} ms");
                TestContext.Out.WriteLine();
                TestContext.Out.WriteLine(summary);

                Assert.Multiple(() =>
                {
                    Assert.That(summary.Median("LCP"), Is.Not.Null.And.LessThanOrEqualTo(lcpMedianThresh));
                    Assert.That(summary.Median("CLS"), Is.Not.Null.And.LessThanOrEqualTo(clsMedianThresh));
                    Assert.That(summary.Median("FCP"), Is.Not.Null.And.LessThanOrEqualTo(fcpMedianThresh));
                    Assert.That(summary.Median("TTFB"), Is.Not.Null.And.LessThanOrEqualTo(ttfbMedianThresh));
                });
                
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine($"Error: {ex}");
                throw;
            }
        }
    }
}
