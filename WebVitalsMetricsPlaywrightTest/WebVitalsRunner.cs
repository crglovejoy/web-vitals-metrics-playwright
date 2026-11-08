using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace WebVitalsMetricsPlaywrightTest
{
    /************************/
    /**  THROTTLE OPTIONS  **/
    /************************/

    /* This test can run on machines with varying resources, so we can try to throttle the network and CPU to reduce the results
     * variance. The default values are based on Lighthouse's "Slow 4G" profile.
     */
    public sealed record ThrottleOptions(
    double LatencyMs = 150,
    double DownloadKbps = 1.6 * 1024,   // Lighthouse "Slow 4G"
    double UploadKbps = 750,
    double CpuRate = 4);  // Slow the CPU by a factor of 4 (NOTE: not all CPUs are equal!).  1 = no CPU throttling.


    /***************************/
    /**  METRIC STATS RECORD  **/
    /***************************/
    public sealed record MetricStats(
        string Name,
        int Samples,
        double Median,
        double Min,
        double Max,
        IReadOnlyList<double> Values);



    /********************************/
    /**  WEB VITALS SUMMARY CLASS  **/
    /********************************/

    public sealed class WebVitalsSummary
    {
        public int Iterations { get; init; }
        public int Failed { get; init; }
        public IReadOnlyDictionary<string, MetricStats> Metrics { get; init; }
            = new Dictionary<string, MetricStats>();


        // Median for a metric, or null if it was never reported (e.g. INP with no interaction)
        public double? Median(string name) =>
            Metrics.TryGetValue(name, out var m) ? m.Median : null;


        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Web vitals over {Iterations} run(s), {Failed} failed");
            foreach (var m in Metrics.Values.OrderBy(m => m.Name))
            {
                sb.AppendLine(
                    $"  {m.Name,-5} median {m.Median,9:F2}   min {m.Min,9:F2}   max {m.Max,9:F2}   samples {m.Samples}");
            }
            return sb.ToString();
        }
    }


    /*******************************/
    /**  WEB VITALS RUNNER CLASS  **/
    /*******************************/

    public static class WebVitalsRunner
    {
        /* NOTE: WebVitals only available for Chromium */
        
        /* Use reportAllChanges for LCP, CLS and INP as these need to be updated as new values
         * become available.  TTFB and FCP are once only values.
         */
        private const string Collector = @"
            ;(function () {
                window.__vitals = {};
                const save = (m) => (window.__vitals[m.name] = m.value);
                const start = () => {
                    webVitals.onTTFB(save);
                    webVitals.onFCP(save);
                    webVitals.onLCP(save, { reportAllChanges: true });
                    webVitals.onCLS(save, { reportAllChanges: true });
                    webVitals.onINP(save, { reportAllChanges: true });
                };
                if (document.readyState === 'loading')
                    window.addEventListener('DOMContentLoaded', start);
                else
                    start();
            })();";


        /*
         * Runs the scenario <paramref name="iterations"/> times (after discarding <paramref name="warmup"/> runs)
         * and returns per-metric median, min, max and raw values.
         * Each run uses a fresh browser context so cache and storage don't leak between runs.
         * 
         * <param name="browser">A Chromium browser (e.g. PageTest.Browser).</param>
         * <param name="url">Page to load.</param>
         * <param name="interact">Optional user actions after load (needed for INP).</param>
         * <param name="iterations">Measured runs.</param>
         * <param name="warmup">Runs executed first and discarded (warms DNS, CDN, server caches).</param>
         * <param name="throttle">Optional network/CPU throttling.</param>
         * <param name="webVitalsPath">Path to web-vitals.iife.js; defaults to the test output folder.</param>
         * <param name="contextOptions">Optional context options (viewport, user agent, etc.).</param>
         * <param name="settleMs">Wait after load/interaction so LCP/CLS/INP can settle.</param>
         */
        public static async Task<WebVitalsSummary> RunAsync(
            IBrowser browser,
            string url,
            Func<IPage, Task>? interact = null,
            int iterations = 5,
            int warmup = 1,
            ThrottleOptions? throttle = null,
            string? webVitalsPath = null,
            BrowserNewContextOptions? contextOptions = null,
            int settleMs = 2000)
        {
            if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));

            var path = webVitalsPath ?? Path.Combine(AppContext.BaseDirectory, "web-vitals.iife.js");
            var script = await File.ReadAllTextAsync(path) + "\n" + Collector;

            var samples = new Dictionary<string, List<double>>();
            var failed = 0;

            for (var i = 0; i < warmup + iterations; i++)
            {
                var isWarmup = i < warmup;
                try
                {
                    var result = await RunOnceAsync(browser, url, script, interact, throttle, contextOptions, settleMs);
                    if (isWarmup) continue;

                    foreach (var (name, value) in result)
                    {
                        if (!samples.TryGetValue(name, out var list))
                        {
                            samples[name] = list = [];
                        }

                        list.Add(value);
                    }
                }
                catch (PlaywrightException) when (!isWarmup)
                {
                    failed++;   // keep going - a flaky run shouldn't discard the others
                }
            }

            var metrics = samples.ToDictionary(
                kv => kv.Key,
                kv =>
                {
                    var sorted = kv.Value.OrderBy(v => v).ToList();
                    return new MetricStats(kv.Key, sorted.Count, MedianOf(sorted), sorted[0], sorted[^1], kv.Value);
                });

            return new WebVitalsSummary { 
                Iterations = iterations, 
                Failed = failed, 
                Metrics = metrics
            };
        }


        private static async Task<Dictionary<string, double>> RunOnceAsync(
            IBrowser browser,
            string url,
            string script,
            Func<IPage, Task>? interact,
            ThrottleOptions? throttle,
            BrowserNewContextOptions? contextOptions,
            int settleMs)
        {
            await using var context = await browser.NewContextAsync(contextOptions ?? new BrowserNewContextOptions());
            var page = await context.NewPageAsync();

            // Init script must be registered before navigation.
            await page.AddInitScriptAsync(script);

            if (throttle is not null)
            {
                var cdp = await context.NewCDPSessionAsync(page);
                await cdp.SendAsync("Network.enable");
                await cdp.SendAsync("Network.emulateNetworkConditions", new Dictionary<string, object>
                {
                    ["offline"] = false,
                    ["latency"] = throttle.LatencyMs,
                    ["downloadThroughput"] = throttle.DownloadKbps * 1024 / 8,
                    ["uploadThroughput"] = throttle.UploadKbps * 1024 / 8
                });
                await cdp.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object>
                {
                    ["rate"] = throttle.CpuRate
                });
            }

            try
            {
                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Load });

                // If any interactions are provided, execute the user actionso on the web page.
                if (interact is not null)
                {
                    await interact(page);
                }

                await page.WaitForTimeoutAsync(settleMs);

                var json = await page.EvaluateAsync<string>("() => JSON.stringify(window.__vitals ?? {})");
                return string.IsNullOrWhiteSpace(json)
                    ? new Dictionary<string, double>()
                    : JsonSerializer.Deserialize<Dictionary<string, double>>(json) ?? new Dictionary<string, double>();
            }
            catch
            {
                throw;
            }
            finally
            {
                if (page != null)
                {
                    await page.CloseAsync();
                }
            }
        }


        private static double MedianOf(IReadOnlyList<double> sorted)
        {
            int cnt = sorted.Count;
            return (cnt % 2 == 1) ? sorted[cnt / 2] : (sorted[cnt / 2 - 1] + sorted[cnt / 2]) / 2.0;
        }
    }
}
