using Microsoft.Playwright;

namespace WebVitalsMetricsPlaywrightTest
{
    public class AvoidBotDetection : PlaywrightTest
    {
        private IPlaywright _playwright = null!;
        private IBrowser _browser = null!;
        public IBrowser Browser => _browser;
        private IBrowserContext _context = null!;

        [OneTimeSetUp]
        public async Task BaseOneTimeSetUp()
        {
            /* Setup the PlayWright header value overrides so the web site interactions do not look like a test tool to the 
             * bot detection implementation employed by the web site.
            */

            _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Channel = "chrome",
                Headless = true
            });

            _context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 }
            });

            await _context.AddInitScriptAsync(@"
                Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
                window.chrome = { runtime: {} };
            ");
        }


        [OneTimeTearDown]
        public async Task BaseOneTimeTearDown()
        {
            // Cleanup our no longer needed resources
            if (_context != null) await _context.CloseAsync();
            if (_browser != null) await _browser.CloseAsync();
            if (_playwright != null) _playwright.Dispose();
        }
    }
}
