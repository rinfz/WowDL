// NOTE you can take a screenshot with the following code:
//     var s = ((ITakesScreenshot)driver).GetScreenshot();
//     s.SaveAsFile("s.png");

using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Globalization;
using System.IO.Compression;
using System.Text;

// Main program
var input = File.ReadAllLines("addons.txt");
var urls = input.Skip(2);

var manager = new AddonManager(input);
manager.Run(urls);

class AddonManager
{
    private Dictionary<string, long> cache;
    private bool acceptedCookie = false;
    private bool needsTab = false;
    private readonly TextInfo textInfo;
    private readonly string downloadsDir;
    private readonly string addonsDir;
    private readonly string[] filesBefore;

    public AddonManager(string[] input)
    {
        textInfo = new CultureInfo("en-US", false).TextInfo;
        cache = LoadCache();
        downloadsDir = input[0];
        addonsDir = input[1];
        filesBefore = Directory.GetFiles(downloadsDir);
    }

    private Dictionary<string, long> LoadCache()
    {
        var cache = new Dictionary<string, long>();
        if (File.Exists("cache.txt"))
        {
            foreach (var line in File.ReadLines("cache.txt"))
            {
                var parts = line.Split(',');
                if (parts.Length == 2 && long.TryParse(parts[1], out long timestamp))
                {
                    cache[parts[0]] = timestamp;
                }
            }
        }
        return cache;
    }

    private static ChromeDriver InitializeDriver()
    {
        var service = ChromeDriverService.CreateDefaultService();
        service.SuppressInitialDiagnosticInformation = true;
        service.HideCommandPromptWindow = true;

        var opts = new ChromeOptions();
        opts.AddArguments(["--window-size=1920,1080", "--log-level=1", "--window-position=-9999,-9999"]);
        opts.AddExcludedArgument("enable-logging");

        var driver = new ChromeDriver(service, opts);
        
        driver.ExecuteScript("Object.defineProperty(navigator, 'webdriver', {get: () => undefined})");
        driver.ExecuteCdpCommand("Network.setUserAgentOverride", new Dictionary<string, object>{
            { "userAgent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.6668.101 Safari/537.36" }
        });

        return driver;
    }

    static Func<IWebDriver, IWebElement?> ElementIsClickable(By locator) {
        return driver => {
            var element = driver.FindElement(locator);
            return (element != null && element.Displayed && element.Enabled) ? element : null;
        };
    }

    private void ProcessFile(IWebDriver driver, string url)
    {
        Console.WriteLine("Downloading addon: {0}...", textInfo.ToTitleCase(url.Split("/").Last()));

        if (!needsTab)
        {
            // first run doesnt need this but subsequent ones do  
            needsTab = true;
        }
        else
        {
            // have to use windows and not tabs otherwise the files dont actually download
            driver.SwitchTo().NewWindow(WindowType.Window);
        }

        driver.Navigate().GoToUrl(url);
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
        try
        {
            if (driver.FindElements(By.CssSelector("iframe[id^=sp_message_iframe]")).Count > 0 && !acceptedCookie)
            {
                var iframe = wait.Until(d => d.FindElement(By.CssSelector("iframe[id^=sp_message_iframe]")));
                driver.SwitchTo().Frame(iframe);
                var accept = wait.Until(d => d.FindElement(By.XPath("//button[.='Accept']")));
                accept.Click();
                driver.SwitchTo().DefaultContent();
                Thread.Sleep(500);
                acceptedCookie = true;
            }
        }
        catch {}

        // check to see when the addon was last updated
        bool requiresUpdate = true;
        var detailsBox = wait.Until(d => d.FindElement(By.CssSelector("div.project-details-box > section > dl")));
        if (detailsBox is not null)
        {
            var updatedDt = detailsBox.FindElements(By.TagName("dt")).FirstOrDefault(dt => dt.Text.Contains("Updated"));
            var updatedDate = updatedDt?.FindElement(By.XPath("following-sibling::dd[1]"));
            if (updatedDate is not null)
            {
                var updatedDateText = updatedDate.Text;
                var parsedDate = DateTime.ParseExact(updatedDateText, "MMM d, yyyy", CultureInfo.InvariantCulture);
                var timestamp = ((DateTimeOffset)parsedDate).ToUnixTimeSeconds();
                requiresUpdate = timestamp > cache.GetValueOrDefault(url, 0);
                cache[url] = timestamp;
            }
        }

        if (requiresUpdate)
        {
            var dl = wait.Until(ElementIsClickable(By.ClassName("download-cta")))!;
            if (dl is null) return;
            dl.Click();

            var modalDl = wait.Until(ElementIsClickable(By.CssSelector("section.modal > div.actions > button")));
            if (modalDl is null) return;
            modalDl.Click();
        }
    }

    private void ExtractAddons(IEnumerable<string> newFiles)
    {
        foreach (var zipFile in newFiles)
        {
            var archive = ZipFile.OpenRead(zipFile);
            var hasToc = archive.Entries.Any(f => f.Name.EndsWith(".toc"));
            archive.Dispose();
            if (hasToc)
            {
                ZipFile.ExtractToDirectory(zipFile, addonsDir, Encoding.Default, true);
                try
                {
                    File.Delete(zipFile);
                }
                catch (IOException)
                {
                    Console.WriteLine("Failed to delete file, please delete manually ({0})", zipFile);
                }
            }
            else
            {
                Console.WriteLine("Unable to process addon, no TOC file found ({0})", zipFile);
            }
        }
    }

    private void SaveCache()
    {
        File.WriteAllLines("cache.txt", cache.Select(kvp => $"{kvp.Key},{kvp.Value}"));
    }

    public void Run(IEnumerable<string> urls)
    {
        using var driver = InitializeDriver();
        
        foreach (var url in urls)
        {
            ProcessFile(driver, url);
        }

        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
        var newFiles = wait.Until(_ => {
            var currentFiles = Directory.GetFiles(downloadsDir);
            var filesDiff = currentFiles.Where(f => !filesBefore.Contains(f));
            return filesDiff.All(f => f.EndsWith(".zip")) ? filesDiff : null;
        })!;

        driver.Quit();

        Console.WriteLine("Addons downloaded, now extracting them to the WoW folder...");
        Thread.Sleep(500);

        ExtractAddons(newFiles);
        SaveCache();

        Console.WriteLine("Done!");
    }
}