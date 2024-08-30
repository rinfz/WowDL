using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.IO.Compression;
using System.Text;

var acceptedCookie = false;

static Func<IWebDriver, IWebElement?> ElementIsClickable(By locator) {
  return driver => {
    var element = driver.FindElement(locator);
    return (element != null && element.Displayed && element.Enabled) ? element : null;
  };
}

void ProcessFile(IWebDriver driver, string url, string dlPath, int currentCount) {
  driver.Navigate().GoToUrl(url);
  var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
  try {
    if (driver.FindElements(By.CssSelector("iframe[id^=sp_message_iframe]")).Count > 0 && !acceptedCookie) {
      var iframe = wait.Until(d => d.FindElement(By.CssSelector("iframe[id^=sp_message_iframe]")));
      driver.SwitchTo().Frame(iframe);
      var accept = wait.Until(d => d.FindElement(By.XPath("//button[.='Accept']")));
      accept.Click();
      driver.SwitchTo().DefaultContent();
      Thread.Sleep(500);
      acceptedCookie = true;
    }
  } catch {}

  var dl = wait.Until(ElementIsClickable(By.ClassName("download-cta")))!;
  if (dl is null) return;
  dl.Click();

  var modalDl = wait.Until(ElementIsClickable(By.CssSelector("section.modal > div.actions > button")));
  if (modalDl is null) return;
  modalDl.Click();

  var dlWait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
  dlWait.Until(_ => Directory.GetFiles(dlPath).Count() > currentCount);
}

var input = File.ReadAllLines("addons.txt");

var downloadsDir = input.First();
var addonsDir = input.Skip(1).First();

var urls = input.Skip(2);
var downloadFilesCount = urls.Count();

var filesBefore = Directory.GetFiles(downloadsDir);
var currentCount = filesBefore.Count();

var opts = new ChromeOptions();
opts.AddArguments("--start-fullscreen");
var driver = new ChromeDriver(opts);

foreach (var url in urls) {
  ProcessFile(driver, url, downloadsDir, currentCount);
  currentCount += 1;
}

var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
var newFiles = wait.Until(_ => {
  var currentFiles = Directory.GetFiles(downloadsDir);
  var filesDiff = currentFiles.Where(f => !filesBefore.Contains(f));
  if (filesDiff.All(f => f.EndsWith(".zip"))) {
    return filesDiff;
  }
  return null;
})!;

driver.Quit();

// unzip new files

Thread.Sleep(500);

foreach (var zipFile in newFiles) {
  var archive = ZipFile.OpenRead(zipFile);
  var hasToc = archive.Entries.Any(f => f.Name.EndsWith(".toc"));
  archive.Dispose();
  if (hasToc) {
    ZipFile.ExtractToDirectory(zipFile, addonsDir, Encoding.Default, true);
    try {
      File.Delete(zipFile);
    } catch (IOException) {
      Console.WriteLine("Failed to delete file, please delete manually ({0})", zipFile);
    }
  } else {
    Console.WriteLine("Unable to process addon, no TOC file found ({0})", zipFile);
  }
}